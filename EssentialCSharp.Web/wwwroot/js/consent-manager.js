/**
 * Cookie Consent Manager for Essential C#
 * Implements Google Consent Mode v2 for Microsoft Clarity and Google Analytics
 * Shown to all visitors — ensures GDPR compliance globally without fragile region detection.
 */

class ConsentManager {
    constructor() {
        this.COOKIE_NAME = 'essential-csharp-consent';
        this.COOKIE_DURATION = 365; // days
        this.CONSENT_VERSION = '2'; // Bump this to re-prompt all users when consent terms change
        this.consentState = {
            analytics_storage: 'denied',
            ad_storage: 'denied',
            ad_user_data: 'denied',
            ad_personalization: 'denied',
            functionality_storage: 'granted', // Always granted for essential functionality
            security_storage: 'granted', // Always granted for security
            personalization_storage: 'denied'
        };
        
        this.init();
    }

    init() {
        this.initGoogleConsentMode();
        
        // Load saved consent preferences (signals GA if already consented)
        this.loadConsentPreferences();

        // Always send Clarity the current consent state (denied by default for new visitors).
        // Clarity's polyfill queues this call and delivers it when the script loads.
        this.updateClarityConsent();
        
        // Show banner if no valid consent stored
        if (this.shouldShowConsentBanner()) {
            this.showConsentBanner();
        }
    }

    initGoogleConsentMode() {
        // Ensure gtag infrastructure exists — the actual 'consent default' is set inline
        // in _Layout.cshtml before gtag.js loads (required by Google Consent Mode v2).
        window.dataLayer = window.dataLayer || [];
        window.gtag = window.gtag || function(){window.dataLayer.push(arguments);};
    }

    loadConsentPreferences() {
        const saved = this.getCookie(this.COOKIE_NAME);
        if (saved) {
            try {
                const preferences = JSON.parse(saved);

                // Invalidate stale consent if the version has changed — treat user as new visitor
                if (preferences._version !== this.CONSENT_VERSION) {
                    this.deleteCookie(this.COOKIE_NAME);
                    return;
                }
                
                // Validate and only apply known consent properties for security.
                // Exclude functionality_storage and security_storage — always essential, never user-overrideable.
                const validConsentKeys = [
                    'analytics_storage', 'ad_storage', 'ad_user_data', 
                    'ad_personalization', 'personalization_storage'
                ];
                
                const validatedPreferences = {};
                validConsentKeys.forEach(key => {
                    if (preferences.hasOwnProperty(key) && 
                        (preferences[key] === 'granted' || preferences[key] === 'denied')) {
                        validatedPreferences[key] = preferences[key];
                    }
                });
                
                this.consentState = { ...this.consentState, ...validatedPreferences };
                this.updateConsentMode();
            } catch (e) {
                // Malformed cookie — delete it so the banner is shown again
                console.warn('Failed to parse consent preferences', e);
                this.deleteCookie(this.COOKIE_NAME);
            }
        }
    }

    shouldShowConsentBanner() {
        // Allow forcing the banner via URL param (useful for testing)
        const urlParams = new URLSearchParams(window.location.search);
        if (urlParams.get('testConsent') === 'true') return true;
        // Show to all visitors who haven't given valid consent yet
        return !this.getCookie(this.COOKIE_NAME);
    }

    showConsentBanner() {
        if (document.getElementById('consent-banner')) return; // Already shown

        const banner = this.createConsentBanner();
        document.body.appendChild(banner);
        
        // Add banner styles if not already added
        this.addConsentStyles();
    }

    createConsentBanner() {
        const banner = document.createElement('div');
        banner.id = 'consent-banner';
        banner.className = 'consent-banner';
        banner.innerHTML = `
            <div class="consent-banner-content">
                <div class="consent-banner-text">
                    <h3>Cookie Preferences</h3>
                    <p>We use cookies to improve your experience and analyze website usage. See our <a href="https://intellitect.com/about/privacy-policy/" target="_blank" rel="noopener noreferrer">Privacy Policy</a> for details.</p>
                </div>
                <div class="consent-banner-actions">
                    <button id="consent-reject-all" class="btn btn-outline-secondary me-2">Reject All</button>
                    <button id="consent-accept-all" class="btn btn-primary me-2">Accept All</button>
                    <button id="consent-customize" class="btn btn-outline-primary">Customize</button>
                </div>
            </div>
            <div id="consent-details" class="consent-details" style="display: none;">
                <div class="consent-category">
                    <label class="consent-switch">
                        <input type="checkbox" id="consent-essential" checked disabled>
                        <span class="consent-slider"></span>
                        <div class="consent-info">
                            <strong>Essential Cookies</strong>
                            <p>Required for basic site functionality and security. Cannot be disabled.</p>
                        </div>
                    </label>
                </div>
                <div class="consent-category">
                    <label class="consent-switch">
                        <input type="checkbox" id="consent-analytics">
                        <span class="consent-slider"></span>
                        <div class="consent-info">
                            <strong>Analytics Cookies</strong>
                            <p>Help us understand how you use our site to improve your experience.</p>
                        </div>
                    </label>
                </div>
                <div class="consent-category">
                    <label class="consent-switch">
                        <input type="checkbox" id="consent-advertising">
                        <span class="consent-slider"></span>
                        <div class="consent-info">
                            <strong>Google Signals</strong>
                            <p>Allows Google to associate your visit with your Google account for analytics modeling and cross-site measurement. No advertisements are served on this site, but Google may use this data across its services.</p>
                        </div>
                    </label>
                </div>
                <div class="consent-actions">
                    <button id="consent-save-preferences" class="btn btn-primary">Save Preferences</button>
                </div>
            </div>
        `;

        // Add event listeners
        this.addBannerEventListeners(banner);
        
        return banner;
    }

    addBannerEventListeners(banner) {
        banner.querySelector('#consent-accept-all').addEventListener('click', () => {
            this.acceptAllConsent();
        });

        banner.querySelector('#consent-reject-all').addEventListener('click', () => {
            this.rejectAllConsent();
        });

        banner.querySelector('#consent-customize').addEventListener('click', () => {
            this.showCustomizeOptions();
        });

        banner.querySelector('#consent-save-preferences').addEventListener('click', () => {
            this.saveCustomPreferences();
        });
    }

    showCustomizeOptions() {
        const details = document.getElementById('consent-details');
        if (details) {
            details.style.display = 'block';
            
            // Load current preferences into checkboxes
            document.getElementById('consent-analytics').checked = 
                this.consentState.analytics_storage === 'granted';
            document.getElementById('consent-advertising').checked = 
                this.consentState.ad_storage === 'granted';
        }
    }

    acceptAllConsent() {
        this.consentState = {
            ...this.consentState,
            analytics_storage: 'granted',
            ad_storage: 'granted',
            ad_user_data: 'granted',
            ad_personalization: 'granted',
            personalization_storage: 'granted'
        };
        
        this.saveConsentAndClose();
    }

    rejectAllConsent() {
        this.consentState = {
            ...this.consentState,
            analytics_storage: 'denied',
            ad_storage: 'denied',
            ad_user_data: 'denied',
            ad_personalization: 'denied',
            personalization_storage: 'denied'
        };
        
        this.saveConsentAndClose();
    }

    saveCustomPreferences() {
        const analyticsChecked = document.getElementById('consent-analytics').checked;
        const advertisingChecked = document.getElementById('consent-advertising').checked;
        
        this.consentState = {
            ...this.consentState,
            analytics_storage: analyticsChecked ? 'granted' : 'denied',
            ad_storage: advertisingChecked ? 'granted' : 'denied',
            ad_user_data: advertisingChecked ? 'granted' : 'denied',
            ad_personalization: advertisingChecked ? 'granted' : 'denied',
            personalization_storage: advertisingChecked ? 'granted' : 'denied'
        };
        
        this.saveConsentAndClose();
    }

    saveConsentAndClose() {
        // Save consent with audit metadata
        const payload = {
            ...this.consentState,
            _timestamp: new Date().toISOString(),
            _version: this.CONSENT_VERSION
        };
        this.setCookie(this.COOKIE_NAME, JSON.stringify(payload), this.COOKIE_DURATION);
        
        // Update consent mode
        this.updateConsentMode();
        
        // Update Clarity consent
        this.updateClarityConsent();
        
        // Remove banner
        this.removeConsentBanner();
    }

    updateConsentMode() {
        if (window.gtag) {
            try {
                window.gtag('consent', 'update', this.consentState);
            } catch (error) {
                console.warn('Failed to update Google Consent Mode:', error);
            }
        }
    }

    updateClarityConsent() {
        // Send consent signal to Microsoft Clarity using Consent API v2
        if (window.clarity) {
            try {
                window.clarity('consentv2', {
                    ad_storage: this.consentState.ad_storage,
                    analytics_storage: this.consentState.analytics_storage
                });
            } catch (error) {
                console.warn('Failed to update Clarity consent:', error);
            }
        }
    }

    removeConsentBanner() {
        const banner = document.getElementById('consent-banner');
        if (banner) {
            banner.remove();
        }
    }

    addConsentStyles() {
        if (document.getElementById('consent-styles')) return; // Already added

        const styles = document.createElement('style');
        styles.id = 'consent-styles';
        styles.textContent = `
            .consent-banner {
                position: fixed;
                bottom: 0;
                left: 0;
                right: 0;
                background: #ffffff;
                border-top: 3px solid #007bff;
                box-shadow: 0 -4px 12px rgba(0,0,0,0.15);
                z-index: 10000;
                font-family: inherit;
            }

            .consent-banner-content {
                max-width: 1200px;
                margin: 0 auto;
                padding: 20px;
                display: flex;
                align-items: center;
                justify-content: space-between;
                gap: 20px;
            }

            .consent-banner-text h3 {
                margin: 0 0 8px 0;
                font-size: 1.2rem;
                color: #333;
            }

            .consent-banner-text p {
                margin: 0;
                color: #666;
                font-size: 0.95rem;
            }

            .consent-banner-actions {
                display: flex;
                gap: 10px;
                flex-wrap: wrap;
            }

            .consent-details {
                border-top: 1px solid #eee;
                padding: 20px;
                max-width: 1200px;
                margin: 0 auto;
            }

            .consent-category {
                margin-bottom: 20px;
            }

            .consent-switch {
                display: flex;
                align-items: center;
                gap: 15px;
                cursor: pointer;
                padding: 15px;
                border: 1px solid #ddd;
                border-radius: 8px;
                background: #f8f9fa;
            }

            .consent-switch input[type="checkbox"] {
                width: 20px;
                height: 20px;
                margin: 0;
            }

            .consent-info strong {
                display: block;
                margin-bottom: 5px;
                color: #333;
            }

            .consent-info p {
                margin: 0;
                color: #666;
                font-size: 0.9rem;
            }

            .consent-actions {
                text-align: center;
                margin-top: 20px;
            }

            @media (max-width: 768px) {
                .consent-banner-content {
                    flex-direction: column;
                    text-align: center;
                }
                
                .consent-banner-actions {
                    justify-content: center;
                    width: 100%;
                }
            }
        `;
        
        document.head.appendChild(styles);
    }

    // Cookie utility methods
    setCookie(name, value, days) {
        const expires = new Date();
        expires.setTime(expires.getTime() + (days * 24 * 60 * 60 * 1000));
        const secure = window.location.protocol === 'https:' ? ';Secure' : '';
        document.cookie = `${name}=${encodeURIComponent(value)};expires=${expires.toUTCString()};path=/;SameSite=Lax${secure}`;
    }

    deleteCookie(name) {
        const secure = window.location.protocol === 'https:' ? ';Secure' : '';
        document.cookie = `${name}=;expires=Thu, 01 Jan 1970 00:00:00 GMT;path=/;SameSite=Lax${secure}`;
    }

    getCookie(name) {
        const nameEQ = name + "=";
        const ca = document.cookie.split(';');
        for (let i = 0; i < ca.length; i++) {
            let c = ca[i];
            while (c.charAt(0) === ' ') c = c.substring(1, c.length);
            if (c.indexOf(nameEQ) === 0) return decodeURIComponent(c.substring(nameEQ.length, c.length));
        }
        return null;
    }

    // Public API for consent preference management
    openConsentPreferences() {
        this.showConsentBanner();
        this.showCustomizeOptions();
    }

    // Check current consent status
    hasAnalyticsConsent() {
        return this.consentState.analytics_storage === 'granted';
    }

    hasAdvertisingConsent() {
        return this.consentState.ad_storage === 'granted';
    }

    // Method to revoke consent (useful for "forget me" functionality)
    revokeAllConsent() {
        this.rejectAllConsent();
        // Also clear any existing tracking cookies
        this.clearTrackingCookies();
        // Note: Clarity consent signal is sent via updateClarityConsent() inside rejectAllConsent() → saveConsentAndClose()
    }

    clearTrackingCookies() {
        // Clear common tracking cookies (Google Analytics and Microsoft Clarity)
        const trackingCookies = ['_ga', '_gid', '_gat', '_clck', '_clsk', 'CLID', 'ANONCHK', 'MR', 'MUID', 'SM'];
        const expired = 'expires=Thu, 01 Jan 1970 00:00:00 GMT';
        const hostname = window.location.hostname;
        // Build candidate domains: exact host plus progressively shorter parent domains.
        // This handles multi-part TLDs (e.g. .co.uk) by trying all suffixes.
        const parts = hostname.split('.');
        const domains = [hostname];
        for (let i = 0; i < parts.length - 1; i++) {
            domains.push('.' + parts.slice(i).join('.'));
        }

        // Also collect GA4 measurement-ID cookies (_ga_XXXXXXXX) from document.cookie
        // since their suffix changes per-property and can't be hardcoded.
        const ga4Cookies = document.cookie.split(';')
            .map(c => c.trim().split('=')[0])
            .filter(name => name.startsWith('_ga_'));
        const allCookies = [...new Set([...trackingCookies, ...ga4Cookies])];

        allCookies.forEach(cookieName => {
            const secure = window.location.protocol === 'https:' ? ';Secure' : '';
            document.cookie = `${cookieName}=;${expired};path=/${secure}`;
            domains.forEach(domain => {
                document.cookie = `${cookieName}=;${expired};path=/;domain=${domain}${secure}`;
            });
        });
    }
}

// Initialize consent manager when DOM is ready
document.addEventListener('DOMContentLoaded', function() {
    window.consentManager = new ConsentManager();
});

// Global function for opening consent preferences
window.openConsentPreferences = function() {
    if (window.consentManager) {
        window.consentManager.openConsentPreferences();
    }
};