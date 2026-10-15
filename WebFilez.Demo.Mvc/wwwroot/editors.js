// Loads TinyMCE, CKEditor 4 and Froala from their CDNs and opens WebFilez when each one
// asks to browse for an image. `browse` is given a function; call it with the chosen URL.

const TINYMCE = 'https://cdn.jsdelivr.net/npm/tinymce@7.7.1';
const CKEDITOR_SRC = 'https://cdn.ckeditor.com/4.22.1/standard/ckeditor.js';
const FROALA_JS = 'https://cdn.jsdelivr.net/npm/froala-editor@4.3.1/js/froala_editor.pkgd.min.js';
const FROALA_CSS = 'https://cdn.jsdelivr.net/npm/froala-editor@4.3.1/css/froala_editor.pkgd.min.css';

const loads = new Map();
let browseRequest = () => {};
let pendingApply = null;
let hooksReady = false;

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

function loadScript(src, ready) {
    if (ready()) {
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

function safeUrl(url) {
    if (typeof url !== 'string' || url.length === 0) {
        return null;
    }

    if (url.startsWith('/') && !url.startsWith('//')) {
        return url;
    }

    try {
        const parsed = new URL(url);
        return parsed.protocol === 'http:' || parsed.protocol === 'https:' ? url : null;
    } catch {
        return null;
    }
}

function escapeAttr(value) {
    return value.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;');
}

function ask(apply) {
    browseRequest(url => {
        const safe = safeUrl(url);
        if (safe) {
            apply(safe);
        }
    });
}

function installHooks() {
    if (hooksReady) {
        return;
    }

    hooksReady = true;
    CKEDITOR.config.versionCheck = false;

    CKEDITOR.plugins.add('webfilez', {
        init(editor) {
            editor.addCommand('webfilez', {
                exec(ed) {
                    ask(url => ed.insertHtml(`<img alt="" src="${escapeAttr(url)}">`));
                },
            });
            editor.ui.addButton('WebFilez', {
                label: 'Browse images',
                command: 'webfilez',
                icon: 'image',
                toolbar: 'insert',
            });
        },
    });

    // filebrowser registers a global listener that points Browse Server at a popup.
    // Listeners run in ascending priority, so this one runs after it and replaces the click.
    CKEDITOR.on('dialogDefinition', event => {
        if (event.data.name !== 'image') {
            return;
        }

        const button = event.data.definition.getContents('info')?.get('browse');
        if (!button) {
            return;
        }

        button.hidden = false;
        button.onClick = function () {
            const dialog = this.getDialog();
            ask(url => dialog.setValueOf('info', 'txtUrl', url));
        };
    }, null, null, 20);

    FroalaEditor.DefineIcon('webfilez', { NAME: 'imageManager', SVG_KEY: 'imageManager' });
    FroalaEditor.RegisterCommand('webfilez', {
        title: 'Browse',
        focus: false,
        undo: false,
        refreshAfterCallback: false,
        callback() {
            const editor = this;
            ask(url => {
                editor.image.insert(url, false, { alt: '' }, null);
                editor.popups.hideAll();
            });
        },
    });
}

let startup = Promise.resolve();

function enqueue(root, stillCurrent, hold) {
    const run = startup.then(async () => {
        try {
            const created = await start(root);
            hold.current = created;
            if (!stillCurrent()) {
                created?.dispose();
                if (hold.current === created) {
                    hold.current = null;
                }
                return null;
            }

            return created;
        } catch {
            return null;
        }
    });
    startup = run.then(() => {}, () => {});
    return run;
}

async function start(root) {
    const status = root.querySelector('[data-editor-status]');
    try {
        await Promise.all([
            loadStyle(FROALA_CSS),
            loadScript(`${TINYMCE}/tinymce.min.js`, () => globalThis.tinymce),
            loadScript(CKEDITOR_SRC, () => globalThis.CKEDITOR),
            loadScript(FROALA_JS, () => globalThis.FroalaEditor),
        ]);
    } catch {
        if (status) {
            status.hidden = false;
            status.textContent = 'The editors could not be loaded. Check your connection and try again.';
        }
        return null;
    }

    installHooks();

    const tinymceHost = root.querySelector('[data-editor="tinymce"]');
    const ckeditorHost = root.querySelector('[data-editor="ckeditor"]');
    const froalaHost = root.querySelector('[data-editor="froala"]');

    tinymce.get(tinymceHost.id)?.remove();
    try {
        CKEDITOR.instances[ckeditorHost.id]?.destroy(true);
    } catch {
        // The previous instance was already going away.
    }

    try {
        froalaHost['data-froala.editor']?.destroy();
    } catch {
        // The previous instance was already going away.
    }

    await tinymce.init({
        target: tinymceHost,
        license_key: 'gpl',
        promotion: false,
        branding: false,
        menubar: false,
        height: 280,
        base_url: TINYMCE,
        suffix: '.min',
        plugins: 'lists link image',
        toolbar: 'undo redo | bold italic underline | bullist numlist | link image',
        convert_urls: false,
        file_picker_types: 'image',
        file_picker_callback: callback => ask(url => callback(url, { alt: '' })),
    });

    const ckeditor = CKEDITOR.replace(ckeditorHost, {
        extraPlugins: 'webfilez',
        versionCheck: false,
        height: 280,
        filebrowserImageBrowseUrl: 'webfilez',
        removeDialogTabs: 'image:advanced',
        toolbar: [
            { name: 'basic', items: ['Bold', 'Italic', 'Underline'] },
            { name: 'paragraph', items: ['NumberedList', 'BulletedList'] },
            { name: 'links', items: ['Link', 'Unlink'] },
            { name: 'insert', items: ['Image', 'WebFilez'] },
        ],
    });

    const froala = await new Promise(resolve => {
        new FroalaEditor(froalaHost, {
            height: 280,
            toolbarButtons: ['bold', 'italic', 'underline', 'formatUL', 'insertLink', 'insertImage', 'undo', 'redo'],
            imageInsertButtons: ['imageBack', '|', 'imageByURL', 'webfilez'],
            imageDefaultWidth: 0,
            events: {
                initialized() {
                    resolve(this);
                },
            },
        });
    });

    return {
        dispose() {
            try {
                tinymce.get(tinymceHost.id)?.remove();
            } catch {
                // Already removed.
            }

            try {
                if (ckeditor.status !== 'destroyed') {
                    ckeditor.destroy(true);
                }
            } catch {
                // Already destroyed.
            }

            try {
                froala.destroy();
            } catch {
                // Already destroyed.
            }
        },
    };
}

// `host` is either a function the page calls with the chosen URL's consumer, or a .NET
// reference whose OpenPicker method shows the file manager. complete() finishes that call.
export function mount(root, host) {
    let disposed = false;
    let editors = null;
    const hold = { current: null };
    const dialog = document.getElementById('wf-editor-picker');
    const fromDotNet = host !== null && typeof host === 'object' && typeof host.invokeMethodAsync === 'function';

    browseRequest = fromDotNet
        ? apply => {
            pendingApply = apply;
            host.invokeMethodAsync('OpenPicker');
        }
        : host;

    const onDialogClose = () => {
        if (!pendingApply || !fromDotNet) {
            return;
        }

        pendingApply = null;
        host.invokeMethodAsync('PickerClosed');
    };
    const onBackdrop = event => {
        if (event.target === dialog) {
            dialog.close();
        }
    };
    dialog?.addEventListener('close', onDialogClose);
    dialog?.addEventListener('click', onBackdrop);

    const ready = enqueue(root, () => !disposed, hold).then(created => {
        if (disposed) {
            created?.dispose();
            return;
        }
        editors = created;
    });

    return {
        ready,
        dispose() {
            disposed = true;
            pendingApply = null;
            dialog?.removeEventListener('close', onDialogClose);
            dialog?.removeEventListener('click', onBackdrop);
            const current = editors ?? hold.current;
            editors = null;
            hold.current = null;
            current?.dispose();
        },
    };
}

export function complete(url) {
    const apply = pendingApply;
    pendingApply = null;
    if (url && apply) {
        apply(url);
    }
}

export function openDialog() {
    const dialog = document.getElementById('wf-editor-picker');
    if (dialog && !dialog.open) {
        dialog.showModal();
    }
}

export function closeDialog() {
    const dialog = document.getElementById('wf-editor-picker');
    if (dialog?.open) {
        dialog.close();
    }
}
