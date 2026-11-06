import { READING_PROFILE_URL, READING_RESET_URL, READING_SESSION_URL } from "../utils/readingConstants.js";

/** @typedef {"ok" | "auth-failure" | "transient"} ApiStatus */

/**
 * @typedef {Object} ReadingInterval
 * @property {string} pageKey
 * @property {number} activeSeconds
 * @property {number} wordsRead
 * @property {boolean} completed
 */

/**
 * @template T
 * @typedef {{ status: ApiStatus, data?: T }} ApiResult
 */

export const STATUS_OK = "ok";
export const STATUS_AUTH_FAILURE = "auth-failure";
export const STATUS_TRANSIENT = "transient";

/** Antiforgery token; the layout emits the meta tag for signed-in users only. */
export function getCsrfToken() {
    return document.querySelector('meta[name="csrf-token"]')?.content ?? "";
}

function statusFor(response) {
    if (response.ok) return STATUS_OK;
    return response.status === 401 || response.status === 403 ? STATUS_AUTH_FAILURE : STATUS_TRANSIENT;
}

/** @returns {Promise<ApiResult<{ wpm: number | null }>>} */
export async function fetchProfile() {
    try {
        const res = await fetch(READING_PROFILE_URL, { headers: { Accept: "application/json" } });
        const status = statusFor(res);
        if (status !== STATUS_OK) return { status };
        return { status, data: await res.json() };
    } catch {
        return { status: STATUS_TRANSIENT };
    }
}

/**
 * @param {ReadingInterval[]} intervals
 * @param {{ keepalive?: boolean }} [options]
 * @returns {Promise<ApiResult<undefined>>}
 */
export async function postSession(intervals, { keepalive = false } = {}) {
    try {
        const res = await fetch(READING_SESSION_URL, {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                RequestVerificationToken: getCsrfToken()
            },
            // The server currently binds the body to a bare array of intervals.
            body: JSON.stringify({ intervals }),
            keepalive
        });
        return { status: statusFor(res) };
    } catch {
        return { status: STATUS_TRANSIENT };
    }
}

/** @returns {Promise<ApiResult<undefined>>} */
export async function resetProfile() {
    try {
        const res = await fetch(READING_RESET_URL, {
            method: "POST",
            headers: { RequestVerificationToken: getCsrfToken() }
        });
        return { status: statusFor(res) };
    } catch {
        return { status: STATUS_TRANSIENT };
    }
}
