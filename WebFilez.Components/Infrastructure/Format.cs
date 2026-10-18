using WebFilez.Components.Localization;

namespace WebFilez.Components.Infrastructure;

internal static class Format
{
    public static string Size(long bytes) => FileKinds.FormatSize(bytes);

    /// <summary>
    /// Recent times as an age, older ones as a date. Dates are in UTC, because under Blazor
    /// Server the component runs on the server and doesn't know the viewer's time zone.
    /// </summary>
    public static string Relative(DateTime utc, WebFilezText? text = null)
    {
        text ??= WebFilezText.English;
        var age = DateTime.UtcNow - utc;

        if (age.TotalMinutes < 1)
        {
            return text["time.justNow"];
        }

        if (age.TotalHours < 1)
        {
            return text.Plural("time.minutes", (int)age.TotalMinutes);
        }

        if (age.TotalDays < 1)
        {
            return text.Plural("time.hours", (int)age.TotalHours);
        }

        if (age.TotalDays < 7)
        {
            return text.Plural("time.days", (int)age.TotalDays);
        }

        return utc.ToString("d MMM yyyy", text.Culture);
    }

    public static string Full(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    public static string Plural(int count, string singular, string? plural = null) =>
        $"{count:N0} {(count == 1 ? singular : plural ?? (singular + "s"))}";
}