/**
 * Behavior for the "Reset reading speed" button on the Manage Profile page.
 * Loaded as its own Vite entry (no inline script, CSP-safe).
 */
import { STATUS_AUTH_FAILURE, STATUS_OK, resetProfile } from "./services/readingApi.js";

const STATUS_CLASSES = ["text-muted", "text-success", "text-danger"];

function showStatus(element, message, tone) {
    element.hidden = false;
    element.textContent = message;
    element.classList.remove(...STATUS_CLASSES);
    element.classList.add(tone);
}

function init() {
    const button = document.getElementById("reset-reading-speed-button");
    const status = document.getElementById("reset-reading-status");
    if (!button || !status) return;

    button.addEventListener("click", async () => {
        if (!window.confirm(button.dataset.confirm)) return;

        button.disabled = true;
        showStatus(status, "Resetting…", "text-muted");

        const result = await resetProfile();
        if (result.status === STATUS_OK) {
            showStatus(status, "Reading speed reset successfully.", "text-success");
            return;
        }

        const message = result.status === STATUS_AUTH_FAILURE
            ? "Your session has expired. Please sign in again."
            : "Reset failed. Please try again.";
        showStatus(status, message, "text-danger");
        button.disabled = false;
    });
}

init();
