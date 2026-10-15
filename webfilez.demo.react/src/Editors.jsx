import { memo, useEffect, useRef, useState } from 'react'
import { FileManager } from './WebFilez.jsx'
import { closeDialog, mount, openDialog } from './editors.js'

const STARTER = 'Write something, then insert an image from the media library.'

const EditorFields = memo(function EditorFields({ rootRef }) {
    return (
        <div className="demo-editors" ref={rootRef}>
            <p className="demo-muted" data-editor-status hidden></p>

            <section className="demo-editor">
                <h2>TinyMCE</h2>
                <p className="demo-muted">In the image dialog, choose Browse files.</p>
                <textarea data-editor="tinymce" id="wf-tinymce" defaultValue={STARTER} />
            </section>

            <section className="demo-editor">
                <h2>CKEditor</h2>
                <p className="demo-muted">Choose Browse images, or Image and then Browse Server.</p>
                <textarea data-editor="ckeditor" id="wf-ckeditor" defaultValue={STARTER} />
            </section>

            <section className="demo-editor">
                <h2>Froala</h2>
                <p className="demo-muted">Open the image popup, then choose Browse.</p>
                <textarea data-editor="froala" id="wf-froala" defaultValue={STARTER} />
            </section>
        </div>
    )
})

export function EditorsView() {
    const rootRef = useRef(null)
    const pending = useRef(null)
    const [isPicking, setIsPicking] = useState(false)

    useEffect(() => {
        const session = mount(rootRef.current, apply => {
            pending.current = apply
            setIsPicking(true)
        })

        return () => session.dispose()
    }, [])

    useEffect(() => {
        if (isPicking) {
            openDialog()
        } else {
            closeDialog()
        }
    }, [isPicking])

    const finish = url => {
        const apply = pending.current
        pending.current = null
        setIsPicking(false)
        if (url && apply) {
            apply(url)
        }
    }

    return (
        <>
            <section className="demo-hero">
                <h1>Rich text editor</h1>
                <p>TinyMCE, CKEditor and Froala each open WebFilez when you browse for an image.</p>
            </section>

            <EditorFields rootRef={rootRef} />

            <dialog
                id="wf-editor-picker"
                className="demo-dialog"
                aria-label="Choose an image"
                onClose={() => finish(null)}
            >
                {isPicking && (
                    <FileManager
                        root="media"
                        selectionMode="single"
                        accept="image/*"
                        selectLabel="Insert image"
                        height="70vh"
                        onSelected={files => {
                            const file = files?.[0]
                            finish(file ? (file.publicUrl ?? file.url) : null)
                        }}
                    />
                )}
            </dialog>
        </>
    )
}
