/**
 * Microsoft Clarity loader for Essential C#.
 * Loads only after analytics consent is granted and updates consent state when available.
 */
(function () {
    const CONSENT_EVENT = "ecs:consent-changed";
    const CLARITY_PROJECT_ID = "g4keetzd2o";
    const SDK_URL = `https://www.clarity.ms/tag/${CLARITY_PROJECT_ID}`;

    let clarityLoadPromise = null;

    function hasAnalyticsConsent() {
        if (window.consentManager && typeof window.consentManager.hasAnalyticsConsent === "function") {
            return window.consentManager.hasAnalyticsConsent();
        }

        const state = typeof window.getEcsConsentState === "function" ? window.getEcsConsentState() : null;
        return !!(state && state.analytics_storage === "granted");
    }

    function ensureClarityQueue() {
        if (typeof window.clarity !== "function") {
            window.clarity = function () {
                (window.clarity.q = window.clarity.q || []).push(arguments);
            };
        }
    }

    function ensureSdkLoaded() {
        ensureClarityQueue();

        if (document.querySelector(`script[src="${SDK_URL}"]`)) {
            return clarityLoadPromise || Promise.resolve();
        }

        if (clarityLoadPromise) {
            return clarityLoadPromise;
        }

        clarityLoadPromise = new Promise((resolve, reject) => {
            const script = document.createElement("script");
            script.src = SDK_URL;
            script.async = true;
            script.onload = () => resolve();
            script.onerror = () => {
                clarityLoadPromise = null;
                script.remove();
                reject(new Error("Failed to load Microsoft Clarity."));
            };
            document.head.appendChild(script);
        });

        return clarityLoadPromise;
    }

    function clearClarityCookies() {
        const clarityCookies = ['_clck', '_clsk', 'CLID', 'ANONCHK', 'MR', 'MUID', 'SM'];
        const expired = 'expires=Thu, 01 Jan 1970 00:00:00 GMT';
        const secure = window.location.protocol === 'https:' ? ';Secure' : '';
        const hostname = window.location.hostname;
        const parts = hostname.split('.');
        const domains = [hostname];
        for (let i = 0; i < parts.length - 1; i++) {
            domains.push('.' + parts.slice(i).join('.'));
        }
        clarityCookies.forEach(name => {
            document.cookie = `${name}=;${expired};path=/${secure}`;
            domains.forEach(domain => {
                document.cookie = `${name}=;${expired};path=/;domain=${domain}${secure}`;
            });
        });
    }

    function updateConsent() {
        if (typeof window.clarity === "function") {
            try {
                window.clarity("consentv2", {
                    analytics_storage: hasAnalyticsConsent() ? "granted" : "denied"
                });
            } catch (error) {
                console.warn("Failed to update Clarity consent:", error);
            }
        }
    }

    function syncConsentState() {
        if (!hasAnalyticsConsent()) {
            updateConsent();
            clearClarityCookies();
            return;
        }

        ensureSdkLoaded()
            .then(() => {
                if (!hasAnalyticsConsent()) {
                    updateConsent();
                    return;
                }

                updateConsent();
            })
            .catch((error) => {
                console.warn("Microsoft Clarity initialization failed:", error);
            });
    }

    function init() {
        window.addEventListener(CONSENT_EVENT, syncConsentState);
        syncConsentState();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init, { once: true });
    } else {
        init();
    }
})();
