<script setup>
import { inject } from "vue";
import TocTree from "./TocTree.vue";

const shell = inject("shell");
const currentPath = window.location.pathname.toLowerCase();
const navLinks = [
    {
        href: "/home",
        iconClass: "fas fa-home me-2",
        label: "Home",
        activePaths: ["/", "/home"]
    },
    {
        href: "/about",
        iconClass: "fas fa-book me-2",
        label: "About",
        activePaths: ["/about"]
    },
    {
        href: "/guidelines",
        iconClass: "fas fa-code me-2",
        label: "Guidelines",
        activePaths: ["/guidelines"]
    },
    {
        href: "/announcements",
        iconClass: "fas fa-bullhorn me-2",
        label: "Announcements",
        activePaths: ["/announcements"]
    }
];

function isActivePath(paths) {
    return paths.includes(currentPath);
}
</script>

<template>
    <Transition name="slide-fade">
        <div
            v-if="shell.sidebarShown"
            id="sidebar"
            class="sidebar toc-padding"
            :class="{ sidebarSmall: shell.smallScreen }"
        >
            <div class="toc-menu">
                <div style="display:grid; width:100%; margin-bottom:.75rem;">
                    <button type="button" class="DocSearch DocSearch-Button DocSearch-Style" aria-label="Search" @click="shell.openSearch">
                        <span class="DocSearch-Button-Container">
                            <svg width="20" height="20" class="DocSearch-Search-Icon" viewBox="0 0 20 20">
                                <path d="M14.386 14.386l4.0877 4.0877-4.0877-4.0877c-2.9418 2.9419-7.7115 2.9419-10.6533 0-2.9419-2.9418-2.9419-7.7115 0-10.6533 2.9418-2.9419 7.7115-2.9419 10.6533 0 2.9419 2.9418 2.9419 7.7115 0 10.6533z" stroke="currentColor" fill="none" fill-rule="evenodd" stroke-linecap="round" stroke-linejoin="round" />
                            </svg>
                            <span class="DocSearch-Button-Placeholder">Search</span>
                        </span>
                        <span class="DocSearch-Button-Keys">
                            <kbd class="DocSearch-Button-Key">
                                <svg width="15" height="15" class="DocSearch-Control-Key-Icon">
                                    <path d="M4.505 4.496h2M5.505 5.496v5M8.216 4.496l.055 5.993M10 7.5c.333.333.5.667.5 1v2M12.326 4.5v5.996M8.384 4.496c1.674 0 2.116 0 2.116 1.5s-.442 1.5-2.116 1.5M3.205 9.303c-.09.448-.277 1.21-1.241 1.203C1 10.5.5 9.513.5 8V7c0-1.57.5-2.5 1.464-2.494.964.006 1.134.598 1.24 1.342M12.553 10.5h1.953" stroke-width="1.2" stroke="currentColor" fill="none" stroke-linecap="square" />
                                </svg>
                            </kbd>
                            <kbd class="DocSearch-Button-Key">K</kbd>
                        </span>
                    </button>
                </div>
                <div class="list-group list-group-flush d-md-none mb-3">
                    <a
                        v-for="navLink in navLinks"
                        :key="navLink.href"
                        :href="navLink.href"
                        class="list-group-item list-group-item-action"
                        :class="{ active: isActivePath(navLink.activePaths) }"
                    >
                        <span :class="navLink.iconClass" />
                        <span class="fs-5">{{ navLink.label }}</span>
                    </a>
                </div>
                <div style="display:flex; align-items:center;">
                    <h5 style="margin-right:8px;">
                        Contents
                    </h5>
                    <i class="fa-solid fa-filter" @click="shell.enableTocFilter = shell.enableTocFilter === 'filter' ? 'none' : 'filter'" />
                </div>
                <div v-if="shell.enableTocFilter === 'filter'" class="filter-input-container">
                    <input
                        v-model="shell.searchQuery"
                        type="text"
                        class="filter-input"
                        placeholder="Search sections..."
                    />
                    <button type="button" class="filter-btn">
                        <i class="fa fa-search icon-light" />
                    </button>
                </div>
            </div>

            <div class="toc-tree" id="toc">
                <ul class="tree">
                    <TocTree
                        v-for="item in shell.filteredTocData"
                        :key="item.key"
                        :item="item"
                        :expanded-tocs="shell.expandedTocs"
                        :current-page="shell.currentPage"
                    />
                </ul>
            </div>

            <div v-if="shell.buildLabel">
                <small>
                    Build: <b>{{ shell.buildLabel }}</b>
                </small>
            </div>
        </div>
    </Transition>
</template>
