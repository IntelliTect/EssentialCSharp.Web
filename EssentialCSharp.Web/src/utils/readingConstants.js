// Tuning values (default WPM, credit cap, idle threshold, flush period) have ONE
// source: the server's ReadingOptions, delivered through window.READING_CONTEXT.
// Only client-mechanics constants live here.

/** Tick period of the active-time timer (ms). */
export const TICK_INTERVAL_MS = 1000;

/** Longest elapsed time one tick may add (s); guards against throttled timers and sleep/resume. */
export const MAX_TICK_DELTA_S = 2;

/** Minimum gap between mousemove events treated as input (ms). */
export const MOUSEMOVE_THROTTLE_MS = 1000;

/** Retry backoff after a transient failure: base * 2^(failures-1), capped (ms). */
export const RETRY_BASE_DELAY_MS = 5000;
export const RETRY_MAX_DELAY_MS = 120000;

/**
 * Unsent intervals kept while the server is unreachable; the oldest are dropped beyond this.
 * Must stay within the server's MaxIntervalsPerRequest (enforced by its options validator).
 */
export const MAX_QUEUED_INTERVALS = 20;

/**
 * How long a persisted batch being sent is reserved for its tab before another tab may take
 * it over. A periodic request is aborted after half of it, so a hung request never outlives
 * its lease.
 */
export const LEASE_MS = 2 * 60 * 1000;
export const SEND_TIMEOUT_MS = LEASE_MS / 2;

/** Cross-tab notification that the reading profile was reset. */
export const READING_RESET_CHANNEL = "essentialcsharp-reading-reset";

export const READING_SESSION_URL = "/api/reading/session";
export const READING_RESET_URL = "/api/reading/reset";
