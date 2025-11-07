using Needle.Models;
using System.Collections.Concurrent;

namespace Needle.Services;

public interface IReplaceService
{
    Task<ReplaceResult> ReplaceInFilesAsync(
        IEnumerable<SearchResult> searchResults,
        string replacementText,
        CancellationToken cancellationToken);
}

public class ReplaceResult
{
    public int FilesModified { get; set; }
    public int TotalReplacements { get; set; }
    public ConcurrentBag<string> Errors { get; set; } = new();
    public bool Success { get; set; }
}
