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

// The user id is not part of READING_CONTEXT: the layout exposes it in a meta tag.
const withUserIdMeta = (content) => {
    const doc = document.implementation.createHTMLDocument("");
    if (content !== undefined) {
        const meta = doc.createElement("meta");
        meta.name = "ecs-auth-user-id";
        meta.content = content;
        doc.head.append(meta);
    }
    return { ...raw(), document: doc };
};

describe("getReadingContext", () => {
    it("returns null when there is no context (not a content page)", () => {
        expect(getReadingContext({})).toBeNull();
    });

    it("reads the server-provided values", () => {
        expect(getReadingContext(raw())).toEqual({ ...complete(), userId: null });
    });

    it("reads the signed-in user id from the layout's meta tag", () => {
        expect(getReadingContext(withUserIdMeta("user-1")).userId).toBe("user-1");
    });

    it("has no user id for anonymous visitors (no meta tag) or an empty one", () => {
        expect(getReadingContext(withUserIdMeta()).userId).toBeNull();
        expect(getReadingContext(withUserIdMeta("")).userId).toBeNull();
    });

    it("ignores a userId in READING_CONTEXT: the meta tag is the only source", () => {
        expect(getReadingContext(raw({ userId: "from-window" })).userId).toBeNull();
    });

    it("parses exactly the fields of the fixture (plus the meta-tag user id): a field added to one side but not the other fails here", () => {
        expect(Object.keys(getReadingContext(raw())).sort()).toEqual([...Object.keys(complete()), "userId"].sort());
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
