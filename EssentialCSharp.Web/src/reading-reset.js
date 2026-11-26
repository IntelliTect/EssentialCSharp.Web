/**
 * Behavior for the "Reset reading speed" button on the Manage Profile page.
 * Loaded as its own Vite entry (no inline script, CSP-safe).
 */
import { STATUS_AUTH_FAILURE, STATUS_OK, resetProfile } from "./services/readingApi.js";
import {
    READING_RESET_CHANNEL,
    RESET_MESSAGE_FAILED,
    RESET_MESSAGE_RESET,
    RESET_MESSAGE_RESETTING
} from "./utils/readingConstants.js";
import { getUserId } from "./utils/readingContext.js";
import { clearStoredIntervals } from "./utils/readingOutboxStorage.js";

const STATUS_CLASSES = ["text-muted", "text-success", "text-danger"];

// The status element is an always-present (never hidden) live region, so
// setting its text is announced.
function showStatus(element, message, tone) {
    element.textContent = message;
    element.classList.remove(...STATUS_CLASSES);
    element.classList.add(tone);
}

/**
 * Tells other open tabs (content pages) about the reset: pause sends before it, then drop their
 * cached WPM and unsent data when it succeeds, or resume with their data when it fails.
 */
function broadcast(type) {
    if (typeof BroadcastChannel !== "function") return;
    const channel = new BroadcastChannel(READING_RESET_CHANNEL);
    channel.postMessage({ type });
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
        // Pause other tabs' sends first, so none can reach the server after the reset and re-create the profile.
        broadcast(RESET_MESSAGE_RESETTING);
        // resetProfile reports failures as a status and never throws.
        const result = await resetProfile();
        button.disabled = false;
        if (result.status === STATUS_OK) {
            // Unsent data of tabs that are no longer open. Only this account's: others may share the browser.
            const userId = getUserId(window);
            if (userId) clearStoredIntervals(userId);
            broadcast(RESET_MESSAGE_RESET);
            showStatus(status, "Reading speed reset successfully.", "text-success");
            return;
        }
        broadcast(RESET_MESSAGE_FAILED); // nothing was cleared: other tabs resume with their data
        const message = result.status === STATUS_AUTH_FAILURE
            ? "Your session has expired. Please sign in again."
            : "Reset failed. Please try again.";
        showStatus(status, message, "text-danger");
    });
}

init();
