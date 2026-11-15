import { describe, expect, it } from "vitest";
import { formatMinutes, formatMinutesSpoken, minutesForWords } from "./readingFormat.js";

describe("formatMinutes", () => {
    it.each([
        [0, "less than a minute"],
        [0.4, "less than a minute"],
        [1, "1 min"],
        [12.4, "12 min"],
        [59.4, "59 min"],
        [60, "1h"],
        [125, "2h 5m"]
    ])("formats %s minutes as %s", (minutes, expected) => {
        expect(formatMinutes(minutes)).toBe(expected);
    });

    it.each([null, undefined, NaN, -1, Infinity, "5"])("returns null for invalid input %s", (v) => {
        expect(formatMinutes(v)).toBeNull();
    });
});

describe("formatMinutesSpoken", () => {
    it.each([
        [0.2, "less than a minute"],
        [1, "about 1 minute"],
        [12, "about 12 minutes"],
        [60, "about 1 hour"],
        [125, "about 2 hours 5 minutes"],
        [61, "about 1 hour 1 minute"]
    ])("speaks %s minutes as %s", (minutes, expected) => {
        expect(formatMinutesSpoken(minutes)).toBe(expected);
    });

    it("returns null for invalid input", () => {
        expect(formatMinutesSpoken(NaN)).toBeNull();
    });
});

describe("minutesForWords", () => {
    it("divides words by words-per-minute", () => {
        expect(minutesForWords(1000, 200)).toBe(5);
    });

    it("never goes negative", () => {
        expect(minutesForWords(-50, 200)).toBe(0);
    });

    it("is null when wpm is unusable", () => {
        expect(minutesForWords(1000, 0)).toBeNull();
        expect(minutesForWords(1000, NaN)).toBeNull();
    });
});
