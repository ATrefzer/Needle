using System.Collections.Concurrent;
using Needle.Models;

namespace Needle.Services;

public interface IReplaceService
{
    Task<ReplaceResult> ReplaceInFilesAsync(IEnumerable<SearchResult> searchResults,
        string replacementText,
        CancellationToken cancellationToken);
}

public class ReplaceResult
{
    private int _filesModified;
    private int _totalReplacements;

    public int FilesModified
    {
        get => _filesModified;
        set => _filesModified = value;
    }

    public int TotalReplacements
    {
        get => _totalReplacements;
        set => _totalReplacements = value;
    }

    public ConcurrentBag<string> Errors { get; set; } = new();
    public bool Success => Errors.Count == 0;

    public void IncrementFilesModified()
    {
        Interlocked.Increment(ref _filesModified);
    }

    public void AddToTotalReplacements(int count)
    {
        Interlocked.Add(ref _totalReplacements, count);
    }
}