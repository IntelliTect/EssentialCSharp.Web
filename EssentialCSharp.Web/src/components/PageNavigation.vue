<script setup>
import { inject } from "vue";

const shell = inject("shell");
</script>

<template>
    <nav class="turn-page">
        <div class="turn-page-tooltip">
            <a v-if="shell.previousPageUrl" id="prev-btn" :href="shell.previousPageUrl" class="arrow-btn" aria-label="Previous page">
                <i class="fa fa-solid fa-arrow-left icon-light arrow-icon" aria-hidden="true" />
            </a>
            <span class="turn-page-tooltip-text tooltip-right" aria-hidden="true">
                <b>Previous Page</b>
                <br />
                ( or use the <i class="fa-solid fa-square-caret-left" /> key)
            </span>
        </div>
        <div class="turn-page-tooltip">
            <a v-if="shell.nextPageUrl" id="next-btn" :href="shell.nextPageUrl" class="arrow-btn" aria-label="Next page">
                <i class="fa fa-solid fa-arrow-right icon-light arrow-icon" aria-hidden="true" />
            </a>
            <span class="turn-page-tooltip-text tooltip-left" aria-hidden="true">
                <b>Next Page</b>
                <br />
                ( or use the <i class="fa-solid fa-square-caret-right" /> key)
            </span>
        </div>
    </nav>
</template>
