/**
 * useReadingTracker - thin orchestrator for the reading-time feature.
 *
 * Runs once at app level (see App.vue) and shares state with the header widget
 * through provide/inject (READING_TRACKER_KEY). Pages are full server-rendered
 * reloads, so flushing is driven by the page lifecycle (visibilitychange to
 * hidden, pagehide), never by component unmount.
 *
 *  - Anonymous visitors are not tracked: they only get a scroll fraction and the
 *    DEFAULT_WPM estimate. No timers, input listeners or requests.
 *  - Signed-in visitors: the server is authoritative for WPM (fetched once per
 *    page load) and receives delta-based session intervals.
 *
 * Pieces: useActivityState (idle/visibility/focus/input), services/readingApi
 * (fetch + CSRF + typed results), utils/readingProgress (pure scroll math).
 */
import { onScopeDispose, readonly, ref } from "vue";
import { STATE_READING, useActivityState } from "./useActivityState.js";
import {
    STATUS_AUTH_FAILURE,
    STATUS_OK,
    fetchProfile,
    postSession
} from "../services/readingApi.js";
import {
    COMPLETION_THRESHOLD,
    DEFAULT_WPM,
    MAX_CREDIT_WPM,
    MAX_TICK_DELTA_S,
    PERIODIC_FLUSH_ACTIVE_S,
    TICK_INTERVAL_MS
} from "../utils/readingConstants.js";
import {
    creditWords,
    displayFraction,
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
    const wpm = ref(DEFAULT_WPM);
    const scrollFraction = ref(0);

    const tracking = context.isAuthenticated && context.pageWords > 0;
    const activity = tracking ? useActivityState() : null;

    let disposed = false;
    let authFailed = false;
    let inFlight = false;
    let rafId = 0;
    let tickId = 0;

    // Unsent active time (fractional seconds) and scroll baseline/high-water
    // mark, both as fractions of the page content. Flushes send the delta and
    // rebase these so nothing is ever sent twice.
    let pendingSeconds = 0;
    let baseline = 0;
    let maxBottom = 0;
    let lastTickAt = performance.now();

    const contentElement = findContentElement();

    function updateProgress() {
        const measure = measureContent(contentElement);
        scrollFraction.value = displayFraction(measure);
        const bottom = fractionAboveViewportBottom(measure);
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
        if (pendingSeconds >= PERIODIC_FLUSH_ACTIVE_S) void flush();
    }

    /**
     * Sends the unsent delta. Lifecycle flushes (keepalive) always send; the
     * periodic one yields to an in-flight request.
     */
    async function flush({ keepalive = false } = {}) {
        if (!tracking || authFailed || (inFlight && !keepalive)) return;

        updateProgress();
        const activeSeconds = Math.floor(pendingSeconds);
        if (activeSeconds < 1) return;

        const wordsRead = creditWords(
            context.pageWords, maxBottom - baseline, activeSeconds, MAX_CREDIT_WPM);
        // Nothing progressed: keep the time pending for when the reader scrolls.
        if (wordsRead <= 0) return;

        const interval = {
            pageKey: context.pageKey,
            activeSeconds,
            wordsRead,
            completed: maxBottom >= COMPLETION_THRESHOLD
        };

        // Rebase before sending so a second flush cannot re-send this delta.
        pendingSeconds -= activeSeconds;
        baseline = maxBottom;

        inFlight = true;
        const result = await postSession([interval], { keepalive });
        inFlight = false;
        // Failures are dropped (no retry queue), but an expired session stops further posts.
        if (result.status === STATUS_AUTH_FAILURE) authFailed = true;
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

    async function loadProfile() {
        const result = await fetchProfile();
        if (disposed) return;
        if (result.status === STATUS_AUTH_FAILURE) {
            authFailed = true;
        } else if (result.status === STATUS_OK && result.data?.wpm > 0) {
            wpm.value = result.data.wpm;
        }
    }

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
        void loadProfile();
    }

    onScopeDispose(() => {
        disposed = true;
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
