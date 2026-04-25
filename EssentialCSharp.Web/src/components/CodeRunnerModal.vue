<script setup>
import { useTryDotNet } from "../../wwwroot/js/trydotnet-module.js";

const {
    isCodeRunnerOpen,
    codeRunnerLoading,
    codeRunnerError,
    codeRunnerOutput,
    codeRunnerOutputError,
    currentListingInfo,
    isRunning,
    closeCodeRunner,
    retryCodeRunner,
    runCode,
    clearEditor,
    clearOutput
} = useTryDotNet();
</script>

<template>
    <div
        class="code-runner-overlay"
        :class="{ active: isCodeRunnerOpen }"
        role="dialog"
        aria-labelledby="code-runner-dialog-title"
        aria-modal="true"
        tabindex="-1"
        @click.self="closeCodeRunner"
    >
        <div class="code-runner-panel">
            <div class="code-runner-header">
                <h3 id="code-runner-dialog-title" class="code-runner-title">
                    <i class="mdi mdi-play-circle-outline" aria-hidden="true" />
                    <span v-if="currentListingInfo">{{ currentListingInfo.title }}</span>
                    <span v-else>Interactive Code Runner</span>
                </h3>
                <button
                    class="code-runner-close-btn"
                    aria-label="Close code runner"
                    title="Close code runner"
                    type="button"
                    @click="closeCodeRunner"
                >
                    <i class="mdi mdi-close" aria-hidden="true" />
                </button>
            </div>

            <div class="code-runner-editor-container">
                <div v-show="!codeRunnerLoading && !codeRunnerError" class="code-runner-editor-header">
                    <h4>Editor</h4>
                    <div class="code-runner-buttons">
                        <button
                            class="code-runner-run-btn"
                            :disabled="isRunning"
                            aria-label="Run code"
                            type="button"
                            @click="runCode"
                        >
                            <i :class="isRunning ? 'mdi mdi-loading mdi-spin' : 'mdi mdi-play'" aria-hidden="true" />
                            <span v-if="isRunning">Running...</span>
                            <span v-else>Run</span>
                        </button>
                        <button
                            class="code-runner-clear-btn"
                            :disabled="isRunning"
                            aria-label="Clear editor"
                            type="button"
                            @click="clearEditor"
                        >
                            <i class="mdi mdi-eraser" aria-hidden="true" />
                            Clear
                        </button>
                    </div>
                </div>

                <iframe
                    class="code-runner-editor"
                    title="C# Code Editor"
                    v-show="!codeRunnerLoading && !codeRunnerError"
                />

                <div v-if="codeRunnerLoading" class="code-runner-loading">
                    <div class="code-runner-spinner" />
                    <div class="code-runner-loading-text">Loading editor...</div>
                </div>

                <div v-if="codeRunnerError" class="code-runner-error">
                    <i class="mdi mdi-alert-circle-outline" aria-hidden="true" />
                    <p>{{ codeRunnerError }}</p>
                    <button
                        class="code-runner-retry-btn"
                        aria-label="Retry loading the code runner"
                        type="button"
                        @click="retryCodeRunner"
                    >
                        <i class="mdi mdi-refresh" aria-hidden="true" />
                        Retry
                    </button>
                </div>
            </div>

            <div class="code-runner-output-container">
                <div class="code-runner-output-header">
                    <h4>
                        <i class="mdi mdi-console" aria-hidden="true" />
                        Output
                    </h4>
                    <button
                        class="code-runner-clear-output-btn"
                        aria-label="Clear output"
                        type="button"
                        @click="clearOutput"
                    >
                        <i class="mdi mdi-delete-outline" aria-hidden="true" />
                        Clear
                    </button>
                </div>
                <pre
                    class="code-runner-output"
                    :class="{ error: codeRunnerOutputError }"
                    role="log"
                    aria-live="polite"
                    aria-label="Code execution output"
                >{{ codeRunnerOutput }}</pre>
            </div>
        </div>
    </div>
</template>
