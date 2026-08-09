/**
 * useReadingTracker — Kindle-style active-reading time tracker.
 *
 * State machine: READING → IDLE (after IDLE_THRESHOLD s of no input)
 *                        → HIDDEN (tab not visible)
 * Only accumulates activeSeconds while in READING state.
 *
 * Persists a rolling aggregate to:
 *   - Server (POST /api/reading/session) when authenticated.
 *   - localStorage key "readingProfile" for anonymous users and as a backup.
 *
 * On first authenticated page load, uploads the localStorage aggregate to the
 * server and clears local storage (one-time sync after login).
 */

import { ref, onMounted, onBeforeUnmount, readonly } from "vue";

// ----- Constants -----
const IDLE_THRESHOLD_S = 300; // 5 minutes
const TICK_INTERVAL_MS = 1000; // 1 second timer
const DEFAULT_WPM = 200; // cold-start display default
const MOUSEMOVE_THROTTLE_MS = 1000;
const LOCAL_STORAGE_KEY = "readingProfile";

// ----- States -----
const STATE_READING = "READING";
const STATE_IDLE = "IDLE";
const STATE_HIDDEN = "HIDDEN";

// ----- localStorage helpers -----

function loadLocalProfile() {
    try {
        const raw = localStorage.getItem(LOCAL_STORAGE_KEY);
        if (!raw) return { totalWords: 0, totalActiveSeconds: 0 };
        return JSON.parse(raw);
    } catch {
        return { totalWords: 0, totalActiveSeconds: 0 };
    }
}

function saveLocalProfile(profile) {
    try {
        localStorage.setItem(LOCAL_STORAGE_KEY, JSON.stringify(profile));
    } catch {
        // Ignore storage errors (private browsing quota exceeded, etc.)
    }
}

function clearLocalProfile() {
    try {
        localStorage.removeItem(LOCAL_STORAGE_KEY);
    } catch {
        // Ignore
    }
}

// ----- API helpers -----

async function fetchServerProfile() {
    try {
        const res = await fetch("/api/reading/profile");
        if (!res.ok) return null;
        return await res.json();
    } catch {
        return null;
    }
}

async function postSession(intervals) {
    if (!intervals || intervals.length === 0) return null;
    try {
        const res = await fetch("/api/reading/session", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(intervals)
        });
        if (!res.ok) return null;
        return await res.json();
    } catch {
        return null;
    }
}

// ----- Composable -----

export function useReadingTracker() {
    // Reactive WPM shown in the UI.
    const wpm = ref(DEFAULT_WPM);
    const activeSeconds = ref(0);

    // Reading state machine.
    let state = STATE_READING;
    let lastActivityAt = Date.now();
    let tickIntervalId = null;
    let lastMousemoveAt = 0;

    // Scroll tracking.
    let maxScrollFraction = 0;

    // Page context (from window globals set by _Layout.cshtml).
    const pageKey = window.CURRENT_PAGE_KEY ?? null;
    const pageWordCount = window.PAGE_WORD_COUNT ?? 0;
    const isAuthenticated = Boolean(window.IS_AUTHENTICATED);

    // In-session server profile for reference WPM (loaded on mount).
    let serverProfile = null;

    // ----- Scroll fraction -----

    function getScrollFraction() {
        const main = document.querySelector("main") ?? document.documentElement;
        const scrollTop = window.scrollY || document.documentElement.scrollTop;
        const scrollable = main.scrollHeight - main.clientHeight;
        if (scrollable <= 0) return 1;
        return Math.min(1, Math.max(0, scrollTop / scrollable));
    }

    // ----- State machine -----

    function markActivity() {
        lastActivityAt = Date.now();
        if (state === STATE_IDLE || state === STATE_HIDDEN) {
            state = STATE_READING;
        }
    }

    function onVisibilityChange() {
        if (document.visibilityState !== "visible") {
            state = STATE_HIDDEN;
        } else {
            // Tab became visible — READING if recently active, else IDLE.
            const idleDuration = (Date.now() - lastActivityAt) / 1000;
            state = idleDuration <= IDLE_THRESHOLD_S ? STATE_READING : STATE_IDLE;
        }
    }

    function onMousemove() {
        const now = Date.now();
        if (now - lastMousemoveAt >= MOUSEMOVE_THROTTLE_MS) {
            lastMousemoveAt = now;
            markActivity();
        }
    }

    function onScroll() {
        markActivity();
        const fraction = getScrollFraction();
        if (fraction > maxScrollFraction) {
            maxScrollFraction = fraction;
        }
    }

    // ----- Tick -----

    function tick() {
        const now = Date.now();
        const idleDuration = (now - lastActivityAt) / 1000;

        if (state === STATE_READING && idleDuration > IDLE_THRESHOLD_S) {
            state = STATE_IDLE;
        }

        if (state === STATE_READING) {
            activeSeconds.value++;
        }
    }

    // ----- Flush (send data on navigation away / unmount) -----

    async function flush() {
        if (!pageKey || activeSeconds.value <= 0 || pageWordCount <= 0) return;

        const wordsRead = Math.round(pageWordCount * maxScrollFraction);
        const scrollFraction = maxScrollFraction;
        // Near-bottom (within 5%) = completed.
        const completed = scrollFraction >= 0.95;

        const interval = {
            pageKey,
            activeSeconds: activeSeconds.value,
            wordsRead,
            completed
        };

        if (isAuthenticated) {
            const updated = await postSession([interval]);
            if (updated) {
                serverProfile = updated;
                if (updated.wpm) {
                    wpm.value = updated.wpm;
                }
            }
        } else {
            // Update localStorage profile.
            const local = loadLocalProfile();
            local.totalWords = (local.totalWords || 0) + wordsRead;
            local.totalActiveSeconds = (local.totalActiveSeconds || 0) + activeSeconds.value;
            saveLocalProfile(local);

            const localWpm = local.totalActiveSeconds > 0
                ? local.totalWords / (local.totalActiveSeconds / 60)
                : 0;
            if (localWpm > 0) {
                wpm.value = localWpm;
            }
        }
    }

    // ----- One-time sync: localStorage → server on first authenticated load -----

    async function syncLocalToServer() {
        if (!isAuthenticated) return;

        const local = loadLocalProfile();
        if (!local || (local.totalWords === 0 && local.totalActiveSeconds === 0)) return;

        // Only upload if server has no data yet.
        const profile = await fetchServerProfile();
        if (profile && (profile.totalWordsRead > 0 || profile.totalActiveSeconds > 0)) {
            // Server already has data — just clear local.
            clearLocalProfile();
            return;
        }

        // Upload local aggregate as a synthetic interval.
        if (local.totalWords > 0 && local.totalActiveSeconds > 0) {
            await postSession([{
                pageKey: "__localStorage_sync__",
                activeSeconds: local.totalActiveSeconds,
                wordsRead: local.totalWords,
                completed: false
            }]);
        }
        clearLocalProfile();
    }

    // ----- Init -----

    onMounted(async () => {
        // Load initial WPM.
        if (isAuthenticated) {
            // Try to sync localStorage first (one-time, idempotent).
            await syncLocalToServer();

            serverProfile = await fetchServerProfile();
            if (serverProfile?.wpm) {
                wpm.value = serverProfile.wpm;
            }
        } else {
            const local = loadLocalProfile();
            const localWpm = local.totalActiveSeconds > 0
                ? local.totalWords / (local.totalActiveSeconds / 60)
                : 0;
            if (localWpm > 0) {
                wpm.value = localWpm;
            }
        }

        // Start tick.
        tickIntervalId = setInterval(tick, TICK_INTERVAL_MS);

        // Activity listeners.
        document.addEventListener("visibilitychange", onVisibilityChange);
        document.addEventListener("mousemove", onMousemove, { passive: true });
        document.addEventListener("keydown", markActivity);
        document.addEventListener("scroll", onScroll, { passive: true });
        document.addEventListener("touchstart", markActivity, { passive: true });
        document.addEventListener("pointerdown", markActivity);
        window.addEventListener("focus", markActivity);

        // Flush on page navigation (SPA or classical).
        window.addEventListener("beforeunload", flush);
    });

    onBeforeUnmount(async () => {
        clearInterval(tickIntervalId);

        document.removeEventListener("visibilitychange", onVisibilityChange);
        document.removeEventListener("mousemove", onMousemove);
        document.removeEventListener("keydown", markActivity);
        document.removeEventListener("scroll", onScroll);
        document.removeEventListener("touchstart", markActivity);
        document.removeEventListener("pointerdown", markActivity);
        window.removeEventListener("focus", markActivity);
        window.removeEventListener("beforeunload", flush);

        await flush();
    });

    return {
        wpm: readonly(wpm),
        activeSeconds: readonly(activeSeconds)
    };
}
