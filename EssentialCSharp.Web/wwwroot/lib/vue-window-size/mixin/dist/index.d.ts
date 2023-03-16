type Config = {
    delay?: number;
};

type Mixin = {
    computed: {
        $windowWidth: () => number;
        $windowHeight: () => number;
    };
};

declare const vueWindowSizeMixin: () => Mixin;
declare const vueWindowSizeAPI: {
    config(config: Config): void;
    init(): void;
    destroy(): void;
};
/** types */
declare module '@vue/runtime-core' {
    interface ComponentCustomProperties {
        $windowWidth: number;
        $windowHeight: number;
    }
}

export { vueWindowSizeAPI, vueWindowSizeMixin };
