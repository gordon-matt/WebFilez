<img src="https://github.com/gordon-matt/WebFilez/blob/master/_Misc/Logo-Light.png" alt="Logo" width="350" />

An embeddable web file manager for .NET 10, in the spirit of elFinder and Responsive File Manager. It is written in Blazor and also runs in ASP.NET Core MVC, React, or any HTML page through a custom element.

Features in this version:

- Browse several roots, each with its own path, permissions and optional public URL
- Folder tree, breadcrumbs, tiles, list and compact views, sorting, filtering by name and kind, paging
- Upload by picker or drag and drop, including whole folders, with progress, cancel and overwrite prompts
- Optional resizing of large uploaded images
- Create, rename, delete, copy, cut, paste, duplicate, and drag-and-drop moves (also across roots)
- Zip and tar compression, extraction with zip-slip and zip-bomb protection
- Single-file download, or a zip of any selection streamed from a one-time link
- Image, audio, video, PDF and text preview; a text editor that detects concurrent edits; other editors plugged in per file type
- Thumbnails (WebP, cached on disk) and an info panel with dimensions and folder sizes
- A picker mode (single or multiple, with an `accept` filter) for choosing files from a form
- Keyboard shortcuts, context menus, light, dark, auto and Bootstrap-friendly themes, and a layout that adapts to its container's width

## Projects

| Project | Purpose |
| --- | --- |
| `WebFilez.Core` | DTOs, enums, `IFileManagerService`, `VirtualPath`, `ApiRoutes`. No dependencies beyond Ardalis.Result. |
| `WebFilez.Services` | The file manager, the file system provider, options, access extension points, archives, thumbnails and transfers. |
| `WebFilez.Storage.AzureBlob` | The Azure Blob Storage provider. Register it with `AddWebFilezAzureBlob()`. |
| `WebFilez.Storage.AzureFiles` | The Azure Files provider. Register it with `AddWebFilezAzureFiles()`. |
| `WebFilez.Storage.AmazonS3` | The Amazon S3 provider. Register it with `AddWebFilezAmazonS3()`. |
| `WebFilez.Storage.GoogleCloud` | The Google Cloud Storage provider. Register it with `AddWebFilezGoogleCloud()`. |
| `WebFilez.AspNetCore` | `AddWebFilez()` and `MapWebFilezApi()`: the HTTP API, returning ProblemDetails on failure. |
| `WebFilez.Components` | The `FileManager` Razor component, its JavaScript and CSS, and an HTTP client for WebAssembly. Editors are registered by the host page; this project does not reference one. |
| `WebFilez.Plugins.FileRobot` | Optional FileRobot image editor. Load its script and name it in `Editors`. The component does not reference this package. |
| `WebFilez.Plugins.SuperDoc` | Optional SuperDoc editor for `.docx`. Load its script and name it in `Editors`. SuperDoc itself is not included; a commercial license key is supplied by the host. |
| `WebFilez.Plugins.FortuneSheet` | Optional Fortune Sheet editor for `.xlsx` and `.csv`. Load its script and name it in `Editors`. Fortune Sheet itself is not included. |
| `WebFilez.CustomElements` | A Blazor WebAssembly app that registers `<wf-file-manager>` for React and plain HTML. |
| `WebFilez.Tests` | xUnit tests for the path safety rules, the services, and an HTTP round trip. |
| `WebFilez.Demo.*` | Blazor, MVC and React demos. |

## How it fits together

The component talks to `IFileManagerService`. On Blazor Server (and therefore MVC) that is the real service, in-process. In WebAssembly it is an HTTP client that calls the API. Either way, bytes always travel over HTTP: uploads, downloads, thumbnails and zips go to the endpoints from `MapWebFilezApi()`, so every host needs the API mapped somewhere.

There is no database in this version. Roots come from configuration. Each root names a storage provider; the file system is the default, and optional packages add Azure Blob Storage, Azure Files, Amazon S3 and Google Cloud Storage. The extension points below are where a host such as ArcaFS plugs in its own roots, permissions, audit history, or a storage provider of its own.

## Getting started

### Configuration

```json
{
  "WebFilez": {
    "Roots": [
      { "Id": "files", "Name": "Files", "Path": "App_Data/files" },
      { "Id": "media", "Name": "Media", "Path": "App_Data/media", "PublicUrl": "/media" },
      { "Id": "handbook", "Name": "Handbook", "Path": "App_Data/handbook", "IsReadOnly": true }
    ],
    "MaxUploadBytes": 104857600,
    "ImageResize": { "Enabled": true, "MaxWidth": 2560, "MaxHeight": 2560 }
  }
}
```

Relative paths on a file system root resolve against the content root. A path on any other provider is a prefix inside that store and is left as written. Other root settings:

- `Permissions`: a `FilePermission` set such as `ReadOnly`, `Contributor` or `Full` (the default). `IsReadOnly` clamps whatever is set to non-changing permissions.
- `PublicUrl`: where the root is served as static files. When set, the picker returns that URL and the info panel shows it. WebFilez doesn't serve it; map it yourself with `UseStaticFiles`.
- `ShowHiddenFiles`: allow dot files and hidden-attribute files to be shown and reached. Off by default.
- `FollowSymlinks`: allow paths through links, which may lead outside the root. Off by default.
- `ExcludePatterns`: glob patterns, such as `*.bak` or `node_modules`, that are hidden and unreachable.

Global settings on `WebFilezOptions` include `AllowedExtensions` (if set, only these may be uploaded or created), `BlockedExtensions`, `FileNames` (replace spaces, lower-case, remove diacritics), `Thumbnails`, `MaxEditableFileBytes`, `MaxExtractedBytes` and `MaxExtractedEntries`.

`BlockedExtensions` starts with a list of server-executable and script-capable types, such as `.php`, `.aspx`, `.html` and `.svg`. Every dotted suffix is checked, so `shell.php.jpg` is refused too. Configuration binding **adds to** a list rather than replacing it, so entries in `appsettings.json` extend the defaults. To remove a default, change the list in code:

```csharp
builder.Services.AddWebFilez(builder.Configuration, options => options.BlockedExtensions.Remove(".svg"));
```

### Blazor

```csharp
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddWebFilez(builder.Configuration);

// ...

app.MapWebFilezApi().RequireAuthorization();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
```

Add the stylesheet to `App.razor`, and `@using WebFilez.Components` to `_Imports.razor`:

```html
<link rel="stylesheet" href="@Assets["_content/WebFilez.Components/webfilez.css"]" />
```

```razor
<FileManager Height="600px" Theme="auto" OnNavigated="location => current = location" />

<FileManager Root="media"
             Path="42/7"
             SelectionMode="single"
             Kind="images"
             Accept="image/*"
             SelectLabel="Use this image"
             OnSelected="files => imageUrl = files[0].PublicUrl ?? files[0].Url" />
```

The component must render interactively. In Blazor WebAssembly, call `AddWebFilezHttpClient(apiBaseUrl)` instead of `AddWebFilez`, and host the API in a server project.

Parameters: `Root`, `Path`, `Theme` (`light`, `dark`, `auto`, `bootstrap`), `View` (`tiles`, `list`, `compact`), `Height`, `SelectionMode` (`none`, `single`, `multiple`), `Accept`, `Kind`, `SelectLabel`, `ShowTree`, `ReadOnly`, `Class`, `OnSelected` and `OnNavigated`. Other attributes are passed to the outer element.

`Kind` fixes the listing to `images`, `documents`, `media`, `archives`, `folders`, or `all`, and hides the type buttons. `images` and `media` still show folders, so the user can open them. `folders` shows directories only. `Accept` still decides which of the listed files the Select button accepts. Double-clicking an accepted file selects it.

The component's root element carries `data-wf-root`, `data-wf-path`, and `data-wf-public-url` for the folder that is open. `data-wf-public-url` is present when the root has a `PublicUrl`, ends with a slash, and can be read synchronously. `wf-navigated` carries the same address as `publicUrl`. A folder field can store that value without a Select button. File and folder items include `PublicUrl` as well; folder addresses end with a slash.

### MVC

```csharp
builder.Services.AddControllersWithViews();
builder.Services.AddServerSideBlazor();
builder.Services.AddWebFilez(builder.Configuration);

// ...

app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
app.MapBlazorHub();
app.MapWebFilezApi().RequireAuthorization();
```

In the layout, add the stylesheet and `<script src="~/_framework/blazor.server.js"></script>`. Then render the component in any view:

```cshtml
<component type="typeof(WebFilez.Components.FileManager)" render-mode="Server" param-Height="@("600px")" />
```

Parameters are passed as `param-Name`. Event callbacks aren't available this way, so the component also raises DOM events that bubble from its element:

```js
document.addEventListener('wf-navigated', e => console.log(e.detail.rootId, e.detail.path, e.detail.publicUrl));
document.addEventListener('wf-selected', e => console.log(e.detail.files));
const folder = document.querySelector('[data-wf-public-url]')?.getAttribute('data-wf-public-url');
```

### React and plain HTML

`WebFilez.CustomElements` is a WebAssembly app that registers `<wf-file-manager>`. Publish it, serve its `_framework` and `_content` folders from your site, and give it an `appsettings.json` that points at the API:

```json
{
  "WebFilez": {
    "ApiBaseUrl": "https://files.example.com/api/webfilez",
    "IncludeCredentials": true
  }
}
```

```html
<link rel="stylesheet" href="/_content/WebFilez.Components/webfilez.css" />
<wf-file-manager height="600px" theme="auto" root="media" path="42/7" selection-mode="single" kind="images" accept="image/*"></wf-file-manager>
<script src="/_framework/blazor.webassembly.js"></script>
```

Attributes are kebab-case versions of the component parameters, and the same `wf-navigated` and `wf-selected` events are raised. The React demo has a wrapper component, `src/WebFilez.jsx`, and a Vite plugin that publishes the custom elements and serves their output, so `npm run dev` is all that's needed.

When the API is on another origin, it needs a CORS policy. Uploads send an `X-WebFilez` header, so that header must be allowed:

```csharp
builder.Services.AddCors(options => options.AddPolicy("WebFilez", policy => policy
    .WithOrigins("https://app.example.com")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()
    .WithExposedHeaders("Content-Disposition")));

app.UseCors();
app.MapWebFilezApi().RequireCors("WebFilez");
```

`IncludeCredentials` makes uploads, downloads and thumbnails send cookies to that origin. In the custom elements app, `IncludeCredentialsHandler` does the same for JSON calls. For bearer tokens, add your own handler through the `configureClient` argument of `AddWebFilezHttpClient`. Downloads and thumbnails are plain URLs, so they need cookie authentication when the API is protected.

### File editors

The built-in editor is for text and code. Anything else is a JavaScript editor the host page registers, with the file types it should open. Several can be registered, and one file manager can use a different mapping from another.

```js
import { registerEditor } from '/_content/WebFilez.Components/webfilez.js';

registerEditor({
  id: 'pintura',
  name: 'Pintura',
  accept: '.jpg,.jpeg,.png,.webp', // same syntax as the Accept parameter
  open(file, host) {
    // file.sourceUrl is a blob URL of the bytes, so a canvas is not tainted.
    // file also has name, path, rootId, url, mimeType, theme and language.
    // host.element is an empty full-screen container.
    // return a function that disposes the editor.
  },
});
```

`host.save(blob, fileName)` writes the bytes back through the upload endpoint and resolves when that finishes, so the editor can keep its own spinner up until then. The same file name replaces the original. A different name is saved beside it, and a conflict or a rejected type comes back as a rejected promise. `host.close()` dismisses the editor.

`Editors` on the component (the `editors` attribute on `<wf-file-manager>`) assigns those ids to types for one instance:

```html
<FileManager Editors="images:.jpg,.jpeg,.png,.webp|annotate:.gif,.bmp" />
```

Entries are separated by `|`. An id with no accept list keeps the types from `registerEditor`. Leave `Editors` empty and every registered editor is offered for the types it named. Double-click opens the first match when the file can be modified. The context menu lists every match as "Edit with ...", and Preview stays there too. Types that no editor claims still open in the text editor or the preview, as before.

An editor can also be queued before the module loads, which suits a plain script tag:

```html
<script>
  window.WebFilez = { editors: [{ id: 'pintura', name: 'Pintura', accept: '.jpg,.png', open(file, host) { /* ... */ } }] };
</script>
```

`WebFilez.Plugins.FileRobot` is one such editor, shipped separately so the file manager does not know about it. The demos reference that package and load its script, which registers id `filerobot` for JPEG, PNG, WebP, GIF, BMP and AVIF:

```html
<script type="module" src="_content/WebFilez.Plugins.FileRobot/filerobot.js"></script>
<FileManager Editors="filerobot:.jpg,.jpeg,.png,.webp,.gif,.bmp,.avif" />
```

GIF animation is flattened to one frame. SVG is never rendered inline, and browsers do not decode TIFF, so those stay on preview or download. The script loads the Roboto font FileRobot asks for; the demo pages load it too. FileRobot's save dialog spells a JPEG as `.jpeg`; saving the original name that way still replaces a `.jpg`. FileRobot Image Editor is MIT, including commercial use, and it has no license key. Another editor is the same shape: its own package, its own script, and an entry in `Editors`.

`WebFilez.Plugins.SuperDoc` registers id `superdoc` for `.docx`. The package does not contain SuperDoc. When a document opens, the script loads SuperDoc 2.24.0 and its DOCX engine from jsDelivr. SuperDoc's editor is [AGPL-3.0](https://www.gnu.org/licenses/agpl-3.0.html) for open-source use, and the engine it downloads is proprietary software used as SuperDoc's dependency. A proprietary or commercial deployment needs a [commercial license](https://www.superdocportal.dev/get-in-touch) and passes that license key itself. No key is included.

```html
<script>
  window.WebFilez = window.WebFilez || {};
  window.WebFilez.superdoc = { licenseKey: 'your-commercial-key' };
</script>
<script type="module" src="_content/WebFilez.Plugins.SuperDoc/superdoc.js"></script>
<FileManager Editors="superdoc:.docx" />
```

`registerSuperDoc({ licenseKey })` does the same thing from a module. The key is sent to SuperDoc as its client-visible license identity. Leave it unset and SuperDoc uses its community identity. The key is not a substitute for signing in to the file manager.

`WebFilez.Plugins.FortuneSheet` registers id `fortunesheet` for `.xlsx` and `.csv`. The package does not contain Fortune Sheet. When a spreadsheet opens, the script loads Fortune Sheet 1.0.4 and the [fortune-excel](https://github.com/Corbe30/FortuneExcel) converter 2.3.4. Both are MIT, including commercial use, and neither takes a license key. Fortune Sheet draws the grid, and fortune-excel reads the file into that grid and writes it back. Saving keeps the original format: a `.csv` is written as CSV, and a `.xlsx` is written as xlsx. The older `.xls` format is not opened here.

```html
<script type="module" src="_content/WebFilez.Plugins.FortuneSheet/fortunesheet.js"></script>
<FileManager Editors="fortunesheet:.xlsx,.csv" />
```

## Layout

The component fills its container's width and the `Height` you give it, and adapts its layout to the container rather than the window. Menus and dialogs are fixed-position overlays, so don't place the component inside an element with a CSS `transform`, `filter` or `perspective`. Those create a new containing block and misplace the overlays. To centre a modal around the picker, use flexbox or `<dialog>`, not `translate(-50%, -50%)`.

## Security

- Protect the API with `RequireAuthorization()` or your own policy. WebFilez does no authentication of its own.
- Every path is normalised and clamped to its root. Links, hidden files and excluded names are refused unless the root allows them, and archive entries that would land outside the destination are skipped.
- HTML, SVG and other script-capable types are always downloaded as attachments, never rendered inline, and every file response sends `X-Content-Type-Options: nosniff`.
- Uploads require the `X-WebFilez` header, which a cross-site form can't send, and are size-limited as they stream in.
- Zip downloads use short-lived, single-use tickets, and the items are permission-checked again when the zip is written.

## Storage providers

`Provider` selects where a root is stored. Leave it out, or set `filesystem`, and the root is a directory on disk. `azure-blob` stores it in an Azure Blob container. `azure-file` stores it in an Azure file share, which has real directories. `amazon-s3` stores it in an S3 bucket. `google-cloud` stores it in a Cloud Storage bucket. Register the providers beside `AddWebFilez`:

```csharp
builder.Services.AddWebFilez(builder.Configuration);
builder.Services.AddWebFilezAzureBlob();
builder.Services.AddWebFilezAzureFiles();
builder.Services.AddWebFilezAmazonS3();
builder.Services.AddWebFilezGoogleCloud();
```

```json
{
  "Id": "uploads",
  "Name": "Uploads",
  "Provider": "azure-blob",
  "Path": "campaigns",
  "Settings": {
    "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net",
    "Container": "files"
  }
}
```

`Path` is the folder inside the container, bucket or share. `Settings.Prefix` overrides it when you want the prefix separate from `Path`. The container, bucket or share is not created for you, and an Azure Files root also needs that directory to exist already.

Blob folders are prefixes. An empty folder is a hidden zero-length marker blob inside that prefix, because the blob client drops a trailing slash from a blob name. Blob names are case-sensitive. For local blob testing, point `ConnectionString` at the [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite) emulator (`UseDevelopmentStorage=true` on `127.0.0.1:10000`). Azurite emulates Blob, Queue, and Table only, so it cannot stand in for an Azure file share. An Azure file share has real directories, so creating a folder makes an empty one, and names are case-insensitive. Dot names stay hidden unless `ShowHiddenFiles` is on. Neither store has symbolic links.

```json
{
  "Id": "shared",
  "Name": "Shared",
  "Provider": "azure-file",
  "Path": "campaigns",
  "Settings": {
    "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net",
    "Share": "files"
  }
}
```

```json
{
  "Id": "photos",
  "Name": "Photos",
  "Provider": "amazon-s3",
  "Path": "campaigns",
  "Settings": {
    "AccessKey": "...",
    "SecretKey": "...",
    "Region": "us-east-1",
    "Bucket": "files"
  }
}
```

```json
{
  "Id": "archive",
  "Name": "Archive",
  "Provider": "google-cloud",
  "Path": "campaigns",
  "Settings": {
    "Bucket": "files",
    "CredentialFile": "C:\\keys\\storage.json"
  }
}
```

S3 and Cloud Storage folders are prefixes, with the same hidden marker as Azure Blob, and object names are case-sensitive. The bucket is not created for you. For local S3 testing, set `ServiceUrl` to a [LocalStack](https://docs.localstack.cloud/) endpoint (`http://127.0.0.1:4566`) and any access key. For local Cloud Storage testing, set `ServiceUrl` to [fake-gcs-server](https://github.com/fsouza/fake-gcs-server) (`http://127.0.0.1:4443`) and leave the credentials out. `CredentialJson` is the other way to pass a Google service-account key.

A host can add anything else by implementing `IStorageProvider` and registering it. The `Key` is the value used in `Provider`:

```csharp
builder.Services.AddSingleton<IStorageProvider, MyStorageProvider>();
builder.Services.AddWebFilez(builder.Configuration);
```

Copy, move and zip across two providers stream the bytes. Two roots on the same provider use that provider's own copy.

## Extension points

These are registered with `TryAdd`, so register your own implementation before calling `AddWebFilez`:

```csharp
builder.Services.AddScoped<IFileRootProvider, TenantRootProvider>();
builder.Services.AddScoped<IFileAccessPolicy, AclAccessPolicy>();
builder.Services.AddScoped<IFileActivitySink, AuditLogSink>();
builder.Services.AddWebFilez(builder.Configuration);
```

- `IFileRootProvider` supplies the roots for the current request. The default reads `WebFilezOptions.Roots`. Use it for per-user or per-tenant roots, or roots stored in a database.
- `IFileAccessPolicy` returns the `FilePermission` set for a root and path. The default uses the root's permissions. Use it to look up per-user or per-folder access.
- `IFileActivitySink` is told about every successful change and download, with the kind, root, path and target. Use it for an audit log, a search index or cache invalidation. Exceptions thrown from it are logged and swallowed.
- `IStorageProvider` stores the bytes for roots whose `Provider` matches its `Key`. The file system provider is registered by `AddWebFilez`, and its key is `FileSystemStorageProvider.ProviderKey`. Every other package publishes its own key, such as `AzureBlobStorageProvider.ProviderKey`, and is registered with `AddSingleton<IStorageProvider, ...>()` or that package's `Add` method. Registering before or after `AddWebFilez` both work. `WebFilez.Services` is the contract those packages reference.

## Running the demos

```powershell
dotnet run --project WebFilez.Demo.Blazor    # https://localhost:7311, also hosts the API for the others
dotnet run --project WebFilez.Demo.Mvc       # https://localhost:7312
dotnet run --project WebFilez.CustomElements # https://localhost:7313, plain HTML
cd webfilez.demo.react; npm install; npm run dev # http://localhost:5173
```

The demos seed sample files into `App_Data` on first run. The custom element and React demos call the Blazor demo's API, so start that first.

## Tests

```powershell
dotnet run --project WebFilez.Tests
```
