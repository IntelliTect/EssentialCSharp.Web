/**
 * Scroll progress measured against the article/content element rather than the
 * whole document, so header/footer/chrome do not skew it.
 */

const clamp01 = (n) => Math.min(1, Math.max(0, Number.isFinite(n) ? n : 0));

/**
 * @typedef {Object} ContentMeasure
 * @property {number} top Distance from viewport top to content top (px, may be negative).
 * @property {number} height Content height (px).
 * @property {number} viewportHeight
 */

/**
 * Fraction of the content that has entered the viewport (everything above the
 * viewport bottom). Used for crediting words read.
 * @param {ContentMeasure} m
 */
export function fractionAboveViewportBottom({ top, height, viewportHeight }) {
    if (!(height > 0)) return 0;
    return clamp01((viewportHeight - top) / height);
}

/**
 * Fraction of the content that has scrolled above the viewport top. Used as the
 * credit baseline: content already scrolled past on load (deep links, restored
 * scroll) is never credited, but the first screen is.
 * @param {ContentMeasure} m
 */
export function fractionAboveViewportTop({ top, height }) {
    if (!(height > 0)) return 0;
    return clamp01(-top / height);
}

/**
 * Classic 0..1 "how far through the article" for display. Content that fits in
 * the viewport reports 0, not 1.
 * @param {ContentMeasure} m
 */
export function displayFraction({ top, height, viewportHeight }) {
    const scrollable = height - viewportHeight;
    if (!(scrollable > 0)) return 0;
    return clamp01(-top / scrollable);
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
 * Words credited for an interval: progress-based, capped by what is plausible
 * for the active time spent.
 * @param {number} pageWords
 * @param {number} progress fraction of the page progressed this interval (0..1)
 * @param {number} activeSeconds
 * @param {number} maxWpm
 */
export function creditWords(pageWords, progress, activeSeconds, maxWpm) {
    const byProgress = Math.round(pageWords * Math.max(0, progress));
    const byTime = Math.floor(Math.max(0, activeSeconds) * maxWpm / 60);
    return Math.max(0, Math.min(byProgress, byTime));
}
