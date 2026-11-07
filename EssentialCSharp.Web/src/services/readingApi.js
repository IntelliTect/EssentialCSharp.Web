import { READING_RESET_URL, READING_SESSION_URL } from "../utils/readingConstants.js";

/**
 * - ok: request succeeded
 * - auth-failure: session expired/forbidden, or antiforgery rejected, or no CSRF token: stop trying
 * - bad-data: the server rejected this payload as invalid: drop it, never retry
 * - transient: 408/429/5xx/network error: keep the data and retry later
 * @typedef {"ok" | "auth-failure" | "bad-data" | "transient"} ApiStatus
 */

/**
 * @typedef {Object} ReadingInterval
 * @property {string} pageKey
 * @property {number} activeSeconds
 * @property {number} wordsRead
 */

/**
 * @template T
 * @typedef {{ status: ApiStatus, data?: T }} ApiResult
 */

export const STATUS_OK = "ok";
export const STATUS_AUTH_FAILURE = "auth-failure";
export const STATUS_BAD_DATA = "bad-data";
export const STATUS_TRANSIENT = "transient";

/** Header (value "1") the server sets on a 400 caused by a rejected antiforgery token. */
export const ANTIFORGERY_FAILURE_HEADER = "X-Antiforgery-Failure";

/** Antiforgery token; the layout emits the meta tag for signed-in users only. */
export function getCsrfToken() {
    return document.querySelector('meta[name="csrf-token"]')?.content ?? "";
}

/**
 * Classifies a response. A 400 is an antiforgery rejection only when the server
 * says so explicitly with the ANTIFORGERY_FAILURE_HEADER response header (set by
 * the server's ReadingAntiforgeryFailureHeaderAttribute). Any other 400, such as a
 * validation failure or one produced by a proxy, is bad data.
 * @param {Response} response
 * @returns {ApiStatus}
 */
export function statusFor(response) {
    if (response.ok) return STATUS_OK;
    const { status } = response;
    if (status === 401 || status === 403) return STATUS_AUTH_FAILURE;
    if (status === 400 && response.headers.get(ANTIFORGERY_FAILURE_HEADER) === "1") return STATUS_AUTH_FAILURE;
    if (status === 408 || status === 429 || status >= 500) return STATUS_TRANSIENT;
    return STATUS_BAD_DATA;
}

/**
 * Sends a CSRF-protected POST and classifies the outcome. Never sends a request
 * without a CSRF token (it cannot succeed): that is reported as an auth failure.
 * @param {string} url
 * @param {{ headers?: Record<string, string>, body?: string, keepalive?: boolean, readJson?: boolean }} [options]
 * @returns {Promise<ApiResult<any>>}
 */
async function postWithCsrf(url, { headers = {}, body, keepalive, readJson = false } = {}) {
    const token = getCsrfToken();
    if (!token) return { status: STATUS_AUTH_FAILURE };
    try {
        const res = await fetch(url, {
            method: "POST",
            headers: { ...headers, RequestVerificationToken: token },
            body,
            keepalive
        });
        const status = statusFor(res);
        if (status !== STATUS_OK || !readJson) return { status };
        let data;
        try {
            data = await res.json();
        } catch {
            data = undefined;
        }
        return { status, data };
    } catch {
        return { status: STATUS_TRANSIENT };
    }
}

/**
 * Posts intervals.
 * Retrying a request whose response was lost can double count the interval on
 * the server (accepted trade-off; there is no idempotency key).
 * @param {ReadingInterval[]} intervals
 * @param {{ keepalive?: boolean }} [options]
 * @returns {Promise<ApiResult<{ wpm?: number | null }>>}
 */
export function postSession(intervals, { keepalive = false } = {}) {
    return postWithCsrf(READING_SESSION_URL, {
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ intervals }),
        keepalive,
        readJson: true
    });
}

/** @returns {Promise<ApiResult<undefined>>} */
export function resetProfile() {
    return postWithCsrf(READING_RESET_URL);
}
