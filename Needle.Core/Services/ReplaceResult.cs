using System.Collections.Concurrent;

namespace Needle.Services;

/// <summary>
///     Thread safe, the files are replaced in parallel.
/// </summary>
public class ReplaceResult
{
    private int _filesModified;
    private int _totalReplacements;

    public int FilesModified => _filesModified;
    public int TotalReplacements => _totalReplacements;

    public ConcurrentBag<string> Errors { get; } = new();
    public bool Success => Errors.IsEmpty;

    public void IncrementFilesModified()
    {
        Interlocked.Increment(ref _filesModified);
    }

    public void AddToTotalReplacements(int count)
    {
        Interlocked.Add(ref _totalReplacements, count);
    }
}
