// WebFilez browser helpers. Loaded as an ES module by WebFilezInterop. State is kept per file
// manager root element, so several instances can share a page.
//
// Uploads go over plain HTTP rather than through Blazor. InputFile would stream every byte over
// the SignalR circuit, which is slow, caps out well below large files and blocks the circuit
// while it runs. XHR also gives real progress events.

const instances = new WeakMap();
const activeUploads = new Map();
let nextUploadId = 1;

// Editors are registered by the host page. The map lives on globalThis so it is shared even when
// the page and the component import this file through two different URLs (a fingerprinted asset
// and the plain one). One editor can be open at a time. It is mounted on document.body so a
// Blazor re-render cannot wipe the third-party DOM out of the component.
const editorRegistry = (globalThis.WebFilez ??= {}).editorRegistry ??= new Map();
let editorSession = null;

const INTERNAL_DRAG_TYPE = 'application/x-webfilez';

export function attach(root, dropZone, dotNetRef) {
    if (!(root instanceof Element) || !dotNetRef) {
        return;
    }

    detach(root);

    const state = { dotNetRef, pending: [], dropZone, depth: 0, handlers: [] };
    instances.set(root, state);

    const on = (element, type, handler) => {
        element.addEventListener(type, handler);
        state.handlers.push(() => element.removeEventListener(type, handler));
    };

    on(root, 'keydown', e => onKeyDown(e, state));

    // Firefox won't start a drag unless dragstart stores some data, and the internal type lets
    // the drop zone tell an item being moved apart from files dragged in from the desktop.
    on(root, 'dragstart', e => {
        if (e.target instanceof Element && e.target.closest('[data-wf-draggable]')) {
            e.dataTransfer.setData(INTERNAL_DRAG_TYPE, '1');
            e.dataTransfer.effectAllowed = 'copyMove';
        }
    });

    if (dropZone instanceof Element) {
        on(dropZone, 'dragenter', e => {
            if (!isExternalFileDrag(e)) {
                return;
            }

            e.preventDefault();

            // dragenter and dragleave also fire for descendants, so a counter stops the
            // highlight flickering as the pointer crosses child elements.
            if (++state.depth === 1) {
                dropZone.classList.add('wf-drop-active');
            }
        });

        on(dropZone, 'dragover', e => {
            if (isExternalFileDrag(e)) {
                e.preventDefault();
                e.dataTransfer.dropEffect = 'copy';
            }
        });

        on(dropZone, 'dragleave', e => {
            if (!isExternalFileDrag(e)) {
                return;
            }

            state.depth = Math.max(0, state.depth - 1);
            if (state.depth === 0) {
                dropZone.classList.remove('wf-drop-active');
            }
        });

        on(dropZone, 'drop', async e => {
            if (!isExternalFileDrag(e)) {
                return;
            }

            e.preventDefault();
            state.depth = 0;
            dropZone.classList.remove('wf-drop-active');

            const entries = await readDataTransfer(e.dataTransfer);
            report(state, entries);
        });
    }
}

export function detach(root) {
    const state = instances.get(root);
    if (!state) {
        return;
    }

    state.handlers.forEach(remove => remove());
    instances.delete(root);
}

/** Opens the OS file picker and reports the chosen files, as a drop would. */
export function pickFiles(root, directories, accept) {
    const state = instances.get(root);
    if (!state) {
        return;
    }

    const input = document.createElement('input');
    input.type = 'file';
    input.multiple = true;

    if (directories) {
        input.webkitdirectory = true;
    } else if (accept) {
        input.accept = accept;
    }

    input.addEventListener('change', () => {
        const entries = Array.from(input.files, file => ({ file, path: file.webkitRelativePath || file.name }));
        report(state, entries);
    });

    input.click();
}

/** Uploads what the last drop or pick reported, one file at a time. */
export async function uploadPending(root, url, rootId, path, overwrite, includeCredentials) {
    const state = instances.get(root);
    if (!state) {
        return;
    }

    const entries = state.pending;
    state.pending = [];

    for (const entry of entries) {
        const uploadId = nextUploadId++;

        try {
            await send(url, rootId, path, entry, overwrite, includeCredentials, uploadId, state.dotNetRef);
            await state.dotNetRef.invokeMethodAsync('OnUploadComplete', uploadId, entry.file.name, true, null);
        } catch (error) {
            await state.dotNetRef.invokeMethodAsync('OnUploadComplete', uploadId, entry.file.name, false, String(error?.message ?? error));
        }
    }

    await state.dotNetRef.invokeMethodAsync('OnUploadBatchComplete');
}

export function cancelUpload(root, uploadId) {
    activeUploads.get(uploadId)?.abort();
    activeUploads.delete(uploadId);
}

/** Starts a download without leaving the page. */
export function download(url) {
    const link = document.createElement('a');
    link.href = url;
    link.rel = 'noopener';
    link.download = '';
    document.body.appendChild(link);
    link.click();
    link.remove();
}

export async function copyText(text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        return false;
    }
}

/** Focuses an input; for file names, selects the part before the extension as Explorer does. */
export function focus(element, selectStem) {
    if (!(element instanceof HTMLElement)) {
        return;
    }

    element.focus();

    if (element instanceof HTMLInputElement) {
        const dot = selectStem ? element.value.lastIndexOf('.') : -1;
        element.setSelectionRange(0, dot > 0 ? dot : element.value.length);
    }
}

/** Focuses one listing row. Called after arrow keys move the selection. */
export function focusItem(root, key) {
    if (!(root instanceof Element)) {
        return;
    }

    const row = root.querySelector(`[data-wf-key="${CSS.escape(key)}"]`);
    if (row instanceof HTMLElement) {
        row.focus({ preventScroll: true });
        row.scrollIntoView({ block: 'nearest', inline: 'nearest' });
    }
}

/** Nudges a fixed-position menu back inside the viewport when it would overflow an edge. */
export function fitToViewport(element) {
    if (!(element instanceof HTMLElement)) {
        return;
    }

    const rect = element.getBoundingClientRect();
    const margin = 8;

    // clientWidth/Height exclude the page's scrollbars, which innerWidth/Height include.
    const viewportWidth = document.documentElement.clientWidth;
    const viewportHeight = document.documentElement.clientHeight;

    if (rect.right > viewportWidth - margin) {
        element.style.left = `${Math.max(margin, viewportWidth - rect.width - margin)}px`;
    }

    if (rect.bottom > viewportHeight - margin) {
        element.style.top = `${Math.max(margin, viewportHeight - rect.height - margin)}px`;
    }
}

export function getStored(key) {
    try {
        return localStorage.getItem(`webfilez:${key}`);
    } catch {
        return null;
    }
}

export function setStored(key, value) {
    try {
        localStorage.setItem(`webfilez:${key}`, value);
    } catch {
        // Storage may be disabled; preferences just won't persist.
    }
}

/**
 * Registers an editor for the file types in `accept` (an HTML accept list: `.jpg,.png,image/webp`).
 * `open(file, host)` mounts into `host.element` and returns a dispose function.
 * `host.save(blob, fileName)` writes the bytes back. The original name replaces the file; a new
 * name is saved beside it. `host.close()` dismisses the editor. `file.sourceUrl` is a blob URL.
 */
export function registerEditor(editor) {
    if (editor === null || typeof editor !== 'object') {
        return;
    }

    const id = typeof editor.id === 'string' ? editor.id.trim() : '';
    if (id.length === 0 || typeof editor.open !== 'function') {
        return;
    }

    const name = typeof editor.name === 'string' && editor.name.trim().length > 0 ? editor.name.trim() : id;
    editorRegistry.set(id, {
        id,
        name,
        accept: typeof editor.accept === 'string' ? editor.accept.trim() : '',
        open: editor.open,
    });
}

/** JSON for the component, so the payload survives trimming. The open function stays here. */
export function listEditors() {
    return JSON.stringify([...editorRegistry.values()].map(editor => ({
        id: editor.id,
        name: editor.name,
        accept: editor.accept,
    })));
}

/**
 * Loads the file, then lets the named editor mount into a full-screen container.
 * `fileJson` is `{ name, path, rootId, url, mimeType, theme, language, loadingLabel }`.
 */
export async function openEditor(id, fileJson, uploadUrl, includeCredentials, dotNetRef) {
    const editor = editorRegistry.get(id);
    if (!editor) {
        throw new Error('That editor is not available.');
    }

    // Replacing another file manager's editor tells it to drop the session. Replacing our own
    // does not, or the close callback would run while this open is still in progress.
    const previous = editorSession;
    if (previous) {
        const notify = sameOwner(previous.owner, dotNetRef) ? null : previous.owner;
        await dismissEditor(previous);
        if (notify) {
            try {
                await notify.invokeMethodAsync('OnEditorClosed');
            } catch {
                // That file manager is already gone.
            }
        }
    }

    const file = JSON.parse(fileJson);
    const session = { owner: dotNetRef, closing: false, sourceUrl: null, shell: null, dispose: null };
    editorSession = session;

    try {
        const sourceUrl = await fetchBlobUrl(file.url, includeCredentials);
        if (editorSession !== session) {
            URL.revokeObjectURL(sourceUrl);
            return;
        }

        session.sourceUrl = sourceUrl;
        const theme = editorTheme(file.theme);
        const shell = document.createElement('div');
        shell.className = 'wf-editor-host';
        shell.dataset.theme = theme;
        shell.setAttribute('role', 'dialog');
        shell.setAttribute('aria-modal', 'true');
        shell.setAttribute('aria-label', file.name);

        const status = document.createElement('p');
        status.className = 'wf-editor-status';
        status.textContent = file.loadingLabel || 'Opening editor…';

        const mount = document.createElement('div');
        mount.className = 'wf-editor-mount';
        mount.tabIndex = -1;
        shell.append(status, mount);
        document.body.append(shell);
        document.body.classList.add('wf-editor-open');
        session.shell = shell;

        const host = {
            element: mount,
            save: (blob, fileName) => saveEditedFile(file, blob, fileName, uploadUrl, includeCredentials, dotNetRef),
            close() {
                if (session.closing) {
                    return;
                }

                session.closing = true;
                // FileRobot calls this from its click handler and then keeps running, so unmount
                // on the next turn. The shell has to go here: the .NET callback arrives with a new
                // wrapper for this component, and closeEditor matches that by id.
                queueMicrotask(() => {
                    void dismissEditor(session);
                });
                dotNetRef.invokeMethodAsync('OnEditorClosed').catch(() => { });
            },
        };

        const dispose = await editor.open({ ...file, sourceUrl, theme }, host);
        if (editorSession !== session) {
            try {
                await dispose?.();
            } catch {
                // The editor was already going away.
            }

            shell.remove();
            URL.revokeObjectURL(sourceUrl);
            return;
        }

        session.dispose = typeof dispose === 'function' ? dispose : null;
        shell.classList.add('is-ready');
        mount.focus();
    } catch (error) {
        if (editorSession === session) {
            editorSession = null;
        }

        session.shell?.remove();
        if (session.sourceUrl) {
            URL.revokeObjectURL(session.sourceUrl);
        }

        if (!editorSession) {
            document.body.classList.remove('wf-editor-open');
        }

        throw error;
    }
}

/** Closes the editor when it belongs to this file manager. A missing owner closes whichever is open. */
export async function closeEditor(owner) {
    if (owner && !sameOwner(editorSession?.owner, owner)) {
        return;
    }

    await dismissEditor(editorSession);
}

// Blazor builds a new JS object for the same .NET reference on every call. The id stays put.
function sameOwner(left, right) {
    if (!left || !right) {
        return false;
    }

    if (left === right) {
        return true;
    }

    const leftId = left._id;
    const rightId = right._id;
    return leftId != null && leftId === rightId;
}

async function dismissEditor(session) {
    if (!session || editorSession !== session) {
        return;
    }

    editorSession = null;
    session.closing = true;

    try {
        await session.dispose?.();
    } catch {
        // Already unmounted.
    }

    session.shell?.remove();
    if (session.sourceUrl) {
        URL.revokeObjectURL(session.sourceUrl);
    }

    if (!editorSession) {
        document.body.classList.remove('wf-editor-open');
    }
}

function editorTheme(theme) {
    if (theme === 'dark') {
        return 'dark';
    }

    if (theme === 'auto' && window.matchMedia('(prefers-color-scheme: dark)').matches) {
        return 'dark';
    }

    return 'light';
}

async function fetchBlobUrl(url, includeCredentials) {
    const response = await fetch(url, { credentials: includeCredentials ? 'include' : 'same-origin' });
    if (!response.ok) {
        throw new Error('The file could not be opened.');
    }

    return URL.createObjectURL(await response.blob());
}

async function saveEditedFile(file, blob, fileName, uploadUrl, includeCredentials, dotNetRef) {
    if (!(blob instanceof Blob)) {
        throw new Error('The editor did not return a file.');
    }

    const requested = bareFileName(fileName);
    const overwrite = requested.length === 0 || sameFileName(requested, file.name);
    const name = overwrite ? file.name : requested;

    try {
        await postFile(uploadUrl, file.rootId, parentPath(file.path), blob, name, overwrite, includeCredentials);
        await dotNetRef.invokeMethodAsync('OnEditorSaved', name);
    } catch (error) {
        const message = String(error?.message ?? error);
        try {
            await dotNetRef.invokeMethodAsync('OnEditorFailed', message);
        } catch {
            // The circuit is gone; the rejection still tells the editor the save failed.
        }

        throw error;
    }
}

function postFile(url, rootId, path, blob, fileName, overwrite, includeCredentials) {
    const form = new FormData();
    form.append('root', rootId);
    form.append('path', path);
    form.append('relativePath', fileName);
    form.append('overwrite', overwrite ? 'true' : 'false');
    form.append('file', blob, fileName);

    return new Promise((resolve, reject) => {
        const request = new XMLHttpRequest();
        request.open('POST', url, true);
        request.withCredentials = !!includeCredentials;
        request.setRequestHeader('X-WebFilez', '1');

        request.onload = () => {
            if (request.status >= 200 && request.status < 300) {
                resolve();
                return;
            }

            reject(new Error(problemText(request) || `HTTP ${request.status}`));
        };

        request.onerror = () => reject(new Error('The connection was lost.'));
        request.onabort = () => reject(new Error('Cancelled.'));
        request.send(form);
    });
}

function problemText(request) {
    let message = request.responseText;
    try {
        const problem = JSON.parse(request.responseText);
        message = problem.detail || Object.values(problem.errors ?? {}).flat()[0] || problem.title || message;
    } catch {
        // Not a problem-details response.
    }

    return message;
}

function parentPath(path) {
    const normalized = String(path ?? '').replaceAll('\\', '/');
    const slash = normalized.lastIndexOf('/');
    return slash > 0 ? normalized.slice(0, slash) : '/';
}

function bareFileName(value) {
    if (typeof value !== 'string') {
        return '';
    }

    const name = value.trim().split(/[/\\]/).pop() ?? '';
    return name === '.' || name === '..' ? '' : name;
}

function sameFileName(left, right) {
    return left.localeCompare(right, undefined, { sensitivity: 'accent' }) === 0;
}

/** Raises a DOM event that bubbles out of shadow roots, for React and plain-HTML hosts. */
export function dispatch(root, eventName, detailJson) {
    if (root instanceof Element) {
        root.dispatchEvent(new CustomEvent(eventName, { bubbles: true, composed: true, detail: JSON.parse(detailJson) }));
    }
}

function onKeyDown(e, state) {
    if (editorSession) {
        return;
    }

    const target = e.target;

    // The editor saves on Ctrl+S in .NET; stop the browser's own "save page" dialog.
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's' && target instanceof Element && target.closest('.wf-dialog-editor')) {
        e.preventDefault();
        return;
    }

    if (target instanceof HTMLElement && (target.isContentEditable || target.closest('button, a, input, textarea, select, [role="dialog"], [role="alertdialog"]'))) {
        return;
    }

    // Shortcuts apply to the file list. A focused toolbar button keeps its own keys.
    if (!(target instanceof Element) || !target.closest('.wf-listing')) {
        return;
    }

    const ctrl = e.ctrlKey || e.metaKey;
    const key = e.key === ' ' ? 'Space' : (e.key.length === 1 ? e.key.toLowerCase() : e.key);
    const parts = [];
    if (e.shiftKey) {
        parts.push('shift');
    }

    if (ctrl) {
        parts.push('ctrl');
    }

    parts.push(key);
    const combo = parts.join('+');

    const handled = [
        'ctrl+a', 'ctrl+c', 'ctrl+x', 'ctrl+v', 'Delete', 'F2', 'Escape', 'Enter', 'Backspace',
        'Space', 'shift+Space',
        'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Home', 'End',
        'shift+ArrowUp', 'shift+ArrowDown', 'shift+ArrowLeft', 'shift+ArrowRight', 'shift+Home', 'shift+End',
        'ctrl+ArrowUp', 'ctrl+ArrowDown', 'ctrl+ArrowLeft', 'ctrl+ArrowRight', 'ctrl+Home', 'ctrl+End',
    ];
    if (!handled.includes(combo)) {
        return;
    }

    e.preventDefault();
    state.dotNetRef.invokeMethodAsync('OnShortcut', combo);
}

function isExternalFileDrag(e) {
    const types = Array.from(e.dataTransfer?.types ?? []);
    return types.includes('Files') && !types.includes(INTERNAL_DRAG_TYPE);
}

function report(state, entries) {
    if (entries.length === 0) {
        return;
    }

    state.pending = entries;
    state.dotNetRef.invokeMethodAsync('OnFilesDropped', entries.map(entry => ({
        name: entry.file.name,
        size: entry.file.size,
        relativePath: entry.path,
    })));
}

function send(url, rootId, path, entry, overwrite, includeCredentials, uploadId, dotNetRef) {
    const form = new FormData();
    form.append('root', rootId);
    form.append('path', path);
    form.append('relativePath', entry.path || entry.file.name);
    form.append('overwrite', overwrite ? 'true' : 'false');
    form.append('file', entry.file, entry.file.name);

    return new Promise((resolve, reject) => {
        const request = new XMLHttpRequest();
        request.open('POST', url, true);
        request.withCredentials = !!includeCredentials;
        request.setRequestHeader('X-WebFilez', '1');

        // Throttled: a fast local upload fires progress far more often than is worth rendering.
        let lastReport = 0;
        request.upload.onprogress = e => {
            if (!e.lengthComputable) {
                return;
            }

            const now = Date.now();
            if (now - lastReport < 150 && e.loaded < e.total) {
                return;
            }

            lastReport = now;
            dotNetRef.invokeMethodAsync('OnUploadProgress', uploadId, entry.file.name, e.loaded, e.total);
        };

        request.onload = () => {
            activeUploads.delete(uploadId);

            if (request.status >= 200 && request.status < 300) {
                resolve();
                return;
            }

            let message = request.responseText;
            try {
                const problem = JSON.parse(request.responseText);
                message = problem.detail || Object.values(problem.errors ?? {}).flat()[0] || problem.title || message;
            } catch {
                // Not a problem-details response.
            }

            reject(new Error(message || `HTTP ${request.status}`));
        };

        request.onerror = () => {
            activeUploads.delete(uploadId);
            reject(new Error('The connection was lost.'));
        };

        request.onabort = () => {
            activeUploads.delete(uploadId);
            reject(new Error('Cancelled.'));
        };

        activeUploads.set(uploadId, request);
        request.send(form);
    });
}

/**
 * Flattens a drop into files with their relative paths, walking folders where the browser
 * exposes them, so dropping a folder keeps its structure.
 */
async function readDataTransfer(dataTransfer) {
    const items = Array.from(dataTransfer.items ?? []);

    if (items.length === 0 || typeof items[0].webkitGetAsEntry !== 'function') {
        return Array.from(dataTransfer.files, file => ({ file, path: file.name }));
    }

    // Entries must be taken synchronously; the DataTransfer is emptied after the first await.
    const files = Array.from(dataTransfer.files);
    const roots = items.map(item => item.webkitGetAsEntry()).filter(entry => entry);

    // Files that don't come from the file system, such as ones made by script or dragged
    // out of some apps, have no entry.
    if (roots.length === 0) {
        return files.map(file => ({ file, path: file.name }));
    }

    const results = [];

    for (const entry of roots) {
        await walkEntry(entry, '', results);
    }

    return results;
}

function walkEntry(entry, prefix, results) {
    return new Promise(resolve => {
        if (entry.isFile) {
            entry.file(file => {
                results.push({ file, path: prefix + file.name });
                resolve();
            }, () => resolve());
            return;
        }

        if (!entry.isDirectory) {
            resolve();
            return;
        }

        const reader = entry.createReader();
        let children = [];

        // readEntries returns at most 100 entries per call, so it has to be drained.
        const readBatch = () => reader.readEntries(async batch => {
            if (batch.length === 0) {
                for (const child of children) {
                    await walkEntry(child, `${prefix}${entry.name}/`, results);
                }

                resolve();
                return;
            }

            children = children.concat(Array.from(batch));
            readBatch();
        }, () => resolve());

        readBatch();
    });
}

// A host can push editors onto WebFilez.editors before this module loads. Later calls use
// registerEditor, which replaces an id, so configuring twice keeps the latest one.
const queuedEditors = globalThis.WebFilez?.editors;
if (Array.isArray(queuedEditors)) {
    queuedEditors.forEach(registerEditor);
}

globalThis.WebFilez = Object.assign(globalThis.WebFilez ?? {}, { registerEditor, listEditors });
