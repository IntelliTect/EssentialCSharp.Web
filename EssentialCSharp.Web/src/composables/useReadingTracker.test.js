import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { effectScope } from "vue";
import { useReadingTracker } from "./useReadingTracker.js";

// Scenario: a 2000-word page whose content is 2000px tall in a 500px viewport.
// Server tuning: default 200 wpm, credit cap 800 wpm, idle after 300s, flush every 60 active seconds.
const realSetTimeout = globalThis.setTimeout;

const context = (overrides = {}) => ({
    isAuthenticated: true,
    pageKey: "chapter-1/page-1",
    pageWords: 2000,
    chapterWords: 8000,
    wordsBeforePage: 0,
    chapterStartWords: 0,
    bookWords: 90000,
    wpm: 240,
    defaultWpm: 200,
    maxCreditWpm: 800,
    idleThresholdSeconds: 300,
    periodicFlushActiveSeconds: 60,
    ...overrides
});

const reply = (status, { body, contentType } = {}) => {
    const res = {
        ok: status >= 200 && status < 300,
        status,
        headers: { get: (name) => (name.toLowerCase() === "content-type" ? contentType ?? null : null) },
        json: async () => {
            if (body === undefined) throw new SyntaxError("empty body");
            return body;
        }
    };
    res.clone = () => res;
    return res;
};

const PROBLEM_JSON = "application/problem+json";
const ANTIFORGERY_BODY = { title: "Bad Request", status: 400, traceId: "t" };
const VALIDATION_BODY = { title: "One or more validation errors occurred.", status: 400, errors: { x: ["bad"] }, traceId: "t" };

describe("useReadingTracker", () => {
    let scope;
    let fetchMock;
    let contentTop;

    function start(ctx = context()) {
        scope = effectScope();
        return scope.run(() => useReadingTracker(ctx));
    }

    const scrollTo = (top) => { contentTop = top; };
    const posts = () => fetchMock.mock.calls.map(([, init]) => JSON.parse(init.body).intervals);
    const advance = (seconds) => vi.advanceTimersByTimeAsync(seconds * 1000);

    beforeEach(() => {
        vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout", "setInterval", "clearInterval", "Date", "performance"] });
        document.head.innerHTML = '<meta name="csrf-token" content="tok">';
        document.body.innerHTML = '<div id="site-layout"><div data-reading-content></div></div>';
        contentTop = 0;
        const content = document.querySelector("[data-reading-content]");
        content.getBoundingClientRect = () => ({ top: contentTop, height: 2000 });
        window.innerHeight = 500;
        vi.spyOn(document, "hasFocus").mockReturnValue(true);
        fetchMock = vi.fn().mockResolvedValue(reply(200, { body: { wpm: 240 } }));
        vi.stubGlobal("fetch", fetchMock);
    });

    afterEach(() => {
        scope?.stop();
        vi.useRealTimers();
        vi.unstubAllGlobals();
        vi.restoreAllMocks();
    });

    /** Real input, then scroll to the end of the page, so progress is not the limiting factor. */
    function readThroughPage() {
        document.dispatchEvent(new Event("keydown"));
        scrollTo(-1500);
    }

    it("does not track anonymous visitors at all", async () => {
        start(context({ isAuthenticated: false, wpm: null }));
        document.dispatchEvent(new Event("keydown"));
        scrollTo(-1500);
        await advance(300);
        window.dispatchEvent(new Event("pagehide"));
        expect(fetchMock).not.toHaveBeenCalled();
    });

    it("estimates with the signed-in user's wpm, or the default when there is none", () => {
        expect(start(context({ wpm: 240 })).wpm.value).toBe(240);
        scope.stop();
        expect(start(context({ wpm: null })).wpm.value).toBe(200);
    });

    it("never accrues time before the first real input", async () => {
        start();
        scrollTo(-1500);
        await advance(120);
        expect(fetchMock).not.toHaveBeenCalled();
    });

    it("sends an interval once 60 active seconds accumulate, capped at the max credit wpm, without a completed flag", async () => {
        start();
        readThroughPage();
        await advance(61);

        expect(fetchMock).toHaveBeenCalledTimes(1);
        // The whole page scrolled in 60s would be 2000 words; 800 wpm allows 800.
        expect(posts()[0]).toEqual([{ pageKey: "chapter-1/page-1", activeSeconds: 60, wordsRead: 800 }]);
    });

    it("carries the uncredited remainder forward instead of discarding it", async () => {
        start();
        readThroughPage();
        await advance(61);
        await advance(60);

        // 800 of 2000 words credited first; the rest of the scrolled progress is credited by later time.
        expect(posts()[1][0].wordsRead).toBe(800);
    });

    it("takes the new wpm from the server response", async () => {
        fetchMock.mockResolvedValue(reply(200, { body: { wpm: 275 } }));
        const tracker = start();
        readThroughPage();
        await advance(61);
        expect(tracker.wpm.value).toBe(275);
    });

    it("credits only the first screen when the reader never scrolls", async () => {
        start();
        document.dispatchEvent(new Event("keydown"));
        await advance(61);
        // 500px of 2000px is on screen: a quarter of 2000 words.
        expect(posts()[0][0].wordsRead).toBe(500);
        await advance(120);
        expect(fetchMock).toHaveBeenCalledTimes(1);
    });

    it("flushes on pagehide with keepalive", async () => {
        start();
        readThroughPage();
        await advance(10);
        window.dispatchEvent(new Event("pagehide"));
        await advance(0);

        expect(fetchMock).toHaveBeenCalledTimes(1);
        expect(fetchMock.mock.calls[0][1].keepalive).toBe(true);
        expect(posts()[0][0].activeSeconds).toBe(10);
    });

    describe("failures", () => {
        it("keeps the interval after a transient failure and retries it after the backoff, not before", async () => {
            fetchMock.mockResolvedValueOnce(reply(503)).mockResolvedValue(reply(200, { body: { wpm: 240 } }));
            start();
            readThroughPage();
            await advance(61);
            expect(fetchMock).toHaveBeenCalledTimes(1);

            await advance(3); // still inside the backoff window
            expect(fetchMock).toHaveBeenCalledTimes(1);

            await advance(5); // backoff elapsed
            expect(fetchMock).toHaveBeenCalledTimes(2);
            expect(posts()[1]).toEqual(posts()[0]);
        });

        it("retries after a network error", async () => {
            fetchMock.mockRejectedValueOnce(new TypeError("offline")).mockResolvedValue(reply(200));
            start();
            readThroughPage();
            await advance(61);
            await advance(10);
            expect(fetchMock).toHaveBeenCalledTimes(2);
            expect(posts()[1]).toEqual(posts()[0]);
        });

        it("backs off further after repeated failures", async () => {
            fetchMock.mockResolvedValue(reply(429));
            start();
            readThroughPage();
            await advance(61);   // attempt 1
            await advance(6);    // attempt 2 after >=5s
            expect(fetchMock).toHaveBeenCalledTimes(2);
            await advance(6);    // second backoff is longer than 6s
            expect(fetchMock).toHaveBeenCalledTimes(2);
            await advance(6);
            expect(fetchMock).toHaveBeenCalledTimes(3);
        });

        it("stops tracking for the page after a 403", async () => {
            fetchMock.mockResolvedValue(reply(403));
            start();
            readThroughPage();
            await advance(61);
            await advance(300);
            window.dispatchEvent(new Event("pagehide"));
            await advance(0);
            expect(fetchMock).toHaveBeenCalledTimes(1);
        });

        it("stops tracking when a 400 problem has no errors member (antiforgery rejection)", async () => {
            fetchMock.mockResolvedValue(reply(400, { contentType: PROBLEM_JSON, body: ANTIFORGERY_BODY }));
            start();
            readThroughPage();
            await advance(61);
            await advance(200);
            expect(fetchMock).toHaveBeenCalledTimes(1);
        });

        it("drops a batch rejected as invalid but keeps tracking", async () => {
            fetchMock.mockResolvedValueOnce(reply(400, { contentType: PROBLEM_JSON, body: VALIDATION_BODY }))
                .mockResolvedValue(reply(200, { body: { wpm: 240 } }));
            start();
            readThroughPage();
            await advance(61);
            await advance(60);

            expect(fetchMock).toHaveBeenCalledTimes(2);
            // The second request carries only newly earned time, not the rejected interval again.
            expect(posts()[1]).toHaveLength(1);
            expect(posts()[1][0].activeSeconds).toBe(60);
        });

        it("splits long pending time so no interval exceeds the idle threshold", async () => {
            start(context({ idleThresholdSeconds: 30 }));
            readThroughPage();
            for (let i = 0; i < 61; i++) {
                document.dispatchEvent(new Event("keydown")); // keep the reader active across the short idle threshold
                await advance(1);
            }
            const [batch] = posts();
            expect(batch.length).toBeGreaterThan(1);
            expect(Math.max(...batch.map((i) => i.activeSeconds))).toBeLessThanOrEqual(30);
            expect(batch.reduce((sum, i) => sum + i.activeSeconds, 0)).toBe(60);
        });

        it("never posts, and stops, when there is no CSRF token", async () => {
            document.head.innerHTML = "";
            start();
            readThroughPage();
            await advance(200);
            expect(fetchMock).not.toHaveBeenCalled();
        });

        it("does not start a second periodic request while one is in flight", async () => {
            let release;
            fetchMock.mockReturnValueOnce(new Promise((resolve) => { release = resolve; }));
            start();
            readThroughPage();
            await advance(61);
            await advance(70);
            expect(fetchMock).toHaveBeenCalledTimes(1);

            release(reply(200, { body: { wpm: 240 } }));
            await advance(2); // time accrued meanwhile is sent on the next tick
            expect(fetchMock).toHaveBeenCalledTimes(2);
        });

        it("a lifecycle flush during an in-flight request neither loses nor duplicates data, and does not release the guard early", async () => {
            const releases = [];
            fetchMock.mockImplementation(() => new Promise((resolve) => { releases.push(resolve); }));
            start();
            readThroughPage();
            await advance(61);              // periodic request #1 in flight
            await advance(5);               // more reading
            window.dispatchEvent(new Event("pagehide")); // keepalive request #2 in flight
            await advance(0);
            expect(fetchMock).toHaveBeenCalledTimes(2);
            expect(posts()[1]).not.toEqual(posts()[0]);

            releases[1](reply(200));        // #2 finishes first; #1 is still pending
            await advance(0);
            await advance(61);              // periodic flush must still wait for #1
            expect(fetchMock).toHaveBeenCalledTimes(2);
        });
    });

    describe("reset in another tab", () => {
        async function resetFromOtherTab() {
            const other = new BroadcastChannel("essentialcsharp-reading-reset");
            other.postMessage({ type: "reset" });
            await new Promise((resolve) => realSetTimeout(resolve, 50));
            other.close();
        }

        it("does not restore a batch that was in flight at reset time when it fails", async () => {
            let fail;
            fetchMock.mockReturnValueOnce(new Promise((resolve) => { fail = resolve; }))
                .mockResolvedValue(reply(200));
            start();
            readThroughPage();
            await advance(61);
            expect(fetchMock).toHaveBeenCalledTimes(1);

            await resetFromOtherTab();
            fail(reply(503));
            await advance(30); // well past the backoff
            expect(fetchMock).toHaveBeenCalledTimes(1);
        });

        it("ignores the wpm of a response to a batch sent before the reset", async () => {
            let succeed;
            fetchMock.mockReturnValueOnce(new Promise((resolve) => { succeed = resolve; }));
            const tracker = start(context({ wpm: 300 }));
            readThroughPage();
            await advance(61);

            await resetFromOtherTab();
            succeed(reply(200, { body: { wpm: 300 } }));
            await advance(1);
            expect(tracker.wpm.value).toBe(200);
        });

        it("returns to the default wpm and discards unsent time", async () => {
            const tracker = start(context({ wpm: 300 }));
            readThroughPage();
            await advance(30);

            const other = new BroadcastChannel("essentialcsharp-reading-reset");
            other.postMessage({ type: "reset" });
            await new Promise((resolve) => realSetTimeout(resolve, 50));
            other.close();

            expect(tracker.wpm.value).toBe(200);
            window.dispatchEvent(new Event("pagehide"));
            await advance(0);
            expect(fetchMock).not.toHaveBeenCalled();
        });
    });
});
