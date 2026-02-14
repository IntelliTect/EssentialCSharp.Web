// TryDotNet Module - Vue.js composable for interactive code execution
import { ref, nextTick, onMounted, onUnmounted } from 'vue';

// Timeout durations (ms)
const HEALTH_CHECK_TIMEOUT = 5000;
const SESSION_CREATION_TIMEOUT = 20000;
const RUN_TIMEOUT = 30000;

// User-friendly error messages
const ERROR_MESSAGES = {
    serviceUnavailable: 'The code execution service is currently unavailable. Please try again later.',
    serviceNotConfigured: 'Interactive code execution is not available at this time.',
    sessionTimeout: 'The code editor took too long to load. The service may be temporarily unavailable.',
    runTimeout: 'Code execution timed out. The service may be temporarily unavailable.',
    editorNotFound: 'Could not initialize the code editor. Please try again.',
    sessionNotInitialized: 'The code editor session is not ready. Please try reopening the code runner.',
    fetchFailed: 'Could not load the listing source code. Please try again.',
};

/**
 * Races a promise against a timeout. Rejects with the given message if the
 * timeout fires first.
 * @param {Promise} promise - The promise to race
 * @param {number} ms - Timeout in milliseconds
 * @param {string} timeoutMsg - Message for the timeout error
 * @returns {Promise}
 */
function withTimeout(promise, ms, timeoutMsg) {
    let timer;
    const timeout = new Promise((_, reject) => {
        timer = setTimeout(() => reject(new Error(timeoutMsg)), ms);
    });
    return Promise.race([promise, timeout]).finally(() => clearTimeout(timer));
}

/**
 * Checks whether the TryDotNet origin is configured and non-empty.
 * @returns {boolean}
 */
function isTryDotNetConfigured() {
    const origin = window.TRYDOTNET_ORIGIN;
    return typeof origin === 'string' && origin.trim().length > 0;
}

/**
 * Creates scaffolding for user code to run in the TryDotNet environment.
 * @param {string} userCode - The user's C# code to wrap
 * @returns {string} Scaffolded code with proper using statements and Main method
 */
function createScaffolding(userCode) {
    return `using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Program
{
    class Program
    {
        static void Main(string[] args)
        {
            #region controller
${userCode}
            #endregion
        }
    }
}`;
}

/**
 * Dynamically loads a script and returns a promise that resolves when loaded.
 * @param {string} url - URL of the script to load
 * @param {string} globalName - Name of the global variable the script creates
 * @param {number} timeLimit - Maximum time to wait for script load
 * @returns {Promise<any>} Promise resolving to the global object
 */
function loadLibrary(url, globalName, timeLimit = 15000) {
    return new Promise((resolve, reject) => {
        // Check if already loaded
        if (globalName && window[globalName]) {
            resolve(window[globalName]);
            return;
        }

        const timeout = setTimeout(() => {
            reject(new Error(`${url} load timeout`));
        }, timeLimit);

        const script = document.createElement('script');
        script.src = url;
        script.async = true;
        script.defer = true;
        script.crossOrigin = 'anonymous';

        script.onload = () => {
            clearTimeout(timeout);
            if (globalName && !window[globalName]) {
                reject(new Error(`${url} loaded but ${globalName} is undefined`));
            } else {
                resolve(window[globalName]);
            }
        };

        script.onerror = () => {
            clearTimeout(timeout);
            reject(new Error(`Failed to load ${url}`));
        };

        document.head.appendChild(script);
    });
}

/**
 * Vue composable for TryDotNet code execution functionality.
 * @returns {Object} Composable state and methods
 */
export function useTryDotNet() {
    // State
    const isCodeRunnerOpen = ref(false);
    const codeRunnerLoading = ref(false);
    const codeRunnerError = ref(null);
    const codeRunnerOutput = ref('');
    const codeRunnerOutputError = ref(false);
    const currentListingInfo = ref(null);
    const isRunning = ref(false);
    const isLibraryLoaded = ref(false);

    // Internal state (not exposed)
    let trydotnet = null;
    let session = null;
    let editorElement = null;
    let currentLoadedListing = null; // Track which listing is currently loaded

    /**
     * Gets the TryDotNet origin URL from config.
     * @returns {string} The TryDotNet service origin URL
     */
    function getTryDotNetOrigin() {
        return window.TRYDOTNET_ORIGIN;
    }

    /**
     * Performs a lightweight reachability check against the TryDotNet origin.
     * Rejects with a user-friendly message when the service is unreachable.
     * @returns {Promise<void>}
     */
    async function checkServiceHealth() {
        const origin = getTryDotNetOrigin();
        const controller = new AbortController();
        const timer = setTimeout(() => controller.abort(), HEALTH_CHECK_TIMEOUT);

        try {
            // Check the actual script endpoint rather than the bare origin,
            // which may not have a handler and would return 404.
            const res = await fetch(`${origin}/api/trydotnet.min.js`, {
                method: 'HEAD',
                mode: 'no-cors',
                signal: controller.signal,
            });
            // mode: 'no-cors' gives an opaque response (status 0), which is fine
            // — we only care that the network request didn't fail.
        } catch {
            throw new Error(ERROR_MESSAGES.serviceUnavailable);
        } finally {
            clearTimeout(timer);
        }
    }

    /**
     * Loads the TryDotNet library from the service.
     * Performs a health check first to fail fast.
     * @returns {Promise<void>}
     */
    async function loadTryDotNetLibrary() {
        if (isLibraryLoaded.value && trydotnet) {
            return;
        }

        if (!isTryDotNetConfigured()) {
            throw new Error(ERROR_MESSAGES.serviceNotConfigured);
        }

        // Fail fast if the service is unreachable
        await checkServiceHealth();

        const origin = getTryDotNetOrigin();
        const trydotnetUrl = `${origin}/api/trydotnet.min.js`;

        try {
            trydotnet = await loadLibrary(trydotnetUrl, 'trydotnet', 15000);
            if (!trydotnet) {
                throw new Error(ERROR_MESSAGES.serviceUnavailable);
            }
            isLibraryLoaded.value = true;
        } catch (error) {
            console.error('Failed to load TryDotNet library:', error);
            throw new Error(ERROR_MESSAGES.serviceUnavailable);
        }
    }

    /**
     * Creates a TryDotNet session with the editor iframe and initial code.
     * @param {HTMLElement} editorEl - The iframe element for the Monaco editor
     * @param {string} userCode - The C# code to display in the editor
     * @returns {Promise<void>}
     */
    async function createSession(editorEl, userCode) {
        if (!trydotnet) {
            throw new Error('TryDotNet library not loaded');
        }

        editorElement = editorEl;

        const hostOrigin = window.location.origin;
        window.postMessage({ type: 'HostEditorReady', editorId: '0' }, hostOrigin);

        const fileName = 'Program.cs';
        const isComplete = isCompleteProgram(userCode);
        const fileContent = isComplete ? userCode : createScaffolding(userCode);
        const files = [{ name: fileName, content: fileContent }];
        const project = { package: 'console', files: files };
        const document = isComplete 
            ? { fileName: fileName } 
            : { fileName: fileName, region: 'controller' };

        const configuration = {
            hostOrigin: hostOrigin,
            trydotnetOrigin: getTryDotNetOrigin(),
            enableLogging: false
        };

        session = await withTimeout(
            trydotnet.createSessionWithProjectAndOpenDocument(
                configuration,
                [editorElement],
                window,
                project,
                document
            ),
            SESSION_CREATION_TIMEOUT,
            ERROR_MESSAGES.sessionTimeout
        );

        // Subscribe to output events
        session.subscribeToOutputEvents((event) => {
            handleOutput(event);
        });
    }

    /**
     * Sets code in the Monaco editor.
     * @param {string} userCode - The C# code to display in the editor
     * @returns {Promise<void>}
     */
    async function setCode(userCode) {
        if (!session || !trydotnet) {
            throw new Error('Session not initialized');
        }

        const isComplete = isCompleteProgram(userCode);
        const fileContent = isComplete ? userCode : createScaffolding(userCode);
        const fileName = 'Program.cs';
        const files = [{ name: fileName, content: fileContent }];
        const project = await trydotnet.createProject({
            packageName: 'console',
            files: files
        });

        await session.openProject(project);

        const defaultEditor = session.getTextEditor();
        const documentOptions = {
            fileName: fileName,
            editorId: defaultEditor.id()
        };
        
        // Only add region for scaffolded code
        if (!isComplete) {
            documentOptions.region = 'controller';
        }
        
        await session.openDocument(documentOptions);
    }

    /**
     * Runs the code currently in the editor.
     * @returns {Promise<void>}
     */
    async function runCode() {
        if (!session) {
            codeRunnerOutput.value = ERROR_MESSAGES.sessionNotInitialized;
            codeRunnerOutputError.value = true;
            return;
        }

        codeRunnerOutput.value = 'Running...';
        codeRunnerOutputError.value = false;
        isRunning.value = true;

        try {
            await withTimeout(session.run(), RUN_TIMEOUT, ERROR_MESSAGES.runTimeout);
        } catch (error) {
            codeRunnerOutput.value = error.message;
            codeRunnerOutputError.value = true;
        } finally {
            isRunning.value = false;
        }
    }

    /**
     * Clears the editor content.
     */
    function clearEditor() {
        if (!session) return;

        const textEditor = session.getTextEditor();
        if (textEditor) {
            textEditor.setContent('');
            codeRunnerOutput.value = 'Editor cleared.';
            codeRunnerOutputError.value = false;
        }
    }

    /**
     * Handles output events from the TryDotNet session.
     * @param {Object} event - Output event from TryDotNet
     */
    function handleOutput(event) {
        if (event.exception) {
            codeRunnerOutput.value = event.exception.join('\n');
            codeRunnerOutputError.value = true;
        } else if (event.diagnostics && event.diagnostics.length > 0) {
            // Handle compilation errors/warnings
            const diagnosticMessages = event.diagnostics.map(d => {
                const severity = d.severity || 'Error';
                const location = d.location ? `(${d.location})` : '';
                const id = d.id ? `${d.id}: ` : '';
                return `${severity} ${location}: ${id}${d.message}`;
            });
            codeRunnerOutput.value = diagnosticMessages.join('\n');
            codeRunnerOutputError.value = true;
        } else if (event.stderr && event.stderr.length > 0) {
            // Handle standard error output
            codeRunnerOutput.value = event.stderr.join('\n');
            codeRunnerOutputError.value = true;
        } else if (event.stdout) {
            codeRunnerOutput.value = event.stdout.join('\n');
            codeRunnerOutputError.value = false;
        } else {
            codeRunnerOutput.value = 'No output';
            codeRunnerOutputError.value = false;
        }
        isRunning.value = false;
    }

    /**
     * Checks if code is a complete C# program that doesn't need scaffolding.
     * Complete programs must have a namespace declaration with class and Main,
     * or be a class named Program with Main.
     * @param {string} code - Source code to check
     * @returns {boolean} True if code is complete, false if it needs scaffolding
     */
    function isCompleteProgram(code) {
        // Check for explicit namespace declaration (most reliable indicator)
        const hasNamespace = /namespace\s+\w+/i.test(code);
        
        // Check if it's a class specifically named "Program" with Main method
        const isProgramClass = /class\s+Program\s*[\r\n{]/.test(code) && 
                              /static\s+(void|async\s+Task)\s+Main\s*\(/.test(code);
        
        // Only consider it complete if it has namespace or is the Program class
        return hasNamespace || isProgramClass;
    }

    /**
     * Extracts executable code snippet from source code.
     * If code contains #region INCLUDE, extracts only that portion.
     * Otherwise returns the full code.
     * @param {string} code - Source code to process
     * @returns {string} Extracted code snippet
     */
    function extractCodeSnippet(code) {
        // Extract code from #region INCLUDE if present
        const regionMatch = code.match(/#region\s+INCLUDE\s*\n([\s\S]*?)\n\s*#endregion\s+INCLUDE/);
        if (regionMatch) {
            return regionMatch[1].trim();
        }
        return code;
    }

    /**
     * Fetches listing source code from the API.
     * @param {string|number} chapter - Chapter number
     * @param {string|number} listing - Listing number
     * @returns {Promise<string>} The listing source code (extracted snippet)
     */
    async function fetchListingCode(chapter, listing) {
        const response = await fetch(`/api/ListingSourceCode/chapter/${chapter}/listing/${listing}`);
        if (!response.ok) {
            throw new Error(ERROR_MESSAGES.fetchFailed);
        }
        const data = await response.json();
        const code = data.content || '';
        // Extract the snippet portion if it has INCLUDE regions
        return extractCodeSnippet(code);
    }

    /**
     * Opens the code runner panel with a specific listing.
     * @param {string|number} chapter - Chapter number
     * @param {string|number} listing - Listing number
     * @param {string} title - Title to display
     */
    async function openCodeRunner(chapter, listing, title) {
        currentListingInfo.value = { chapter, listing, title };
        isCodeRunnerOpen.value = true;
        codeRunnerLoading.value = true;
        codeRunnerError.value = null;
        codeRunnerOutput.value = 'Click "Run" to execute the code.';
        codeRunnerOutputError.value = false;

        const listingKey = `${chapter}.${listing}`;

        try {
            // Load the library if not already loaded
            if (!isLibraryLoaded.value) {
                await loadTryDotNetLibrary();
            }

            // Wait for the panel to render and get the editor element
            await nextTick();

            const editorEl = document.querySelector('.code-runner-editor');
            if (!editorEl) {
                throw new Error(ERROR_MESSAGES.editorNotFound);
            }

            // Check if this listing is already loaded in the session
            if (session && currentLoadedListing === listingKey) {
                // Listing already loaded, just show the panel
                codeRunnerLoading.value = false;
                return;
            }

            // Fetch the listing code
            const code = await fetchListingCode(chapter, listing);

            // Create session if needed with the fetched code
            if (!session) {
                await createSession(editorEl, code);
                currentLoadedListing = listingKey;
            } else {
                // Session exists, update the code
                await setCode(code);
                currentLoadedListing = listingKey;
            }

            codeRunnerLoading.value = false;
        } catch (error) {
            console.error('Failed to open code runner:', error);
            codeRunnerError.value = error.message || ERROR_MESSAGES.serviceUnavailable;
            codeRunnerLoading.value = false;
        }
    }

    /**
     * Retries opening the code runner after a failure.
     * Resets the session so a fresh connection is attempted.
     */
    function retryCodeRunner() {
        // Reset session state so a fresh connection is attempted
        session = null;
        currentLoadedListing = null;
        isLibraryLoaded.value = false;
        trydotnet = null;

        if (currentListingInfo.value) {
            const { chapter, listing, title } = currentListingInfo.value;
            openCodeRunner(chapter, listing, title);
        }
    }

    /**
     * Closes the code runner panel.
     */
    function closeCodeRunner() {
        isCodeRunnerOpen.value = false;
        currentListingInfo.value = null;
        // Note: We keep the session and currentLoadedListing to avoid recreating when reopened
    }

    /**
     * Clears the output console.
     */
    function clearOutput() {
        codeRunnerOutput.value = '';
        codeRunnerOutputError.value = false;
    }

    /**
     * Injects Run buttons into code block sections.
     * Skipped entirely when TryDotNet origin is not configured.
     */
    function injectRunButtons() {
        if (!isTryDotNetConfigured()) {
            return; // Don't show Run buttons when the service is not configured
        }

        const codeBlocks = document.querySelectorAll('.code-block-section');

        codeBlocks.forEach((block) => {
            const heading = block.querySelector('.code-block-heading');
            if (!heading) return;

            // Skip if button already injected
            if (heading.querySelector('.code-runner-btn')) return;

            // Parse chapter and listing numbers from the heading
            // Format 1: <span class="CDTNUM">Listing </span><span class="TBLNUM">1.</span><span class="CDTNUM">22</span>
            // Format 2: <span class="CDTNUM">Listing</span> <span class="TBLNUM">1.</span>1: Title
            let chapter = null;
            let listing = null;

            // First, try to extract from the full heading text
            // Pattern: "Listing 1.22" or "Listing 1.1:"
            const headingText = heading.textContent;
            const listingMatch = headingText.match(/Listing\s+(\d+)\.(\d+)/i);
            
            if (listingMatch) {
                chapter = listingMatch[1];
                listing = listingMatch[2];
            } else {
                // Fallback to old method for other formats
                const spans = heading.querySelectorAll('span');
                
                spans.forEach((span) => {
                    if (span.classList.contains('TBLNUM')) {
                        // Extract chapter number (format: "1." -> "1")
                        const match = span.textContent.match(/(\d+)\./);
                        if (match) {
                            chapter = match[1];
                        }
                    }
                    if (span.classList.contains('CDTNUM') && chapter !== null && listing === null) {
                        // The CDTNUM after TBLNUM contains the listing number
                        const num = span.textContent.trim();
                        if (/^\d+$/.test(num)) {
                            listing = num;
                        }
                    }
                });
            }

            // Only add button for listing 1.1
            if (chapter === '1' && listing === '1') {
                // Wrap existing content in a span to keep it together
                const contentWrapper = document.createElement('span');
                while (heading.firstChild) {
                    contentWrapper.appendChild(heading.firstChild);
                }
                
                // Make heading a flex container
                heading.style.display = 'flex';
                heading.style.justifyContent = 'space-between';
                heading.style.alignItems = 'center';
                
                // Add wrapped content back
                heading.appendChild(contentWrapper);

                // Create run button
                const runButton = document.createElement('button');
                runButton.className = 'code-runner-btn';
                runButton.type = 'button';
                runButton.title = `Run Listing ${chapter}.${listing}`;
                runButton.innerHTML = '<i class="mdi mdi-play-circle-outline" aria-hidden="true"></i> Run';
                runButton.setAttribute('aria-label', `Run Listing ${chapter}.${listing}`);

                runButton.addEventListener('click', (e) => {
                    e.preventDefault();
                    e.stopPropagation();
                    openCodeRunner(chapter, listing, `Listing ${chapter}.${listing}`);
                });

                heading.appendChild(runButton);
            }
        });
    }

    // Lifecycle hooks
    onMounted(() => {
        // Inject run buttons after component mounts
        nextTick(() => {
            injectRunButtons();
        });
    });

    // Return composable interface
    return {
        // State
        isCodeRunnerOpen,
        codeRunnerLoading,
        codeRunnerError,
        codeRunnerOutput,
        codeRunnerOutputError,
        currentListingInfo,
        isRunning,
        isLibraryLoaded,

        // Methods
        openCodeRunner,
        closeCodeRunner,
        retryCodeRunner,
        runCode,
        clearEditor,
        clearOutput,
        injectRunButtons
    };
}
