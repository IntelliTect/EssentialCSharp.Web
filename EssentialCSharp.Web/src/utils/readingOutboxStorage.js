/**
 * Persistence for intervals that could not be delivered, so a failed send is not lost
 * when the next full page load replaces the in-memory queue (the site is a multi-page app).
 *
 * Every function tolerates unavailable, full or corrupt storage (it behaves as empty).
 * Safety rules:
 *  - A record belongs to one user (READING_CONTEXT.userId); records of any other user
 *    are discarded, so data from another account is never sent.
 *  - Each tab writes its own record (its own key), so tabs never overwrite each other.
 *  - Records are taken by removal: whoever removes a record owns its entries, so two tabs
 *    cannot both send them. Delivery is at-most-once; a request in flight when its page is
 *    destroyed, or a record taken and then lost, is not recovered.
 */
import { MAX_QUEUED_INTERVALS } from "./readingConstants.js";

const KEY_PREFIX = "essentialcsharp-reading-outbox:";

const isPositiveNumber = (value) => typeof value === "number" && Number.isFinite(value) && value > 0;

/** @returns {import("../services/readingApi.js").ReadingInterval | null} */
function toInterval(raw) {
    if (!raw || typeof raw !== "object") return null;
    const { pageKey, activeSeconds, wordsRead } = raw;
    if (typeof pageKey !== "string" || !pageKey) return null;
    if (!isPositiveNumber(activeSeconds) || !isPositiveNumber(wordsRead)) return null;
    return { pageKey, activeSeconds, wordsRead };
}

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
    return KEY_PREFIX + Math.random().toString(36).slice(2) + Date.now().toString(36);
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

/**
 * Takes ownership of the record under `key` by removing it.
 * @returns {boolean} false when the record is gone (another tab took it, or storage failed)
 */
export function claimOutbox(key) {
    try {
        if (localStorage.getItem(key) === null) return false;
        localStorage.removeItem(key);
        return true;
    } catch {
        return false;
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
