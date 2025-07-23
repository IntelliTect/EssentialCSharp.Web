// Chat Module - Vue.js composable for AI chat functionality
import { ref, nextTick } from 'vue';

export function useChatWidget() {
    // Chat state with persistence
    const showChatDialog = ref(false);
    const chatMessages = ref([]);
    const chatInput = ref('');
    const isTyping = ref(false);
    const chatMessagesEl = ref(null);
    const chatInputField = ref(null);
    const lastResponseId = ref(null);

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

    // Save chat history to localStorage
    function saveChatHistory() {
        try {
            const data = {
                messages: chatMessages.value,
                lastResponseId: lastResponseId.value,
                timestamp: Date.now()
            };
            localStorage.setItem('aiChatHistory', JSON.stringify(data));
        } catch (error) {
            console.warn('Failed to save chat history:', error);
        }
    }

    // Initialize chat history on load
    loadChatHistory();

    // Chat functions  
    function openChatDialog() {
        showChatDialog.value = true;
        nextTick(() => {
            if (chatInputField.value) {
                chatInputField.value.focus();
            }
            scrollToBottom();
        });
    }

    function closeChatDialog() {
        showChatDialog.value = false;
    }

    function clearChat() {
        chatMessages.value = [];
        lastResponseId.value = null;
        saveChatHistory();
    }

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

    async function sendChatMessage() {
        if (!chatInput.value.trim() || isTyping.value) return;

        const userMessage = chatInput.value.trim();
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

        try {
            const response = await fetch('/api/chat/stream', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify({
                    message: userMessage,
                    enableContextualSearch: true,
                    previousResponseId: lastResponseId.value
                })
            });

            if (!response.ok) {
                throw new Error(`HTTP error! status: ${response.status}`);
            }

            // Handle streaming response
            const reader = response.body.getReader();
            const decoder = new TextDecoder();
            let assistantMessage = '';

            // Add empty assistant message that we'll update
            chatMessages.value.push({
                role: 'assistant',
                content: '',
                timestamp: new Date().toISOString()
            });

            const assistantMessageIndex = chatMessages.value.length - 1;

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
            chatMessages.value.push({
                role: 'assistant',
                content: 'Sorry, I encountered an error while processing your request. Please try again.',
                timestamp: new Date().toISOString()
            });
            saveChatHistory();
        } finally {
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
        showChatDialog,
        chatMessages,
        chatInput,
        isTyping,
        chatMessagesEl,
        chatInputField,
        
        // Methods
        openChatDialog,
        closeChatDialog,
        clearChat,
        formatMessage,
        sendChatMessage
    };
}
