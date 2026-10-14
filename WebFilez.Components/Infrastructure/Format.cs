namespace WebFilez.Components.Infrastructure;

internal static class Format
{
    public static string Size(long bytes) => FileKinds.FormatSize(bytes);

    /// <summary>
    /// Recent times as an age, older ones as a date. Dates are in UTC, because under Blazor
    /// Server the component runs on the server and doesn't know the viewer's time zone.
    /// </summary>
    public static string Relative(DateTime utc)
    {
        var age = DateTime.UtcNow - utc;

        return age switch
        {
            { TotalMinutes: < 1 } => "just now",
            { TotalHours: < 1 } => $"{(int)age.TotalMinutes} min ago",
            { TotalDays: < 1 } => $"{(int)age.TotalHours} h ago",
            { TotalDays: < 7 } => $"{(int)age.TotalDays} d ago",
            _ => utc.ToString("d MMM yyyy", CultureInfo.InvariantCulture),
        };
    }

    public static string Full(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    public static string Plural(int count, string singular, string? plural = null) =>
        $"{count:N0} {(count == 1 ? singular : plural ?? singular + "s")}";
}
