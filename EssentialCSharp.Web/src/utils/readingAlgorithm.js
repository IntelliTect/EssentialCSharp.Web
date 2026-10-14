// Keep these rules aligned with ReadingController.PostSession.
export const MAX_WPM_HARD_CUTOFF = 900;
export const SLOW_OUTLIER_FACTOR = 0.25;

export function deriveWpm(totalWords, totalActiveSeconds) {
    return totalWords > 0 && totalActiveSeconds > 0
        ? totalWords / (totalActiveSeconds / 60)
        : 0;
}

/**
 * Applies the reading-speed outlier rules to one interval.
 * Returns null when the interval must be discarded; otherwise returns the
 * words and seconds that should be added to the aggregate profile.
 */
export function applyOutlierRules(wordsRead, activeSeconds, referenceWpm) {
    if (activeSeconds <= 0 || wordsRead <= 0) return null;

    const intervalWpm = deriveWpm(wordsRead, activeSeconds);
    if (intervalWpm > MAX_WPM_HARD_CUTOFF) return null;

    let effectiveSeconds = activeSeconds;
    if (referenceWpm > 0 && intervalWpm < SLOW_OUTLIER_FACTOR * referenceWpm) {
        effectiveSeconds = Math.max(
            1,
            Math.ceil(wordsRead / (SLOW_OUTLIER_FACTOR * referenceWpm) * 60));
    }

    return { words: wordsRead, seconds: effectiveSeconds };
}

export function getSessionWordsRead(pageWordCount, startScrollFraction, maxScrollFraction) {
    const progress = Math.max(0, maxScrollFraction - startScrollFraction);
    return Math.round(pageWordCount * progress);
}
