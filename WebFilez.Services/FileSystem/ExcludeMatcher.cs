using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace WebFilez.Services.FileSystem;

/// <summary>
/// Matches entry names against a root's exclude globs. Compiled once per distinct pattern set
/// and cached, because a listing tests every entry against every pattern.
/// </summary>
public sealed class ExcludeMatcher
{
    private static readonly ExcludeMatcher Empty = new([]);

    private static readonly ConcurrentDictionary<string, ExcludeMatcher> Cache = new(StringComparer.Ordinal);

    private readonly Regex[] patterns;

    private ExcludeMatcher(Regex[] patterns) => this.patterns = patterns;

    public static ExcludeMatcher For(FileRoot root) => For(root.ExcludePatterns);

    public static ExcludeMatcher For(IReadOnlyCollection<string>? patterns)
    {
        if (patterns is null || patterns.Count == 0)
        {
            return Empty;
        }

        string key = string.Join('\n', patterns);
        return Cache.GetOrAdd(key, static (_, list) => Build(list), patterns);
    }

    public bool IsExcluded(string name) =>
        patterns.Length != 0 && patterns.Any(p => p.IsMatch(name));

    private static ExcludeMatcher Build(IEnumerable<string> globs)
    {
        var regexes = globs
            .Select(g => g.Trim())
            .Where(g => g.Length > 0 && !g.StartsWith('#'))
            .Select(TryCompile)
            .OfType<Regex>()
            .ToArray();

        return regexes.Length == 0 ? Empty : new ExcludeMatcher(regexes);
    }

    private static Regex? TryCompile(string glob)
    {
        try
        {
            return new Regex(GlobToRegex(glob), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch (ArgumentException)
        {
            // A malformed pattern shouldn't break browsing; it just excludes nothing.
            return null;
        }
    }

    /// <summary>
    /// Translates a shell glob to an anchored regex. Only <c>*</c> and <c>?</c> are meaningful;
    /// everything else is escaped, so <c>node_modules</c> matches literally and a pattern can't
    /// accidentally become a catastrophically backtracking expression.
    /// </summary>
    internal static string GlobToRegex(string glob)
    {
        var builder = new StringBuilder("^");

        foreach (char c in glob)
        {
            builder.Append(c switch
            {
                '*' => "[^/]*",
                '?' => "[^/]",
                _ => Regex.Escape(c.ToString()),
            });
        }

        return builder.Append('$').ToString();
    }
}
