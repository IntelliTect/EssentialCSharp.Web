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

/** A non-keepalive request still pending after this long is aborted and treated as transient (ms). */
export const SEND_TIMEOUT_MS = 60 * 1000;

/** Cross-tab notification that the reading profile was reset, and the message sent on it. */
export const READING_RESET_CHANNEL = "essentialcsharp-reading-reset";
export const RESET_MESSAGE = "reset";

export const READING_SESSION_URL = "/api/reading/session";
export const READING_RESET_URL = "/api/reading/reset";
