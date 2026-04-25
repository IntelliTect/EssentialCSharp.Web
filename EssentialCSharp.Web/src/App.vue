<script setup>
import { onMounted, onUnmounted, provide, reactive, watch } from "vue";
import HeaderStatus from "./components/HeaderStatus.vue";
import SidebarPanel from "./components/SidebarPanel.vue";
import PageNavigation from "./components/PageNavigation.vue";
import SnackbarMessage from "./components/SnackbarMessage.vue";
import ChatWidget from "./components/ChatWidget.vue";
import CodeRunnerModal from "./components/CodeRunnerModal.vue";
import { useSiteShell } from "./composables/useSiteShell.js";

const shell = reactive(useSiteShell());
provide("shell", shell);

function updateLayoutClasses() {
    const siteLayout = document.getElementById("site-layout");
    if (!siteLayout) {
        return;
    }

    siteLayout.classList.toggle("has-sidebar", Boolean(shell.sidebarShown) && !shell.smallScreen);
}

function handleSidebarToggleClick(event) {
    event.preventDefault();
    shell.toggleSidebar();
}

function handleCopyLinkClick(event) {
    const trigger = event.target.closest("[v-on\\:click]");
    if (!trigger) {
        return;
    }

    const expression = trigger.getAttribute("v-on:click")?.trim();
    const match = expression?.match(/^copyToClipboard\((['"])(.*)\1\)$/);
    if (!match) {
        return;
    }

    event.preventDefault();
    shell.copyToClipboard(match[2]);
}

let sidebarToggleButton = null;

onMounted(() => {
    sidebarToggleButton = document.getElementById("sidebar-toggle-button");
    sidebarToggleButton?.addEventListener("click", handleSidebarToggleClick);
    document.addEventListener("click", handleCopyLinkClick);
    window.copyToClipboard = shell.copyToClipboard;
    updateLayoutClasses();
});

onUnmounted(() => {
    sidebarToggleButton?.removeEventListener("click", handleSidebarToggleClick);
    document.removeEventListener("click", handleCopyLinkClick);
    delete window.copyToClipboard;
});

watch(() => shell.sidebarShown, updateLayoutClasses);
watch(() => shell.smallScreen, updateLayoutClasses);
</script>

<template>
    <Teleport to="#header-status-host">
        <HeaderStatus />
    </Teleport>

    <Teleport to="#sidebarContainer">
        <SidebarPanel />
    </Teleport>

    <Teleport to="#page-nav-host">
        <PageNavigation />
    </Teleport>

    <Teleport to="#snackbar-host">
        <SnackbarMessage />
    </Teleport>

    <Teleport v-if="shell.enableChatWidget" to="#chat-widget-host">
        <ChatWidget />
    </Teleport>

    <Teleport to="#code-runner-host">
        <CodeRunnerModal />
    </Teleport>
</template>
