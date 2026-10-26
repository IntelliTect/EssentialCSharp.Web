<script setup>
import { computed, inject } from "vue";

const shell = inject("shell");
const progressLabel = computed(() => `${Math.round(shell.percentComplete)}% complete`);
</script>

<template>
    <div
        class="d-flex align-items-center justify-content-center gap-3 overflow-hidden"
        :title="shell.isContentPage ? progressLabel : null"
    >
        <a
            v-if="shell.chapterParentPage"
            :href="shell.chapterParentPage.href"
            class="page-menu menu-chapter-title text-light"
        >
            <span>{{ shell.chapterParentPage.title }}</span>
        </a>
        <div v-if="shell.isContentPage" class="menu-progress-track" :title="progressLabel">
            <div
                class="menu-progress-bar"
                role="progressbar"
                :aria-valuenow="Math.round(shell.percentComplete)"
                aria-valuemin="0"
                aria-valuemax="100"
                :aria-label="progressLabel"
                :style="{ width: `${shell.percentComplete}%` }"
            ></div>
        </div>
    </div>
</template>
