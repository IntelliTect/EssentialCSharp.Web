<script setup>
import { inject } from "vue";
import { useChatWidget } from "../../wwwroot/js/chat-module.js";

const shell = inject("shell");
const {
    isAuthenticated,
    showChatDialog,
    chatMessages,
    chatInput,
    isTyping,
    chatMessagesEl,
    chatInputField,
    openChatDialog,
    closeChatDialog,
    clearChatHistory,
    formatMessage,
    getErrorMessageClass,
    getErrorIconClass,
    sendChatMessage
} = useChatWidget();
</script>

<template>
    <div class="chat-widget">
        <button
            class="chat-button elevation-6"
            :class="{ 'chat-button--active': showChatDialog }"
            :aria-expanded="showChatDialog"
            :aria-label="isAuthenticated ? 'Open AI Chat Assistant' : 'Login required for AI Chat'"
            :title="isAuthenticated ? 'Chat with AI Assistant about C# programming' : 'Login required to use chat'"
            type="button"
            @click="openChatDialog"
        >
            <i class="mdi mdi-robot" aria-hidden="true" />
            <span v-if="!shell.smallScreen" class="chat-button-text">AI Chat</span>
        </button>

        <div
            v-if="showChatDialog"
            class="chat-overlay"
            role="dialog"
            aria-labelledby="chat-dialog-title"
            aria-modal="true"
            tabindex="-1"
            @click.self="closeChatDialog"
        >
            <div class="chat-card elevation-12">
                <div class="chat-header">
                    <h2 id="chat-dialog-title" class="chat-title">
                        <i class="mdi mdi-robot me-2" aria-hidden="true" />
                        AI Assistant
                    </h2>
                    <div class="chat-header-actions">
                        <v-menu>
                            <template #activator="{ props }">
                                <v-btn
                                    icon="mdi-dots-vertical"
                                    variant="text"
                                    size="small"
                                    aria-label="Chat options menu"
                                    title="Chat options"
                                    v-bind="props"
                                />
                            </template>

                            <v-list>
                                <v-list-item
                                    :disabled="chatMessages.length === 0"
                                    prepend-icon="mdi-delete-outline"
                                    @click="clearChatHistory"
                                >
                                    <v-list-item-title>Clear History</v-list-item-title>
                                </v-list-item>
                            </v-list>
                        </v-menu>

                        <button
                            class="chat-close-button"
                            aria-label="Close chat dialog"
                            title="Close chat"
                            type="button"
                            @click="closeChatDialog"
                        >
                            <i class="mdi mdi-close" aria-hidden="true" />
                        </button>
                    </div>
                </div>

                <div
                    ref="chatMessagesEl"
                    class="chat-messages"
                    role="log"
                    aria-live="polite"
                    aria-label="Chat conversation"
                >
                    <div v-if="chatMessages.length === 0 && isAuthenticated" class="welcome-message">
                        <i class="mdi mdi-chat-outline" aria-hidden="true" />
                        <p>Hi! I'm your AI assistant. Ask me anything about C# programming!</p>
                    </div>

                    <div v-if="!isAuthenticated" class="login-required-message">
                        <i class="mdi mdi-lock-outline" aria-hidden="true" />
                        <h3>Login Required</h3>
                        <p>Please log in to chat with the AI assistant about C# programming.</p>
                        <a
                            href="/Identity/Account/Login"
                            class="btn btn-primary mt-3"
                            aria-label="Go to login page"
                        >
                            <i class="mdi mdi-login me-2" aria-hidden="true" />
                            Login
                        </a>
                    </div>

                    <div
                        v-for="(message, index) in chatMessages"
                        :key="index"
                        v-show="isAuthenticated || message.role === 'error'"
                    >
                        <div v-if="message.role === 'error'" :class="getErrorMessageClass(message.errorType)">
                            <i :class="getErrorIconClass(message.errorType)" />
                            <div class="message-text">
                                <h4 v-if="message.errorType === 'rate-limit'">Rate Limit Reached</h4>
                                <h4 v-else-if="message.errorType === 'auth-error'">Authentication Required</h4>
                                <h4 v-else-if="message.errorType === 'captcha-error'">Verification Required</h4>
                                <h4 v-else-if="message.errorType === 'validation-error'">Invalid Input</h4>
                                <h4 v-else>Error</h4>
                                <p v-html="formatMessage(message.content)" />
                                <div v-if="message.errorType === 'rate-limit'" class="retry-info">
                                    Please wait before sending another message
                                </div>
                            </div>
                        </div>

                        <div
                            v-else-if="isAuthenticated"
                            class="message-wrapper"
                            :class="message.role"
                        >
                            <div class="message-bubble" :class="message.role">
                                <div class="message-content" v-html="formatMessage(message.content)" />
                            </div>
                        </div>
                    </div>

                    <div v-if="isTyping && isAuthenticated" class="message-wrapper assistant">
                        <div class="message-bubble assistant">
                            <div class="typing-indicator">
                                <span class="typing-text">AI is thinking</span>
                                <div class="typing-dots" aria-hidden="true">
                                    <span />
                                    <span />
                                    <span />
                                </div>
                            </div>
                        </div>
                    </div>
                </div>

                <div v-if="isAuthenticated" class="chat-footer">
                    <form class="chat-form" @submit.prevent="sendChatMessage">
                        <div class="input-wrapper">
                            <label for="chat-input" class="visually-hidden">
                                Enter your message about C# programming
                            </label>
                            <input
                                id="chat-input"
                                ref="chatInputField"
                                v-model="chatInput"
                                class="chat-input"
                                placeholder="Ask me about C#..."
                                :disabled="isTyping || !isAuthenticated"
                                autocomplete="off"
                                aria-describedby="chat-input-help"
                                maxlength="500"
                            />
                            <button
                                type="submit"
                                class="send-button"
                                :disabled="isTyping || !chatInput.trim() || !isAuthenticated"
                                aria-label="Send message"
                                title="Send message"
                            >
                                <i class="mdi mdi-send" aria-hidden="true" />
                            </button>
                        </div>
                        <div id="chat-input-help" class="visually-hidden">
                            Type your question and press Enter or click send. Maximum 500 characters.
                        </div>
                    </form>
                </div>
            </div>
        </div>
    </div>
</template>
