/**
 * Activity detection (idle / visibility / input / focus) with no tracking logic.
 *
 * Rules:
 *  - Starts IDLE: an untouched page never accrues time until real input.
 *  - A bare window "focus" event is NOT input.
 *  - Scroll counts as activity only after the first real input (scrolls caused
 *    by anchors / scroll restoration before that are ignored).
 *  - READING requires the document to be visible AND focused. With several tabs
 *    or windows open only the focused one counts, which mitigates double
 *    counting across tabs.
 */
import { onScopeDispose } from "vue";
import { MOUSEMOVE_THROTTLE_MS } from "../utils/readingConstants.js";

export const STATE_READING = "READING";
export const STATE_IDLE = "IDLE";
export const STATE_HIDDEN = "HIDDEN";

/**
 * @param {{ visible: boolean, focused: boolean, lastInputAt: number | null, now: number, idleThresholdSeconds: number }} input
 *        timestamps in ms from the same clock
 * @returns {"READING" | "IDLE" | "HIDDEN"}
 */
export function computeActivityState({ visible, focused, lastInputAt, now, idleThresholdSeconds }) {
    if (!visible || !focused) return STATE_HIDDEN;
    if (lastInputAt === null || (now - lastInputAt) / 1000 > idleThresholdSeconds) return STATE_IDLE;
    return STATE_READING;
}

/** @param {number} idleThresholdSeconds no input for longer than this switches READING to IDLE */
export function useActivityState(idleThresholdSeconds) {
    let lastInputAt = null;
    let lastMousemoveAt = 0;

    function markInput() {
        lastInputAt = performance.now();
    }

    function markScroll() {
        if (lastInputAt !== null) markInput();
    }

    function onMousemove() {
        const now = performance.now();
        if (now - lastMousemoveAt >= MOUSEMOVE_THROTTLE_MS) {
            lastMousemoveAt = now;
            markInput();
        }
    }

    const listeners = [
        [document, "mousemove", onMousemove],
        [document, "wheel", markInput],
        [document, "touchstart", markInput],
        [document, "keydown", markInput],
        [document, "pointerdown", markInput],
        [window, "scroll", markScroll]
    ];
    for (const [target, type, handler] of listeners) {
        target.addEventListener(type, handler, { passive: true });
    }

    onScopeDispose(() => {
        for (const [target, type, handler] of listeners) {
            target.removeEventListener(type, handler);
        }
    });

    return {
        getState: () => computeActivityState({
            visible: document.visibilityState === "visible",
            focused: document.hasFocus(),
            lastInputAt,
            now: performance.now(),
            idleThresholdSeconds
        }),
        hasUserInput: () => lastInputAt !== null,
        /** Forget input (e.g. page restored from bfcache) so the page starts IDLE again. */
        reset: () => { lastInputAt = null; }
    };
}
