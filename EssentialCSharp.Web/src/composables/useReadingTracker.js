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
 *    backoff. The queue is also saved to localStorage (see readingOutboxStorage)
 *    so the next page load of the same user sends it. While a persisted batch is
 *    sent by a periodic flush its record stays stored (leased) until the response
 *    arrives. A lifecycle (keepalive) send removes its record first: the page is
 *    about to be destroyed, so the response can never acknowledge it, and keeping
 *    the record would resend (double count) every successful send. A keepalive
 *    request that is aborted with its page is therefore lost (accepted: a rare
 *    undercount is better than routine double counting).
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
    STATUS_TRANSIENT,
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
    CLAIM_CLAIMED,
    CLAIM_GONE,
    claimOutbox,
    clearStoredIntervals,
    isOutboxStored,
    newOutboxKey,
    removeOutbox,
    saveOutbox,
    takeStoredIntervals
} from "../utils/readingOutboxStorage.js";
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
    // Key of the localStorage record mirroring the outbox, null while nothing is persisted.
    // Persisting starts after a failed send; while it is set every outbox change is mirrored.
    let storedKey = null;
    let failures = 0;
    let retryAt = 0;
    // Bumped when another tab resets the profile; responses to batches sent before
    // the reset are ignored (no restore, no wpm update).
    let generation = 0;

    const contentElement = findContentElement();

    function persistOutbox() {
        if (!context.userId) return;
        if (outbox.length === 0) {
            if (storedKey) saveOutbox(storedKey, context.userId, outbox);
            storedKey = null;
            return;
        }
        const key = storedKey ?? newOutboxKey();
        if (saveOutbox(key, context.userId, outbox)) {
            storedKey = key;
        } else {
            // Storage full: nothing reliable is stored, so keep the data in memory only
            // (a stale record would later be mistaken for this tab's data).
            removeOutbox(key);
            storedKey = null;
        }
    }

    function discardPersistedOutbox() {
        storedKey = null;
        // Only this account's records: another signed-in account may share the browser.
        if (context.userId) clearStoredIntervals(context.userId);
    }

    // Intervals left by an earlier page load (never another user's) are sent like new ones.
    if (tracking && context.userId) {
        outbox = takeStoredIntervals(context.userId);
        persistOutbox();
    }

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
            safeFlush();
        } else if (outbox.length > 0) {
            safeFlush({ collect: false }); // retry of previously failed intervals
        }
    }

    /**
     * Turns pending time + progress into a queued interval. Progress that time
     * could not account for (the credit was capped) stays after the baseline and
     * is credited by later time.
     */
    function collect() {
        try {
            // The server rejects intervals above MaxActiveSecondsPerInterval (its options
            // validator enforces idle threshold <= that cap), so never emit
            // one longer than the idle threshold: split long pending time into several
            // intervals of the same batch.
            while (Math.floor(pendingSeconds) >= 1) {
                const activeSeconds = Math.min(Math.floor(pendingSeconds), context.idleThresholdSeconds);
                const { wordsRead, progressConsumed } = creditInterval({
                    pageWords: context.pageWords,
                    progress: maxBottom - baseline,
                    activeSeconds,
                    maxWpm: context.maxCreditWpm
                });
                if (wordsRead <= 0) {
                    // Nothing progressed. Keep the time (idle time was already excluded) so a slow
                    // reader's later scroll is credited at their real, slower speed.
                    return;
                }
                pendingSeconds -= activeSeconds;
                baseline += progressConsumed;
                outbox.push({ pageKey: context.pageKey, activeSeconds, wordsRead });
            }
        } finally {
            trimOutbox();
            if (storedKey) persistOutbox();
        }
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

        // If another tab already took our persisted record (it sends those entries), drop them
        // before collect() could write them back and have them sent twice.
        if (storedKey && !isOutboxStored(storedKey)) {
            storedKey = null;
            outbox = [];
        }

        if (doCollect) {
            updateProgress();
            collect();
        }
        if (outbox.length === 0) return;
        if (!keepalive && (inFlight > 0 || performance.now() < retryAt)) return;

        // A persisted batch is sent only by the tab that holds its record: if another tab
        // took it, that tab sends it and this copy is dropped. If storage itself fails nothing
        // is known about the record, so the batch is still sent (never dropped on a guess).
        // For a periodic send the record stays stored (leased) until the response is
        // acknowledged; a keepalive send cannot be acknowledged, so its record is removed now.
        let sentKey = null;
        if (storedKey) {
            const claim = claimOutbox(storedKey);
            if (claim === CLAIM_GONE) {
                storedKey = null;
                outbox = [];
                return;
            }
            if (claim === CLAIM_CLAIMED) {
                if (keepalive) removeOutbox(storedKey);
                else sentKey = storedKey;
            }
            storedKey = null;
        }
        const batch = outbox;
        outbox = [];
        const batchGeneration = generation;

        // A keepalive send has no timeout and can stay pending on a stalled network. Its batch has left the
        // outbox already, so it must not hold back the periodic flushes (inFlight only gates those).
        if (!keepalive) inFlight++;
        let result;
        try {
            result = await postSession(batch, { keepalive });
        } finally {
            if (!keepalive) inFlight--;
        }
        // Acknowledged (delivered or rejected for good): the record is no longer needed.
        // Transient failures keep it, re-saved below.
        if (sentKey && result.status !== STATUS_TRANSIENT) removeOutbox(sentKey);
        if (disposed || batchGeneration !== generation) return;

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
            discardPersistedOutbox();
        } else if (result.status === STATUS_BAD_DATA) {
            failures = 0; // batch dropped; the data was the problem, not the server
        } else {
            // Transient: put the batch back in front of anything queued meanwhile. If another
            // tab took our record meanwhile, it sends what that record held (everything queued
            // since this send started); only this batch, which that record never held, is kept.
            if (storedKey && !isOutboxStored(storedKey)) {
                storedKey = null;
                outbox = [];
            }
            outbox = batch.concat(outbox);
            // Drop the leased record: persistOutbox re-saves everything (under a newer record
            // if one exists, else a fresh one), and our own lease must not make the next retry
            // treat the record as another tab's.
            if (sentKey) removeOutbox(sentKey);
            trimOutbox();
            persistOutbox();
            failures++;
            retryAt = performance.now()
                + Math.min(RETRY_BASE_DELAY_MS * 2 ** (failures - 1), RETRY_MAX_DELAY_MS);
        }
    }

    // Fire-and-forget flush for timers and page lifecycle events: never an unhandled rejection.
    function safeFlush(options) {
        flush(options).catch(() => {});
    }

    function onVisibilityChange() {
        if (document.visibilityState === "hidden") {
            safeFlush({ keepalive: true });
        } else {
            lastTickAt = performance.now();
        }
    }

    function onPageHide() {
        safeFlush({ keepalive: true });
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
        generation++;
        wpm.value = context.defaultWpm;
        if (!tracking) return;
        pendingSeconds = 0;
        outbox = [];
        discardPersistedOutbox();
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
