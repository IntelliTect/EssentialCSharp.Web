/**
 * Application Insights browser telemetry manager for Essential C#.
 * Reuses the existing consent-manager analytics consent signal.
 */
(function () {
    const SDK_URL = "https://js.monitor.azure.com/scripts/b/ai.3.gbl.min.js";
    const CONSENT_EVENT = "ecs:consent-changed";

    let appInsights = null;
    let sdkLoadPromise = null;
    let didInitialPageView = false;

    function getConnectionString() {
        const value = window.APPLICATIONINSIGHTS_CONNECTION_STRING;
        return typeof value === "string" && value.trim().length > 0 ? value.trim() : null;
    }

    function hasAnalyticsConsent() {
        if (window.consentManager && typeof window.consentManager.hasAnalyticsConsent === "function") {
            return window.consentManager.hasAnalyticsConsent();
        }

        const state = typeof window.getEcsConsentState === "function" ? window.getEcsConsentState() : null;
        return !!(state && state.analytics_storage === "granted");
    }

    function getAuthenticatedUserId() {
        const userId = window.AUTHENTICATED_USER_ID;
        return typeof userId === "string" && userId.trim().length > 0 ? userId.trim() : null;
    }

    function setAuthenticatedContext() {
        if (!appInsights) {
            return;
        }

        const userId = getAuthenticatedUserId();
        if (userId) {
            appInsights.setAuthenticatedUserContext(userId);
        } else if (typeof appInsights.clearAuthenticatedUserContext === "function") {
            appInsights.clearAuthenticatedUserContext();
        }
    }

    function clearAuthenticatedContext() {
        if (appInsights && typeof appInsights.clearAuthenticatedUserContext === "function") {
            appInsights.clearAuthenticatedUserContext();
        }
    }

    function ensureSdkLoaded() {
        if (window.Microsoft?.ApplicationInsights?.ApplicationInsights) {
            return Promise.resolve();
        }
        if (sdkLoadPromise) {
            return sdkLoadPromise;
        }

        sdkLoadPromise = new Promise((resolve, reject) => {
            const existing = document.querySelector(`script[src="${SDK_URL}"]`);
            if (existing) {
                existing.addEventListener("load", () => resolve(), { once: true });
                existing.addEventListener("error", () => reject(new Error("Failed to load App Insights SDK.")), { once: true });
                return;
            }

            const script = document.createElement("script");
            script.src = SDK_URL;
            script.async = true;
            script.defer = true;
            script.onload = () => resolve();
            script.onerror = () => reject(new Error("Failed to load App Insights SDK."));
            document.head.appendChild(script);
        });

        return sdkLoadPromise;
    }

    function createAppInsights() {
        const connectionString = getConnectionString();
        if (!connectionString) {
            return null;
        }
        if (!window.Microsoft?.ApplicationInsights?.ApplicationInsights) {
            return null;
        }

        const instance = new window.Microsoft.ApplicationInsights.ApplicationInsights({
            config: {
                connectionString,
                disableAjaxTracking: true, // avoid duplicate/debatable dependency telemetry from browser fetch/XHR
                disableTelemetry: false
            }
        });

        instance.loadAppInsights();
        setAuthenticatedContext();

        if (!didInitialPageView) {
            instance.trackPageView();
            didInitialPageView = true;
        }

        return instance;
    }

    function onConsentGranted() {
        const connectionString = getConnectionString();
        if (!connectionString) {
            return;
        }

        ensureSdkLoaded()
            .then(() => {
                if (!appInsights) {
                    appInsights = createAppInsights();
                    window.ecsAppInsights = appInsights;
                } else {
                    appInsights.config.disableTelemetry = false;
                    setAuthenticatedContext();
                }
            })
            .catch((error) => {
                console.warn("Application Insights SDK initialization failed:", error);
            });
    }

    function onConsentRevoked() {
        if (!appInsights) {
            return;
        }

        clearAuthenticatedContext();
        appInsights.config.disableTelemetry = true;
    }

    function syncConsentState() {
        if (hasAnalyticsConsent()) {
            onConsentGranted();
        } else {
            onConsentRevoked();
        }
    }

    function getCurrentTraceId() {
        const traceId = appInsights?.context?.telemetryTrace?.traceID;
        if (typeof traceId === "string" && /^[a-f0-9]{32}$/i.test(traceId)) {
            return traceId.toLowerCase();
        }
        return null;
    }

    window.ecsGetAppInsights = function () {
        return appInsights;
    };

    window.ecsGetCorrelationContext = function () {
        return getCurrentTraceId();
    };

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
