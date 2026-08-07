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
