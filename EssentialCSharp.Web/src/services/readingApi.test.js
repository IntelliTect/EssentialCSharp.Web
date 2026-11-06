import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
    STATUS_AUTH_FAILURE, STATUS_BAD_DATA, STATUS_OK, STATUS_TRANSIENT,
    getCsrfToken, postSession, resetProfile, statusFor
} from "./readingApi.js";

const PROBLEM = "application/problem+json; charset=utf-8";

// Bodies observed from the server (ReadingAntiforgeryContractTests).
const antiforgeryBody = { type: "https://tools.ietf.org/html/rfc9110#section-15.5.1", title: "Bad Request", status: 400, traceId: "00-abc-01" };
const validationBody = {
    type: "https://tools.ietf.org/html/rfc9110#section-15.5.1",
    title: "One or more validation errors occurred.",
    status: 400,
    errors: { "intervals[0].ActiveSeconds": ["The field ActiveSeconds must be between 1 and 1800."] },
    traceId: "00-abc-01"
};

const response = (status, body, contentType) => new Response(body === undefined ? null : body, {
    status,
    headers: contentType ? { "content-type": contentType } : {}
});

describe("statusFor", () => {
    it("maps 2xx to ok", async () => {
        expect(await statusFor(response(200))).toBe(STATUS_OK);
        expect(await statusFor(response(204))).toBe(STATUS_OK);
    });

    it.each([401, 403])("treats %s as an auth failure", async (status) => {
        expect(await statusFor(response(status))).toBe(STATUS_AUTH_FAILURE);
    });

    it("treats a 400 validation problem (has an errors object) as bad data (drop)", async () => {
        expect(await statusFor(response(400, JSON.stringify(validationBody), PROBLEM))).toBe(STATUS_BAD_DATA);
    });

    it("treats a 400 antiforgery problem (no errors member) as an auth failure (stop)", async () => {
        expect(await statusFor(response(400, JSON.stringify(antiforgeryBody), PROBLEM))).toBe(STATUS_AUTH_FAILURE);
    });

    it("treats a 400 with an empty or non-JSON body as an auth failure (stop)", async () => {
        expect(await statusFor(response(400))).toBe(STATUS_AUTH_FAILURE);
        expect(await statusFor(response(400, "<html>bad</html>", "text/html"))).toBe(STATUS_AUTH_FAILURE);
    });

    it.each([408, 429, 500, 502, 503])("treats %s as transient (retry)", async (status) => {
        expect(await statusFor(response(status))).toBe(STATUS_TRANSIENT);
    });

    it("drops other client errors", async () => {
        expect(await statusFor(response(404))).toBe(STATUS_BAD_DATA);
        expect(await statusFor(response(413))).toBe(STATUS_BAD_DATA);
    });

    it("leaves the body readable for the caller", async () => {
        const res = response(400, JSON.stringify(validationBody), PROBLEM);
        await statusFor(res);
        expect((await res.json()).title).toBe(validationBody.title);
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
    });
});
