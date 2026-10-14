import { useEffect, useRef } from 'react'

/**
 * A React wrapper for the <wf-file-manager> custom element. Attributes are kebab-case versions of
 * the Blazor component's parameters; index.html loads the runtime that defines the element.
 *
 * onNavigated receives { rootId, path }. onSelected receives the picked files, each with
 * { rootId, path, name, sizeBytes, mimeType, url, publicUrl }, when selectionMode is "single" or
 * "multiple".
 */
export function FileManager({
    root,
    path,
    theme = 'light',
    view,
    height = '600px',
    selectionMode = 'none',
    accept,
    selectLabel,
    showTree = true,
    readOnly = false,
    onNavigated,
    onSelected,
}) {
    const ref = useRef(null)
    const handlers = useRef({ onNavigated, onSelected })

    useEffect(() => {
        handlers.current = { onNavigated, onSelected }
    })

    useEffect(() => {
        const element = ref.current
        const navigated = event => handlers.current.onNavigated?.(event.detail)
        const selected = event => handlers.current.onSelected?.(event.detail.files)

        element.addEventListener('wf-navigated', navigated)
        element.addEventListener('wf-selected', selected)

        return () => {
            element.removeEventListener('wf-navigated', navigated)
            element.removeEventListener('wf-selected', selected)
        }
    }, [])

    return (
        <wf-file-manager
            ref={ref}
            root={root}
            path={path}
            theme={theme}
            view={view}
            height={height}
            selection-mode={selectionMode}
            accept={accept}
            select-label={selectLabel}
            show-tree={String(showTree)}
            read-only={String(readOnly)}
        />
    )
}
