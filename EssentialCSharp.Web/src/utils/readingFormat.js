const MINUTES_PER_HOUR = 60;

function isValidMinutes(minutes) {
    return typeof minutes === "number" && Number.isFinite(minutes) && minutes >= 0;
}

/**
 * Compact label, e.g. "less than a minute", "12 min", "2h 5m". Null for invalid input.
 * @param {number | null | undefined} minutes
 * @returns {string | null}
 */
export function formatMinutes(minutes) {
    if (!isValidMinutes(minutes)) return null;
    const rounded = Math.round(minutes);
    if (rounded < 1) return "less than a minute";
    if (rounded < MINUTES_PER_HOUR) return `${rounded} min`;
    const hours = Math.floor(rounded / MINUTES_PER_HOUR);
    const mins = rounded % MINUTES_PER_HOUR;
    return mins > 0 ? `${hours}h ${mins}m` : `${hours}h`;
}

const plural = (n, unit) => `${n} ${unit}${n === 1 ? "" : "s"}`;

/**
 * Spoken wording for screen readers, e.g. "about 12 minutes".
 * @param {number | null | undefined} minutes
 * @returns {string | null}
 */
export function formatMinutesSpoken(minutes) {
    if (!isValidMinutes(minutes)) return null;
    const rounded = Math.round(minutes);
    if (rounded < 1) return "less than a minute";
    if (rounded < MINUTES_PER_HOUR) return `about ${plural(rounded, "minute")}`;
    const hours = Math.floor(rounded / MINUTES_PER_HOUR);
    const mins = rounded % MINUTES_PER_HOUR;
    return mins > 0
        ? `about ${plural(hours, "hour")} ${plural(mins, "minute")}`
        : `about ${plural(hours, "hour")}`;
}

/**
 * @param {number} wordsLeft
 * @param {number} wpm
 * @returns {number | null} minutes, or null when it cannot be computed
 */
export function minutesForWords(wordsLeft, wpm) {
    if (!Number.isFinite(wordsLeft) || !Number.isFinite(wpm) || wpm <= 0) return null;
    return Math.max(0, wordsLeft) / wpm;
}
