<script setup>
defineProps({
    item: {
        type: Object,
        required: true
    },
    expandedTocs: {
        type: Object,
        required: true
    },
    currentPage: {
        type: Array,
        required: true
    }
});

function handleToggle(event, item, expandedTocs) {
    if (event.target.open) {
        expandedTocs.add(item.key);
    }
    else {
        expandedTocs.delete(item.key);
    }
}
</script>

<template>
    <li v-if="item.items.length">
        <details :open="expandedTocs.has(item.key)" @toggle="handleToggle($event, item, expandedTocs)">
            <summary
                :class="{
                    'toc-content': item.level === 0,
                    nested: item.level > 0,
                    'current-section': currentPage.some((page) => page.key === item.key)
                }"
            >
                {{ item.title }}
            </summary>
            <ul>
                <li
                    :class="{
                        [`indent-level-${item.level + 1}`]: true,
                        'current-li': currentPage.some((page) => page.key === item.key) && !currentPage.some((page) => page.level > item.level)
                    }"
                >
                    <a
                        class="section-link"
                        :class="{
                            [`indent-level-${item.level + 1}`]: true,
                            'current-section': currentPage.some((page) => page.key === item.key) && !currentPage.some((page) => page.level > item.level)
                        }"
                        :href="item.href"
                    >
                        Introduction
                    </a>
                </li>
                <TocTree
                    v-for="childItem in item.items"
                    :key="childItem.key"
                    :item="childItem"
                    :expanded-tocs="expandedTocs"
                    :current-page="currentPage"
                />
            </ul>
            <hr v-if="item.level === 0" class="divider" />
        </details>
    </li>
    <li
        v-else
        :class="{
            [`indent-level-${item.level + 1}`]: true,
            'current-li': currentPage.some((page) => page.key === item.key) && !currentPage.some((page) => page.level > item.level)
        }"
    >
        <a
            class="section-link"
            :class="{
                [`indent-level-${item.level}`]: true,
                'current-section': currentPage.some((page) => page.key === item.key)
            }"
            :href="item.href"
        >
            {{ item.title }}
        </a>
    </li>
</template>
