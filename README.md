# WebFilez

An embeddable web file manager for .NET 10, in the spirit of elFinder and Responsive File Manager. It is written in Blazor and also runs in ASP.NET Core MVC, React, or any HTML page through a custom element.

Features in this version:

- Browse several roots, each with its own path, permissions and optional public URL
- Folder tree, breadcrumbs, tiles, list, compact and gallery views, sorting, filtering by name and kind, paging
- Upload by picker or drag and drop, including whole folders, with progress, cancel and overwrite prompts
- Optional resizing of large uploaded images
- Create, rename, delete, copy, cut, paste, duplicate, and drag-and-drop moves (also across roots)
- Zip and tar compression, extraction with zip-slip and zip-bomb protection
- Single-file download, or a zip of any selection streamed from a one-time link
- Image, audio, video, PDF and text preview; a text editor that detects concurrent edits
- Thumbnails (WebP, cached on disk) and an info panel with dimensions and folder sizes
- A picker mode (single or multiple, with an `accept` filter) for choosing files from a form
- Keyboard shortcuts, context menus, light, dark, auto and Bootstrap-friendly themes, and a layout that adapts to its container's width

## Projects

| Project | Purpose |
| --- | --- |
| `WebFilez.Core` | DTOs, enums, `IFileManagerService`, `VirtualPath`, `ApiRoutes`. No dependencies beyond Ardalis.Result. |
| `WebFilez.Services` | The file system implementation, options, access extension points, archives, thumbnails and transfers. |
| `WebFilez.AspNetCore` | `AddWebFilez()` and `MapWebFilezApi()`: the HTTP API, returning ProblemDetails on failure. |
| `WebFilez.Components` | The `FileManager` Razor component, its JavaScript and CSS, and an HTTP client for WebAssembly. |
| `WebFilez.CustomElements` | A Blazor WebAssembly app that registers `<wf-file-manager>` for React and plain HTML. |
| `WebFilez.Tests` | xUnit tests for the path safety rules, the services, and an HTTP round trip. |
| `WebFilez.Demo.*` | Blazor, MVC and React demos. |

## How it fits together

The component talks to `IFileManagerService`. On Blazor Server (and therefore MVC) that is the real service, in-process. In WebAssembly it is an HTTP client that calls the API. Either way, bytes always travel over HTTP: uploads, downloads, thumbnails and zips go to the endpoints from `MapWebFilezApi()`, so every host needs the API mapped somewhere.

There is no database in this version. Roots come from configuration, and everything else is read from the file system. The extension points below are where a host such as ArcaFS plugs in its own storage of roots, permissions and audit history.

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

Relative root paths resolve against the content root. Other root settings:

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

Parameters: `Root`, `Path`, `Theme` (`light`, `dark`, `auto`, `bootstrap`), `View` (`tiles`, `list`, `compact`, `gallery`), `Height`, `SelectionMode` (`none`, `single`, `multiple`), `Accept`, `Kind`, `SelectLabel`, `ShowTree`, `ReadOnly`, `Class`, `OnSelected` and `OnNavigated`. Other attributes are passed to the outer element.

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

### Layout

The component fills its container's width and the `Height` you give it, and adapts its layout to the container rather than the window. Menus and dialogs are fixed-position overlays, so don't place the component inside an element with a CSS `transform`, `filter` or `perspective`. Those create a new containing block and misplace the overlays. To centre a modal around the picker, use flexbox or `<dialog>`, not `translate(-50%, -50%)`.

## Security

- Protect the API with `RequireAuthorization()` or your own policy. WebFilez does no authentication of its own.
- Every path is normalised and clamped to its root. Links, hidden files and excluded names are refused unless the root allows them, and archive entries that would land outside the destination are skipped.
- HTML, SVG and other script-capable types are always downloaded as attachments, never rendered inline, and every file response sends `X-Content-Type-Options: nosniff`.
- Uploads require the `X-WebFilez` header, which a cross-site form can't send, and are size-limited as they stream in.
- Zip downloads use short-lived, single-use tickets, and the items are permission-checked again when the zip is written.

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
