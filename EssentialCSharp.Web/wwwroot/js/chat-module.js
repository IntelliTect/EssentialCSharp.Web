// Chat Module - Vue.js composable for AI chat functionality
import { ref, nextTick } from 'vue';

export function useChatWidget() {
    // Chat state
    const showChatDialog = ref(false);
    const chatMessages = ref([]);
    const chatInput = ref('');
    const isTyping = ref(false);
    const chatMessagesEl = ref(null);
    const chatInputField = ref(null);

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
            content: userMessage
        });

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
                    enableContextualSearch: true
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
                content: ''
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
                            // We can handle responseId if needed for conversation history
                            else if (parsed.type === 'responseId' && parsed.data) {
                                // Store responseId for future use if needed
                                console.log('Response ID:', parsed.data);
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
                content: 'Sorry, I encountered an error while processing your request. Please try again.'
            });
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
