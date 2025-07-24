// Chat Module - Vue.js composable for AI chat functionality
import { ref, nextTick, watch } from 'vue';

export function useChatWidget() {
    // Authentication state
    const isAuthenticated = ref(window.IS_AUTHENTICATED || false);
    
    // Chat state with persistence
    const showChatDialog = ref(false);
    const chatMessages = ref([]);
    const chatInput = ref('');
    const isTyping = ref(false);
    const chatMessagesEl = ref(null);
    const chatInputField = ref(null);
    const lastResponseId = ref(null);
    // Captcha and throttling state
    const showCaptcha = ref(false);
    const captchaSiteKey = ref(window.HCAPTCHA_SITE_KEY || '');
    const captchaResponse = ref('');
    const throttlingStatus = ref({ RequiresCaptcha: false, CurrentUsageCount: 0, ThresholdForCaptcha: 10 });

    // Load chat history from localStorage on initialization
    function loadChatHistory() {
        try {
            const saved = localStorage.getItem('aiChatHistory');
            if (saved) {
                const data = JSON.parse(saved);
                chatMessages.value = data.messages || [];
                lastResponseId.value = data.lastResponseId || null;
            }
        } catch (error) {
            console.warn('Failed to load chat history:', error);
        }
    }

    // Save chat history to localStorage with message limits
    function saveChatHistory() {
        try {
            // Limit messages to prevent memory issues (keep last 100 messages)
            const maxMessages = 100;
            const messagesToSave = chatMessages.value.slice(-maxMessages);
            
            const data = {
                messages: messagesToSave,
                lastResponseId: lastResponseId.value,
                timestamp: Date.now()
            };
            localStorage.setItem('aiChatHistory', JSON.stringify(data));
        } catch (error) {
            console.warn('Failed to save chat history:', error);
            // If localStorage is full, try clearing and saving only recent messages
            try {
                const recentMessages = chatMessages.value.slice(-20);
                const fallbackData = {
                    messages: recentMessages,
                    lastResponseId: lastResponseId.value,
                    timestamp: Date.now()
                };
                localStorage.setItem('aiChatHistory', JSON.stringify(fallbackData));
            } catch (fallbackError) {
                console.error('Failed to save even minimal chat history:', fallbackError);
            }
        }
    }

    // Initialize chat history on load
    loadChatHistory();

    // Clear chat if user is not authenticated
    if (!isAuthenticated.value) {
        chatMessages.value = [];
        lastResponseId.value = null;
    }

    // Watch for authentication changes and clear chat when user logs out
    watch(isAuthenticated, (newAuth, oldAuth) => {
        if (oldAuth === true && newAuth === false) {
            // User logged out, clear chat
            clearChat();
        }
    });

    // Chat functions  
    function openChatDialog() {
        // Update authentication status in case it changed without page refresh
        isAuthenticated.value = window.IS_AUTHENTICATED || false;
        
        showChatDialog.value = true;
        nextTick(() => {
            if (chatInputField.value && isAuthenticated.value) {
                chatInputField.value.focus();
            }
            scrollToBottom();
        });
    }

    function closeChatDialog() {
        showChatDialog.value = false;
        // Reset captcha when closing
        showCaptcha.value = false;
        captchaResponse.value = '';
        if (window.hcaptcha && document.getElementById('hcaptcha-modal')) {
            window.hcaptcha.reset('hcaptcha-modal');
        }
    }

    function clearChat() {
        chatMessages.value = [];
        lastResponseId.value = null;
        saveChatHistory();
        // Reset captcha state
        showCaptcha.value = false;
        captchaResponse.value = '';
        if (window.hcaptcha && document.getElementById('hcaptcha-modal')) {
            window.hcaptcha.reset('hcaptcha-modal');
        }
        // Refresh throttling status
        if (isAuthenticated.value) {
            checkThrottlingStatus();
        }
    }

    // Check throttling status from server
    async function checkThrottlingStatus() {
        if (!isAuthenticated.value) return;
        
        try {
            const response = await fetch('/api/chat/throttling-status', {
                method: 'GET',
                headers: {
                    'Content-Type': 'application/json',
                }
            });

            if (response.ok) {
                throttlingStatus.value = await response.json();
                showCaptcha.value = throttlingStatus.value.RequiresCaptcha;
                
                if (showCaptcha.value) {
                    // Initialize hCaptcha when needed
                    nextTick(() => {
                        if (window.hcaptcha && document.getElementById('hcaptcha-modal')) {
                            window.hcaptcha.render('hcaptcha-modal', {
                                sitekey: captchaSiteKey.value,
                                callback: 'onCaptchaSuccess',
                                'expired-callback': 'onCaptchaExpired',
                                'error-callback': 'onCaptchaError'
                            });
                        }
                    });
                }
            }
        } catch (error) {
            console.warn('Failed to check throttling status:', error);
        }
    }

    // Captcha callback functions (need to be global)
    window.onCaptchaSuccess = function(token) {
        captchaResponse.value = token;
        showCaptcha.value = false;
    };

    window.onCaptchaExpired = function() {
        captchaResponse.value = '';
        showCaptcha.value = true;
    };

    window.onCaptchaError = function(error) {
        console.error('hCaptcha error:', error);
        captchaResponse.value = '';
        showCaptcha.value = true;
    };

    function scrollToBottom() {
        if (chatMessagesEl.value) {
            nextTick(() => {
                chatMessagesEl.value.scrollTop = chatMessagesEl.value.scrollHeight;
            });
        }
    }

    function formatMessage(content) {
        if (!content) return '';
        
        // Use marked.js for markdown rendering with DOMPurify sanitization
        if (typeof window.marked !== 'undefined' && typeof window.DOMPurify !== 'undefined') {
            const rawHtml = window.marked.parse(content);
            return window.DOMPurify.sanitize(rawHtml);
        }
        
        // Fallback to simple line break replacement
        return content.replace(/\n/g, '<br>');
    }

    function getErrorMessageClass(errorType) {
        if (errorType === 'rate-limit') {
            return 'rate-limit-error';
        } else if (errorType === 'auth-error') {
            return 'error-message';
        } else if (errorType === 'captcha-error') {
            return 'error-message';
        } else if (errorType === 'validation-error') {
            return 'error-message';
        } else {
            return 'error-message';
        }
    }

    function getErrorIconClass(errorType) {
        if (errorType === 'rate-limit') {
            return 'fas fa-clock';
        } else if (errorType === 'auth-error') {
            return 'fas fa-lock';
        } else if (errorType === 'captcha-error') {
            return 'fas fa-shield-alt';
        } else if (errorType === 'validation-error') {
            return 'fas fa-exclamation-circle';
        } else if (errorType === 'network-error') {
            return 'fas fa-wifi';
        } else if (errorType === 'connection-error') {
            return 'fas fa-plug';
        } else {
            return 'fas fa-exclamation-triangle';
        }
    }

    async function sendChatMessage() {
        if (!chatInput.value.trim() || isTyping.value) return;

        // Check authentication first
        if (!isAuthenticated.value) {
            chatMessages.value.push({
                role: 'error',
                errorType: 'auth-error',
                content: 'You must be logged in to use the chat feature. Please log in and try again.',
                timestamp: new Date().toISOString()
            });
            saveChatHistory();
            return;
        }

        const userMessage = chatInput.value.trim();
        
        // Client-side validation
        if (userMessage.length > 500) {
            chatMessages.value.push({
                role: 'error',
                errorType: 'validation-error',
                content: 'Your message is too long. Please keep it under 500 characters.',
                timestamp: new Date().toISOString()
            });
            saveChatHistory();
            return;
        }
        
        chatInput.value = '';

        // Add user message
        chatMessages.value.push({
            role: 'user',
            content: userMessage,
            timestamp: new Date().toISOString()
        });

        // Save immediately after adding user message
        saveChatHistory();

        // Show typing indicator
        isTyping.value = true;

        // Scroll to bottom
        nextTick(() => {
            if (chatMessagesEl.value) {
                chatMessagesEl.value.scrollTop = chatMessagesEl.value.scrollHeight;
            }
        });

        let reader = null;
        try {
            const requestBody = {
                message: userMessage,
                enableContextualSearch: true,
                previousResponseId: lastResponseId.value
            };

            // Include captcha response if available
            if (captchaResponse.value) {
                requestBody.captchaResponse = captchaResponse.value;
            }

            const response = await fetch('/api/chat/stream', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify(requestBody)
            });

            if (!response.ok) {
                if (response.status === 401) {
                    throw new Error('Authentication required');
                } else if (response.status === 429) {
                    // Handle rate limiting
                    let errorData;
                    try {
                        errorData = await response.json();
                    } catch (e) {
                        errorData = { 
                            error: 'Rate limit exceeded. Please wait before sending another message.',
                            retryAfter: 60,
                            requiresCaptcha: true
                        };
                    }
                    
                    // Show captcha if required (rate limiting triggers captcha requirement)
                    if (errorData.requiresCaptcha) {
                        showCaptcha.value = true;
                        captchaResponse.value = '';
                        
                        // Initialize hCaptcha
                        nextTick(() => {
                            if (window.hcaptcha && document.getElementById('hcaptcha-modal')) {
                                window.hcaptcha.render('hcaptcha-modal', {
                                    sitekey: captchaSiteKey.value,
                                    callback: 'onCaptchaSuccess',
                                    'expired-callback': 'onCaptchaExpired',
                                    'error-callback': 'onCaptchaError'
                                });
                            }
                        });
                    }
                    
                    const retryAfter = errorData.retryAfter || 60;
                    const errorMessage = errorData.requiresCaptcha 
                        ? `Rate limit exceeded. Please complete the captcha below and wait ${Math.ceil(retryAfter)} seconds before sending another message.`
                        : `Rate limit exceeded. Please wait ${Math.ceil(retryAfter)} seconds before sending another message.`;
                    
                    throw new Error(errorMessage);
                } else if (response.status === 400) {
                    // Handle captcha requirement
                    const errorData = await response.json();
                    if (errorData.requiresCaptcha) {
                        showCaptcha.value = true;
                        captchaResponse.value = '';
                        
                        // Initialize hCaptcha
                        nextTick(() => {
                            if (window.hcaptcha && document.getElementById('hcaptcha-modal')) {
                                window.hcaptcha.render('hcaptcha-modal', {
                                    sitekey: captchaSiteKey.value,
                                    callback: 'onCaptchaSuccess',
                                    'expired-callback': 'onCaptchaExpired',
                                    'error-callback': 'onCaptchaError'
                                });
                            }
                        });
                        
                        throw new Error(errorData.error || 'Captcha verification required');
                    }
                    throw new Error(errorData.error || 'Bad request');
                }
                throw new Error(`HTTP error! status: ${response.status}`);
            }

            // Reset captcha after successful request
            captchaResponse.value = '';
            showCaptcha.value = false;

            // Handle streaming response
            reader = response.body.getReader();
            const decoder = new TextDecoder();
            let assistantMessage = '';
            let assistantMessageIndex = -1;
            let hasStartedStreaming = false;

            while (true) {
                const { done, value } = await reader.read();
                if (done) break;

                const chunk = decoder.decode(value);
                const lines = chunk.split('\n');

                for (const line of lines) {
                    if (line.startsWith('data: ')) {
                        const data = line.slice(6);
                        if (data === '[DONE]') {
                            isTyping.value = false;
                            // Save final state
                            saveChatHistory();
                            continue;
                        }

                        try {
                            const parsed = JSON.parse(data);
                            if (parsed.type === 'text' && parsed.data) {
                                // If this is the first chunk, hide typing indicator and add assistant message
                                if (!hasStartedStreaming) {
                                    isTyping.value = false;
                                    chatMessages.value.push({
                                        role: 'assistant',
                                        content: '',
                                        timestamp: new Date().toISOString()
                                    });
                                    assistantMessageIndex = chatMessages.value.length - 1;
                                    hasStartedStreaming = true;
                                }
                                
                                assistantMessage += parsed.data;
                                chatMessages.value[assistantMessageIndex].content = assistantMessage;

                                // Scroll to bottom
                                nextTick(() => {
                                    if (chatMessagesEl.value) {
                                        chatMessagesEl.value.scrollTop = chatMessagesEl.value.scrollHeight;
                                    }
                                });
                            }
                            // Store responseId for conversation continuity
                            else if (parsed.type === 'responseId' && parsed.data) {
                                lastResponseId.value = parsed.data;
                            }
                        } catch (e) {
                            console.warn('Failed to parse SSE data:', data);
                        }
                    }
                }
            }

        } catch (error) {
            console.error('Chat error:', error);
            
            // Hide typing indicator if still showing
            isTyping.value = false;
            
            // Provide more specific error messages with types
            let errorMessage = 'Sorry, I encountered an error while processing your request. Please try again.';
            let errorType = 'error';
            
            if (error.name === 'AbortError') {
                errorMessage = 'Request was cancelled. Please try again.';
                errorType = 'error';
            } else if (error.message?.includes('Authentication required')) {
                errorMessage = 'You must be logged in to use the chat feature. Please log in and try again.';
                errorType = 'auth-error';
                isAuthenticated.value = false; // Update auth state
            } else if (error.message?.includes('Rate limit exceeded')) {
                errorMessage = error.message; // Use the specific rate limit message with timing
                errorType = 'rate-limit';
            } else if (error.message?.includes('Captcha verification')) {
                errorMessage = error.message; // Use the captcha-specific message
                errorType = 'captcha-error';
            } else if (error.message?.includes('HTTP error')) {
                errorMessage = 'Unable to connect to the chat service. Please check your connection and try again.';
                errorType = 'connection-error';
            } else if (error.message?.includes('Failed to fetch')) {
                errorMessage = 'Network error. Please check your internet connection and try again.';
                errorType = 'network-error';
            }
            
            chatMessages.value.push({
                role: 'error',
                errorType: errorType,
                content: errorMessage,
                timestamp: new Date().toISOString()
            });
            saveChatHistory();
        } finally {
            // Ensure reader is properly closed
            if (reader) {
                try {
                    await reader.cancel();
                } catch (e) {
                    console.warn('Failed to cancel reader:', e);
                }
            }
            
            // Ensure typing indicator is hidden
            isTyping.value = false;
            
            // Focus back on input
            nextTick(() => {
                if (chatInputField.value) {
                    chatInputField.value.focus();
                }
            });
        }
    }

    // Clean up old chat sessions (keep only last 7 days)
    function cleanupOldSessions() {
        try {
            const saved = localStorage.getItem('aiChatHistory');
            if (saved) {
                const data = JSON.parse(saved);
                const sevenDaysAgo = Date.now() - (7 * 24 * 60 * 60 * 1000);
                
                if (data.timestamp && data.timestamp < sevenDaysAgo) {
                    localStorage.removeItem('aiChatHistory');
                    chatMessages.value = [];
                    lastResponseId.value = null;
                }
            }
        } catch (error) {
            console.warn('Failed to cleanup old sessions:', error);
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
        chatMessagesEl,
        chatInputField,
        showCaptcha,
        captchaSiteKey,
        throttlingStatus,
        
        // Methods
        openChatDialog,
        closeChatDialog,
        clearChat,
        formatMessage,
        getErrorMessageClass,
        getErrorIconClass,
        sendChatMessage,
        checkThrottlingStatus
    };
}
