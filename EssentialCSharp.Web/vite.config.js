import { basename, extname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";

const rootDir = fileURLToPath(new URL(".", import.meta.url));

export default defineConfig({
    plugins: [vue()],
    base: "/dist/",
    publicDir: false,
    resolve: {
        alias: {
            "@": resolve(rootDir, "src")
        }
    },
    build: {
        outDir: resolve(rootDir, "wwwroot", "dist"),
        emptyOutDir: true,
        cssCodeSplit: false,
        rollupOptions: {
            input: {
                "site-shell": resolve(rootDir, "src", "main.js"),
                "password-strength": resolve(rootDir, "wwwroot", "js", "password-strength.js")
            },
            output: {
                entryFileNames: "assets/[name].js",
                chunkFileNames: "assets/[name].js",
                assetFileNames: ({ name }) => {
                    const assetName = name ?? "asset";
                    const extension = extname(assetName);
                    const baseName = basename(assetName, extension);
                    return `assets/${baseName}${extension}`;
                }
            }
        }
    }
});
