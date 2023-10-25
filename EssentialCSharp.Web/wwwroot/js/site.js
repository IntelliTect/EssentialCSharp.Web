import {
  createApp,
  ref,
  reactive,
  onMounted,
  markRaw,
  watch,
  computed,
} from "vue";
import { useWindowSize } from "vue-window-size";

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

//Add new content or features here:

const featuresComingSoonList = [
  {
    title: "Client-side Compiler",
    text: "Write, compile, and run code snippets right from your browser. Enjoy hands-on experience with the code as you go through the site.",
  },
  {
    title: "Interactive Code Listings",
    text: "Edit, compile, and run the code listings found throughout Essential C#.",
  },
  {
    title: "Hyperlinking",
    text: "Easily navigate to interesting and relevant sites as well as related sections in Essential C#.",
  },
  {
    title: "Table of Contents Filtering",
    text: "The Table of Contents filter will let you narrow down the list of topics to help you quickly and easily find your destination.",
  },
];

const contentComingSoonList = [
  {
    title: "File Scoped <span class='code-text'>namespaces</span>",
    text: "New feature from C# 10.",
  },
  {
    title: "<span class='code-text'>using</span> Statement Improvements",
    text: "New feature from C# 10.",
  },
  {
    title: "Pattern Matching Improvements",
    text: "Feature improvements from C# 11.",
  },
];

const completedFeaturesList = [
  {
    title: "Copying Header Hyperlinks",
    text: "Easily copy a header URL to link to a book section.",
  },
  {
    title: "Home Page",
    text: "Add a home page that features a short description of the book and a high level mindmap.",
  },
  {
    title: "Keyboard Shortcuts",
    text: "Quickly navigate through the book via keyboard shortcuts (right/left arrows, 'n', 'p').",
  },
];

/**
 * Find the path of TOC entries that lead to the current page.
 * @param {TocItem[]} path
 * @param {TocItem[]} items
 * @returns {TocItem[] | undefined} path of items to the current page
 * */
function findCurrentPage(path, items) {
  for (const item of items) {
    const itemPath = [item, ...path];
    if (
      window.location.href.endsWith("/" + item.href) ||
      window.location.href.endsWith("/" + item.key)
    ) {
      return itemPath;
    }

    const recursivePath = findCurrentPage(itemPath, item.items);
    if (recursivePath) return recursivePath;
  }
}

function openSearch() {
  const el = document
    .getElementById("docsearch")
    .querySelector(".DocSearch-Button");
  el.click();
}

const smallScreenSize = 768;

const removeHashPath = (path) => {
  if (!path) {
    return null;
  }
  let index = path.indexOf("#");
  index = index > 0 ? index : path.length;
  return path.substring(0, index);
};
// v-bind dont like the # in the url
const nextPagePath = removeHashPath(NEXT_PAGE);
const previousPagePath = removeHashPath(PREVIOUS_PAGE);

const app = createApp({
  setup() {
    const { width: windowWidth } = useWindowSize();

    const nextPageUrl = ref(nextPagePath);
    const previousPageUrl = ref(previousPagePath);

    let snackbarTimeoutId = null;
    const snackbarMessage = ref();
    const snackbarColor = ref();

    function copyToClipboard(copyText) {
      navigator.clipboard
        .writeText(window.location.origin + "/" + copyText)
        .then(
          function () {
            /* Success */
            snackbarColor.value = "white";
            snackbarMessage.value = "Copied url to clipboard!";
          },
          function (err) {
            console.error("Could not copy text to clipboard: ", err);
            snackbarColor.value = "red";
            snackbarMessage.value =
              "Error: Could not copy text to clipboard: " + err;
          }
        );
      // Hide after 3 seconds
      if (snackbarTimeoutId != null) {
        clearTimeout(snackbarTimeoutId);
        snackbarMessage.value = null;
      }
      snackbarTimeoutId = setTimeout(
        () => (snackbarMessage.value = null),
        3000
      );
    }

    function goToPrevious() {
      window.location.href = "/" + PREVIOUS_PAGE;
    }
    function goToNext() {
      window.location.href = "/" + NEXT_PAGE;
    }

    document.addEventListener("keydown", (e) => {
        let selectionString = document.getSelection().toString();
        if (e.key == "ArrowRight") {
            if (!selectionString) {
                goToNext();
            }
        }

        if (e.key == "ArrowLeft") {
            if (!selectionString) {
                goToPrevious();
            }
        }
    });

    const sidebarShown = ref(false);

    const smallScreen = computed(() => {
      return (windowWidth.value || 0) < smallScreenSize;
    });

    /** @type {import("vue").Ref<"toc" | "search">} */
    const sidebarTab = ref("toc");

    const currentPage = findCurrentPage([], tocData) ?? [];

    const chapterParentPage = currentPage.find((parent) => parent.level === 0);

    const sectionTitle = ref(currentPage?.[0]?.title || "Essential C#");
    const expandedTocs = reactive(new Set());
    for (const item of currentPage) {
      expandedTocs.add(item.key);
    }

    // hide the sidebar when resizing to small screen
    // to do: make it re emerge when going from small to big
    watch(windowWidth, (newWidth, oldWidth) => {
      //+ 50 so that the side bar diappears before the css media class changes the sidebar to take
      // over the full screen
      if (newWidth < smallScreenSize) {
        sidebarShown.value = false;
      }
      // when making screen bigger reveal sidebar
      else {
        if (!sidebarShown.value) {
          sidebarShown.value = true;
        }
      }
    });

    // prevent scrolling of the page when the sidebar is visible on small screens
    watch(sidebarShown, (newValue, oldValue) => {
      if (windowWidth.value <= smallScreenSize) {
        if (newValue) {
          document.body.classList.add("noScrollOnSmallScreen");
        } else {
          document.body.classList.remove("noScrollOnSmallScreen");
        }
      }
    });

    onMounted(() => {
      if (windowWidth.value > smallScreenSize) {
        sidebarShown.value = true;
      }

      // Scroll the current selected page in the TOC into view of the TOC.
      [...document.querySelectorAll(".current-section")]
        .reverse()[0]
        ?.scrollIntoView({
          behavior: "auto",
          block: "center",
          inline: "center",
        });
    });

    return {
      previousPageUrl,
      nextPageUrl,
      goToPrevious,
      goToNext,
      openSearch,

      snackbarMessage,
      snackbarColor,
      copyToClipboard,

      contentComingSoonList,
      featuresComingSoonList,
      completedFeaturesList,

      sidebarShown,
      sidebarTab,

      smallScreen,

      sectionTitle,
      tocData,
      expandedTocs,
      currentPage,
      chapterParentPage,
    };
  },
});

app.component("toc-tree", {
  props: ["item", "expandedTocs", "currentPage"],
  template: "#toc-tree",
});

app.mount("#app");
