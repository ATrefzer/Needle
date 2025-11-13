using System.Text.RegularExpressions;
using Needle.Models;

namespace Needle.Services;

public interface ISearchService
{
    Task SearchAsync(SearchParameters parameters,  CancellationToken cancellationToken);

    event EventHandler<SearchResult> FileCompleted;
    event EventHandler<ulong> MatchFound;
}

public partial class SearchParameters
{
    public string StartDirectory { get; init; } = string.Empty;
    public string FileMasks { get; init; } = string.Empty; // Semicolon separated
    public string Pattern { get; init; } = string.Empty;
    public Regex? Regex { get; init; }
    public bool IsCaseSensitive { get; init; }
    public bool IncludeSubdirectories { get; init; } = true;

    public List<Regex> CreateFilePatterns()
    {
      return CreateFilePatterns(FileMasks);
    }

    private static List<Regex> CreateFilePatterns(string masks)
    {
        var parsed = ParseFileMasks(masks);

        return parsed.Select(mask =>

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