/**
 * Deterministic in-memory BroadcastChannel: postMessage delivers synchronously to
 * every other open instance with the same name (like the real one, never to the
 * sender). Avoids real timers/event-loop waits in tests.
 */
export function installFakeBroadcastChannel(vi) {
    const open = new Set();
    class FakeBroadcastChannel {
        constructor(name) {
            this.name = name;
            this.onmessage = null;
            open.add(this);
        }
        postMessage(data) {
            for (const other of open) {
                if (other !== this && other.name === this.name) other.onmessage?.({ data });
            }
        }
        close() { open.delete(this); }
    }
    vi.stubGlobal("BroadcastChannel", FakeBroadcastChannel);
    return { openCount: () => open.size };
}
