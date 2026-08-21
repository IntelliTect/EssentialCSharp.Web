<script setup>
import { computed, inject } from "vue";
import { useReadingTracker } from "../composables/useReadingTracker.js";

const shell = inject("shell");

// Only render on chapter/content pages.
const isContentPage = computed(() => Boolean(window.CURRENT_PAGE_KEY));

// Word count data from window globals (set by _Layout.cshtml / HomeController).
const pageWordCount = window.PAGE_WORD_COUNT ?? 0;
const chapterWordCount = window.CHAPTER_WORD_COUNT ?? 0;
const chapterStartWords = window.CHAPTER_START_WORDS ?? 0;
const bookWordCount = window.BOOK_WORD_COUNT ?? 0;
const wordsBeforePage = window.WORDS_BEFORE_PAGE ?? 0;

const { wpm, activeSeconds } = useReadingTracker();

// Live scroll fraction from the page.
function getScrollFraction() {
    const main = document.querySelector("main") ?? document.documentElement;
    const scrollTop = window.scrollY || document.documentElement.scrollTop;
    const scrollable = main.scrollHeight - main.clientHeight;
    if (scrollable <= 0) return 1;
    return Math.min(1, Math.max(0, scrollTop / scrollable));
}

// Recomputed every second via activeSeconds dependency.
const scrollFraction = computed(() => {
    // Reference activeSeconds so Vue re-evaluates as the timer ticks.
    void activeSeconds.value;
    return getScrollFraction();
});

const wordsReadOnPage = computed(() => Math.round(pageWordCount * scrollFraction.value));

const minutesLeftInChapter = computed(() => {
    if (!wpm.value || wpm.value <= 0 || chapterWordCount <= 0) return null;
    const wordsLeft = chapterWordCount - chapterStartWords - wordsReadOnPage.value;
    return Math.max(0, wordsLeft) / wpm.value;
});

const minutesLeftInBook = computed(() => {
    if (!wpm.value || wpm.value <= 0 || bookWordCount <= 0) return null;
    const wordsLeft = bookWordCount - wordsBeforePage - wordsReadOnPage.value;
    return Math.max(0, wordsLeft) / wpm.value;
});

function formatMinutes(minutes) {
    if (minutes === null || minutes === undefined) return null;
    const rounded = Math.round(minutes);
    if (rounded < 60) {
        return `${rounded} min`;
    }
    const hours = Math.floor(rounded / 60);
    const mins = rounded % 60;
    return mins > 0 ? `${hours}h ${mins}m` : `${hours}h`;
}

const chapterLabel = computed(() => {
    const m = minutesLeftInChapter.value;
    if (m === null) return null;
    return `${formatMinutes(m)} left in chapter`;
});

const bookLabel = computed(() => {
    const m = minutesLeftInBook.value;
    if (m === null) return null;
    return `${formatMinutes(m)} left in book`;
});
</script>

<template>
    <div
        v-if="isContentPage && (chapterLabel || bookLabel)"
        class="reading-time-remaining text-light d-flex align-items-center gap-2"
        aria-label="Reading time remaining"
    >
        <span v-if="chapterLabel" class="reading-time-chapter d-none d-sm-inline">{{ chapterLabel }}</span>
        <span v-if="chapterLabel && bookLabel" class="d-none d-lg-inline text-light opacity-50">·</span>
        <span v-if="bookLabel" class="reading-time-book d-none d-lg-inline">{{ bookLabel }}</span>
    </div>
</template>

<style scoped>
.reading-time-remaining {
    font-size: 0.8rem;
    white-space: nowrap;
    opacity: 0.85;
}
</style>
