/**
 * Chat functionality for Vue.js application
 * Provides reactive chat state and methods for AI chat assistant
 */

export function useChatComposable() {
    // Chat state
    const showChatDialog = ref(false);
    const chatMessages = ref([
        {
            role: 'assistant',
            content: '<strong>👋 Hello!</strong> I\'m your AI assistant with access to Essential C# book content. How can I help you today?'
        }
    ]);
    const chatInput = ref('');
    const isTyping = ref(false);
    const previousResponseId = ref(null);
    let chatInputField = ref(null);
    let chatMessages_el = ref(null);

    /**
     * Opens the chat dialog and focuses the input field
     */
    function openChatDialog() {
        showChatDialog.value = true;
        // Focus input after dialog opens
        setTimeout(() => {
            if (chatInputField.value) {
                chatInputField.value.focus();
            }
        }, 100);
    }

    /**
     * Closes the chat dialog
     */
    function closeChatDialog() {
        showChatDialog.value = false;
    }

    /**
     * Scrolls the chat messages container to the bottom
     */
    function scrollChatToBottom() {
        setTimeout(() => {
            if (chatMessages_el.value) {
                chatMessages_el.value.scrollTop = chatMessages_el.value.scrollHeight;
            }
        }, 50);
    }

    /**
     * Formats message text using markdown and sanitizes HTML
     * @param {string} text - The text to format
     * @returns {string} - Formatted and sanitized HTML
     */
    function formatMessage(text) {
        try {
            // Check if required libraries are available
            if (typeof marked === 'undefined' || typeof DOMPurify === 'undefined') {
                console.warn('Markdown libraries not available, using plain text');
                return String(text || '').replace(/\n/g, '<br>');
            }

            // Ensure text is a string
            const textStr = String(text || '');

            // Configure marked options for better rendering
            marked.setOptions({
                breaks: true,
                gfm: true,
                sanitize: false,
                smartLists: true,
                smartypants: false
            });
            
            // Parse markdown
            const htmlContent = marked.parse(textStr);
            
            // Sanitize the HTML to prevent XSS
            const cleanHtml = DOMPurify.sanitize(htmlContent, {
                ALLOWED_TAGS: ['p', 'br', 'strong', 'em', 'code', 'pre', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6', 'ul', 'ol', 'li', 'blockquote', 'a'],
                ALLOWED_ATTR: ['href', 'title'],
                ALLOW_DATA_ATTR: false
            });
            
            return cleanHtml;
        } catch (error) {
            // Fallback to escaped text if parsing fails
            console.warn('Failed to parse markdown:', error);
            return String(text || '').replace(/\n/g, '<br>');
        }
    }

    /**
     * Sends a chat message to the AI assistant
     */
    async function sendChatMessage() {
        const message = chatInput.value.trim();
        if (!message || isTyping.value) return;

        // Add user message
        chatMessages.value.push({
            role: 'user',
            content: message
        });
        
        chatInput.value = '';
        isTyping.value = true;
        scrollChatToBottom();

        try {
            const response = await fetch('/api/chat/stream', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify({
                    message: message,
                    previousResponseId: previousResponseId.value,
                    enableContextualSearch: true
                })
            });

            if (!response.ok) {
                throw new Error(`HTTP error! status: ${response.status}`);
            }

            // Add assistant message placeholder
            chatMessages.value.push({
                role: 'assistant',
                content: ''
            });

            let accumulatedText = '';
            const reader = response.body.getReader();
            const decoder = new TextDecoder();

            while (true) {
                const { done, value } = await reader.read();
                if (done) break;

                const chunk = decoder.decode(value, { stream: true });
                const lines = chunk.split('\n');

                for (const line of lines) {
                    if (line.startsWith('data: ')) {
                        try {
                            const data = JSON.parse(line.slice(6));
                            if (data.token) {
                                accumulatedText += data.token;
                                // Update the last message with accumulated text
                                const lastMessage = chatMessages.value[chatMessages.value.length - 1];
                                lastMessage.content = formatMessage(accumulatedText);
                                scrollChatToBottom();
                            }
                            if (data.responseId) {
                                previousResponseId.value = data.responseId;
                            }
                            if (data.done) {
                                break;
                            }
                        } catch (e) {
                            console.warn('Failed to parse SSE data:', e);
                        }
                    }
                }
            }

        } catch (error) {
            console.error('Chat error:', error);
            chatMessages.value.push({
                role: 'assistant',
                content: `Sorry, I encountered an error: ${error.message}`
            });
        } finally {
            isTyping.value = false;
            scrollChatToBottom();
        }
    }

    /**
     * Clears all chat messages and resets to initial state
     */
    function clearChat() {
        chatMessages.value = [
            {
                role: 'assistant',
                content: '<strong>👋 Hello!</strong> I\'m your AI assistant with access to Essential C# book content. How can I help you today?'
            }
        ];
        previousResponseId.value = null;
    }

    // Return all chat-related state and methods
    return {
        // State
        showChatDialog,
        chatMessages,
        chatInput,
        isTyping,
        chatInputField,
        chatMessagesEl: chatMessages_el,

        // Methods
        openChatDialog,
        closeChatDialog,
        sendChatMessage,
        clearChat,
        formatMessage,
        scrollChatToBottom
    };
}
