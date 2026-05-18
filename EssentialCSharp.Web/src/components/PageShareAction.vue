<script setup>
import { inject } from "vue";

const shell = inject("shell");
</script>

<template>
    <div v-if="shell.isContentPage" class="page-share-action">
        <div class="turn-page-tooltip">
            <button
                id="share-btn"
                type="button"
                class="arrow-btn page-share-btn"
                aria-label="Share this page"
                @click="shell.shareCurrentPage()"
            >
                <i class="fa-solid fa-share-from-square arrow-icon" />
            </button>
            <span class="turn-page-tooltip-text page-share-tooltip">
                <b>Share Page</b>
                <br />
                Copies a link to this page
            </span>
        </div>
    </div>
</template>
