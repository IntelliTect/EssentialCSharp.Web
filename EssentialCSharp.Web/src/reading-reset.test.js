import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { READING_RESET_CHANNEL, READING_RESET_URL } from "./utils/readingConstants.js";
import { installFakeBroadcastChannel } from "./test-utils/fakeBroadcastChannel.js";

describe("reading-reset (Manage Profile button)", () => {
    let fetchMock;
    let confirmMock;
    let received;
    let button;
    let status;

    // The module wires itself up on import, so load a fresh copy against each test's DOM.
    async function load() {
        vi.resetModules();
        await import("./reading-reset.js");
    }
    const click = () => button.dispatchEvent(new Event("click"));
    const settled = () => vi.waitFor(() => expect(button.disabled).toBe(false));

    beforeEach(() => {
        document.head.innerHTML = '<meta name="csrf-token" content="tok">';
        document.body.innerHTML =
            '<button id="reset-reading-speed-button" data-confirm="Really reset?">Reset</button>'
            + '<span id="reset-reading-status" class="text-muted"></span>';
        button = document.getElementById("reset-reading-speed-button");
        status = document.getElementById("reset-reading-status");

        confirmMock = vi.fn(() => true);
        vi.stubGlobal("confirm", confirmMock);
        window.confirm = confirmMock;
        fetchMock = vi.fn(async () => new Response(null, { status: 200 }));
        vi.stubGlobal("fetch", fetchMock);

        installFakeBroadcastChannel(vi);
        received = [];
        const listener = new BroadcastChannel(READING_RESET_CHANNEL);
        listener.onmessage = (event) => received.push(event.data);
    });

    afterEach(() => {
        document.body.innerHTML = "";
        document.head.innerHTML = "";
        vi.unstubAllGlobals();
        vi.restoreAllMocks();
    });

    it("sends no request when the confirmation is declined", async () => {
        confirmMock.mockReturnValue(false);
        await load();
        click();
        await Promise.resolve();
        expect(confirmMock).toHaveBeenCalledWith("Really reset?");
        expect(fetchMock).not.toHaveBeenCalled();
        expect(status.textContent).toBe("");
        expect(received).toHaveLength(0);
    });

    it("shows a success message and tells other tabs when the reset succeeds", async () => {
        await load();
        click();
        await settled();
        expect(fetchMock.mock.calls[0][0]).toBe(READING_RESET_URL);
        expect(status.textContent).toMatch(/success/i);
        expect(status.classList.contains("text-success")).toBe(true);
        expect(status.classList.contains("text-muted")).toBe(false);
        expect(received).toEqual([{ type: "reset" }]);
    });

    it("shows an error and does not broadcast when the server fails", async () => {
        fetchMock.mockResolvedValue(new Response(null, { status: 503 }));
        await load();
        click();
        await settled();
        expect(status.textContent).toMatch(/failed/i);
        expect(status.classList.contains("text-danger")).toBe(true);
        expect(received).toHaveLength(0);
    });

    it("tells the user to sign in again when the session has expired", async () => {
        fetchMock.mockResolvedValue(new Response(null, { status: 401 }));
        await load();
        click();
        await settled();
        expect(status.textContent).toMatch(/sign in/i);
        expect(received).toHaveLength(0);
    });

    it("shows an error when the request throws", async () => {
        fetchMock.mockRejectedValue(new TypeError("offline"));
        await load();
        click();
        await settled();
        expect(status.classList.contains("text-danger")).toBe(true);
        expect(received).toHaveLength(0);
    });

    it("disables the button while the request is pending", async () => {
        let finish;
        fetchMock.mockReturnValue(new Promise((resolve) => { finish = () => resolve(new Response(null, { status: 200 })); }));
        await load();
        click();
        await vi.waitFor(() => expect(button.disabled).toBe(true));
        finish();
        await settled();
    });
});
