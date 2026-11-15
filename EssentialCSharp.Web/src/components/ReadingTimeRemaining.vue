<script setup>
import { computed, inject } from "vue";
import { READING_TRACKER_KEY } from "../composables/useReadingTracker.js";
import { formatMinutes, formatMinutesSpoken, minutesForWords } from "../utils/readingFormat.js";

// Null on non-content pages (no READING_CONTEXT): render nothing.
const tracker = inject(READING_TRACKER_KEY, null);

const context = tracker?.context;
const wordsReadOnPage = computed(() =>
    tracker ? Math.round(context.pageWords * tracker.scrollFraction.value) : 0);

const minutesLeftInChapter = computed(() => {
    if (!tracker || context.chapterWords <= 0) return null;
    return minutesForWords(
        context.chapterWords - context.chapterStartWords - wordsReadOnPage.value,
        tracker.wpm.value);
});

const minutesLeftInBook = computed(() => {
    if (!tracker || context.bookWords <= 0) return null;
    return minutesForWords(
        context.bookWords - context.wordsBeforePage - wordsReadOnPage.value,
        tracker.wpm.value);
});

const chapterText = computed(() => formatMinutes(minutesLeftInChapter.value));
const bookText = computed(() => formatMinutes(minutesLeftInBook.value));

const spokenText = computed(() => {
    const parts = [];
    const chapter = formatMinutesSpoken(minutesLeftInChapter.value);
    const book = formatMinutesSpoken(minutesLeftInBook.value);
    if (chapter) parts.push(`${chapter} left in this chapter`);
    if (book) parts.push(`${book} left in the book`);
    return parts.length ? `${parts.join(", ")}.` : "";
});
</script>

<template>
    <div
        v-if="chapterText || bookText"
        class="reading-time-remaining text-light d-flex align-items-center gap-2"
    >
        <span class="visually-hidden">{{ spokenText }}</span>
        <span v-if="chapterText" class="reading-time-chapter" aria-hidden="true">
            {{ chapterText }}<span class="d-none d-sm-inline"> left in chapter</span>
        </span>
        <span v-if="chapterText && bookText" class="reading-time-separator d-none d-lg-inline" aria-hidden="true">·</span>
        <span v-if="bookText" class="reading-time-book d-none d-lg-inline" aria-hidden="true">{{ bookText }} left in book</span>
    </div>
</template>

<style scoped>
/* #f8f9fa on the header's rgb(26, 48, 70) is roughly 10:1 contrast (WCAG AA needs 4.5:1). */
.reading-time-remaining {
    font-size: 0.8rem;
    white-space: nowrap;
}

.reading-time-separator {
    opacity: 0.7;
}
</style>
