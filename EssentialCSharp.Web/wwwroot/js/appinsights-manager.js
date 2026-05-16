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
        // Read from a <meta> tag rather than a window global to avoid exposing the stable
        // user GUID to third-party scripts that enumerate window properties.
        const meta = document.querySelector('meta[name="ecs-auth-user-id"]');
        if (!meta) { return null; }
        const value = meta.getAttribute("content") || "";
        return value.trim().length > 0 ? value.trim() : null;
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
                // Guard: script may have already loaded successfully
                if (window.Microsoft?.ApplicationInsights?.ApplicationInsights) {
                    resolve();
                    return;
                }
                // Guard: script may have already errored — add timeout so promise doesn't hang forever.
                // On timeout, remove the dead element so the next retry can append a fresh one.
                const timeoutId = setTimeout(() => {
                    sdkLoadPromise = null;
                    existing.remove();
                    reject(new Error("App Insights SDK load timed out."));
                }, 15000);
                existing.addEventListener("load", () => { clearTimeout(timeoutId); resolve(); }, { once: true });
                existing.addEventListener("error", () => {
                    clearTimeout(timeoutId);
                    sdkLoadPromise = null;
                    existing.remove(); // remove so the next retry appends a fresh element
                    reject(new Error("Failed to load App Insights SDK."));
                }, { once: true });
                return;
            }

            const script = document.createElement("script");
            script.src = SDK_URL;
            script.async = true;
            script.defer = true;
            script.onload = () => resolve();
            script.onerror = () => {
                sdkLoadPromise = null; // allow retry on transient failure
                script.remove(); // remove dead element so the next retry appends a fresh one
                reject(new Error("Failed to load App Insights SDK."));
            };
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

        // Set authenticated context on `instance` directly — the module-level `appInsights` variable
        // is not yet assigned at this point, so setAuthenticatedContext() would be a no-op.
        const userId = getAuthenticatedUserId();
        if (userId) {
            instance.setAuthenticatedUserContext(userId);
        }

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
                // Re-check consent — user may have revoked while the SDK script was downloading
                if (!hasAnalyticsConsent()) {
                    return;
                }
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
        clearAuthenticatedContext(); // guards internally
        if (appInsights) {
            appInsights.config.disableTelemetry = true;
        }

        // Run unconditionally — appInsights may never have been initialized this session
        // (user has always denied), but ai_user/ai_session cookies from a prior consented
        // session can still be present in the browser.
        // consent-manager.clearTrackingCookies() only runs on the "forget me" path;
        // normal reject/revoke flows fire the consent event without calling it.
        const expired = "expires=Thu, 01 Jan 1970 00:00:00 GMT";
        const secure = window.location.protocol === "https:" ? ";Secure" : "";
        const hostname = window.location.hostname;
        ["ai_user", "ai_session"].forEach(function (name) {
            document.cookie = `${name}=;${expired};path=/${secure}`;
            document.cookie = `${name}=;${expired};path=/;domain=${hostname}${secure}`;
            document.cookie = `${name}=;${expired};path=/;domain=.${hostname}${secure}`;
        });
    }

    function syncConsentState() {
        if (hasAnalyticsConsent()) {
            onConsentGranted();
        } else {
            onConsentRevoked();
        }
    }

    function generateSpanId() {
        const arr = new Uint8Array(8);
        crypto.getRandomValues(arr);
        return Array.from(arr, function (b) { return b.toString(16).padStart(2, "0"); }).join("");
    }

    function getCurrentTraceparent() {
        const traceId = appInsights?.context?.telemetryTrace?.traceID;
        if (typeof traceId === "string" && /^[a-f0-9]{32}$/i.test(traceId)) {
            // Return a full W3C traceparent so callers don't need to synthesise span IDs.
            return `00-${traceId.toLowerCase()}-${generateSpanId()}-01`;
        }
        return null;
    }

    window.ecsGetAppInsights = function () {
        return appInsights;
    };

    // Returns a W3C traceparent string (00-{traceId}-{spanId}-01) suitable for passing
    // as configuration.correlationContext to the TryDotNet SDK.
    window.ecsGetCorrelationContext = function () {
        return getCurrentTraceparent();
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
