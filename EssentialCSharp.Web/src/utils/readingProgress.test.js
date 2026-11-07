import { describe, expect, it } from "vitest";
import {
    creditInterval,
    fractionAboveViewportBottom,
    fractionAboveViewportTop
} from "./readingProgress.js";

describe("progress fraction (viewport bottom edge)", () => {
    it("counts the first screen at the top of the page", () => {
        // 1000px content starting at the viewport top, 500px viewport: half has been on screen.
        expect(fractionAboveViewportBottom({ top: 0, height: 1000, viewportHeight: 500 })).toBe(0.5);
    });

    it("reaches 1 once the content bottom reaches the viewport bottom", () => {
        expect(fractionAboveViewportBottom({ top: -500, height: 1000, viewportHeight: 500 })).toBe(1);
    });

    it("is 1 for content shorter than the viewport and 0 for empty content", () => {
        expect(fractionAboveViewportBottom({ top: 0, height: 100, viewportHeight: 500 })).toBe(1);
        expect(fractionAboveViewportBottom({ top: 0, height: 0, viewportHeight: 500 })).toBe(0);
    });

    it("is 0 while the content is still below the viewport", () => {
        expect(fractionAboveViewportBottom({ top: 800, height: 1000, viewportHeight: 500 })).toBe(0);
    });
});

describe("credit baseline (viewport top edge)", () => {
    it("is the share of content already scrolled past", () => {
        expect(fractionAboveViewportTop({ top: -250, height: 1000, viewportHeight: 500 })).toBe(0.25);
    });

    it("is 0 when the content top is on screen", () => {
        expect(fractionAboveViewportTop({ top: 40, height: 1000, viewportHeight: 500 })).toBe(0);
    });
});

describe("creditInterval", () => {
    it("credits words for the progress made when time allows", () => {
        // 2000-word page, a quarter read in 60s with an 800 wpm cap (800 words allowed).
        const r = creditInterval({ pageWords: 2000, progress: 0.25, activeSeconds: 60, maxWpm: 800 });
        expect(r.wordsRead).toBe(500);
        expect(r.progressConsumed).toBe(0.25);
    });

    it("limits credit to what the time could plausibly cover and keeps the remainder", () => {
        // Half a 2000-word page in 6s at an 800 wpm cap => only 80 words; 4% of the page consumed.
        const r = creditInterval({ pageWords: 2000, progress: 0.5, activeSeconds: 6, maxWpm: 800 });
        expect(r.wordsRead).toBe(80);
        expect(r.progressConsumed).toBeCloseTo(0.04, 10);
    });

    it("credits nothing without progress or without time", () => {
        expect(creditInterval({ pageWords: 2000, progress: 0, activeSeconds: 60, maxWpm: 800 }).wordsRead).toBe(0);
        expect(creditInterval({ pageWords: 2000, progress: 0.5, activeSeconds: 0, maxWpm: 800 }).wordsRead).toBe(0);
    });

    it("treats negative progress as none", () => {
        expect(creditInterval({ pageWords: 2000, progress: -0.3, activeSeconds: 60, maxWpm: 800 }).wordsRead).toBe(0);
    });
});
