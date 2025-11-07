using System.IO;
using Needle.Services;

namespace Needle.Models;

public class SearchResult
{
    public SearchResult(SearchParameters parameters, string filePath, IReadOnlyList<MatchLine> matches)
    {
        Parameters = parameters;
        FilePath = filePath;
        Matches = matches;
    }

    public string FilePath { get; }
    public IReadOnlyList<MatchLine> Matches { get; }
    public int MatchCount => Matches?.Count ?? 0;
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>
    /// Used search parameters for this search result.
    /// </summary>
    public SearchParameters Parameters { get; }
}