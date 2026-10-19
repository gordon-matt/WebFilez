// Optional FileRobot adapter. The file manager does not reference this file.
// Load it from the host page, then map the id with Editors, for example
// filerobot:.jpg,.jpeg,.png,.webp.
// GIF animation is kept as a single frame, which is all a canvas editor can save.

const SCRIPT = 'https://scaleflex.cloudimg.io/v7/plugins/filerobot-image-editor/4.8.1/filerobot-image-editor.min.js';
const FONT = 'https://fonts.googleapis.com/css2?family=Roboto:wght@400;500&display=swap';

/** Raster types the browser can decode and Filerobot can export. SVG and TIFF are left out. */
export const ACCEPT = '.jpg,.jpeg,.png,.webp,.gif,.bmp,.avif';

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
    if (globalThis.FilerobotImageEditor) {
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

function savedType(fileName) {
    const extension = fileName.includes('.') ? fileName.split('.').pop().toLowerCase() : '';
    if (extension === 'jpg' || extension === 'jpeg') {
        return 'jpeg';
    }

    return extension === 'png' || extension === 'webp' ? extension : 'png';
}

function stem(fileName) {
    const dot = fileName.lastIndexOf('.');
    return dot > 0 ? fileName.slice(0, dot) : fileName;
}

function extensionOf(fileName) {
    const dot = fileName.lastIndexOf('.');
    return dot > 0 ? fileName.slice(dot + 1).toLowerCase() : '';
}

/**
 * FileRobot's save dialog names a JPEG "photo.jpeg" even when the source is "photo.jpg".
 * That spelling is the original file. A new stem or a different format is a new file.
 */
function savedFileName(originalName, fullName) {
    const requested = typeof fullName === 'string' ? (fullName.trim().split(/[/\\]/).pop() ?? '') : '';
    if (requested === '' || requested === '.' || requested === '..') {
        return originalName;
    }

    const requestedExt = extensionOf(requested);
    const originalExt = extensionOf(originalName);
    const bothJpeg = (requestedExt === 'jpg' || requestedExt === 'jpeg')
        && (originalExt === 'jpg' || originalExt === 'jpeg');
    const sameStem = stem(requested).localeCompare(stem(originalName), undefined, { sensitivity: 'accent' }) === 0;
    return bothJpeg && sameStem ? originalName : requested;
}

/** Filerobot's default theme is light. Dark follows the file manager's palette. */
function theme(mode) {
    if (mode !== 'dark') {
        return undefined;
    }

    return {
        palette: {
            'bg-secondary': '#161b22',
            'bg-primary': '#0d1117',
            'bg-primary-active': '#1c2128',
            'accent-primary': '#4493f8',
            'accent-primary-active': '#2f6fbf',
            'icons-primary': '#e6edf3',
            'icons-secondary': '#8d96a0',
            'borders-secondary': '#30363d',
            'borders-primary': '#30363d',
            'borders-strong': '#8d96a0',
            'light-shadow': 'rgba(1, 4, 9, 0.6)',
            'warning': '#d29922',
        },
        typography: { fontFamily: 'Roboto, Arial' },
    };
}

/** The canvas export carries the quality chosen in the save dialog; the raw canvas does not. */
function blobFromDataUrl(dataUrl) {
    const match = typeof dataUrl === 'string' ? /^data:([^;,]+);base64,([\s\S]*)$/.exec(dataUrl) : null;
    if (!match) {
        throw new Error('The editor did not return an image.');
    }

    const binary = atob(match[2]);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) {
        bytes[i] = binary.charCodeAt(i);
    }

    return new Blob([bytes], { type: match[1] });
}

async function open(file, host) {
    await Promise.all([loadStyle(FONT), loadScript(SCRIPT)]);

    const Filerobot = globalThis.FilerobotImageEditor;
    if (typeof Filerobot !== 'function') {
        throw new Error('FileRobot did not load.');
    }

    const language = typeof file.language === 'string' && file.language.length >= 2
        ? file.language.slice(0, 2)
        : 'en';

    const editor = new Filerobot(host.element, {
        source: file.sourceUrl,
        theme: theme(file.theme),
        useBackendTranslations: false,
        language,
        defaultSavedImageName: stem(file.name),
        defaultSavedImageType: savedType(file.name),
        disableSaveIfNoChanges: true,
        avoidChangesNotSavedAlertOnLeave: true,
        // The default of 4 upscales past the image and can exhaust a tab on a large photo.
        savingPixelRatio: 1,
        closeAfterSave: false,
        forceToPngInEllipticalCrop: true,
        onSave: async (image) => {
            await host.save(blobFromDataUrl(image?.imageBase64), savedFileName(file.name, image?.fullName));
        },
    });

    editor.render({
        onClose: () => host.close(),
    });

    return () => {
        try {
            editor.terminate();
        } catch {
            // Already unmounted.
        }
    };
}

export function registerFileRobot(options = {}) {
    const editor = {
        id: options.id ?? 'filerobot',
        name: options.name ?? 'FileRobot',
        accept: options.accept ?? ACCEPT,
        open,
    };

    // The component publishes registerEditor when its module has loaded. A page that loads this
    // script first leaves the editor on WebFilez.editors, which that module drains on startup.
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

registerFileRobot();
