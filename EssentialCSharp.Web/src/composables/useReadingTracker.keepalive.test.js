// A keepalive send (fired on every hide / pagehide) has no timeout; if it hangs it must not stop later
// periodic flushes or cause queued reading time to be trimmed away.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { effectScope } from "vue";
import { useReadingTracker } from "./useReadingTracker.js";
import { MAX_QUEUED_INTERVALS } from "../utils/readingConstants.js";
import { installFakeBroadcastChannel } from "../test-utils/fakeBroadcastChannel.js";

const VIEWPORT = 500;
const CONTENT_HEIGHT = 200000; // long page so progress never limits credit
const context = () => ({
    isAuthenticated: true, userId: "user-1", pageKey: "c/p", pageWords: 400000, chapterWords: 400000,
    wordsBeforePage: 0, chapterStartWords: 0, bookWords: 900000, wpm: 240, defaultWpm: 200,
    maxCreditWpm: 800, idleThresholdSeconds: 300, periodicFlushActiveSeconds: 60
});

describe("hung keepalive send", () => {
    let scope, contentTop, visibility, calls, pending, scrollPos;
    const advance = (s) => vi.advanceTimersByTimeAsync(s * 1000);
    const hide = () => { visibility = "hidden"; document.dispatchEvent(new Event("visibilitychange")); };
    const show = () => { visibility = "visible"; document.dispatchEvent(new Event("visibilitychange")); };
    // keep reading: input + scroll further each second-chunk
    const readFor = async (seconds) => {
        for (let i = 0; i < seconds; i += 5) {
            document.dispatchEvent(new Event("keydown"));
            scrollPos += 400;
            contentTop = -scrollPos;
            await advance(5);
        }
    };
    const start = () => { scope = effectScope(); return scope.run(() => useReadingTracker(context())); };

    beforeEach(() => {
        vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout", "setInterval", "clearInterval", "Date", "performance"] });
        document.head.innerHTML = '<meta name="csrf-token" content="tok">';
        document.body.innerHTML = '<div id="site-layout"><div data-reading-content></div></div>';
        contentTop = 0; scrollPos = 0; visibility = "visible";
        document.querySelector("[data-reading-content]").getBoundingClientRect = () => ({ top: contentTop, height: CONTENT_HEIGHT });
        window.innerHeight = VIEWPORT;
        vi.spyOn(document, "hasFocus").mockReturnValue(true);
        vi.spyOn(document, "visibilityState", "get").mockImplementation(() => visibility);
        calls = [];
        pending = null;
        installFakeBroadcastChannel(vi);
    });
    afterEach(() => {
        scope?.stop(); document.body.innerHTML = ""; vi.useRealTimers(); vi.unstubAllGlobals();
        localStorage.clear(); vi.restoreAllMocks();
    });

    it("does not stop later periodic flushes", async () => {
        vi.stubGlobal("fetch", vi.fn((url, init) => {
            const intervals = JSON.parse(init.body).intervals;
            calls.push({ keepalive: init.keepalive, intervals });
            if (init.keepalive) return new Promise(() => {}); // never settles
            return Promise.resolve(new Response(JSON.stringify({ wpm: 240 }), { status: 200, headers: { "content-type": "application/json" } }));
        }));
        start();
        await readFor(30);
        hide();            // keepalive flush with ~30s of data: hangs
        await advance(0);
        show();
        // 25 periodic periods of reading after coming back (60s each)
        await readFor(25 * 60 + 30);
        const periodic = calls.filter(c => !c.keepalive);
        const sentSeconds = periodic.flatMap(c => c.intervals).reduce((t, i) => t + i.activeSeconds, 0);
        // Expected: ~1500s of post-show reading delivered by periodic flushes.
        expect(periodic.length).toBeGreaterThan(0);
        expect(sentSeconds).toBeGreaterThan(1400);
    });

    it("does not lose reading time to outbox trimming", async () => {
        vi.stubGlobal("fetch", vi.fn((url, init) => {
            calls.push({ keepalive: init.keepalive, intervals: JSON.parse(init.body).intervals });
            return init.keepalive && calls.length === 1 ? new Promise(() => {}) : Promise.resolve(new Response(JSON.stringify({ wpm: 240 }), { status: 200, headers: { "content-type": "application/json" } }));
        }));
        start();
        await readFor(30); hide(); await advance(0); show();
        await readFor(40 * 60); // 40 minutes of reading
        window.dispatchEvent(new Event("pagehide")); await advance(0);
        const last = calls[calls.length - 1];
        const total = calls.slice(1).flatMap(c => c.intervals).reduce((t, i) => t + i.activeSeconds, 0);
        expect(last.intervals.length).toBeLessThanOrEqual(MAX_QUEUED_INTERVALS);
        expect(total).toBeGreaterThan(2300); // all ~2400s should eventually arrive
    });
});
