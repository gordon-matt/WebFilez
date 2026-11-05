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

/**
 * Filerobot's default theme is light, and an omitted token keeps that light value.
 * Save is a primary button: its fill is accent-stateless, and while it is disabled
 * (until the image changes) the fill is accent_1_2_opacity with borders-disabled text.
 * Secondary and icon buttons use bg-stateless, which is white unless it is set here.
 * The icon token is icon-primary; icons-primary is not a key this build reads.
 * accent-primary-active is the selected tool colour and also Save's pressed fill,
 * so it stays a dark blue. filerobot.css lifts the selected foreground off that blue,
 * and paints the value field, which ignores this palette for its resting fill.
 */
function theme(mode) {
    if (mode !== 'dark') {
        return undefined;
    }

    const accent = '#4493f8';

    return {
        palette: {
            'txt-primary': '#e6edf3',
            'txt-secondary': '#8d96a0',
            'txt-secondary-invert': '#c9d1d9',
            'txt-placeholder': '#8d96a0',
            'icon-primary': '#e6edf3',
            'icons-primary': '#e6edf3',
            'icons-secondary': '#8d96a0',
            'icons-placeholder': '#6e7681',
            'icons-muted': '#6e7681',
            'icons-primary-hover': '#ffffff',
            'icons-secondary-hover': '#e6edf3',
            'bg-primary': '#0d1117',
            'bg-primary-light': '#161b22',
            'bg-primary-hover': '#1c2128',
            'bg-primary-active': '#2d333b',
            'bg-primary-0-5-opacity': 'rgba(13, 17, 23, 0.5)',
            'bg-secondary': '#161b22',
            'bg-stateless': '#1c2128',
            'bg-hover': '#2d333b',
            'bg-active': '#30363d',
            'bg-grey': '#21262d',
            'accent-primary': accent,
            'accent-primary-hover': '#2f6fbf',
            'accent-primary-active': '#1f5aa8',
            'accent-stateless': accent,
            'accent-stateless_0_4_opacity': 'rgba(68, 147, 248, 0.4)',
            'accent_0_5_opacity': 'rgba(68, 147, 248, 0.12)',
            'accent_1_2_opacity': 'rgba(68, 147, 248, 0.35)',
            'accent_1_8_opacity': 'rgba(68, 147, 248, 0.28)',
            'accent_2_8_opacity': 'rgba(68, 147, 248, 0.4)',
            'accent_4_0_opacity': 'rgba(68, 147, 248, 0.5)',
            'btn-primary-text': '#ffffff',
            'btn-primary-text-0-6': 'rgba(255, 255, 255, 0.6)',
            'btn-secondary-text': '#e6edf3',
            'btn-disabled-text': '#8d96a0',
            'borders-primary': '#30363d',
            'borders-secondary': '#30363d',
            'borders-strong': '#8d96a0',
            'borders-button': '#3d444d',
            'borders-item': '#30363d',
            'borders-disabled': '#c9d1d9',
            'border-primary-stateless': '#30363d',
            'link-primary': '#8d96a0',
            'link-stateless': '#e6edf3',
            'link-hover': '#ffffff',
            'link-active': '#e6edf3',
            'link-muted': '#6e7681',
            'active-secondary': '#1c2128',
            'active-secondary-hover': '#2d333b',
            'light-shadow': 'rgba(1, 4, 9, 0.6)',
            'medium-shadow': 'rgba(1, 4, 9, 0.7)',
            'warning': '#d29922',
            'gradient-right': 'linear-gradient(270deg, #0d1117 1.56%, rgba(13, 17, 23, 0) 100%)',
            'gradient-right-active': 'linear-gradient(270deg, #1c2128 1.56%, rgba(28, 33, 40, 0) 100%)',
            'gradient-right-hover': 'linear-gradient(270deg, #161b22 1.56%, rgba(22, 27, 34, 0) 100%)',
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
    const style = new URL('./filerobot.css', import.meta.url).href;
    await Promise.all([loadStyle(FONT), loadStyle(style), loadScript(SCRIPT)]);

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
