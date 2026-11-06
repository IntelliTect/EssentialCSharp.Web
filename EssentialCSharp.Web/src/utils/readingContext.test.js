import { describe, expect, it } from "vitest";
import { getReadingContext } from "./readingContext.js";

const complete = () => ({
    isAuthenticated: true,
    pageKey: "chapter-1/page-2",
    pageWords: 1200,
    chapterWords: 5000,
    wordsBeforePage: 300,
    chapterStartWords: 100,
    bookWords: 90000,
    wpm: 240,
    defaultWpm: 200,
    maxCreditWpm: 800,
    idleThresholdSeconds: 300,
    periodicFlushActiveSeconds: 60
});

describe("getReadingContext", () => {
    it("returns null when there is no context (not a content page)", () => {
        expect(getReadingContext({})).toBeNull();
    });

    it("reads the server-provided values", () => {
        expect(getReadingContext({ READING_CONTEXT: complete() })).toEqual(complete());
    });

    it("uses null wpm when the server has none", () => {
        const raw = { ...complete(), wpm: null };
        expect(getReadingContext({ READING_CONTEXT: raw }).wpm).toBeNull();
    });

    it.each(["defaultWpm", "maxCreditWpm", "idleThresholdSeconds", "periodicFlushActiveSeconds"])(
        "is disabled (null) when %s is missing: there are no client fallbacks", (field) => {
            const raw = complete();
            delete raw[field];
            expect(getReadingContext({ READING_CONTEXT: raw })).toBeNull();
        });

    it("is disabled when a tuning value is not a positive number", () => {
        expect(getReadingContext({ READING_CONTEXT: { ...complete(), maxCreditWpm: 0 } })).toBeNull();
        expect(getReadingContext({ READING_CONTEXT: { ...complete(), idleThresholdSeconds: "300" } })).toBeNull();
    });

    it("coerces bad word counts to 0", () => {
        const ctx = getReadingContext({ READING_CONTEXT: { ...complete(), pageWords: "x", bookWords: -5 } });
        expect(ctx.pageWords).toBe(0);
        expect(ctx.bookWords).toBe(0);
    });
});
