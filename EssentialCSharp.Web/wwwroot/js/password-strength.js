/**
 * Password strength meter with zxcvbn-ts scoring and HaveIBeenPwned breach advisory.
 *
 * Auto-initialises on any element with [data-password-field] in the page.
 * Attributes:
 *   data-password-field        — CSS selector for the password input (e.g. "#Input_Password")
 *   data-user-input-fields     — Comma-separated CSS selectors for fields whose live values
 *                                should penalise the strength score (e.g. "#Input_Email,#Input_UserName")
 */

import { zxcvbn, zxcvbnOptions } from '@zxcvbn-ts/core';

const SCORE_CONFIG = [
    { label: 'Very weak', barClass: 'bg-danger',            width: 20 },
    { label: 'Weak',      barClass: 'bg-warning text-dark', width: 40 },
    { label: 'Fair',      barClass: 'bg-info text-dark',    width: 60 },
    { label: 'Strong',    barClass: 'bg-primary',           width: 80 },
    { label: 'Very strong', barClass: 'bg-success',         width: 100 },
];

let zxcvbnReady = false;
let zxcvbnLoadPromise = null;

async function ensureZxcvbn() {
    if (zxcvbnReady) return;
    if (!zxcvbnLoadPromise) {
        zxcvbnLoadPromise = Promise.all([
            import('@zxcvbn-ts/language-common'),
            import('@zxcvbn-ts/language-en'),
        ]);
    }
    const [zxcvbnCommon, zxcvbnEn] = await zxcvbnLoadPromise;
    zxcvbnOptions.setOptions({
        translations: zxcvbnEn.translations,
        graphs: zxcvbnCommon.adjacencyGraphs,
        dictionary: { ...zxcvbnCommon.dictionary, ...zxcvbnEn.dictionary },
        useLevenshteinDistance: true,
    });
    zxcvbnReady = true;
}

function debounce(fn, ms) {
    let timer;
    return (...args) => {
        clearTimeout(timer);
        timer = setTimeout(() => fn(...args), ms);
    };
}

function getUserInputValues(userInputFieldIds) {
    if (!userInputFieldIds) return [];
    const raw = userInputFieldIds
        .split(',')
        .map(sel => document.querySelector(sel.trim())?.value)
        .filter(Boolean);

    const parts = [];
    for (const val of raw) {
        parts.push(val);
        // Split email addresses: "john.doe@gmail.com" → "john.doe", "john", "doe", "gmail"
        if (val.includes('@')) {
            const [local, domain] = val.split('@');
            parts.push(local);
            parts.push(...local.split(/[._+\-]/));
            const domainBase = domain?.split('.')[0];
            if (domainBase) parts.push(domainBase);
        }
        // Split on common word-separators: spaces, dots, underscores, hyphens
        parts.push(...val.split(/[\s._@+\-]+/));
    }
    // Deduplicate and discard single-character fragments (too noisy)
    return [...new Set(parts.filter(s => s.length >= 2))];
}

function updateMeter(container, score, feedback, crackTimesDisplay) {
    const config = SCORE_CONFIG[score];
    const bar = container.querySelector('.password-strength-bar');
    const label = container.querySelector('.password-strength-label');
    const warningEl = container.querySelector('.password-strength-warning');
    const suggestionsEl = container.querySelector('.password-strength-suggestions');
    const crackTimeEl = container.querySelector('.password-strength-cracktime');

    // Reset bar classes
    bar.className = 'progress-bar password-strength-bar ' + config.barClass;
    bar.style.width = config.width + '%';

    // Update aria
    const progressEl = container.querySelector('.progress');
    progressEl.setAttribute('aria-valuenow', config.width);

    label.textContent = config.label;

    // Warning: what's wrong (null for score >= 3)
    if (warningEl) {
        warningEl.textContent = feedback.warning ?? '';
        warningEl.classList.toggle('d-none', !feedback.warning);
    }

    // Suggestions: how to improve
    if (suggestionsEl) {
        const tips = (feedback.suggestions ?? []).filter(Boolean);
        suggestionsEl.textContent = tips.join(' ');
        suggestionsEl.classList.toggle('d-none', tips.length === 0);
    }

    // Crack time estimate (online throttled — most relevant for web app context)
    if (crackTimeEl) {
        const display = crackTimesDisplay?.onlineThrottling100PerHour;
        crackTimeEl.textContent = display ? `Time to crack: ${display}` : '';
        crackTimeEl.classList.toggle('d-none', !display);
    }
}

function clearMeter(container) {
    const bar = container.querySelector('.password-strength-bar');
    bar.className = 'progress-bar password-strength-bar';
    bar.style.width = '0';
    container.querySelector('.progress').setAttribute('aria-valuenow', '0');
    container.querySelector('.password-strength-label').textContent = '';
    const warningEl = container.querySelector('.password-strength-warning');
    if (warningEl) { warningEl.textContent = ''; warningEl.classList.add('d-none'); }
    const suggestionsEl = container.querySelector('.password-strength-suggestions');
    if (suggestionsEl) { suggestionsEl.textContent = ''; suggestionsEl.classList.add('d-none'); }
    const crackTimeEl = container.querySelector('.password-strength-cracktime');
    if (crackTimeEl) { crackTimeEl.textContent = ''; crackTimeEl.classList.add('d-none'); }
    container.querySelector('.password-hibp-warning').classList.add('d-none');
}

async function checkHibp(password) {
    try {
        const data = new TextEncoder().encode(password);
        const hashBuf = await crypto.subtle.digest('SHA-1', data);
        const hashHex = [...new Uint8Array(hashBuf)]
            .map(b => b.toString(16).padStart(2, '0'))
            .join('')
            .toUpperCase();

        const prefix = hashHex.slice(0, 5);
        const suffix = hashHex.slice(5);

        const resp = await fetch(`https://api.pwnedpasswords.com/range/${prefix}`, {
            headers: { 'Add-Padding': 'true' }
        });
        if (!resp.ok) return false;

        const text = await resp.text();
        for (const line of text.split(/\r?\n/)) {
            const [hash, countStr] = line.trim().split(':');
            // Padded responses include decoy entries with count=0; discard them per the HIBP spec.
            if (hash === suffix && parseInt(countStr, 10) > 0) return true;
        }
        return false;
    } catch {
        // Network failure — fail silently, do not alarm the user
        return false;
    }
}

function initMeter(container) {
    const passwordSelector = container.dataset.passwordField;
    const userInputFieldIds = container.dataset.userInputFields || '';

    const passwordInput = document.querySelector(passwordSelector);
    if (!passwordInput) return;

    let hibpWarningActive = false;
    let blurGeneration = 0;

    const onInput = debounce(async () => {
        const password = passwordInput.value;

        if (!password) {
            clearMeter(container);
            hibpWarningActive = false;
            return;
        }

        await ensureZxcvbn();
        const userInputs = getUserInputValues(userInputFieldIds);
        const result = zxcvbn(password, userInputs);
        updateMeter(container, result.score, result.feedback, result.crackTimesDisplay);

        // Re-show HIBP warning if previously triggered and password unchanged
        const hibpEl = container.querySelector('.password-hibp-warning');
        if (hibpWarningActive) {
            hibpEl.classList.remove('d-none');
        }
    }, 300);

    const onBlur = async () => {
        const password = passwordInput.value;
        if (!password) return;

        const generation = ++blurGeneration;
        const hibpEl = container.querySelector('.password-hibp-warning');
        const breached = await checkHibp(password);
        // Discard if the password changed or a newer blur already started.
        if (passwordInput.value !== password || generation !== blurGeneration) return;
        hibpWarningActive = breached;
        hibpEl.classList.toggle('d-none', !breached);
    };

    // Clear HIBP warning when user starts changing password again
    passwordInput.addEventListener('input', () => {
        hibpWarningActive = false;
        blurGeneration++;
        container.querySelector('.password-hibp-warning').classList.add('d-none');
        onInput();
    });

    passwordInput.addEventListener('blur', onBlur);

    // Recompute strength score when related fields (email, username) change,
    // since they are used as zxcvbn user inputs to penalise guessable passwords.
    if (userInputFieldIds) {
        for (const sel of userInputFieldIds.split(',')) {
            const el = document.querySelector(sel.trim());
            if (el) el.addEventListener('input', onInput);
        }
    }
}

// Auto-init all meters on the page
document.querySelectorAll('[data-password-field]').forEach(initMeter);
