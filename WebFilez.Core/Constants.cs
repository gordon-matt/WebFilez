namespace WebFilez;

/// <summary>Paths, headers, and the names the component and the API share.</summary>
public static class Constants
{
    /// <summary>
    /// Path separator for virtual (root-relative) paths. Always forward slash regardless of the
    /// host OS, so the paths a browser sees are the same on Windows and Linux.
    /// </summary>
    public const char PathSeparator = '/';

    /// <summary>Virtual path of a root.</summary>
    public const string RootPath = "/";

    /// <summary>Where <c>MapWebFilezApi()</c> mounts the HTTP API unless told otherwise.</summary>
    public const string DefaultApiPrefix = "/api/webfilez";

    /// <summary>Suffix for in-progress upload and save temp files, which listings never show.</summary>
    public const string TempFileSuffix = ".webfilez.tmp";

    /// <summary>
    /// Sent by the components with every upload. A cross-site HTML form can't set a custom header,
    /// so requiring it keeps another site from posting files into a signed-in user's folders.
    /// </summary>
    public const string RequestHeaderName = "X-WebFilez";

    /// <summary>The only accepted value of <see cref="RequestHeaderName"/>.</summary>
    public const string RequestHeaderValue = "1";

    /// <summary>
    /// Carries a scope token (<see cref="IFileScopeProtector"/>) on API calls the file manager
    /// makes itself. Also accepted as the <c>scope</c> query value, because an image or a download
    /// cannot set a header.
    /// </summary>
    public const string ScopeHeaderName = "X-WebFilez-Scope";

    /// <summary>Query name that carries a scope token on a download, thumbnail, or upload URL.</summary>
    public const string ScopeQueryName = "scope";

    /// <summary>Largest file the built-in editor opens.</summary>
    public const long DefaultMaxEditableFileBytes = 5 * 1024 * 1024;

    /// <summary>Largest single upload accepted unless configured otherwise.</summary>
    public const long DefaultMaxUploadBytes = 100L * 1024 * 1024;

    /// <summary>Values for the file manager's theme parameter.</summary>
    public static class Themes
    {
        /// <summary>A light surface.</summary>
        public const string Light = "light";

        /// <summary>A dark surface.</summary>
        public const string Dark = "dark";

        /// <summary>Follows the operating system's color scheme.</summary>
        public const string Auto = "auto";

        /// <summary>Takes its colours from the host page's Bootstrap 5.3 variables.</summary>
        public const string Bootstrap = "bootstrap";
    }

    /// <summary>Values for the file manager's selection mode.</summary>
    public static class SelectionModes
    {
        /// <summary>A file manager, with no Select button.</summary>
        public const string None = "none";

        /// <summary>A picker that returns one file.</summary>
        public const string Single = "single";

        /// <summary>A picker that returns every selected file.</summary>
        public const string Multiple = "multiple";
    }

    /// <summary>DOM events raised on the component's root element, for hosts that aren't Blazor.</summary>
    public static class DomEvents
    {
        /// <summary><c>detail</c> is <c>{ rootId, path, publicUrl }</c>. <c>publicUrl</c> is the open folder.</summary>
        public const string Navigated = "wf-navigated";

        /// <summary><c>detail</c> is <c>{ files: [...] }</c>, one entry per selected file.</summary>
        public const string Selected = "wf-selected";
    }
}