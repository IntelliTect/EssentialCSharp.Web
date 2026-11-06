/**
 * useReadingTracker - thin orchestrator for the reading-time feature.
 *
 * Runs once at app level (see App.vue) and shares state with the header widget
 * through provide/inject (READING_TRACKER_KEY). Pages are full server-rendered
 * reloads, so flushing is driven by the page lifecycle (visibilitychange to
 * hidden, pagehide), never by component unmount.
 *
 *  - Anonymous visitors are not tracked: they only get a scroll fraction and the
 *    default WPM estimate. No timers, input listeners or requests.
 *  - Signed-in visitors: WPM comes from READING_CONTEXT (server-derived) and is
 *    refreshed from each POST response. Delta-based intervals are queued and sent.
 *
 * Failure handling (see postSession statuses):
 *  - ok: queue entry sent; WPM refreshed from the response.
 *  - auth-failure (401/403, antiforgery rejection, no CSRF token): tracking stops
 *    for the rest of the page.
 *  - bad-data: that batch is dropped, tracking continues.
 *  - transient: the batch goes back to the queue and is retried with exponential
 *    backoff on a later tick; lifecycle flushes (hidden/pagehide) ignore the
 *    backoff. A keepalive flush that fails during pagehide is lost (accepted).
 *
 * Pieces: useActivityState (idle/visibility/focus/input), services/readingApi
 * (fetch + CSRF + typed results), utils/readingProgress (pure scroll math).
 */
import { onScopeDispose, readonly, ref } from "vue";
import { STATE_READING, useActivityState } from "./useActivityState.js";
import {
    STATUS_AUTH_FAILURE,
    STATUS_BAD_DATA,
    STATUS_OK,
    postSession
} from "../services/readingApi.js";
import {
    MAX_QUEUED_INTERVALS,
    MAX_TICK_DELTA_S,
    READING_RESET_CHANNEL,
    RETRY_BASE_DELAY_MS,
    RETRY_MAX_DELAY_MS,
    TICK_INTERVAL_MS
} from "../utils/readingConstants.js";
import {
    creditInterval,
    findContentElement,
    fractionAboveViewportBottom,
    fractionAboveViewportTop,
    measureContent
} from "../utils/readingProgress.js";

/** Injection key for the tracker state consumed by ReadingTimeRemaining. */
export const READING_TRACKER_KEY = Symbol("readingTracker");

/**
 * @typedef {Object} ReadingTracker
 * @property {import("vue").Ref<number>} wpm words per minute used for estimates
 * @property {import("vue").Ref<number>} scrollFraction 0..1 progress through the page content
 * @property {import("../utils/readingContext.js").ReadingContext} context
 */

/**
 * @param {import("../utils/readingContext.js").ReadingContext} context
 * @returns {ReadingTracker}
 */
export function useReadingTracker(context) {
    const wpm = ref(context.wpm ?? context.defaultWpm);
    const scrollFraction = ref(0);

    const tracking = context.isAuthenticated && context.pageWords > 0;
    const activity = tracking ? useActivityState(context.idleThresholdSeconds) : null;

    let disposed = false;
    let stopped = false;
    let inFlight = 0;
    let rafId = 0;
    let tickId = 0;

    // Unsent active time (fractional seconds). baseline..maxBottom is the
    // not-yet-credited span of the page (fractions, see readingProgress.js).
    let pendingSeconds = 0;
    let baseline = 0;
    let maxBottom = 0;
    let lastTickAt = performance.now();

    // Credited intervals awaiting delivery, and retry state.
    /** @type {import("../services/readingApi.js").ReadingInterval[]} */
    let outbox = [];
    let failures = 0;
    let retryAt = 0;

    const contentElement = findContentElement();

    function updateProgress() {
        const measure = measureContent(contentElement);
        const bottom = fractionAboveViewportBottom(measure);
        scrollFraction.value = bottom;
        if (!activity?.hasUserInput()) {
            // Anchors / scroll restoration before real input only move the baseline.
            baseline = fractionAboveViewportTop(measure);
            maxBottom = bottom;
        } else if (bottom > maxBottom) {
            maxBottom = bottom;
        }
    }

    function scheduleProgressUpdate() {
        if (rafId) return;
        rafId = requestAnimationFrame(() => {
            rafId = 0;
            if (!disposed) updateProgress();
        });
    }

    function tick() {
        const now = performance.now();
        const deltaS = Math.min((now - lastTickAt) / 1000, MAX_TICK_DELTA_S);
        lastTickAt = now;
        if (activity.getState() === STATE_READING) pendingSeconds += deltaS;
        updateProgress();
        if (pendingSeconds >= context.periodicFlushActiveSeconds) {
            void flush();
        } else if (outbox.length > 0) {
            void flush({ collect: false }); // retry of previously failed intervals
        }
    }

    /**
     * Turns pending time + progress into a queued interval. Progress that time
     * could not account for (the credit was capped) stays after the baseline and
     * is credited by later time.
     */
    function collect() {
        const activeSeconds = Math.floor(pendingSeconds);
        if (activeSeconds < 1) return;

        const { wordsRead, progressConsumed } = creditInterval({
            pageWords: context.pageWords,
            progress: maxBottom - baseline,
            activeSeconds,
            maxWpm: context.maxCreditWpm
        });
        if (wordsRead <= 0) {
            // Nothing progressed. Keep at most one flush period of time for a
            // coming scroll so long stationary reading cannot inflate later credit.
            pendingSeconds = Math.min(pendingSeconds, context.periodicFlushActiveSeconds);
            return;
        }

        pendingSeconds -= activeSeconds;
        baseline += progressConsumed;
        outbox.push({ pageKey: context.pageKey, activeSeconds, wordsRead });
        trimOutbox();
    }

    function trimOutbox() {
        if (outbox.length > MAX_QUEUED_INTERVALS) outbox.splice(0, outbox.length - MAX_QUEUED_INTERVALS);
    }

    /**
     * Sends the queue. Lifecycle flushes (keepalive) always send; periodic ones
     * yield to an in-flight request and to the retry backoff.
     */
    async function flush({ keepalive = false, collect: doCollect = true } = {}) {
        if (!tracking || stopped) return;

        if (doCollect) {
            updateProgress();
            collect();
        }
        if (outbox.length === 0) return;
        if (!keepalive && (inFlight > 0 || performance.now() < retryAt)) return;

        const batch = outbox;
        outbox = [];

        inFlight++;
        let result;
        try {
            result = await postSession(batch, { keepalive });
        } finally {
            inFlight--;
        }
        if (disposed) return;

        if (result.status === STATUS_OK) {
            failures = 0;
            retryAt = 0;
            const serverWpm = result.data?.wpm;
            if (typeof serverWpm === "number" && Number.isFinite(serverWpm) && serverWpm > 0) {
                wpm.value = serverWpm;
            }
        } else if (result.status === STATUS_AUTH_FAILURE) {
            stopped = true;
            outbox = [];
            pendingSeconds = 0;
        } else if (result.status === STATUS_BAD_DATA) {
            failures = 0; // batch dropped; the data was the problem, not the server
        } else {
            // Transient: put the batch back in front of anything queued meanwhile.
            outbox = batch.concat(outbox);
            trimOutbox();
            failures++;
            retryAt = performance.now()
                + Math.min(RETRY_BASE_DELAY_MS * 2 ** (failures - 1), RETRY_MAX_DELAY_MS);
        }
    }

    function onVisibilityChange() {
        if (document.visibilityState === "hidden") {
            void flush({ keepalive: true });
        } else {
            lastTickAt = performance.now();
        }
    }

    function onPageHide() {
        void flush({ keepalive: true });
    }

    function onPageShow(event) {
        if (!event.persisted) return;
        // Restored from the back/forward cache: start fresh and IDLE.
        activity?.reset();
        lastTickAt = performance.now();
        updateProgress();
    }

    // Another tab reset the reading profile: return to the default estimate and
    // drop unsent data so it does not repopulate the profile that was just cleared.
    function onReset() {
        wpm.value = context.defaultWpm;
        if (!tracking) return;
        pendingSeconds = 0;
        outbox = [];
        failures = 0;
        retryAt = 0;
        updateProgress();
        baseline = maxBottom;
    }

    const resetChannel = typeof BroadcastChannel === "function"
        ? new BroadcastChannel(READING_RESET_CHANNEL)
        : null;
    if (resetChannel) resetChannel.onmessage = onReset;

    // Progress display works for everyone.
    const onScrollOrResize = () => scheduleProgressUpdate();
    window.addEventListener("scroll", onScrollOrResize, { passive: true });
    window.addEventListener("resize", onScrollOrResize, { passive: true });
    window.addEventListener("load", onScrollOrResize);
    updateProgress();

    if (tracking) {
        document.addEventListener("visibilitychange", onVisibilityChange);
        window.addEventListener("pagehide", onPageHide);
        window.addEventListener("pageshow", onPageShow);
        tickId = window.setInterval(tick, TICK_INTERVAL_MS);
    }

    onScopeDispose(() => {
        disposed = true;
        resetChannel?.close();
        clearInterval(tickId);
        if (rafId) cancelAnimationFrame(rafId);
        window.removeEventListener("scroll", onScrollOrResize);
        window.removeEventListener("resize", onScrollOrResize);
        window.removeEventListener("load", onScrollOrResize);
        document.removeEventListener("visibilitychange", onVisibilityChange);
        window.removeEventListener("pagehide", onPageHide);
        window.removeEventListener("pageshow", onPageShow);
    });

    return { wpm: readonly(wpm), scrollFraction: readonly(scrollFraction), context };
}
