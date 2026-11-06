import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { effectScope } from "vue";
import { useReadingTracker } from "./useReadingTracker.js";
import {
    MAX_QUEUED_INTERVALS,
    MAX_TICK_DELTA_S,
    READING_RESET_CHANNEL,
    RETRY_BASE_DELAY_MS,
    RETRY_MAX_DELAY_MS,
    TICK_INTERVAL_MS
} from "../utils/readingConstants.js";
import { installFakeBroadcastChannel } from "../test-utils/fakeBroadcastChannel.js";

// These tests assert requirements (what is credited, delivered, dropped or stopped),
// not timer arithmetic: waits are derived from the real constants and the server
// tuning in the context below, and totals are compared with the active time that
// actually elapsed.

const VIEWPORT = 500;
const CONTENT_HEIGHT = 2000;
const DEFAULT_WPM = 200;

const context = (overrides = {}) => ({
    isAuthenticated: true,
    pageKey: "chapter-1/page-1",
    pageWords: 2000,
    chapterWords: 8000,
    wordsBeforePage: 0,
    chapterStartWords: 0,
    bookWords: 90000,
    wpm: 240,
    defaultWpm: DEFAULT_WPM,
    maxCreditWpm: 800,
    idleThresholdSeconds: 300,
    periodicFlushActiveSeconds: 60,
    ...overrides
});
// Slower credit cap for tests that read for several minutes, so the page's words are not exhausted
// (once all progress is credited, further stationary time is deliberately not credited).
const SLOW = { maxCreditWpm: 100 };
const FLUSH_S = context().periodicFlushActiveSeconds;
const BASE_DELAY_S = RETRY_BASE_DELAY_MS / 1000;

const PROBLEM_JSON = "application/problem+json";
const jsonResponse = (status, body, contentType = "application/json") => new Response(
    body === undefined ? null : JSON.stringify(body),
    { status, headers: body === undefined ? {} : { "content-type": contentType } });
const ok = (wpm = 240) => jsonResponse(200, { wpm });
const ANTIFORGERY_BODY = { title: "Bad Request", status: 400, traceId: "t" };
const VALIDATION_BODY = { title: "Invalid", status: 400, errors: { x: ["bad"] }, traceId: "t" };

const sum = (intervals, key) => intervals.reduce((total, i) => total + i[key], 0);

describe("useReadingTracker", () => {
    let scope;
    let contentTop;
    let originalInnerHeight;
    // Fake server: every request is recorded; intervals in an ok response count as delivered.
    let calls;
    let delivered;
    let handler;
    let visibility;
    let focused;
    let timeOffsetMs;
    let elapsed; // seconds of fake time advanced

    function start(ctx = context()) {
        scope = effectScope();
        return scope.run(() => useReadingTracker(ctx));
    }

    const scrollTo = (top) => { contentTop = top; };
    const advance = async (seconds) => { elapsed += seconds; await vi.advanceTimersByTimeAsync(seconds * 1000); };
    const press = () => document.dispatchEvent(new Event("keydown"));
    const hide = () => { visibility = "hidden"; document.dispatchEvent(new Event("visibilitychange")); };
    const show = () => { visibility = "visible"; document.dispatchEvent(new Event("visibilitychange")); };
    const leavePage = async () => { window.dispatchEvent(new Event("pagehide")); await advance(0); };
    const deliveredSeconds = () => sum(delivered, "activeSeconds");

    /** Real input, then scroll to the end of the page, so progress is not the limiting factor. */
    function readThroughPage() {
        press();
        scrollTo(-(CONTENT_HEIGHT - VIEWPORT));
    }

    beforeEach(() => {
        vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout", "setInterval", "clearInterval", "Date", "performance"] });
        document.head.innerHTML = '<meta name="csrf-token" content="tok">';
        document.body.innerHTML = '<div id="site-layout"><div data-reading-content></div></div>';
        contentTop = 0;
        document.querySelector("[data-reading-content]").getBoundingClientRect =
            () => ({ top: contentTop, height: CONTENT_HEIGHT });
        originalInnerHeight = window.innerHeight;
        window.innerHeight = VIEWPORT;

        visibility = "visible";
        focused = true;
        timeOffsetMs = 0;
        elapsed = 0;
        const fakeNow = performance.now.bind(performance);
        vi.spyOn(performance, "now").mockImplementation(() => fakeNow() + timeOffsetMs);
        vi.spyOn(document, "hasFocus").mockImplementation(() => focused);
        vi.spyOn(document, "visibilityState", "get").mockImplementation(() => visibility);

        calls = [];
        delivered = [];
        handler = () => ok();
        vi.stubGlobal("fetch", vi.fn(async (url, init) => {
            const intervals = JSON.parse(init.body).intervals;
            calls.push({ at: performance.now(), intervals, keepalive: init.keepalive });
            const response = await handler(calls.length, intervals);
            if (response.ok) delivered.push(...intervals);
            return response;
        }));
        installFakeBroadcastChannel(vi);
    });

    afterEach(() => {
        scope?.stop();
        scope = undefined;
        window.innerHeight = originalInnerHeight;
        document.body.innerHTML = "";
        document.head.innerHTML = "";
        vi.useRealTimers();
        vi.unstubAllGlobals();
        vi.restoreAllMocks();
    });

    describe("who is tracked", () => {
        it("does not track anonymous visitors at all", async () => {
            start(context({ isAuthenticated: false, wpm: null }));
            readThroughPage();
            await advance(300);
            await leavePage();
            hide();
            expect(calls).toHaveLength(0);
        });

        it("estimates with the signed-in user's wpm, or the default when there is none", () => {
            expect(start(context({ wpm: 240 })).wpm.value).toBe(240);
            scope.stop();
            expect(start(context({ wpm: null })).wpm.value).toBe(DEFAULT_WPM);
        });

        it("never posts when there is no CSRF token", async () => {
            document.head.innerHTML = "";
            start();
            readThroughPage();
            await advance(200);
            await leavePage();
            expect(calls).toHaveLength(0);
        });
    });

    describe("crediting", () => {
        it("credits every active second exactly once across all posted intervals", async () => {
            start(context(SLOW));
            readThroughPage();
            await advance(2.5 * FLUSH_S);
            await leavePage();

            expect(calls.length).toBeGreaterThan(1); // periodic flushes happened, not only the final one
            expect(deliveredSeconds()).toBe(elapsed);
        });

        it("never credits more words than the max credit wpm allows for the time spent", async () => {
            const ctx = context();
            start(ctx);
            readThroughPage();
            await advance(100);
            await leavePage();

            for (const interval of delivered) {
                expect(interval.wordsRead).toBeLessThanOrEqual(interval.activeSeconds * ctx.maxCreditWpm / 60);
                expect(interval.pageKey).toBe(ctx.pageKey);
            }
            expect(sum(delivered, "wordsRead")).toBeLessThanOrEqual(deliveredSeconds() * ctx.maxCreditWpm / 60);
        });

        it("carries uncredited progress forward until time accounts for it, never beyond the page", async () => {
            const ctx = context();
            start(ctx);
            readThroughPage();

            await advance(FLUSH_S);
            await leavePage();
            const firstWords = sum(delivered, "wordsRead");
            expect(firstWords).toBeLessThan(ctx.pageWords); // time was the limit, so progress is left over

            // Enough further reading time to cover the rest of the page at the credit cap.
            await advance(Math.ceil(ctx.pageWords * 60 / ctx.maxCreditWpm));
            await leavePage();

            const total = sum(delivered, "wordsRead");
            expect(total).toBeGreaterThan(firstWords);
            expect(total).toBeLessThanOrEqual(ctx.pageWords);
            // Only rounding (under one second of credit) may stay unsent.
            expect(ctx.pageWords - total).toBeLessThanOrEqual(ctx.maxCreditWpm / 60 + 1);
        });

        it("credits only the first screen when the reader never scrolls", async () => {
            const ctx = context();
            start(ctx);
            press();
            await advance(FLUSH_S + 100);
            await leavePage();
            expect(sum(delivered, "wordsRead")).toBe(Math.round(ctx.pageWords * VIEWPORT / CONTENT_HEIGHT));
        });

        it("does not credit content that was already scrolled past when the page loaded", async () => {
            const ctx = context();
            const alreadyPast = 1000;
            scrollTo(-alreadyPast);
            start(ctx);
            readThroughPage();
            await advance(250);
            await leavePage();
            const creditable = ctx.pageWords * (1 - alreadyPast / CONTENT_HEIGHT);
            expect(sum(delivered, "wordsRead")).toBeLessThanOrEqual(creditable);
            expect(sum(delivered, "wordsRead")).toBeGreaterThan(0);
        });

        it("splits long pending time so no interval exceeds the idle threshold", async () => {
            const ctx = context({ idleThresholdSeconds: 30 });
            start(ctx);
            readThroughPage();
            for (let i = 0; i < 2 * FLUSH_S + 1; i++) {
                press(); // keep the reader active across the short idle threshold
                await advance(1);
            }
            await leavePage();
            expect(Math.max(...delivered.map((i) => i.activeSeconds))).toBeLessThanOrEqual(ctx.idleThresholdSeconds);
            expect(deliveredSeconds()).toBe(elapsed);
        });

        it("takes the new wpm from the server response", async () => {
            const serverWpm = 275;
            handler = () => ok(serverWpm);
            const tracker = start();
            readThroughPage();
            await advance(FLUSH_S + 1);
            expect(calls.length).toBeGreaterThan(0);
            expect(tracker.wpm.value).toBe(serverWpm);
        });
    });

    describe("activity", () => {
        it("never accrues time before the first real input", async () => {
            start();
            scrollTo(-(CONTENT_HEIGHT - VIEWPORT));
            await advance(200);
            await leavePage();
            expect(calls).toHaveLength(0);
        });

        it("does not treat a bare focus event or a pre-input scroll as input", async () => {
            start();
            window.dispatchEvent(new Event("focus"));
            window.dispatchEvent(new Event("scroll"));
            await advance(200);
            await leavePage();
            expect(calls).toHaveLength(0);
        });

        it("counts scrolling as activity once there has been real input", async () => {
            const ctx = context(SLOW);
            start(ctx);
            readThroughPage();
            // Two waits that are each inside the idle threshold but together exceed it.
            const wait = ctx.idleThresholdSeconds - 10;
            await advance(wait);
            window.dispatchEvent(new Event("scroll"));
            await advance(wait);
            await leavePage();
            expect(deliveredSeconds()).toBe(2 * wait);
        });

        it("stops crediting once the reader is idle for longer than the idle threshold", async () => {
            const ctx = context();
            start(ctx);
            readThroughPage();
            await advance(ctx.idleThresholdSeconds + 200);
            await leavePage();
            expect(deliveredSeconds()).toBeLessThanOrEqual(ctx.idleThresholdSeconds);
            expect(deliveredSeconds()).toBeGreaterThan(0);
        });

        it("stops crediting when the window loses focus", async () => {
            start();
            readThroughPage();
            const focusedSeconds = 20;
            await advance(focusedSeconds);
            focused = false;
            await advance(100);
            await leavePage();
            expect(deliveredSeconds()).toBeLessThanOrEqual(focusedSeconds);
            expect(deliveredSeconds()).toBeGreaterThan(0);
        });

        it("does not credit time while hidden, sends nothing then, and resumes when visible again", async () => {
            start();
            readThroughPage();
            const before = 20;
            await advance(before);
            hide();
            await advance(0);
            const callsAfterHide = calls.length;
            await advance(100);
            expect(calls).toHaveLength(callsAfterHide);
            expect(deliveredSeconds()).toBe(before);

            show();
            press();
            const after = 15;
            await advance(after);
            await leavePage();
            expect(deliveredSeconds()).toBe(before + after);
        });

        it("does not credit a sleep/resume gap as reading", async () => {
            const ctx = context();
            start(ctx);
            readThroughPage();
            const before = 10;
            await advance(before);
            timeOffsetMs += (ctx.idleThresholdSeconds / 2) * 1000; // machine slept, still inside the idle threshold
            await advance(TICK_INTERVAL_MS / 1000);
            await leavePage();
            expect(deliveredSeconds()).toBeLessThanOrEqual(before + MAX_TICK_DELTA_S);
        });
    });

    describe("lifecycle flushes", () => {
        it.each([
            ["the page becomes hidden", hide],
            ["pagehide", () => window.dispatchEvent(new Event("pagehide"))]
        ])("flushes pending data with keepalive when %s", async (_name, trigger) => {
            start();
            readThroughPage();
            const pending = 10;
            await advance(pending);
            trigger();
            await advance(0);
            expect(calls).toHaveLength(1);
            expect(calls[0].keepalive).toBe(true);
            expect(sum(calls[0].intervals, "activeSeconds")).toBe(pending);
        });

        it("releases all listeners and timers when the scope stops", async () => {
            start();
            readThroughPage();
            await advance(10);
            scope.stop();
            scope = undefined;
            press();
            await advance(300);
            await leavePage();
            hide();
            await advance(0);
            expect(calls).toHaveLength(0);
        });
    });

    describe("failures", () => {
        it("keeps data after a transient failure and delivers it after the backoff, not before", async () => {
            handler = (n) => (n === 1 ? jsonResponse(503) : ok());
            start(context(SLOW));
            readThroughPage();
            await advance(FLUSH_S + 1);
            expect(calls).toHaveLength(1);

            await advance(BASE_DELAY_S - 2); // still inside the backoff window
            expect(calls).toHaveLength(1);

            await advance(BASE_DELAY_S);
            expect(calls.length).toBeGreaterThan(1);
            expect(calls[1].at - calls[0].at).toBeGreaterThanOrEqual(RETRY_BASE_DELAY_MS);
            expect(calls[1].intervals).toEqual(expect.arrayContaining(calls[0].intervals));

            await leavePage();
            expect(deliveredSeconds()).toBe(elapsed); // the failed batch was neither lost nor duplicated
        });

        it("retries after a network error", async () => {
            handler = (n) => {
                if (n === 1) throw new TypeError("offline");
                return ok();
            };
            start();
            readThroughPage();
            await advance(FLUSH_S + 2 * BASE_DELAY_S);
            await leavePage();
            expect(calls.length).toBeGreaterThan(1);
            expect(deliveredSeconds()).toBe(elapsed);
        });

        it("backs off further after repeated failures, within the maximum delay", async () => {
            handler = () => jsonResponse(429);
            start();
            readThroughPage();
            await advance(FLUSH_S);
            for (let i = 0; i < 2 * RETRY_MAX_DELAY_MS / 1000 && calls.length < 4; i++) await advance(1);
            expect(calls.length).toBeGreaterThanOrEqual(4);

            const gaps = calls.slice(1).map((c, i) => c.at - calls[i].at);
            expect(gaps[0]).toBeGreaterThanOrEqual(RETRY_BASE_DELAY_MS);
            for (let i = 1; i < gaps.length; i++) expect(gaps[i]).toBeGreaterThan(gaps[i - 1]);
            for (const gap of gaps) expect(gap).toBeLessThanOrEqual(RETRY_MAX_DELAY_MS + TICK_INTERVAL_MS);
        });

        it("stops tracking for the page after an auth failure (403) and never sends again", async () => {
            handler = () => jsonResponse(403);
            start();
            readThroughPage();
            await advance(FLUSH_S + 1);
            expect(calls).toHaveLength(1);
            await advance(300);
            press();
            await advance(100);
            await leavePage();
            hide();
            await advance(0);
            expect(calls).toHaveLength(1);
        });

        it("stops tracking when a 400 problem has no errors member (antiforgery rejection)", async () => {
            handler = () => jsonResponse(400, ANTIFORGERY_BODY, PROBLEM_JSON);
            start();
            readThroughPage();
            await advance(FLUSH_S + 1);
            await advance(200);
            await leavePage();
            expect(calls).toHaveLength(1);
        });

        it("drops a batch rejected as invalid, never resends it, but keeps tracking", async () => {
            handler = (n) => (n === 1 ? jsonResponse(400, VALIDATION_BODY, PROBLEM_JSON) : ok());
            start();
            readThroughPage();
            await advance(2 * FLUSH_S + 5);
            await leavePage();

            const rejectedSeconds = sum(calls[0].intervals, "activeSeconds");
            expect(deliveredSeconds()).toBeGreaterThan(0);
            // Delivered + rejected account for all time: nothing resent, nothing else lost.
            expect(deliveredSeconds() + rejectedSeconds).toBe(elapsed);
        });

        it("sends no second request while one is in flight, and neither loses nor duplicates data", async () => {
            let release;
            handler = (n) => (n === 1 ? new Promise((resolve) => { release = () => resolve(ok()); }) : ok());
            start(context(SLOW));
            readThroughPage();
            await advance(FLUSH_S + 200);
            expect(calls).toHaveLength(1);

            release();
            await advance(5);
            await leavePage();
            expect(deliveredSeconds()).toBe(elapsed);
        });

        it("a lifecycle flush during an in-flight request sends only new data, so nothing is sent twice", async () => {
            let release;
            handler = (n) => (n === 1 ? new Promise((resolve) => { release = () => resolve(ok()); }) : ok());
            start();
            readThroughPage();
            await advance(FLUSH_S + 1);
            await advance(5);
            await leavePage(); // keepalive flush, concurrent by design
            expect(calls).toHaveLength(2);
            expect(calls[1].keepalive).toBe(true);
            expect(sum(calls[0].intervals, "activeSeconds") + sum(calls[1].intervals, "activeSeconds"))
                .toBeLessThanOrEqual(elapsed);

            release();
            await advance(30);
            await leavePage();
            expect(deliveredSeconds()).toBe(elapsed);
        });

        it("when the server stays down, trims the oldest unsent intervals and keeps the newest", async () => {
            // One interval per second. Scrolling i px at second i earns i words on this page
            // (1px = 1 word), so every queued interval is identifiable.
            const ctx = context({ periodicFlushActiveSeconds: 1, maxCreditWpm: 60000 });
            handler = () => jsonResponse(503);
            start(ctx);
            press();
            const seconds = MAX_QUEUED_INTERVALS + 10;
            for (let i = 1; i <= seconds; i++) {
                scrollTo(-(i * (i + 1)) / 2);
                await advance(1);
            }
            handler = () => ok();
            await leavePage();

            expect(delivered.length).toBeGreaterThan(0);
            expect(delivered.length).toBeLessThanOrEqual(MAX_QUEUED_INTERVALS);
            const words = delivered.map((i) => i.wordsRead);
            expect(words.at(-1)).toBe(seconds); // newest kept
            // Oldest dropped: the first interval (which carried the whole first screen) is gone.
            expect(Math.max(...words)).toBeLessThanOrEqual(seconds);
            expect(words).toEqual([...words].sort((a, b) => a - b));
        });
    });

    describe("reset in another tab", () => {
        // Deterministic: the fake channel delivers synchronously.
        function resetFromOtherTab() {
            const other = new BroadcastChannel(READING_RESET_CHANNEL);
            other.postMessage({ type: "reset" });
            other.close();
        }

        it("does not restore a batch that was in flight at reset time when it fails", async () => {
            let fail;
            handler = (n) => (n === 1 ? new Promise((resolve) => { fail = () => resolve(jsonResponse(503)); }) : ok());
            start();
            readThroughPage();
            await advance(FLUSH_S + 1);
            expect(calls).toHaveLength(1);
            const resetAt = elapsed;

            resetFromOtherTab();
            fail();
            await advance(RETRY_MAX_DELAY_MS / 1000 + 10);
            await leavePage();
            // The pre-reset interval is gone: only time earned after the reset can be delivered.
            expect(deliveredSeconds()).toBeLessThanOrEqual(elapsed - resetAt);
            expect(calls.slice(1).flatMap((c) => c.intervals)).not.toEqual(expect.arrayContaining(calls[0].intervals));
        });

        it("ignores the wpm of a response to a batch sent before the reset", async () => {
            let succeed;
            handler = (n) => (n === 1 ? new Promise((resolve) => { succeed = () => resolve(ok(300)); }) : ok());
            const tracker = start(context({ wpm: 300 }));
            readThroughPage();
            await advance(FLUSH_S + 1);

            resetFromOtherTab();
            succeed();
            await advance(1);
            expect(tracker.wpm.value).toBe(DEFAULT_WPM);
        });

        it("returns to the default wpm and discards unsent data", async () => {
            const tracker = start(context({ wpm: 300 }));
            readThroughPage();
            await advance(FLUSH_S / 2);

            resetFromOtherTab();
            expect(tracker.wpm.value).toBe(DEFAULT_WPM);
            await leavePage();
            expect(calls).toHaveLength(0);
        });

        it("does not re-credit progress that was already made before the reset", async () => {
            start();
            readThroughPage();
            await advance(FLUSH_S / 2);
            resetFromOtherTab();
            await advance(FLUSH_S);
            await leavePage();
            expect(sum(delivered, "wordsRead")).toBe(0);
        });
    });
});
