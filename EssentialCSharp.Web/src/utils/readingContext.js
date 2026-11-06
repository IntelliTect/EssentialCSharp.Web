/**
 * @typedef {Object} ReadingContext
 * @property {boolean} isAuthenticated
 * @property {string} pageKey
 * @property {number} pageWords
 * @property {number} chapterWords
 * @property {number} wordsBeforePage
 * @property {number} chapterStartWords
 * @property {number} bookWords
 */

function toCount(value) {
    const n = Number(value);
    return Number.isFinite(n) && n > 0 ? n : 0;
}

/**
 * Reads window.READING_CONTEXT (emitted by the layout on content pages only).
 * @param {Window} [win]
 * @returns {ReadingContext | null} null when this is not a content page.
 */
export function getReadingContext(win = window) {
    const raw = win.READING_CONTEXT;
    if (!raw || typeof raw !== "object" || !raw.pageKey) return null;
    return {
        isAuthenticated: raw.isAuthenticated === true,
        pageKey: String(raw.pageKey),
        pageWords: toCount(raw.pageWords),
        chapterWords: toCount(raw.chapterWords),
        wordsBeforePage: toCount(raw.wordsBeforePage),
        chapterStartWords: toCount(raw.chapterStartWords),
        bookWords: toCount(raw.bookWords)
    };
}
