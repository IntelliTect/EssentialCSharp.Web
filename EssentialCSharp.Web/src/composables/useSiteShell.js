import { computed, nextTick, onMounted, onUnmounted, reactive, ref, watch } from "vue";
import { useWindowSize } from "./useWindowSize.js";

const SMALL_SCREEN_SIZE = 768;

/**
 * Find the path of TOC entries that lead to the current page.
 * @param {Array} path
 * @param {Array} items
 * @returns {Array | undefined}
 */
function findCurrentPage(path, items) {
    for (const item of items) {
        const itemPath = [item, ...path];
        if (
            window.location.href.endsWith(`/${item.href}`) ||
            window.location.href.endsWith(`/${item.key}`)
        ) {
            return itemPath;
        }

        const recursivePath = findCurrentPage(itemPath, item.items);
        if (recursivePath) {
            return recursivePath;
        }
    }
}

function readSidebarPreference() {
    const storedValue = localStorage.getItem("sidebarShown");
    if (storedValue === null) {
        return null;
    }

    return storedValue === "true";
}

function isIdentityPath() {
    return window.location.pathname.toLowerCase().startsWith("/identity");
}

function removeHashPath(path) {
    if (!path) {
        return null;
    }

    let index = path.indexOf("#");
    index = index > 0 ? index : path.length;
    return path.substring(0, index);
}

export function useSiteShell() {
    const tocData = window.TOC_DATA ?? [];
    const previousPageUrl = ref(removeHashPath(window.PREVIOUS_PAGE));
    const nextPageUrl = ref(removeHashPath(window.NEXT_PAGE));
    const snackbarMessage = ref(null);
    const snackbarColor = ref(null);
    const identityPage = isIdentityPath();
    const sidebarShown = ref(identityPage ? false : readSidebarPreference());
    const enableTocFilter = ref("none");
    const searchQuery = ref("");
    const expandedTocs = reactive(new Set());
    const percentComplete = ref(window.PERCENT_COMPLETE);
    const buildLabel = window.BUILD_LABEL ?? null;
    const enableChatWidget = Boolean(window.ENABLE_CHAT_WIDGET);
    const { width: windowWidth } = useWindowSize();

    let snackbarTimeoutId = null;
    let keydownHandler = null;

    const smallScreen = computed(() => (windowWidth.value || 0) < SMALL_SCREEN_SIZE);
    const currentPage = findCurrentPage([], tocData) ?? [];
    const chapterParentPage = currentPage.find((parent) => parent.level === 0) ?? null;
    const isContentPage = computed(() => percentComplete.value !== null);

    for (const item of currentPage) {
        expandedTocs.add(item.key);
    }

    function openSearch() {
        const container = document.getElementById("docsearch");
        const button = container?.querySelector(".DocSearch-Button");
        button?.click();
    }

    function addQueryParam(url, key, value) {
        const urlObject = new URL(url, window.location.origin);
        urlObject.searchParams.set(key, value);
        return urlObject.toString();
    }

    function addReferralIdToUrl(url) {
        const referralId = window.REFERRAL_ID;
        if (typeof referralId === "string" && referralId.trim().length > 0) {
            return addQueryParam(url, "rid", referralId);
        }
        return url;
    }

    function writeToClipboard(url, successMessage = "Copied to clipboard!") {
        navigator.clipboard
            .writeText(url)
            .then(
                () => {
                    snackbarColor.value = "white";
                    snackbarMessage.value = successMessage;
                    resetSnackbarTimeout();
                },
                (error) => {
                    console.error("Could not copy text to clipboard: ", error);
                    snackbarColor.value = "red";
                    snackbarMessage.value = `Error: Could not copy text to clipboard: ${error}`;
                    resetSnackbarTimeout();
                }
            );
    }

    function resetSnackbarTimeout() {
        if (snackbarTimeoutId !== null) {
            clearTimeout(snackbarTimeoutId);
        }
        snackbarTimeoutId = setTimeout(() => {
            snackbarMessage.value = null;
        }, 3000);
    }

    function copyToClipboard(copyText) {
        let url;

        if (copyText.includes("#")) {
            url = `${window.location.origin}/${copyText}`;
        }
        else {
            const currentUrl = window.location.href.split("#")[0];
            url = `${currentUrl}#${copyText}`;
        }

        writeToClipboard(addReferralIdToUrl(url), "Copied url to clipboard!");
    }

    function shareCurrentPage() {
        const url = window.location.href.split("#")[0];
        writeToClipboard(addReferralIdToUrl(url), "Copied page url to clipboard!");
    }

    function goToPrevious() {
        if (window.PREVIOUS_PAGE !== null) {
            window.location.href = `/${window.PREVIOUS_PAGE}`;
        }
    }

    function goToNext() {
        if (window.NEXT_PAGE !== null) {
            window.location.href = `/${window.NEXT_PAGE}`;
        }
    }

    function toggleSidebar() {
        sidebarShown.value = !sidebarShown.value;
        if (!identityPage) {
            localStorage.setItem("sidebarShown", String(sidebarShown.value));
        }
    }

    function normalizeString(value) {
        return value.replace(/[^\w\s]|_/g, "").replace(/\s+/g, " ").toLowerCase();
    }

    function filterItem(item, query) {
        let matches = normalizeString(item.title).includes(query);
        if (item.items && item.items.length > 0) {
            matches = matches || item.items.some((child) => filterItem(child, query));
        }

        return matches;
    }

    const filteredTocData = computed(() => {
        if (!searchQuery.value) {
            return tocData;
        }

        const query = normalizeString(searchQuery.value);
        return tocData.filter((item) => filterItem(item, query));
    });

    watch(windowWidth, (newWidth) => {
        if (sidebarShown.value === null) {
            if (newWidth < SMALL_SCREEN_SIZE) {
                sidebarShown.value = false;
            }
            else {
                sidebarShown.value = true;
            }
        }
    });

    watch(sidebarShown, (newValue) => {
        if (windowWidth.value <= SMALL_SCREEN_SIZE) {
            if (newValue) {
                document.body.classList.add("noScrollOnSmallScreen");
            }
            else {
                document.body.classList.remove("noScrollOnSmallScreen");
            }
        }
        else {
            document.body.classList.remove("noScrollOnSmallScreen");
        }
    });

    watch(searchQuery, (newQuery) => {
        expandedTocs.clear();

        if (!newQuery) {
            for (const item of currentPage) {
                expandedTocs.add(item.key);
            }
            return;
        }

        const query = normalizeString(newQuery);
        tocData.forEach((item) => {
            if (filterItem(item, query)) {
                expandedTocs.add(item.key);
            }
        });
    });

    onMounted(() => {
        if (sidebarShown.value === null && windowWidth.value > SMALL_SCREEN_SIZE) {
            sidebarShown.value = true;
        }

        keydownHandler = (event) => {
            const selectionString = document.getSelection()?.toString();
            if (event.key === "ArrowRight" && !selectionString) {
                goToNext();
            }

            if (event.key === "ArrowLeft" && !selectionString) {
                goToPrevious();
            }

            if (event.code === "KeyM" && event.ctrlKey) {
                toggleSidebar();
            }
        };

        document.addEventListener("keydown", keydownHandler);

        nextTick(() => {
            [...document.querySelectorAll(".current-section")]
                .reverse()[0]
                ?.scrollIntoView({
                    behavior: "auto",
                    block: "center",
                    inline: "center"
                });
        });
    });

    onUnmounted(() => {
        if (keydownHandler) {
            document.removeEventListener("keydown", keydownHandler);
        }

        if (snackbarTimeoutId !== null) {
            clearTimeout(snackbarTimeoutId);
        }

        document.body.classList.remove("noScrollOnSmallScreen");
    });

    return {
        previousPageUrl,
        nextPageUrl,
        snackbarMessage,
        snackbarColor,
        sidebarShown,
        enableTocFilter,
        searchQuery,
        expandedTocs,
        percentComplete,
        buildLabel,
        enableChatWidget,
        smallScreen,
        currentPage,
        chapterParentPage,
        isContentPage,
        filteredTocData,
        copyToClipboard,
        shareCurrentPage,
        goToPrevious,
        goToNext,
        openSearch,
        toggleSidebar
    };
}
