import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
    STATUS_AUTH_FAILURE, STATUS_BAD_DATA, STATUS_OK, STATUS_TRANSIENT,
    getCsrfToken, postSession, resetProfile, statusFor
} from "./readingApi.js";
import { READING_RESET_URL } from "../utils/readingConstants.js";

const response = (status, body, headers = {}) => new Response(body === undefined ? null : body, { status, headers });

describe("statusFor", () => {
    it("maps 2xx to ok", () => {
        expect(statusFor(response(200))).toBe(STATUS_OK);
        expect(statusFor(response(204))).toBe(STATUS_OK);
    });

    it.each([401, 403])("treats %s as an auth failure", (status) => {
        expect(statusFor(response(status))).toBe(STATUS_AUTH_FAILURE);
    });

    it("treats a 400 flagged with the antiforgery failure header as an auth failure (stop)", () => {
        expect(statusFor(response(400, undefined, { "X-Antiforgery-Failure": "1" }))).toBe(STATUS_AUTH_FAILURE);
    });

    it("treats any other 400 as bad data (drop), whatever its body", () => {
        expect(statusFor(response(400, JSON.stringify({ errors: { x: ["bad"] } }), { "content-type": "application/problem+json" }))).toBe(STATUS_BAD_DATA);
        expect(statusFor(response(400))).toBe(STATUS_BAD_DATA);
        expect(statusFor(response(400, "<html>bad</html>", { "content-type": "text/html" }))).toBe(STATUS_BAD_DATA);
    });

    it.each([408, 429, 500, 502, 503])("treats %s as transient (retry)", (status) => {
        expect(statusFor(response(status))).toBe(STATUS_TRANSIENT);
    });

    it("drops other client errors", () => {
        expect(statusFor(response(404))).toBe(STATUS_BAD_DATA);
        expect(statusFor(response(413))).toBe(STATUS_BAD_DATA);
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
        expect(result).toEqual({ status: STATUS_OK, data: { wpm: 250 } });
    });

    it("still reports ok when the response body is not JSON", async () => {
        fetchMock.mockResolvedValue(new Response("", { status: 200 }));
        expect((await postSession([])).status).toBe(STATUS_OK);
    });

    it("never sends a request without a CSRF token and reports an auth failure", async () => {
        document.head.innerHTML = "";
        expect((await postSession([{ pageKey: "p", activeSeconds: 1, wordsRead: 1 }])).status).toBe(STATUS_AUTH_FAILURE);
        expect((await resetProfile()).status).toBe(STATUS_AUTH_FAILURE);
        expect(fetchMock).not.toHaveBeenCalled();
    });

    it("reports a network error as transient", async () => {
        fetchMock.mockRejectedValue(new TypeError("offline"));
        expect((await postSession([])).status).toBe(STATUS_TRANSIENT);
        expect((await resetProfile()).status).toBe(STATUS_TRANSIENT);
    });

    it("resets with a POST carrying the antiforgery header and no body", async () => {
        fetchMock.mockResolvedValue(new Response(null, { status: 200 }));

        const result = await resetProfile();

        const [url, init] = fetchMock.mock.calls[0];
        expect(url).toBe(READING_RESET_URL);
        expect(init.method).toBe("POST");
        expect(init.headers.RequestVerificationToken).toBe("tok123");
        expect(init.body).toBeUndefined();
        expect(result).toEqual({ status: STATUS_OK });
    });
});
