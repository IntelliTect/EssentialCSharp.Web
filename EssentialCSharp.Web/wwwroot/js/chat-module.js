// Chat Module - Vue.js composable for AI chat functionality
import DOMPurify from "dompurify";
import { marked } from "marked";
import { ref, nextTick, watch } from "vue";

const CHAT_HISTORY_KEY = "aiChatHistory";
const CHAT_HISTORY_RETENTION_DAYS = 7;
const MAX_MESSAGE_LENGTH = 500;
const MAX_SAVED_MESSAGES = 100;
const MINIMAL_SAVED_MESSAGES = 20;
const CAPTCHA_SCRIPT_TIMEOUT_MS = 10000;
const CAPTCHA_TIMEOUT_MS = 15000;

const DEFAULT_ERROR_DISPLAY = {
    heading: "Error",
    className: "error-message",
    iconClass: "fas fa-exclamation-triangle"
};

const ERROR_DISPLAY = {
    "auth-error": {
        ...DEFAULT_ERROR_DISPLAY,
        heading: "Authentication Required",
        iconClass: "fas fa-lock"
    },
    "captcha-error": {
        ...DEFAULT_ERROR_DISPLAY,
        heading: "Verification Required"
    },
    "validation-error": {
        ...DEFAULT_ERROR_DISPLAY,
        heading: "Invalid Input",
        iconClass: "fas fa-exclamation-circle"
    },
    "rate-limit": {
        ...DEFAULT_ERROR_DISPLAY,
        heading: "Rate Limit Reached",
        className: "rate-limit-error",
        iconClass: "fas fa-clock"
    },
    "network-error": {
        ...DEFAULT_ERROR_DISPLAY,
        iconClass: "fas fa-wifi"
    },
    "connection-error": {
        ...DEFAULT_ERROR_DISPLAY,
        iconClass: "fas fa-plug"
    }
};

function nowIso() {
    return new Date().toISOString();
}

function createChatError(errorType, message) {
    const error = new Error(message);
    error.chatErrorType = errorType;
    return error;
}

export function useChatWidget() {
    // Authentication state
    const isAuthenticated = ref(window.IS_AUTHENTICATED || false);

    // Chat state with persistence
    const showChatDialog = ref(false);
    const chatMessages = ref([]);
    const chatInput = ref("");
    const isTyping = ref(false);
    const isSubmitting = ref(false);
    const chatMessagesEl = ref(null);
    const chatInputField = ref(null);
    const lastResponseId = ref(null);

    // hCaptcha invisible widget state
    const captchaContainerEl = ref(null);
    let captchaWidgetId = null;
    let captchaResolve = null;
    let captchaReject = null;

    function resetConversationState() {
        chatMessages.value = [];
        lastResponseId.value = null;
    }

    function buildChatHistoryData(messages) {
        return {
            messages,
            lastResponseId: lastResponseId.value,
            timestamp: Date.now()
        };
    }

    function readSavedChatHistory() {
        const saved = localStorage.getItem(CHAT_HISTORY_KEY);
        return saved ? JSON.parse(saved) : null;
    }

    function saveChatHistorySnapshot(messages) {
        localStorage.setItem(CHAT_HISTORY_KEY, JSON.stringify(buildChatHistoryData(messages)));
    }

    // Load chat history from localStorage on initialization
    function loadChatHistory() {
        try {
            const data = readSavedChatHistory();
            if (data) {
                chatMessages.value = data.messages || [];
                lastResponseId.value = data.lastResponseId || null;
            }
        } catch (error) {
            console.warn("Failed to load chat history:", error);
        }
    }

    // Save chat history to localStorage with message limits
    function saveChatHistory() {
        try {
            saveChatHistorySnapshot(chatMessages.value.slice(-MAX_SAVED_MESSAGES));
        } catch (error) {
            console.warn("Failed to save chat history:", error);

            try {
                saveChatHistorySnapshot(chatMessages.value.slice(-MINIMAL_SAVED_MESSAGES));
            } catch (fallbackError) {
                console.error("Failed to save even minimal chat history:", fallbackError);
            }
        }
    }

    function focusChatInput() {
        nextTick(() => {
            if (chatInputField.value) {
                chatInputField.value.focus();
            }
        });
    }

    function scrollToBottom() {
        nextTick(() => {
            if (chatMessagesEl.value) {
                chatMessagesEl.value.scrollTop = chatMessagesEl.value.scrollHeight;
            }
        });
    }

    function createMessage(role, content, extra = {}) {
        return {
            role,
            content,
            timestamp: nowIso(),
            ...extra
        };
    }

    function pushMessage(role, content, extra = {}) {
        chatMessages.value.push(createMessage(role, content, extra));
        return chatMessages.value.length - 1;
    }

    function pushError(errorType, content) {
        pushMessage("error", content, { errorType });
        saveChatHistory();
    }

    function restorePendingUserMessage(userMessageIndex, userMessage) {
        if (userMessageIndex >= 0 && userMessageIndex < chatMessages.value.length) {
            chatMessages.value.splice(userMessageIndex, 1);
        }

        chatInput.value = userMessage;
    }

    function getErrorDisplay(errorType) {
        return ERROR_DISPLAY[errorType] || DEFAULT_ERROR_DISPLAY;
    }

    // Initialize chat history on load
    loadChatHistory();

    // Clear chat if user is not authenticated
    if (!isAuthenticated.value) {
        resetConversationState();
    }

    // Watch for authentication changes and clear chat when user logs out
    watch(isAuthenticated, (newAuth, oldAuth) => {
        if (oldAuth === true && newAuth === false) {
            clearChatHistory();
        }
    });

    // Chat functions
    function openChatDialog() {
        isAuthenticated.value = window.IS_AUTHENTICATED || false;
        showChatDialog.value = true;

        if (isAuthenticated.value) {
            focusChatInput();
        }

        scrollToBottom();
    }

    function closeChatDialog() {
        showChatDialog.value = false;
    }

    function clearChatHistory() {
        resetConversationState();
        saveChatHistory();

        nextTick(() => {
            if (chatMessagesEl.value) {
                chatMessagesEl.value.scrollTop = 0;
            }
        });
    }

    function resetCaptchaCallbacks() {
        captchaResolve = null;
        captchaReject = null;
    }

    // Captcha callbacks used by the hCaptcha invisible widget during chat requests.
    function onCaptchaSuccess(token) {
        if (!captchaResolve) {
            return;
        }

        const resolve = captchaResolve;
        resetCaptchaCallbacks();
        resolve(token);
    }

    function onCaptchaExpired() {
        if (!captchaReject) {
            return;
        }

        const reject = captchaReject;
        resetCaptchaCallbacks();
        reject(new Error("Captcha expired"));
    }

    function onCaptchaError() {
        if (!captchaReject) {
            return;
        }

        const reject = captchaReject;
        resetCaptchaCallbacks();
        reject(new Error("Captcha error"));
    }

    async function ensureCaptchaWidget() {
        const siteKey = window.HCAPTCHA_SITE_KEY?.trim();
        if (!siteKey) {
            throw new Error("Captcha is not configured.");
        }

        await nextTick();

        if (captchaWidgetId !== null) {
            return;
        }

        if (!captchaContainerEl.value) {
            throw new Error("Captcha container is missing.");
        }

        await new Promise((resolve, reject) => {
            const timeout = setTimeout(() => reject(new Error("Captcha script is not ready.")), CAPTCHA_SCRIPT_TIMEOUT_MS);
            window.EssentialCSharp.HCaptcha.whenHcaptchaReady(() => {
                clearTimeout(timeout);
                resolve();
            });
        });

        captchaWidgetId = window.hcaptcha.render(captchaContainerEl.value, {
            sitekey: siteKey,
            size: "invisible",
            callback: onCaptchaSuccess,
            "expired-callback": onCaptchaExpired,
            "error-callback": onCaptchaError
        });
    }

    async function getFreshCaptchaToken() {
        await ensureCaptchaWidget();

        return await new Promise((resolve, reject) => {
            const timeoutId = setTimeout(() => {
                if (!captchaReject) {
                    return;
                }

                const rejectCaptcha = captchaReject;
                resetCaptchaCallbacks();
                rejectCaptcha(new Error("Captcha timed out"));
            }, CAPTCHA_TIMEOUT_MS);

            captchaResolve = (token) => {
                clearTimeout(timeoutId);
                resolve(token);
            };
            captchaReject = (error) => {
                clearTimeout(timeoutId);
                reject(error);
            };

            window.hcaptcha.reset(captchaWidgetId);
            window.hcaptcha.execute(captchaWidgetId);
        });
    }
    // The captcha service can still be used elsewhere in the application

    function formatMessage(content) {
        if (!content) {
            return "";
        }

        const rawHtml = marked.parse(content);
        return DOMPurify.sanitize(rawHtml);
    }

    function getErrorMessageClass(errorType) {
        return getErrorDisplay(errorType).className;
    }

    function getErrorIconClass(errorType) {
        return getErrorDisplay(errorType).iconClass;
    }

    function getErrorHeading(errorType) {
        return getErrorDisplay(errorType).heading;
    }

    function normalizeUnexpectedChatError(error) {
        if (error?.chatErrorType && error?.message) {
            return {
                errorType: error.chatErrorType,
                errorMessage: error.message
            };
        }

        if (error?.name === "AbortError") {
            return {
                errorType: "error",
                errorMessage: "Request was cancelled. Please try again."
            };
        }

        if (error?.message?.includes("Failed to fetch")) {
            return {
                errorType: "network-error",
                errorMessage: "Network error. Please check your internet connection and try again."
            };
        }

        return {
            errorType: "error",
            errorMessage: "Sorry, I encountered an error while processing your request. Please try again."
        };
    }

    async function tryReadJson(response, fallback = {}) {
        try {
            return await response.json();
        } catch {
            return fallback;
        }
    }

    function extractSseLines(buffer, flushRemainder = false) {
        const lines = buffer.split("\n");

        if (flushRemainder) {
            return {
                lines,
                remainder: ""
            };
        }

        return {
            lines: lines.slice(0, -1),
            remainder: lines[lines.length - 1] ?? ""
        };
    }

    function handleStreamLine(line, streamState) {
        const trimmedLine = line.trimEnd();
        if (!trimmedLine.startsWith("data: ")) {
            return;
        }

        const data = trimmedLine.slice(6);
        if (data === "[DONE]") {
            isTyping.value = false;
            saveChatHistory();
            return;
        }

        let parsed;
        try {
            parsed = JSON.parse(data);
        } catch {
            console.warn("Failed to parse SSE data:", data);
            return;
        }

        if (parsed.type === "text" && parsed.data) {
            if (!streamState.hasStartedStreaming) {
                isTyping.value = false;
                streamState.assistantMessageIndex = pushMessage("assistant", "");
                streamState.hasStartedStreaming = true;
            }

            streamState.assistantMessage += parsed.data;
            chatMessages.value[streamState.assistantMessageIndex].content = streamState.assistantMessage;
            scrollToBottom();
            return;
        }

        if (parsed.type === "responseId" && parsed.data) {
            lastResponseId.value = parsed.data;
            return;
        }

        if (parsed.type === "error") {
            throw createChatError("connection-error", parsed.message || parsed.data || "Stream interrupted. Please try again.");
        }
    }

    async function consumeChatStream(reader) {
        const decoder = new TextDecoder();
        let bufferedChunk = "";
        const streamState = {
            assistantMessage: "",
            assistantMessageIndex: -1,
            hasStartedStreaming: false
        };

        while (true) {
            const { done, value } = await reader.read();
            if (done) {
                bufferedChunk += decoder.decode();
                const { lines } = extractSseLines(bufferedChunk, true);
                for (const line of lines) {
                    handleStreamLine(line, streamState);
                }
                break;
            }

            bufferedChunk += decoder.decode(value, { stream: true });

            const { lines, remainder } = extractSseLines(bufferedChunk);
            for (const line of lines) {
                handleStreamLine(line, streamState);
            }

            bufferedChunk = remainder;
        }

        if (streamState.hasStartedStreaming) {
            saveChatHistory();
        }
    }

    async function retryCaptchaChallenge(streamResponse, userMessage, userMessageIndex) {
        const errorData = await tryReadJson(streamResponse);
        if (!errorData.retryable) {
            throw createChatError("captcha-error", "Security verification failed. Please try again.");
        }

        let retryToken;
        try {
            retryToken = await getFreshCaptchaToken();
        } catch {
            throw createChatError("captcha-error", "Security verification failed. Please try again.");
        }

        const retryResponse = await fetchChatStream(userMessage, retryToken);
        if (!retryResponse.ok) {
            restorePendingUserMessage(userMessageIndex, userMessage);
            if (retryResponse.status === 403) {
                throw createChatError("captcha-error", "Security verification failed. Please try again.");
            }

            await throwForErrorResponse(retryResponse);
        }

        return retryResponse;
    }

    async function throwForErrorResponse(streamResponse) {
        if (streamResponse.status === 401) {
            isAuthenticated.value = false;
            throw createChatError("auth-error", "You must be logged in to use the chat feature. Please log in and try again.");
        }

        if (streamResponse.status === 429) {
            const errorData = await tryReadJson(streamResponse, {
                error: "Rate limit exceeded. Please wait before sending another message.",
                retryAfter: 60
            });
            const retryAfter = errorData.retryAfter || 60;

            throw createChatError("rate-limit", `Rate limit exceeded. Please wait ${Math.ceil(retryAfter)} seconds before sending another message.`);
        }

        if (streamResponse.status === 400) {
            const errorData = await tryReadJson(streamResponse, { error: "Bad request" });
            throw createChatError("validation-error", errorData.error || "Bad request");
        }

        if (streamResponse.status === 503) {
            const errorData = await tryReadJson(streamResponse);
            if (errorData.errorCode === "captcha_unavailable") {
                throw createChatError("captcha-error", "Security verification is temporarily unavailable. Please try again later.");
            }

            throw createChatError("connection-error", errorData.error || "Service unavailable");
        }

        throw createChatError("connection-error", "Unable to connect to the chat service. Please check your connection and try again.");
    }

    async function ensureSuccessfulStreamResponse(streamResponse, userMessage, userMessageIndex) {
        if (streamResponse.ok) {
            return streamResponse;
        }

        if (streamResponse.status === 403) {
            return await retryCaptchaChallenge(streamResponse, userMessage, userMessageIndex);
        }

        await throwForErrorResponse(streamResponse);
    }

    async function startChatStream(userMessage, captchaToken, userMessageIndex) {
        const streamResponse = await fetchChatStream(userMessage, captchaToken);
        return await ensureSuccessfulStreamResponse(streamResponse, userMessage, userMessageIndex);
    }

    async function sendChatMessage() {
        if (!chatInput.value.trim() || isTyping.value || isSubmitting.value) {
            return;
        }

        if (!isAuthenticated.value) {
            pushError("auth-error", "You must be logged in to use the chat feature. Please log in and try again.");
            return;
        }

        const userMessage = chatInput.value.trim();
        if (userMessage.length > MAX_MESSAGE_LENGTH) {
            pushError("validation-error", `Your message is too long. Please keep it under ${MAX_MESSAGE_LENGTH} characters.`);
            return;
        }

        isSubmitting.value = true;
        let reader = null;
        try {
            let captchaToken;
            try {
                captchaToken = await getFreshCaptchaToken();
            } catch (captchaErr) {
                console.warn("Captcha acquisition failed:", captchaErr);
                pushError("captcha-error", "Security verification failed. Please refresh the page and try again.");
                return;
            }

            chatInput.value = "";

            const userMessageIndex = pushMessage("user", userMessage);
            saveChatHistory();
            isTyping.value = true;
            scrollToBottom();

            const streamResponse = await startChatStream(userMessage, captchaToken, userMessageIndex);
            reader = streamResponse.body?.getReader() ?? null;

            if (!reader) {
                throw createChatError("connection-error", "Unable to connect to the chat service. Please check your connection and try again.");
            }

            await consumeChatStream(reader);
        } catch (error) {
            console.error("Chat error:", error);
            isTyping.value = false;

            const { errorType, errorMessage } = normalizeUnexpectedChatError(error);
            pushError(errorType, errorMessage);
        } finally {
            if (reader) {
                try {
                    await reader.cancel();
                } catch (error) {
                    console.warn("Failed to cancel reader:", error);
                }
            }

            isTyping.value = false;
            isSubmitting.value = false;
            focusChatInput();
        }
    }

    function fetchChatStream(message, captchaToken) {
        return fetch("/api/chat/stream", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                message,
                enableContextualSearch: true,
                previousResponseId: lastResponseId.value,
                captchaToken
            })
        });
    }

    // Clean up old chat sessions (keep only last 7 days)
    function cleanupOldSessions() {
        try {
            const data = readSavedChatHistory();
            if (!data) {
                return;
            }

            const maxAge = CHAT_HISTORY_RETENTION_DAYS * 24 * 60 * 60 * 1000;
            const retentionCutoff = Date.now() - maxAge;

            if (data.timestamp && data.timestamp < retentionCutoff) {
                localStorage.removeItem(CHAT_HISTORY_KEY);
                resetConversationState();
            }
        } catch (error) {
            console.warn("Failed to cleanup old sessions:", error);
        }
    }

    // Run cleanup on initialization
    cleanupOldSessions();

    return {
        // State
        isAuthenticated,
        showChatDialog,
        chatMessages,
        chatInput,
        isTyping,
        isSubmitting,
        chatMessagesEl,
        chatInputField,
        captchaContainerEl,

        // Methods
        openChatDialog,
        closeChatDialog,
        clearChatHistory,
        formatMessage,
        getErrorHeading,
        getErrorMessageClass,
        getErrorIconClass,
        sendChatMessage
    };
}
