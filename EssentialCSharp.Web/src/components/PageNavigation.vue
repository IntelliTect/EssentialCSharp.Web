<script setup>
import { inject } from "vue";

const shell = inject("shell");
</script>

<template>
    <nav class="turn-page">
        <div class="turn-page-tooltip">
            <a v-if="shell.previousPageUrl" id="prev-btn" :href="shell.previousPageUrl" class="arrow-btn">
                <i class="fa fa-solid fa-arrow-left icon-light arrow-icon" />
            </a>
            <span class="turn-page-tooltip-text tooltip-right">
                <b>Previous Page</b>
                <br />
                ( or use the <i class="fa-solid fa-square-caret-left" /> key)
            </span>
        </div>
        <div v-if="shell.isContentPage" class="turn-page-tooltip">
            <button id="share-btn" class="arrow-btn" aria-label="Share this page" @click="shell.shareCurrentPage()">
                <i class="fa-solid fa-share-from-square arrow-icon" />
            </button>
            <span class="turn-page-tooltip-text tooltip-center">
                <b>Share Page</b>
                <br />
                Copies a link to this page
            </span>
        </div>
        <div class="turn-page-tooltip">
            <a v-if="shell.nextPageUrl" id="next-btn" :href="shell.nextPageUrl" class="arrow-btn">
                <i class="fa fa-solid fa-arrow-right icon-light arrow-icon" />
            </a>
            <span class="turn-page-tooltip-text tooltip-left">
                <b>Next Page</b>
                <br />
                ( or use the <i class="fa-solid fa-square-caret-right" /> key)
            </span>
        </div>
    </nav>
</template>
