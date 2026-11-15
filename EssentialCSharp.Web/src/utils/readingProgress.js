/**
 * Scroll progress measured against the article/content element rather than the
 * whole document, so header/footer/chrome do not skew it.
 *
 * Single definition of "progress": the fraction of the content at or above the
 * viewport BOTTOM edge (content the reader has had on screen). Both the header
 * estimate (words left) and the credit model use it, so what is displayed and
 * what is credited cannot disagree. The credit baseline is the same quantity
 * measured at the viewport TOP when the page loaded (content already scrolled
 * past is never credited, the first screen is).
 *
 * Limitation: the content element includes non-prose blocks (code, images), so
 * the fraction is by height, not by words.
 */

const clamp01 = (n) => Math.min(1, Math.max(0, Number.isFinite(n) ? n : 0));

/**
 * @typedef {Object} ContentMeasure
 * @property {number} top Distance from viewport top to content top (px, may be negative).
 * @property {number} height Content height (px).
 * @property {number} viewportHeight
 */

/**
 * Progress: fraction of the content that has entered the viewport.
 * @param {ContentMeasure} m
 */
export function fractionAboveViewportBottom({ top, height, viewportHeight }) {
    if (!(height > 0)) return 0;
    return clamp01((viewportHeight - top) / height);
}

/**
 * Fraction of the content that has scrolled above the viewport top; the credit
 * baseline at load time.
 * @param {ContentMeasure} m
 */
export function fractionAboveViewportTop({ top, height }) {
    if (!(height > 0)) return 0;
    return clamp01(-top / height);
}

/**
 * Finds the element whose extent represents the readable content.
 * Prefers an explicit [data-reading-content] marker, then the layout container.
 * @param {Document} [doc]
 * @returns {Element | null}
 */
export function findContentElement(doc = document) {
    return doc.querySelector("[data-reading-content]")
        ?? doc.querySelector("#site-layout > .container")
        ?? doc.getElementById("site-layout");
}

/**
 * @param {Element | null} element
 * @returns {ContentMeasure}
 */
export function measureContent(element) {
    const viewportHeight = window.innerHeight;
    if (!element) return { top: 0, height: 0, viewportHeight };
    const { top, height } = element.getBoundingClientRect();
    return { top, height, viewportHeight };
}

/**
 * Credit for one interval: progress-based, capped by what is plausible for the
 * active time spent. When time is the binding limit only the matching share of
 * progress is consumed, so the remainder stays creditable later.
 * @param {{ pageWords: number, progress: number, activeSeconds: number, maxWpm: number }} input
 *        progress: fraction of the page progressed since the last credited point (0..1)
 * @returns {{ wordsRead: number, progressConsumed: number }}
 */
export function creditInterval({ pageWords, progress, activeSeconds, maxWpm }) {
    const available = Math.max(0, progress);
    const byProgress = Math.round(pageWords * available);
    const byTime = Math.floor(Math.max(0, activeSeconds) * maxWpm / 60);
    const wordsRead = Math.max(0, Math.min(byProgress, byTime));
    const progressConsumed = byProgress <= byTime || !(pageWords > 0)
        ? available
        : wordsRead / pageWords;
    return { wordsRead, progressConsumed };
}
