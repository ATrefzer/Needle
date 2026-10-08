using System.Text;
using System.Text.RegularExpressions;

namespace Needle.Services;

[Flags]
public enum SearchScope
{
    Content = 1,
    FileName = 2,
    Both = Content | FileName
}

public class SearchParameters
{
    private readonly Lazy<Regex?> _regex;

    public SearchParameters()
    {
        // Created on first use, after the init properties are set. Thread safe, the search runs in parallel.
        _regex = new Lazy<Regex?>(() => IsRegex ? CreateRegex(Pattern, IsCaseSensitive) : null);
    }

    public SearchScope Scope { get; init; } = SearchScope.Content;
    public bool SearchInContent => Scope.HasFlag(SearchScope.Content);
    public bool SearchInFileName => Scope.HasFlag(SearchScope.FileName);
    public string StartDirectory { get; init; } = string.Empty;
    public string FileMasks { get; init; } = string.Empty; // Semicolon separated
    public string Pattern { get; init; } = string.Empty;
    public bool IsRegex { get; init; }
    public bool IsCaseSensitive { get; init; }
    public bool IncludeSubdirectories { get; init; } = true;

    /// <summary>
    ///     The pattern as regular expression, or null if <see cref="IsRegex" /> is false.
    ///     Throws <see cref="RegexParseException" /> if the pattern is invalid.
    /// </summary>
    public Regex? Regex => _regex.Value;

    /// <summary>
    ///     Used to read files without BOM. Files with BOM are read with the encoding of the BOM.
    /// </summary>
    public Encoding EncodingWithoutBom { get; init; } = new UTF8Encoding(false);

    private static Regex CreateRegex(string pattern, bool isCaseSensitive)
    {
        var options = RegexOptions.Compiled | RegexOptions.Multiline;
        if (!isCaseSensitive)
        {
            options |= RegexOptions.IgnoreCase;
        }

        return new Regex(pattern, options, TimeSpan.FromSeconds(1));
    }

    public List<Regex> CreateFilePatterns()
    {
        return ParseFileMasks(FileMasks).Select(mask =>

            // \* because of the Regex.Escape
            new Regex(
                "^" + Regex.Escape(mask).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled
            )).ToList();
    }

    private static List<string> ParseFileMasks(string fileMasks)
    {
        if (string.IsNullOrWhiteSpace(fileMasks))
        {
            return ["*.txt"];
        }

        return fileMasks
            .Split([';', ',', '|'], StringSplitOptions.RemoveEmptyEntries)
            .Select(m => m.Trim())
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .ToList();
    }
}
