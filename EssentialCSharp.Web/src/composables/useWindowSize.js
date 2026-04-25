import { onMounted, onUnmounted, ref } from "vue";

export function useWindowSize() {
    const width = ref(typeof window === "undefined" ? 0 : window.innerWidth);

    function updateWidth() {
        width.value = window.innerWidth;
    }

    onMounted(() => {
        updateWidth();
        window.addEventListener("resize", updateWidth);
    });

    onUnmounted(() => {
        window.removeEventListener("resize", updateWidth);
    });

    return {
        width
    };
}
