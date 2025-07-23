/**
 * AI Chat Widget Component
 * Provides a floating chat interface with AI assistant functionality
 * Supports markdown rendering and streaming responses
 */
class ChatWidget {
    constructor() {
        this.isOpen = false;
        this.isMinimized = false;
        this.previousResponseId = null;
        this.initializeElements();
        this.bindEvents();
        this.enableInput();
    }

    initializeElements() {
        this.chatToggle = document.getElementById('chatToggle');
        this.chatPanel = document.getElementById('chatPanel');
        this.chatClose = document.getElementById('chatClose');
        this.chatMinimize = document.getElementById('chatMinimize');
        this.chatInput = document.getElementById('chatWidgetInput');
        this.chatSend = document.getElementById('chatWidgetSend');
        this.chatMessages = document.getElementById('chatWidgetMessages');
        this.chatClear = document.getElementById('chatWidgetClear');
    }

    bindEvents() {
        this.chatToggle.addEventListener('click', () => this.toggleChat());
        this.chatClose.addEventListener('click', () => this.closeChat());
        this.chatMinimize.addEventListener('click', () => this.minimizeChat());
        this.chatSend.addEventListener('click', () => this.sendMessage());
        this.chatClear.addEventListener('click', () => this.clearChat());
        
        this.chatInput.addEventListener('keypress', (e) => {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                this.sendMessage();
            }
        });

        // Close chat when clicking outside
        document.addEventListener('click', (e) => {
            if (this.isOpen && !this.chatPanel.contains(e.target) && !this.chatToggle.contains(e.target)) {
                this.closeChat();
            }
        });
    }

    toggleChat() {
        if (this.isOpen) {
            this.closeChat();
        } else {
            this.openChat();
        }
    }

    openChat() {
        this.isOpen = true;
        this.isMinimized = false;
        this.chatPanel.style.display = 'flex';
        this.chatInput.focus();
        this.scrollToBottom();
    }

    closeChat() {
        this.isOpen = false;
        this.isMinimized = false;
        this.chatPanel.style.display = 'none';
    }

    minimizeChat() {
        this.isMinimized = true;
        this.chatPanel.style.display = 'none';
    }

    enableInput() {
        this.chatInput.disabled = false;
        this.chatSend.disabled = false;
    }

    disableInput() {
        this.chatInput.disabled = true;
        this.chatSend.disabled = true;
    }

    async sendMessage() {
        const message = this.chatInput.value.trim();
        if (!message) return;

        this.addMessage('user', message);
        this.chatInput.value = '';
        this.disableInput();
        this.showTypingIndicator();

        // Create a placeholder message for the streaming response
        let assistantMessage = null;
        let currentResponseId = null;

        try {
            const response = await fetch('/api/chat/stream', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify({
                    message: message,
                    previousResponseId: this.previousResponseId,
                    enableContextualSearch: true
                })
            });

            this.hideTypingIndicator();

            if (!response.ok) {
                // Handle error responses
                const responseText = await response.text();
                try {
                    const errorData = JSON.parse(responseText);
                    this.addMessage('error', `Error: ${errorData.error || 'Unknown error occurred'}`);
                } catch (jsonError) {
                    if (response.status === 503) {
                        this.addMessage('error', 'AI Chat service is currently unavailable. Please check the configuration and try again later.');
                    } else {
                        this.addMessage('error', `Server error (${response.status}): ${responseText || 'Please try again later.'}`);
                    }
                }
                return;
            }

            // Create initial assistant message
            assistantMessage = this.addMessage('assistant', '');
            const messageContent = assistantMessage.querySelector('.message-content');
            let accumulatedText = '';

            // Process the Server-Sent Events stream
            const reader = response.body.getReader();
            const decoder = new TextDecoder();

            while (true) {
                const { done, value } = await reader.read();
                if (done) break;

                const chunk = decoder.decode(value, { stream: true });
                const lines = chunk.split('\n');

                for (const line of lines) {
                    if (line.startsWith('data: ')) {
                        const data = line.slice(6); // Remove 'data: ' prefix
                        
                        if (data === '[DONE]') {
                            // Stream is complete
                            if (currentResponseId) {
                                this.previousResponseId = currentResponseId;
                            }
                            break;
                        }

                        try {
                            const parsed = JSON.parse(data);
                            
                            if (parsed.type === 'text' && parsed.data) {
                                accumulatedText += parsed.data;
                                messageContent.innerHTML = this.formatMessage(accumulatedText);
                                this.scrollToBottom();
                            } else if (parsed.type === 'responseId' && parsed.data) {
                                currentResponseId = parsed.data;
                            }
                        } catch (parseError) {
                            // Ignore malformed JSON in stream
                            console.warn('Failed to parse streaming data:', data);
                        }
                    }
                }
            }

        } catch (error) {
            this.hideTypingIndicator();
            if (assistantMessage) {
                assistantMessage.remove();
            }
            this.addMessage('error', `Network error: ${error.message}`);
        } finally {
            this.enableInput();
        }
    }

    addMessage(role, content) {
        const messageDiv = document.createElement('div');
        messageDiv.className = `chat-message ${role}`;
        
        const formattedContent = role === 'assistant' ? this.formatMessage(content) : this.escapeHtml(content);
        messageDiv.innerHTML = `
            <div class="message-content">${formattedContent}</div>
        `;

        this.chatMessages.appendChild(messageDiv);
        this.scrollToBottom();
        return messageDiv;
    }

    showTypingIndicator() {
        let indicator = document.querySelector('.chat-typing-indicator');
        if (!indicator) {
            indicator = document.createElement('div');
            indicator.className = 'chat-typing-indicator';
            indicator.innerHTML = `
                <div class="message-content">
                    <span>AI is typing</span>
                    <div class="chat-typing-dots">
                        <span></span>
                        <span></span>
                        <span></span>
                    </div>
                </div>
            `;
            this.chatMessages.appendChild(indicator);
        }
        indicator.style.display = 'block';
        this.scrollToBottom();
    }

    hideTypingIndicator() {
        const indicator = document.querySelector('.chat-typing-indicator');
        if (indicator) {
            indicator.style.display = 'none';
        }
    }

    clearChat() {
        this.chatMessages.innerHTML = `
            <div class="chat-message assistant">
                <div class="message-content">
                    <strong>👋 Hello!</strong> I'm your AI assistant with access to Essential C# book content. How can I help you today?
                </div>
            </div>
        `;
        this.previousResponseId = null;
    }

    scrollToBottom() {
        this.chatMessages.scrollTop = this.chatMessages.scrollHeight;
    }

    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    formatMessage(text) {
        try {
            // Check if required libraries are available
            if (typeof marked === 'undefined' || typeof DOMPurify === 'undefined') {
                console.warn('Markdown libraries not loaded, falling back to basic formatting');
                return this.escapeHtml(text).replace(/\n/g, '<br>');
            }

            // Ensure text is a string
            const textStr = String(text || '');

            // Configure marked options for better rendering
            const renderer = new marked.Renderer();
            
            // Customize code block rendering
            renderer.code = function(code, language) {
                // Handle both string and token object formats
                const codeStr = typeof code === 'object' ? (code.text || code.raw || '') : String(code || '');
                const langStr = typeof language === 'object' ? (language.lang || '') : String(language || '');
                return `<pre><code class="language-${langStr}">${codeStr}</code></pre>`;
            };
            
            // Customize inline code rendering
            renderer.codespan = function(code) {
                // Handle both string and token object formats
                const codeStr = typeof code === 'object' ? (code.text || code.raw || '') : String(code || '');
                return `<code>${codeStr}</code>`;
            };
            
            // Configure marked
            marked.setOptions({
                renderer: renderer,
                breaks: true,        // Convert \n to <br>
                gfm: true,          // GitHub Flavored Markdown
                sanitize: false,    // We'll use DOMPurify for sanitization
                smartLists: true,
                smartypants: false
            });
            
            // Parse markdown
            const htmlContent = marked.parse(textStr);
            
            // Sanitize the HTML to prevent XSS
            const cleanHtml = DOMPurify.sanitize(htmlContent, {
                ALLOWED_TAGS: ['p', 'br', 'strong', 'em', 'code', 'pre', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6', 'ul', 'ol', 'li', 'blockquote', 'a', 'table', 'thead', 'tbody', 'tr', 'th', 'td'],
                ALLOWED_ATTR: ['href', 'title', 'class'],
                ALLOW_DATA_ATTR: false
            });
            
            return cleanHtml;
        } catch (error) {
            // Fallback to escaped text if parsing fails
            console.warn('Failed to parse markdown:', error);
            return this.escapeHtml(String(text || '')).replace(/\n/g, '<br>');
        }
    }
}

// Auto-initialize chat widget when DOM is loaded
document.addEventListener('DOMContentLoaded', () => {
    new ChatWidget();
});

// Export for potential manual initialization or testing
if (typeof module !== 'undefined' && module.exports) {
    module.exports = ChatWidget;
}
