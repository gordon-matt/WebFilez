// WebFilez browser helpers. Loaded as an ES module by WebFilezInterop. State is kept per file
// manager root element, so several instances can share a page.
//
// Uploads go over plain HTTP rather than through Blazor. InputFile would stream every byte over
// the SignalR circuit, which is slow, caps out well below large files and blocks the circuit
// while it runs. XHR also gives real progress events.

const instances = new WeakMap();
const activeUploads = new Map();
let nextUploadId = 1;

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

/** Raises a DOM event that bubbles out of shadow roots, for React and plain-HTML hosts. */
export function dispatch(root, eventName, detailJson) {
    if (root instanceof Element) {
        root.dispatchEvent(new CustomEvent(eventName, { bubbles: true, composed: true, detail: JSON.parse(detailJson) }));
    }
}

function onKeyDown(e, state) {
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
