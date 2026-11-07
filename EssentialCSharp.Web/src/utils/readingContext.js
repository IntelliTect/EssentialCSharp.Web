/**
 * @typedef {Object} ReadingContext
 * @property {boolean} isAuthenticated
 * @property {string | null} userId signed-in user id (from the layout's meta tag), scopes persisted unsent intervals to one account
 * @property {string} pageKey
 * @property {number} pageWords
 * @property {number} chapterWords
 * @property {number} wordsBeforePage
 * @property {number} chapterStartWords
 * @property {number} bookWords
 * @property {number | null} wpm signed-in user's server-derived WPM, null when none
 * @property {number} defaultWpm
 * @property {number} maxCreditWpm
 * @property {number} idleThresholdSeconds
 * @property {number} periodicFlushActiveSeconds
 */

function toCount(value) {
    const n = Number(value);
    return Number.isFinite(n) && n > 0 ? n : 0;
}

const isPositiveNumber = (value) => typeof value === "number" && Number.isFinite(value) && value > 0;

/** The layout exposes the signed-in user id in a meta tag (not a window global); anonymous has none. */
function getUserId(win) {
    const id = win.document?.querySelector('meta[name="ecs-auth-user-id"]')?.content;
    return id ? id : null;
}

/**
 * Reads window.READING_CONTEXT (emitted by the layout on content pages only).
 * The tuning values come from the server (ReadingOptions) and have no client
 * fallback: if any is missing or invalid the feature is disabled.
 * @param {Window} [win]
 * @returns {ReadingContext | null} null when this is not a content page or the context is incomplete.
 */
export function getReadingContext(win = window) {
    const raw = win.READING_CONTEXT;
    if (!raw || typeof raw !== "object" || !raw.pageKey) return null;
    const required = [raw.defaultWpm, raw.maxCreditWpm, raw.idleThresholdSeconds, raw.periodicFlushActiveSeconds];
    if (!required.every(isPositiveNumber)) return null;
    return {
        isAuthenticated: raw.isAuthenticated === true,
        userId: getUserId(win),
        pageKey: String(raw.pageKey),
        pageWords: toCount(raw.pageWords),
        chapterWords: toCount(raw.chapterWords),
        wordsBeforePage: toCount(raw.wordsBeforePage),
        chapterStartWords: toCount(raw.chapterStartWords),
        bookWords: toCount(raw.bookWords),
        wpm: isPositiveNumber(raw.wpm) ? raw.wpm : null,
        defaultWpm: raw.defaultWpm,
        maxCreditWpm: raw.maxCreditWpm,
        idleThresholdSeconds: raw.idleThresholdSeconds,
        periodicFlushActiveSeconds: raw.periodicFlushActiveSeconds
    };
}
