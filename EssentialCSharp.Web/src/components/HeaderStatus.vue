<script setup>
import { inject } from "vue";

const shell = inject("shell");
</script>

<template>
    <div class="d-flex align-items-center justify-content-center gap-3 overflow-hidden">
        <a
            v-if="shell.chapterParentPage"
            :href="shell.chapterParentPage.href"
            class="page-menu menu-chapter-title text-light"
        >
            <span>{{ shell.chapterParentPage.title }}</span>
        </a>
        <div v-if="shell.isContentPage" class="page-menu menu-progress text-light">
            <span>{{ shell.percentComplete }}%</span>
        </div>
    </div>
</template>
