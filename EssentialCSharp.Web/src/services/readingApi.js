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

/** Antiforgery token; the layout emits the meta tag for signed-in users only. */
export function getCsrfToken() {
    return document.querySelector('meta[name="csrf-token"]')?.content ?? "";
}

/**
 * Classifies a response. Isolated here because the rule for telling an
 * antiforgery rejection from a validation failure depends on server behavior
 * (verified by the server's ReadingAntiforgeryContractTests): both are 400
 * application/problem+json, but only a validation failure carries an `errors`
 * object. Any other 400 (no `errors`, empty or unparseable body) is treated as
 * an antiforgery rejection.
 * @param {Response} response
 * @returns {Promise<ApiStatus>}
 */
export async function statusFor(response) {
    if (response.ok) return STATUS_OK;
    const { status } = response;
    if (status === 401 || status === 403) return STATUS_AUTH_FAILURE;
    if (status === 400) {
        try {
            const body = await response.clone().json();
            const errors = body?.errors;
            return errors && typeof errors === "object" ? STATUS_BAD_DATA : STATUS_AUTH_FAILURE;
        } catch {
            return STATUS_AUTH_FAILURE;
        }
    }
    if (status === 408 || status === 429 || status >= 500) return STATUS_TRANSIENT;
    return STATUS_BAD_DATA;
}

/**
 * Posts intervals. Never sends a request without a CSRF token (it cannot
 * succeed): that is reported as an auth failure.
 * Retrying a request whose response was lost can double count the interval on
 * the server (accepted trade-off; there is no idempotency key).
 * @param {ReadingInterval[]} intervals
 * @param {{ keepalive?: boolean }} [options]
 * @returns {Promise<ApiResult<{ wpm?: number | null }>>}
 */
export async function postSession(intervals, { keepalive = false } = {}) {
    const token = getCsrfToken();
    if (!token) return { status: STATUS_AUTH_FAILURE };
    try {
        const res = await fetch(READING_SESSION_URL, {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                RequestVerificationToken: token
            },
            body: JSON.stringify({ intervals }),
            keepalive
        });
        const status = await statusFor(res);
        if (status !== STATUS_OK) return { status };
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

/** @returns {Promise<ApiResult<undefined>>} */
export async function resetProfile() {
    const token = getCsrfToken();
    if (!token) return { status: STATUS_AUTH_FAILURE };
    try {
        const res = await fetch(READING_RESET_URL, {
            method: "POST",
            headers: { RequestVerificationToken: token }
        });
        return { status: await statusFor(res) };
    } catch {
        return { status: STATUS_TRANSIENT };
    }
}
