namespace WebFilez;

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

    public const string RequestHeaderValue = "1";

    /// <summary>Largest file the built-in editor opens.</summary>
    public const long DefaultMaxEditableFileBytes = 5 * 1024 * 1024;

    /// <summary>Largest single upload accepted unless configured otherwise.</summary>
    public const long DefaultMaxUploadBytes = 100L * 1024 * 1024;

    public static class Themes
    {
        public const string Light = "light";
        public const string Dark = "dark";
        public const string Auto = "auto";

        /// <summary>Takes its colours from the host page's Bootstrap 5.3 variables.</summary>
        public const string Bootstrap = "bootstrap";
    }

    public static class SelectionModes
    {
        public const string None = "none";
        public const string Single = "single";
        public const string Multiple = "multiple";
    }

    /// <summary>DOM events raised on the component's root element, for hosts that aren't Blazor.</summary>
    public static class DomEvents
    {
        /// <summary><c>detail</c> is <c>{ root, path }</c>.</summary>
        public const string Navigated = "wf-navigated";

        /// <summary><c>detail</c> is <c>{ files: [...] }</c>, one entry per selected file.</summary>
        public const string Selected = "wf-selected";
    }
}
