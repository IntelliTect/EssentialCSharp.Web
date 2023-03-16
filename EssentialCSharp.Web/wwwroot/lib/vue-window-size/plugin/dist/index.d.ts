import { App } from 'vue';

type Config = {
    delay?: number;
};

declare const vueWindowSizeAPI: {
    config(config: Config): void;
    init(): void;
    destroy(): void;
};
declare function install(app: App, { delay }?: Config): void;
declare const VueWindowSizePlugin: {
    install: typeof install;
};
declare module 'vue' {
    interface ComponentCustomProperties {
        $windowWidth: number;
        $windowHeight: number;
    }
}
type VueWindowSizeOptionApiConfig = Config;

export { VueWindowSizeOptionApiConfig, VueWindowSizePlugin, vueWindowSizeAPI };
