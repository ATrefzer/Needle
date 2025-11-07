using System.Text.RegularExpressions;
using Needle.Models;

namespace Needle.Services;

public interface ISearchService
{
    Task SearchAsync(SearchParameters parameters, Action<SearchResult> onResult, CancellationToken cancellationToken);
}

public class SearchParameters
{
    public string StartDirectory { get; set; } = string.Empty;
    public string FileMasks { get; set; } = string.Empty; // Semicolon separated
    public string Pattern { get; set; } = string.Empty;
    public Regex? Regex { get; set; }
    public bool IsCaseSensitive { get; set; }
    public bool IncludeSubdirectories { get; set; } = true;
    public string ReplacementText { get; set; } = string.Empty;
}