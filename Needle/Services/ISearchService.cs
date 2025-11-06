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
    public bool IsRegex { get; set; }
    public bool IsCaseSensitive { get; set; }
    public bool IncludeSubdirectories { get; set; } = true;
}