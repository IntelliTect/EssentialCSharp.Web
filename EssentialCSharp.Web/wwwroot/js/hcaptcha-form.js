window.EssentialCSharp = window.EssentialCSharp || {};

(function (namespace) {
    const callbacks = [];
    let isLoaded = typeof window.hcaptcha !== 'undefined';

    function isHcaptchaReady() {
        return isLoaded
            && typeof window.hcaptcha !== 'undefined'
            && typeof window.hcaptcha.render === 'function'
            && typeof window.hcaptcha.execute === 'function';
    }

    function flushCallbacks() {
        if (!isHcaptchaReady()) {
            return;
        }

        while (callbacks.length > 0) {
            const callback = callbacks.shift();
            callback();
        }
    }

    function onDomReady(callback) {
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', callback, { once: true });
            return;
        }

        callback();
    }

    function isFormValid(form) {
        if (!form.checkValidity()) {
            form.reportValidity();
            return false;
        }

        if (!window.jQuery || !window.jQuery.fn || typeof window.jQuery.fn.valid !== 'function') {
            return true;
        }

        return window.jQuery(form).valid();
    }

    function resolveGlobalCallback(callbackName) {
        if (!callbackName) {
            return null;
        }

        const callback = window[callbackName];
        return typeof callback === 'function' ? callback : null;
    }

    function renderDeclarativeWidgets() {
        document.querySelectorAll('.h-captcha[data-sitekey]').forEach(function (element) {
            if (element.dataset.ecsRendered === 'true') {
                return;
            }

            const siteKey = element.dataset.sitekey;
            if (!siteKey) {
                return;
            }

            const options = {
                sitekey: siteKey,
                size: element.dataset.size || 'normal'
            };

            const callback = resolveGlobalCallback(element.dataset.callback);
            if (callback) {
                options.callback = callback;
            }

            window.hcaptcha.render(element, options);
            element.dataset.ecsRendered = 'true';
        });
    }

    namespace.whenHcaptchaReady = function (callback) {
        if (isHcaptchaReady()) {
            callback();
            return;
        }

        callbacks.push(callback);
    };

    namespace.onHcaptchaLoad = function () {
        isLoaded = true;
        flushCallbacks();
    };

    namespace.initializeInvisibleForm = function (options) {
        const formId = options.formId;
        const containerId = options.containerId;
        const siteKey = options.siteKey;

        onDomReady(function () {
            const form = document.getElementById(formId);
            const container = document.getElementById(containerId);

            if (!form || !container) {
                console.error('Unable to initialize hCaptcha form.', { formId, containerId });
                return;
            }

            if (!siteKey) {
                console.error('Missing hCaptcha site key.', { formId, containerId });
                return;
            }

            let widgetId = null;
            let captchaSolved = false;

            form.addEventListener('submit', function (event) {
                if (captchaSolved) {
                    return;
                }

                if (!isFormValid(form)) {
                    event.preventDefault();
                    return;
                }

                if (widgetId === null) {
                    event.preventDefault();
                    console.error('hCaptcha widget is not ready.', { formId, containerId });
                    return;
                }

                event.preventDefault();
                window.hcaptcha.execute(widgetId);
            });

            namespace.whenHcaptchaReady(function () {
                if (widgetId !== null) {
                    return;
                }

                widgetId = window.hcaptcha.render(containerId, {
                    sitekey: siteKey,
                    size: 'invisible',
                    callback: function () {
                        captchaSolved = true;
                        form.requestSubmit();
                    },
                    'expired-callback': function () {
                        captchaSolved = false;
                    },
                    'error-callback': function () {
                        captchaSolved = false;
                    }
                });
            });
        });
    };

    onDomReady(function () {
        namespace.whenHcaptchaReady(function () {
            renderDeclarativeWidgets();
        });
    });
})(window.EssentialCSharp.HCaptcha = window.EssentialCSharp.HCaptcha || {});

window.ecsOnHcaptchaLoad = function () {
    window.EssentialCSharp.HCaptcha.onHcaptchaLoad();
};
