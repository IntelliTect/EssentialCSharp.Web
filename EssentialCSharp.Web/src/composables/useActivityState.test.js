import { describe, expect, it } from "vitest";
import { computeActivityState } from "./useActivityState.js";

const base = { visible: true, focused: true, lastInputAt: 0, now: 0, idleThresholdSeconds: 300 };

describe("computeActivityState", () => {
    it("is READING shortly after input in a visible, focused page", () => {
        expect(computeActivityState({ ...base, now: 10_000 })).toBe("READING");
    });

    it("is still READING exactly at the idle threshold", () => {
        expect(computeActivityState({ ...base, now: 300_000 })).toBe("READING");
    });

    it("is IDLE after the idle threshold without input", () => {
        expect(computeActivityState({ ...base, now: 300_001 })).toBe("IDLE");
    });

    it("honors the threshold it is given", () => {
        expect(computeActivityState({ ...base, idleThresholdSeconds: 30, now: 31_000 })).toBe("IDLE");
    });

    it("is IDLE before any input", () => {
        expect(computeActivityState({ ...base, lastInputAt: null, now: 1000 })).toBe("IDLE");
    });

    it("is HIDDEN when not visible or not focused, regardless of input", () => {
        expect(computeActivityState({ ...base, visible: false })).toBe("HIDDEN");
        expect(computeActivityState({ ...base, focused: false })).toBe("HIDDEN");
    });
});
