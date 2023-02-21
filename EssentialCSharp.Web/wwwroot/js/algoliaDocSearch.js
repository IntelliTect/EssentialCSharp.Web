const docsearchScriptSrc = "https://cdn.jsdelivr.net/npm/@docsearch/js@3";

function loadScript(src) {
    return new Promise((resolve, reject) => {
        const script = document.createElement("script");
        script.src = src;
        script.onload = () => resolve();
        script.onerror = () => reject();
        document.head.appendChild(script);
    });
}
async function loadScripts() {

    await loadScript(docsearchScriptSrc);


  docsearch({
        container: '#docsearch',
        appId: 'GB72QKIBIQ',
        apiKey: '83579df03a856321aed20274e4341247',
        indexName: 'essentialcsharp',
    });
}


loadScripts();
