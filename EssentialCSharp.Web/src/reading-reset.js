/**
 * Behavior for the "Reset reading speed" button on the Manage Profile page.
 * Loaded as its own Vite entry (no inline script, CSP-safe).
 */
import { STATUS_AUTH_FAILURE, STATUS_OK, resetProfile } from "./services/readingApi.js";
import { READING_RESET_CHANNEL } from "./utils/readingConstants.js";
import { clearStoredIntervals } from "./utils/readingOutboxStorage.js";

const STATUS_CLASSES = ["text-muted", "text-success", "text-danger"];

// The status element is an always-present (never hidden) live region, so
// setting its text is announced.
function showStatus(element, message, tone) {
    element.textContent = message;
    element.classList.remove(...STATUS_CLASSES);
    element.classList.add(tone);
}

/** Tells other open tabs (content pages) to drop their cached WPM and unsent data. */
function broadcastReset() {
    if (typeof BroadcastChannel !== "function") return;
    const channel = new BroadcastChannel(READING_RESET_CHANNEL);
    channel.postMessage({ type: "reset" });
    channel.close();
}

function init() {
    const button = document.getElementById("reset-reading-speed-button");
    const status = document.getElementById("reset-reading-status");
    if (!button || !status) return;

    button.addEventListener("click", async () => {
        if (!window.confirm(button.dataset.confirm)) return;

        button.disabled = true;
        showStatus(status, "Resetting…", "text-muted");
        try {
            const result = await resetProfile();
            if (result.status === STATUS_OK) {
                clearStoredIntervals(); // unsent data of tabs that are no longer open
                broadcastReset();
                showStatus(status, "Reading speed reset successfully.", "text-success");
                return;
            }
            const message = result.status === STATUS_AUTH_FAILURE
                ? "Your session has expired. Please sign in again."
                : "Reset failed. Please try again.";
            showStatus(status, message, "text-danger");
        } catch {
            showStatus(status, "Reset failed. Please try again.", "text-danger");
        } finally {
            button.disabled = false;
        }
    });
}

init();
