// Optional Fortune Sheet adapter. The file manager does not reference this file, and this
// file does not include Fortune Sheet. It loads the published builds when a spreadsheet opens.
//
// Fortune Sheet (@fortune-sheet/react) is MIT and draws the grid. It does not read spreadsheet
// files itself. @corbe30/fortune-excel, also MIT, is the import and export plugin Fortune Sheet
// documents: https://github.com/ruilisi/fortune-sheet
// https://github.com/Corbe30/FortuneExcel

const FORTUNE_VERSION = '1.0.4';
const EXCEL_VERSION = '2.3.4';
const REACT_VERSION = '18.3.1';

const STYLE = `https://cdn.jsdelivr.net/npm/@fortune-sheet/react@${FORTUNE_VERSION}/dist/index.css`;
const REACT = `https://esm.sh/react@${REACT_VERSION}`;
const REACT_DOM = `https://esm.sh/react-dom@${REACT_VERSION}/client?deps=react@${REACT_VERSION}`;
const FORTUNE = `https://esm.sh/@fortune-sheet/react@${FORTUNE_VERSION}?deps=react@${REACT_VERSION},react-dom@${REACT_VERSION}`;
const EXCEL = `https://esm.sh/@corbe30/fortune-excel@${EXCEL_VERSION}?deps=react@${REACT_VERSION},react-dom@${REACT_VERSION},@fortune-sheet/react@${FORTUNE_VERSION}`;

/** Spreadsheets. The older .xls format is left out; the converter reads xlsx and csv. */
export const ACCEPT = '.xlsx,.csv';

const MIME = {
    csv: 'text/csv',
    xlsx: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
};

let runtime;

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

function loadRuntime() {
    runtime ??= (async () => {
        const style = new URL('./fortunesheet.css', import.meta.url).href;
        await Promise.all([loadStyle(style), loadStyle(STYLE)]);
        const [react, reactDom, fortune, excel] = await Promise.all([
            import(REACT),
            import(REACT_DOM),
            import(FORTUNE),
            import(EXCEL),
        ]);

        // Workbook is a React forwardRef component, so it is an object rather than a function.
        if (!fortune.Workbook || typeof excel.transformExcelToFortune !== 'function') {
            throw new Error('Fortune Sheet did not load.');
        }

        return {
            React: react.default ?? react,
            createRoot: reactDom.createRoot,
            Workbook: fortune.Workbook,
            transformExcelToFortune: excel.transformExcelToFortune,
            transformFortuneToExcel: excel.transformFortuneToExcel,
        };
    })();

    return runtime;
}

function extensionOf(fileName) {
    const dot = fileName.lastIndexOf('.');
    return dot > 0 ? fileName.slice(dot + 1).toLowerCase() : '';
}

function formatOf(fileName) {
    return extensionOf(fileName) === 'csv' ? 'csv' : 'xlsx';
}

async function open(file, host) {
    const { React, createRoot, Workbook, transformExcelToFortune, transformFortuneToExcel } = await loadRuntime();

    const response = await fetch(file.sourceUrl);
    if (!response.ok) {
        throw new Error('The file could not be opened.');
    }

    const format = formatOf(file.name);
    const sheetFile = new File([await response.blob()], file.name, { type: MIME[format] });

    const rootElement = document.createElement('div');
    rootElement.className = 'wf-fortune';

    const bar = document.createElement('div');
    bar.className = 'wf-fortune-bar';

    const save = document.createElement('button');
    save.type = 'button';
    save.className = 'wf-fortune-save';
    save.textContent = 'Save';
    save.disabled = true;

    const close = document.createElement('button');
    close.type = 'button';
    close.className = 'wf-fortune-close';
    close.textContent = 'Close';

    const grid = document.createElement('div');
    grid.className = 'wf-fortune-grid';

    bar.append(save, close);
    rootElement.append(bar, grid);
    host.element.replaceChildren(rootElement);

    const sheetRef = React.createRef();
    let imported = false;
    let importError = null;

    function SheetApp() {
        const [sheets, setSheets] = React.useState([{ name: 'Sheet1' }]);
        const [key, setKey] = React.useState(0);

        React.useEffect(() => {
            transformExcelToFortune(sheetFile, setSheets, setKey, sheetRef).then(
                () => {
                    imported = true;
                },
                (error) => {
                    importError = error;
                },
            );
        }, []);

        return React.createElement(Workbook, { key, data: sheets, ref: sheetRef });
    }

    const root = createRoot(grid);
    root.render(React.createElement(SheetApp));

    const ready = new Promise((resolve, reject) => {
        const started = Date.now();
        const tick = () => {
            if (importError) {
                reject(importError instanceof Error ? importError : new Error('The spreadsheet could not be opened.'));
                return;
            }

            // The converter replaces the grid after it parses, so wait for that and for the
            // remounted workbook before Save can read the sheets.
            if (imported && sheetRef.current?.getAllSheets) {
                resolve();
                return;
            }

            if (Date.now() - started > 30000) {
                reject(new Error('The spreadsheet could not be opened.'));
                return;
            }

            setTimeout(tick, 50);
        };
        tick();
    });

    try {
        await ready;
    } catch (error) {
        root.unmount();
        throw error;
    }

    save.disabled = false;
    close.addEventListener('click', () => host.close());
    save.addEventListener('click', async () => {
        save.disabled = true;
        try {
            const exported = await transformFortuneToExcel(sheetRef, format, false);
            if (!exported) {
                throw new Error('The spreadsheet could not be saved.');
            }

            await host.save(new Blob([exported], { type: MIME[format] }), file.name);
        } finally {
            save.disabled = false;
        }
    });

    return () => {
        root.unmount();
    };
}

export function registerFortuneSheet(options = {}) {
    const editor = {
        id: options.id ?? 'fortunesheet',
        name: options.name ?? 'Fortune Sheet',
        accept: options.accept ?? ACCEPT,
        open: (file, host) => open(file, host),
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

registerFortuneSheet();
