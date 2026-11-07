/**
 * Persistence for intervals that could not be delivered, so a failed send is not lost
 * when the next full page load replaces the in-memory queue (the site is a multi-page app).
 *
 * Every function tolerates unavailable, full or corrupt storage (it behaves as empty).
 * Safety rules:
 *  - A record belongs to one user (READING_CONTEXT.userId); records of any other user
 *    are discarded, so data from another account is never sent.
 *  - Each tab writes its own record (its own key), so tabs never overwrite each other.
 *  - A record is taken at page load by removal, or leased by its own tab while that tab
 *    sends it. A leased record stays stored until the send is acknowledged, so a request
 *    aborted with its page is recovered once the lease expires (at-least-once: a send whose
 *    response was lost can be counted twice, there is no idempotency key). Other tabs skip
 *    a record whose lease is still active, so two tabs do not send the same entries.
 */
import { MAX_QUEUED_INTERVALS } from "./readingConstants.js";

const KEY_PREFIX = "essentialcsharp-reading-outbox:";

/** How long a record being sent is reserved for its tab before others may take it over. */
const LEASE_MS = 2 * 60 * 1000;

// The server DTO uses integers: a fractional value would be rejected (400) and drop the whole batch.
const isPositiveInteger = (value) => Number.isInteger(value) && value > 0;

/** @returns {import("../services/readingApi.js").ReadingInterval | null} */
function toInterval(raw) {
    if (!raw || typeof raw !== "object") return null;
    const { pageKey, activeSeconds, wordsRead } = raw;
    if (typeof pageKey !== "string" || !pageKey) return null;
    if (!isPositiveInteger(activeSeconds) || !isPositiveInteger(wordsRead)) return null;
    return { pageKey, activeSeconds, wordsRead };
}

const isLeased = (record) => typeof record?.leaseUntil === "number" && record.leaseUntil > Date.now();

function storedKeys() {
    const keys = [];
    for (let i = 0; i < localStorage.length; i++) {
        const key = localStorage.key(i);
        if (key?.startsWith(KEY_PREFIX)) keys.push(key);
    }
    return keys;
}

/** @returns {string} a key for a new record owned by the calling tab */
export function newOutboxKey() {
    const id = typeof crypto?.randomUUID === "function"
        ? crypto.randomUUID()
        : Math.random().toString(36).slice(2) + Date.now().toString(36);
    return KEY_PREFIX + id;
}

/** Replaces the record under `key` (removes it when there is nothing to keep). */
export function saveOutbox(key, userId, intervals) {
    try {
        if (intervals.length === 0) localStorage.removeItem(key);
        else localStorage.setItem(key, JSON.stringify({ userId, intervals }));
    } catch {
        // Storage unavailable or full: persistence is best effort.
    }
}

/** Outcomes of claimOutbox. */
export const CLAIM_CLAIMED = "claimed";
export const CLAIM_GONE = "gone";
export const CLAIM_ERROR = "error";

/**
 * Reserves the record under `key` for sending: it stays stored (leased) until the caller
 * acknowledges it with removeOutbox or re-saves it with saveOutbox.
 * @returns {"claimed" | "gone" | "error"} gone: confirmed absent, or leased by another sender;
 *   error: storage failed, so nothing is known about the record
 */
export function claimOutbox(key) {
    try {
        const record = JSON.parse(localStorage.getItem(key) ?? "null");
        if (!record || isLeased(record)) return CLAIM_GONE;
        localStorage.setItem(key, JSON.stringify({ ...record, leaseUntil: Date.now() + LEASE_MS }));
        return CLAIM_CLAIMED;
    } catch {
        return CLAIM_ERROR;
    }
}

/** Removes the record under `key` (the send was acknowledged). */
export function removeOutbox(key) {
    try {
        localStorage.removeItem(key);
    } catch {
        // Storage unavailable: nothing to remove.
    }
}

/**
 * Takes every stored record of `userId` (removing them) and returns their valid
 * intervals, oldest first and limited to the newest MAX_QUEUED_INTERVALS.
 * Records of other users and unreadable records are removed and ignored.
 * @returns {import("../services/readingApi.js").ReadingInterval[]}
 */
export function takeStoredIntervals(userId) {
    const intervals = [];
    try {
        for (const key of storedKeys()) {
            let record = null;
            try {
                record = JSON.parse(localStorage.getItem(key) ?? "null");
            } catch {
                // Corrupt: removed below.
            }
            // Being sent by a live tab: leave it until acknowledged or the lease expires.
            if (isLeased(record)) continue;
            localStorage.removeItem(key);
            if (record?.userId !== userId || !Array.isArray(record.intervals)) continue;
            for (const raw of record.intervals) {
                const interval = toInterval(raw);
                if (interval) intervals.push(interval);
            }
        }
    } catch {
        // Storage unavailable: nothing to recover.
    }
    return intervals.slice(-MAX_QUEUED_INTERVALS);
}

/** Removes every stored record (profile reset, auth failure). */
export function clearStoredIntervals() {
    try {
        for (const key of storedKeys()) localStorage.removeItem(key);
    } catch {
        // Storage unavailable: nothing to clear.
    }
}
