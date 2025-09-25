/**
 * Typesense Search Module
 * Provides search functionality using the Typesense search service
 */

class TypesenseSearch {
    constructor(options = {}) {
        this.baseUrl = options.baseUrl || '/api/search';
        this.resultsContainer = options.resultsContainer || '#search-results';
        this.searchForm = options.searchForm || '#search-form';
        this.searchInput = options.searchInput || '#search-input';
        this.loadingIndicator = options.loadingIndicator || '#search-loading';
        this.errorContainer = options.errorContainer || '#search-error';
        
        this.currentQuery = '';
        this.currentPage = 1;
        this.isLoading = false;
        
        this.init();
    }

    init() {
        this.bindEvents();
        this.createSearchInterface();
    }

    bindEvents() {
        // Handle search form submission
        const form = document.querySelector(this.searchForm);
        if (form) {
            form.addEventListener('submit', (e) => {
                e.preventDefault();
                this.performSearch();
            });
        }

        // Handle search input changes with debouncing
        const input = document.querySelector(this.searchInput);
        if (input) {
            let debounceTimer;
            input.addEventListener('input', (e) => {
                clearTimeout(debounceTimer);
                debounceTimer = setTimeout(() => {
                    if (e.target.value.trim().length > 2) {
                        this.performSearch(e.target.value.trim());
                    } else if (e.target.value.trim().length === 0) {
                        this.clearResults();
                    }
                }, 300);
            });
        }

        // Handle keyboard shortcuts
        document.addEventListener('keydown', (e) => {
            if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
                e.preventDefault();
                this.openSearch();
            }
            if (e.key === 'Escape') {
                this.closeSearch();
            }
        });
    }

    createSearchInterface() {
        // Create search modal if it doesn't exist
        if (!document.querySelector('#typesense-search-modal')) {
            const modal = document.createElement('div');
            modal.id = 'typesense-search-modal';
            modal.className = 'search-modal';
            modal.innerHTML = `
                <div class="search-modal-backdrop" onclick="window.typesenseSearch.closeSearch()"></div>
                <div class="search-modal-content">
                    <div class="search-header">
                        <form id="search-form" class="search-form">
                            <div class="search-input-container">
                                <svg class="search-icon" width="20" height="20" viewBox="0 0 20 20">
                                    <path d="M14.386 14.386l4.0877 4.0877-4.0877-4.0877c-2.9418 2.9419-7.7115 2.9419-10.6533 0-2.9419-2.9418-2.9419-7.7115 0-10.6533 2.9418-2.9419 7.7115-2.9419 10.6533 0 2.9419 2.9418 2.9419 7.7115 0 10.6533z" stroke="currentColor" fill="none" stroke-linecap="round" stroke-linejoin="round"></path>
                                </svg>
                                <input 
                                    id="search-input" 
                                    type="text" 
                                    placeholder="Search Essential C# content..." 
                                    autocomplete="off"
                                    spellcheck="false"
                                />
                                <button type="button" class="search-close" onclick="window.typesenseSearch.closeSearch()">
                                    <svg width="20" height="20" viewBox="0 0 20 20">
                                        <path stroke="currentColor" fill="none" stroke-linecap="round" stroke-linejoin="round" d="M15 5L5 15M5 5l10 10"></path>
                                    </svg>
                                </button>
                            </div>
                        </form>
                    </div>
                    <div class="search-body">
                        <div id="search-loading" class="search-loading" style="display: none;">
                            <div class="spinner"></div>
                            <span>Searching...</span>
                        </div>
                        <div id="search-error" class="search-error" style="display: none;"></div>
                        <div id="search-results" class="search-results"></div>
                    </div>
                    <div class="search-footer">
                        <div class="search-footer-info">
                            <span>Search powered by</span>
                            <strong>Typesense</strong>
                        </div>
                    </div>
                </div>
            `;
            document.body.appendChild(modal);

            // Update references to the new elements
            this.searchForm = '#search-form';
            this.searchInput = '#search-input';
        }
    }

    async performSearch(query = null) {
        const input = document.querySelector(this.searchInput);
        const searchQuery = query || (input ? input.value.trim() : '');
        
        if (!searchQuery || searchQuery.length < 1) {
            this.clearResults();
            return;
        }

        if (this.isLoading) {
            return;
        }

        this.currentQuery = searchQuery;
        this.currentPage = 1;
        this.showLoading();
        this.hideError();

        try {
            const response = await fetch(`${this.baseUrl}?q=${encodeURIComponent(searchQuery)}&page=${this.currentPage}&per_page=10`, {
                method: 'GET',
                headers: {
                    'Content-Type': 'application/json',
                }
            });

            if (!response.ok) {
                throw new Error(`Search failed: ${response.status} ${response.statusText}`);
            }

            const data = await response.json();
            this.displayResults(data);
        } catch (error) {
            console.error('Search error:', error);
            this.showError('Search is temporarily unavailable. Please try again later.');
        } finally {
            this.hideLoading();
        }
    }

    displayResults(data) {
        const container = document.querySelector(this.resultsContainer);
        if (!container) return;

        if (!data.results || data.results.length === 0) {
            container.innerHTML = `
                <div class="search-no-results">
                    <div class="no-results-icon">🔍</div>
                    <h3>No results found</h3>
                    <p>Try adjusting your search terms or browse the table of contents.</p>
                </div>
            `;
            return;
        }

        const resultsHtml = data.results.map(result => `
            <div class="search-result-item">
                <div class="search-result-content">
                    <a href="${result.url}" class="search-result-title" onclick="window.typesenseSearch.closeSearch()">
                        ${this.highlightText(result.title, this.currentQuery)}
                    </a>
                    <div class="search-result-meta">
                        <span class="search-result-chapter">${result.chapter}</span>
                        ${result.section ? `<span class="search-result-section">${result.section}</span>` : ''}
                    </div>
                    <div class="search-result-snippet">
                        ${this.highlightText(this.truncateText(result.content, 150), this.currentQuery)}
                    </div>
                </div>
            </div>
        `).join('');

        const statsHtml = `
            <div class="search-results-stats">
                <span>${data.totalCount} results found in ${data.searchTimeMs}ms</span>
            </div>
        `;

        container.innerHTML = statsHtml + '<div class="search-results-list">' + resultsHtml + '</div>';
    }

    highlightText(text, query) {
        if (!query || !text) return text;
        
        const regex = new RegExp(`(${query.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')})`, 'gi');
        return text.replace(regex, '<mark>$1</mark>');
    }

    truncateText(text, maxLength) {
        if (!text || text.length <= maxLength) return text;
        return text.substr(0, maxLength) + '...';
    }

    showLoading() {
        this.isLoading = true;
        const loading = document.querySelector(this.loadingIndicator);
        if (loading) loading.style.display = 'flex';
    }

    hideLoading() {
        this.isLoading = false;
        const loading = document.querySelector(this.loadingIndicator);
        if (loading) loading.style.display = 'none';
    }

    showError(message) {
        const errorContainer = document.querySelector(this.errorContainer);
        if (errorContainer) {
            errorContainer.textContent = message;
            errorContainer.style.display = 'block';
        }
    }

    hideError() {
        const errorContainer = document.querySelector(this.errorContainer);
        if (errorContainer) {
            errorContainer.style.display = 'none';
        }
    }

    clearResults() {
        const container = document.querySelector(this.resultsContainer);
        if (container) {
            container.innerHTML = '';
        }
        this.hideError();
    }

    openSearch() {
        const modal = document.querySelector('#typesense-search-modal');
        const input = document.querySelector(this.searchInput);
        
        if (modal) {
            modal.classList.add('active');
            document.body.classList.add('search-modal-open');
            if (input) {
                setTimeout(() => input.focus(), 100);
            }
        }
    }

    closeSearch() {
        const modal = document.querySelector('#typesense-search-modal');
        const input = document.querySelector(this.searchInput);
        
        if (modal) {
            modal.classList.remove('active');
            document.body.classList.remove('search-modal-open');
            if (input) {
                input.value = '';
            }
            this.clearResults();
        }
    }
}

// Global search function to replace openSearch()
function openSearch() {
    if (window.typesenseSearch) {
        window.typesenseSearch.openSearch();
    }
}

// Initialize Typesense search when DOM is loaded
document.addEventListener('DOMContentLoaded', function() {
    window.typesenseSearch = new TypesenseSearch();
});

// Export for module systems
if (typeof module !== 'undefined' && module.exports) {
    module.exports = TypesenseSearch;
}