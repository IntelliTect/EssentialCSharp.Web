import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { getCsrfToken, postSession, resetProfile, statusFor } from "./readingApi.js";

const response = (status, contentType) => new Response(null, {
    status,
    headers: contentType ? { "content-type": contentType } : {}
});

describe("statusFor", () => {
    it("maps 2xx to ok", () => {
        expect(statusFor(response(200))).toBe("ok");
        expect(statusFor(response(204))).toBe("ok");
    });

    it.each([401, 403])("treats %s as an auth failure", (status) => {
        expect(statusFor(response(status))).toBe("auth-failure");
    });

    it("treats a 400 problem+json validation failure as bad data (drop)", () => {
        expect(statusFor(response(400, "application/problem+json; charset=utf-8"))).toBe("bad-data");
    });

    it("treats a 400 without a problem+json body (antiforgery rejection) as an auth failure (stop)", () => {
        expect(statusFor(response(400))).toBe("auth-failure");
    });

    it.each([408, 429, 500, 502, 503])("treats %s as transient (retry)", (status) => {
        expect(statusFor(response(status))).toBe("transient");
    });

    it("drops other client errors", () => {
        expect(statusFor(response(404))).toBe("bad-data");
        expect(statusFor(response(413))).toBe("bad-data");
    });
});

describe("postSession / resetProfile", () => {
    let fetchMock;

    beforeEach(() => {
        document.head.innerHTML = '<meta name="csrf-token" content="tok123">';
        fetchMock = vi.fn();
        vi.stubGlobal("fetch", fetchMock);
    });
    afterEach(() => {
        vi.unstubAllGlobals();
        document.head.innerHTML = "";
    });

    it("reads the CSRF token from the meta tag", () => {
        expect(getCsrfToken()).toBe("tok123");
    });

    it("posts { intervals } as JSON with the antiforgery header and no completed field", async () => {
        fetchMock.mockResolvedValue(new Response('{"wpm":250}', { status: 200 }));
        const intervals = [{ pageKey: "p", activeSeconds: 30, wordsRead: 100 }];

        const result = await postSession(intervals, { keepalive: true });

        const [url, init] = fetchMock.mock.calls[0];
        expect(url).toBe("/api/reading/session");
        expect(init.method).toBe("POST");
        expect(init.keepalive).toBe(true);
        expect(init.headers.RequestVerificationToken).toBe("tok123");
        expect(JSON.parse(init.body)).toEqual({ intervals });
        expect(init.body).not.toContain("completed");
        expect(result).toEqual({ status: "ok", data: { wpm: 250 } });
    });

    it("still reports ok when the response body is not JSON", async () => {
        fetchMock.mockResolvedValue(new Response("", { status: 200 }));
        expect((await postSession([])).status).toBe("ok");
    });

    it("never sends a request without a CSRF token and reports an auth failure", async () => {
        document.head.innerHTML = "";
        expect((await postSession([{ pageKey: "p", activeSeconds: 1, wordsRead: 1 }])).status).toBe("auth-failure");
        expect((await resetProfile()).status).toBe("auth-failure");
        expect(fetchMock).not.toHaveBeenCalled();
    });

    it("reports a network error as transient", async () => {
        fetchMock.mockRejectedValue(new TypeError("offline"));
        expect((await postSession([])).status).toBe("transient");
    });
});
