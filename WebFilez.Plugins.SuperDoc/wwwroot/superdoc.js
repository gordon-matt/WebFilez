// Optional SuperDoc adapter. The file manager does not reference this file, and this file
// does not include SuperDoc. It loads the published build from jsDelivr when a document opens.
//
// SuperDoc's editor is AGPL-3.0 unless you have a commercial license. The DOCX engine it
// downloads is proprietary and is used as SuperDoc's dependency. This package ships neither.
// A commercial deployment sets its own license key; none is included here.
// https://docs.superdoc.dev/resources/license
// https://www.superdocportal.dev/get-in-touch

const VERSION = '2.24.0';
const SCRIPT = `https://cdn.jsdelivr.net/npm/superdoc@${VERSION}/dist-cdn/superdoc.min.js`;
const STYLE = `https://cdn.jsdelivr.net/npm/superdoc@${VERSION}/dist-cdn/superdoc.min.css`;

/** Word documents. SuperDoc opens DOCX, not the older .doc format. */
export const ACCEPT = '.docx';

const loads = new Map();

function loadStyle(href) {
    if (document.querySelector(`link[href="${href}"]`)) {
        return Promise.resolve();
    }

    return new Promise((resolve, reject) => {
        const link = document.createElement('link');
        link.rel = 'stylesheet';
        link.href = href;
        link.onload = () => resolve();
        link.onerror = () => reject(new Error(href));
        document.head.appendChild(link);
    });
}

function loadScript(src) {
    if (typeof globalThis.SuperDoc === 'function') {
        return Promise.resolve();
    }

    const existing = loads.get(src);
    if (existing) {
        return existing;
    }

    const promise = new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = src;
        script.onload = () => resolve();
        script.onerror = () => reject(new Error(src));
        document.head.appendChild(script);
    });
    loads.set(src, promise);
    return promise;
}

function stem(fileName) {
    const dot = fileName.lastIndexOf('.');
    return dot > 0 ? fileName.slice(0, dot) : fileName;
}

function cleanKey(value) {
    return typeof value === 'string' && value.trim().length > 0 ? value.trim() : '';
}

/**
 * The key passed to registerSuperDoc wins. Otherwise the page may set
 * WebFilez.superdoc.licenseKey before a document opens. An empty value is omitted,
 * and SuperDoc then uses its community identity.
 */
function licenseKey(options) {
    return cleanKey(options.licenseKey) || cleanKey(globalThis.WebFilez?.superdoc?.licenseKey);
}

async function open(file, host, options) {
    const style = new URL('./superdoc.css', import.meta.url).href;
    await Promise.all([loadStyle(style), loadStyle(STYLE), loadScript(SCRIPT)]);

    const SuperDoc = globalThis.SuperDoc;
    if (typeof SuperDoc !== 'function') {
        throw new Error('SuperDoc did not load.');
    }

    // Pass the bytes as a File. The engine worker is loaded from another origin, which cannot
    // read the page's blob URL.
    const response = await fetch(file.sourceUrl);
    if (!response.ok) {
        throw new Error('The file could not be opened.');
    }

    const docx = new File([await response.blob()], file.name, {
        type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
    });

    const root = document.createElement('div');
    root.className = 'wf-superdoc';

    const bar = document.createElement('div');
    bar.className = 'wf-superdoc-bar';

    const save = document.createElement('button');
    save.type = 'button';
    save.className = 'wf-superdoc-save';
    save.textContent = 'Save';
    save.disabled = true;

    const close = document.createElement('button');
    close.type = 'button';
    close.className = 'wf-superdoc-close';
    close.textContent = 'Close';

    const toolbar = document.createElement('div');
    toolbar.className = 'wf-superdoc-toolbar';

    const documentHost = document.createElement('div');
    documentHost.className = 'wf-superdoc-document';

    bar.append(save, close);
    root.append(bar, toolbar, documentHost);
    host.element.replaceChildren(root);

    const key = licenseKey(options);
    const editor = new SuperDoc({
        selector: documentHost,
        document: docx,
        documentMode: 'editing',
        contained: true,
        title: stem(file.name),
        ...(key ? { licenseKey: key } : {}),
        ui: { toolbar: { container: toolbar } },
        onReady: () => {
            save.disabled = false;
        },
    });

    close.addEventListener('click', () => host.close());
    save.addEventListener('click', async () => {
        save.disabled = true;
        try {
            const blob = await editor.export({
                exportType: ['docx'],
                triggerDownload: false,
                exportedName: stem(file.name),
            });
            await host.save(blob, file.name);
        } finally {
            save.disabled = false;
        }
    });

    return () => {
        try {
            editor.destroy();
        } catch {
            // Already unmounted.
        }
    };
}

export function registerSuperDoc(options = {}) {
    const editor = {
        id: options.id ?? 'superdoc',
        name: options.name ?? 'SuperDoc',
        accept: options.accept ?? ACCEPT,
        open: (file, host) => open(file, host, options),
    };

    const register = globalThis.WebFilez?.registerEditor;
    if (typeof register === 'function') {
        register(editor);
        return;
    }

    const bucket = (globalThis.WebFilez ??= {});
    if (Array.isArray(bucket.editors)) {
        bucket.editors.push(editor);
    } else {
        bucket.editors = [editor];
    }
}

registerSuperDoc();
