import { createApp, ref, reactive, onMounted, markRaw } from 'vue'

/**
 * @typedef {Object} TocItem 
 * @prop {number} [level]
 * @prop {string} [key]
 * @prop {string} [href]
 * @prop {string} [title]
 * @prop {TocItem[]} [items]
 */
/** @type {TocItem} */
const tocData = markRaw(TOC_DATA);

/** 
 * Find the path of TOC entries that lead to the current page.
 * @param {TocItem[]} path
 * @param {TocItem[]} items
 * @returns {TocItem[] | undefined} path of items to the current page
 * */
function findCurrentPage(path, items) {
    for (const item of items) {
        const itemPath = [item, ...path];
        if (window.location.href.endsWith("/" + item.href) || window.location.href.endsWith("/" + item.key)) {
            return itemPath
        }

        const recursivePath = findCurrentPage(itemPath, item.items);
        if (recursivePath) return recursivePath;
    }
}

const app = createApp({
    setup() {

        const clipboardError = ref();
        function copyToClipboard(copyText) {
            navigator.clipboard.writeText(window.location.origin + "/" + copyText).then(function () {
                /* Success */
            }, function (err) {
                console.error('Could not copy text to clipboard: ', err);
                clipboardError.value = 'Error: Could not copy text to clipboard: ' + err;
            });
            // Hide after 3 seconds
            setTimeout(() => clipboardError.value = null, 3000);
        }

        function goToPrevious() {
            window.location.href = "/" + PREVIOUS_PAGE
        }
        function goToNext() {
            window.location.href = "/" + NEXT_PAGE
        }

        document.addEventListener('keydown', (e) => {
            if (e.key.toLowerCase() === 'n' || e.key == "ArrowRight") {
                goToNext()
            }

            if (e.key.toLowerCase() === 'p' || e.key == "ArrowLeft") {
                goToPrevious()
            }
        });

        const comingSoonSidebarShown = ref(false);
        const sidebarShown = ref(true);

        /** @type {import("vue").Ref<"toc" | "search">} */
        const sidebarTab = ref("toc");

        const currentPage = findCurrentPage([], tocData) ?? []
        const sectionTitle = ref(currentPage?.[0]?.title || "Essential C#")
        const expandedTocs = reactive(new Set());
        for (const item of currentPage) {
            expandedTocs.add(item.key)
        }

        onMounted(() => {
            // Scroll the current selected page in the TOC into view of the TOC.
            [...document.querySelectorAll(".current-section")].reverse()[0]?.scrollIntoView({
                behavior: 'auto',
                block: 'center',
                inline: 'center'
            })
        })


        return {
            previousPageUrl: PREVIOUS_PAGE,
            nextPageUrl: NEXT_PAGE,
            goToPrevious,
            goToNext,

            clipboardError,
            copyToClipboard,

            comingSoonSidebarShown,
            sidebarShown,
            sidebarTab,

            sectionTitle,
            tocData,
            expandedTocs,
            currentPage
        }

    }
})

app.component("toc-tree", {
    props: ["item", "expandedTocs", "currentPage"],
    template: "#toc-tree"
})

app.mount("#app")