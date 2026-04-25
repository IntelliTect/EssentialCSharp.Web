<script setup>
import { inject } from "vue";

const shell = inject("shell");
</script>

<template>
    <div
        v-if="shell.snackbarMessage"
        id="snackbar"
        class="show"
        :style="{ color: shell.snackbarColor }"
    >
        {{ shell.snackbarMessage }}
    </div>
</template>
