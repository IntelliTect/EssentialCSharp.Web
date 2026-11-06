import { describe, expect, it } from "vitest";
import { getReadingContext } from "./readingContext.js";

// NOTE: this fixture is hand-written, so it cannot detect drift in what the server emits.
// The server side of the contract is owned by a C# contract test. Here we only make sure
// the parser and this fixture cannot silently drift apart (see the key-set test below).
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

const raw = (overrides = {}) => ({ READING_CONTEXT: { ...complete(), ...overrides } });

describe("getReadingContext", () => {
    it("returns null when there is no context (not a content page)", () => {
        expect(getReadingContext({})).toBeNull();
    });

    it("reads the server-provided values", () => {
        expect(getReadingContext(raw())).toEqual(complete());
    });

    it("parses exactly the fields of the fixture: a field added to one side but not the other fails here", () => {
        expect(Object.keys(getReadingContext(raw())).sort()).toEqual(Object.keys(complete()).sort());
    });

    it("uses null wpm when the server has none", () => {
        expect(getReadingContext(raw({ wpm: null })).wpm).toBeNull();
    });

    // Derived from the fixture, so every field is covered automatically: a missing field
    // must either disable the feature (null) or degrade without throwing.
    it.each(Object.keys(complete()))("handles %s being missing without throwing", (field) => {
        const context = complete();
        delete context[field];
        expect(() => getReadingContext({ READING_CONTEXT: context })).not.toThrow();
    });

    it("has no client fallbacks: each server tuning value is required", () => {
        for (const field of ["defaultWpm", "maxCreditWpm", "idleThresholdSeconds", "periodicFlushActiveSeconds"]) {
            const context = complete();
            delete context[field];
            expect(getReadingContext({ READING_CONTEXT: context }), field).toBeNull();
        }
    });

    it("is disabled when a tuning value is not a positive number", () => {
        expect(getReadingContext(raw({ maxCreditWpm: 0 }))).toBeNull();
        expect(getReadingContext(raw({ idleThresholdSeconds: "300" }))).toBeNull();
    });

    it("coerces bad word counts to 0", () => {
        const ctx = getReadingContext(raw({ pageWords: "x", bookWords: -5 }));
        expect(ctx.pageWords).toBe(0);
        expect(ctx.bookWords).toBe(0);
    });
});
