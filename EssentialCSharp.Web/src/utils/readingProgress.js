export function getDocumentScrollFraction() {
    const scrollingElement = document.scrollingElement ?? document.documentElement;
    const scrollable = scrollingElement.scrollHeight - scrollingElement.clientHeight;

    if (scrollable <= 0) return 1;

    const scrollTop = scrollingElement.scrollTop ?? window.scrollY ?? 0;
    return Math.min(1, Math.max(0, scrollTop / scrollable));
}
