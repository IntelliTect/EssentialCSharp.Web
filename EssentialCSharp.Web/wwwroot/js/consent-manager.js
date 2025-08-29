/**
 * Cookie Consent Manager for Essential C#
 * Implements Google Consent Mode v2 for Microsoft Clarity and Google Analytics
 * Compliant with GDPR requirements for EEA, UK, and Switzerland
 */

class ConsentManager {
    constructor() {
        this.COOKIE_NAME = 'essential-csharp-consent';
        this.COOKIE_DURATION = 365; // days
        this.consentState = {
            analytics_storage: 'denied',
            ad_storage: 'denied',
            ad_user_data: 'denied',
            ad_personalization: 'denied',
            functionality_storage: 'granted', // Always granted for essential functionality
            security_storage: 'granted', // Always granted for security
            personalization_storage: 'denied'
        };
        
        // Check if user is in EEA/UK/Switzerland region
        this.requiresConsent = this.checkRegionRequiresConsent();
        
        this.init();
    }

    init() {
        // Initialize Google Consent Mode
        this.initGoogleConsentMode();
        
        // Load saved consent preferences
        this.loadConsentPreferences();
        
        // Show banner if consent required and not yet given
        if (this.shouldShowConsentBanner()) {
            this.showConsentBanner();
        }
    }

    initGoogleConsentMode() {
        // Initialize gtag if not already loaded
        window.dataLayer = window.dataLayer || [];
        function gtag(){dataLayer.push(arguments);}
        window.gtag = window.gtag || gtag;
        
        // Set default consent state - denial for all except essential
        gtag('consent', 'default', this.consentState);
    }

    checkRegionRequiresConsent() {
        // Check for forced testing via URL parameter
        const urlParams = new URLSearchParams(window.location.search);
        if (urlParams.get('testConsent') === 'true') {
            return true;
        }
        
        // Simple region detection - in production, you might want to use a more reliable service
        const timezone = Intl.DateTimeFormat().resolvedOptions().timeZone;
        
        // EEA countries, UK, and Switzerland timezones
        const requiresConsentTimezones = [
            // Western Europe
            'Europe/London', 'Europe/Dublin', 'Europe/Lisbon', 'Europe/Madrid', 
            'Europe/Paris', 'Europe/Amsterdam', 'Europe/Brussels', 'Europe/Luxembourg',
            'Europe/Zurich', 'Europe/Vienna', 'Europe/Rome', 'Europe/Vatican',
            'Europe/San_Marino', 'Europe/Malta', 'Europe/Monaco',
            
            // Central Europe  
            'Europe/Berlin', 'Europe/Prague', 'Europe/Budapest', 'Europe/Warsaw',
            'Europe/Bratislava', 'Europe/Ljubljana', 'Europe/Zagreb', 'Europe/Belgrade',
            'Europe/Sarajevo', 'Europe/Podgorica', 'Europe/Skopje', 'Europe/Tirane',
            
            // Northern Europe
            'Europe/Stockholm', 'Europe/Oslo', 'Europe/Copenhagen', 'Europe/Helsinki',
            'Europe/Tallinn', 'Europe/Riga', 'Europe/Vilnius', 'Europe/Reykjavik',
            
            // Eastern Europe
            'Europe/Bucharest', 'Europe/Sofia', 'Europe/Athens', 'Europe/Nicosia',
            
            // Additional EEA territories
            'Atlantic/Canary', 'Atlantic/Madeira', 'Atlantic/Azores',
            'Europe/Gibraltar', 'Africa/Ceuta'
        ];
        
        return requiresConsentTimezones.includes(timezone);
    }

    loadConsentPreferences() {
        const saved = this.getCookie(this.COOKIE_NAME);
        if (saved) {
            try {
                const preferences = JSON.parse(saved);
                this.consentState = { ...this.consentState, ...preferences };
                this.updateConsentMode();
            } catch (e) {
                console.warn('Failed to parse consent preferences', e);
            }
        }
    }

    shouldShowConsentBanner() {
        // Show banner if in EEA/UK/Switzerland and no consent stored
        return this.requiresConsent && !this.getCookie(this.COOKIE_NAME);
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
                    <p>We use cookies to improve your experience and analyze website usage. You can manage your preferences below.</p>
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
                            <strong>Advertising Cookies</strong>
                            <p>Used to deliver relevant advertisements and measure their effectiveness.</p>
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
            details.style.display = details.style.display === 'none' ? 'block' : 'none';
            
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
            personalization_storage: analyticsChecked ? 'granted' : 'denied'
        };
        
        this.saveConsentAndClose();
    }

    saveConsentAndClose() {
        // Save consent to cookie
        this.setCookie(this.COOKIE_NAME, JSON.stringify(this.consentState), this.COOKIE_DURATION);
        
        // Update consent mode
        this.updateConsentMode();
        
        // Update Clarity consent
        this.updateClarityConsent();
        
        // Remove banner
        this.removeConsentBanner();
    }

    updateConsentMode() {
        if (window.gtag) {
            gtag('consent', 'update', this.consentState);
            
            // Configure Google Analytics if analytics consent is granted
            if (this.consentState.analytics_storage === 'granted') {
                gtag('config', 'G-761B4BMK2R');
            }
        }
    }

    updateClarityConsent() {
        // Send consent signal to Microsoft Clarity
        if (window.clarity) {
            const analyticsConsent = this.consentState.analytics_storage === 'granted';
            clarity('consent', analyticsConsent);
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
        document.cookie = `${name}=${value};expires=${expires.toUTCString()};path=/;SameSite=Lax`;
    }

    getCookie(name) {
        const nameEQ = name + "=";
        const ca = document.cookie.split(';');
        for (let i = 0; i < ca.length; i++) {
            let c = ca[i];
            while (c.charAt(0) === ' ') c = c.substring(1, c.length);
            if (c.indexOf(nameEQ) === 0) return c.substring(nameEQ.length, c.length);
        }
        return null;
    }

    // Public API for consent preference management
    openConsentPreferences() {
        this.showConsentBanner();
        // If banner already exists, show customize options
        setTimeout(() => {
            if (document.getElementById('consent-banner')) {
                this.showCustomizeOptions();
            }
        }, 100);
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
    }

    clearTrackingCookies() {
        // Clear common tracking cookies
        const trackingCookies = ['_ga', '_gid', '_gat', '_clck', '_clsk'];
        trackingCookies.forEach(cookieName => {
            document.cookie = `${cookieName}=;expires=Thu, 01 Jan 1970 00:00:00 GMT;path=/`;
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