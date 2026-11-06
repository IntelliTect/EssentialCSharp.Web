/** Cold-start / anonymous words-per-minute used for estimates. */
export const DEFAULT_WPM = 200;

/** No real input for this long switches READING to IDLE (seconds). */
export const IDLE_THRESHOLD_S = 300;

/** Fraction of the page (viewport bottom) at which a page counts as completed. */
export const COMPLETION_THRESHOLD = 0.95;

/** Tick period of the active-time timer (ms). */
export const TICK_INTERVAL_MS = 1000;

/** Longest elapsed time one tick may add (s); guards against throttled timers and sleep/resume. */
export const MAX_TICK_DELTA_S = 2;

/** Minimum gap between mousemove events treated as input (ms). */
export const MOUSEMOVE_THROTTLE_MS = 1000;

/**
 * Plausibility cap only: words credited for an interval never exceed
 * activeSeconds * MAX_CREDIT_WPM / 60. The server enforces its own limits.
 */
export const MAX_CREDIT_WPM = 800;

/** While the page stays visible and active, flush at least this often (active seconds). */
export const PERIODIC_FLUSH_ACTIVE_S = 60;

export const READING_PROFILE_URL = "/api/reading/profile";
export const READING_SESSION_URL = "/api/reading/session";
export const READING_RESET_URL = "/api/reading/reset";
