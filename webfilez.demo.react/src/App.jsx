import { useState } from 'react'
import { FileManager } from './WebFilez.jsx'
import { EditorsView } from './Editors.jsx'
import './App.css'

function ManagerView() {
    const [location, setLocation] = useState(null)

    return (
        <>
            <FileManager
                height="calc(100vh - 150px)"
                theme="auto"
                editors="filerobot:.jpg,.jpeg,.png,.webp,.gif,.bmp,.avif"
                onNavigated={setLocation}
            />
            <p className="demo-muted">
                {location
                    ? <>React heard <code>wf-navigated</code>: {location.rootId}:{location.path}</>
                    : 'Drop files anywhere on the listing to upload them.'}
            </p>
        </>
    )
}

function PickerView() {
    const [isPicking, setIsPicking] = useState(false)
    const [image, setImage] = useState(null)

    // Public URLs come back absolute, because the API is on another origin.
    const imageUrl = image && (image.publicUrl ?? image.url)

    const handleSelected = ([file]) => {
        setImage(file)
        setIsPicking(false)
    }

    return (
        <div className="demo-field">
            <label htmlFor="hero-image">Hero image</label>
            <div className="demo-field-row">
                <input id="hero-image" readOnly value={imageUrl ?? ''} placeholder="No image chosen" />
                <button type="button" onClick={() => setIsPicking(true)}>Browse…</button>
                {image && <button type="button" onClick={() => setImage(null)}>Clear</button>}
            </div>
            {imageUrl && <img className="demo-preview" src={imageUrl} alt={image.name} />}

            {isPicking && (
                <div className="demo-modal" onClick={() => setIsPicking(false)}>
                    <div className="demo-modal-body" onClick={e => e.stopPropagation()}>
                        <FileManager
                            root="media"
                            selectionMode="single"
                            accept="image/*"
                            selectLabel="Use image"
                            height="70vh"
                            onSelected={handleSelected}
                        />
                    </div>
                </div>
            )}
        </div>
    )
}

export default function App() {
    const [view, setView] = useState('manager')

    return (
        <>
            <header className="demo-nav">
                <span className="demo-brand">
                    <img src="/img/Logo.png" alt="WebFilez" />
                    <span>React demo</span>
                </span>
                <nav>
                    <button type="button" aria-pressed={view === 'manager'} onClick={() => setView('manager')}>File manager</button>
                    <button type="button" aria-pressed={view === 'picker'} onClick={() => setView('picker')}>Picker</button>
                    <button type="button" aria-pressed={view === 'editors'} onClick={() => setView('editors')}>Rich Text Editor</button>
                </nav>
            </header>

            <main className="demo-main">
                {view === 'manager' && <ManagerView />}
                {view === 'picker' && <PickerView />}
                {view === 'editors' && <EditorsView />}
            </main>
        </>
    )
}
