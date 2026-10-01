using Needle.Models;

namespace Needle.Services;

public interface ISearchService
{
    Task SearchAsync(SearchParameters parameters, CancellationToken cancellationToken);

    /// <summary>
    ///     A file with matches is completely searched.
    /// </summary>
    event EventHandler<SearchResult> FileCompleted;

    /// <summary>
    ///     Number of matches found in a line. Intermediate progress for large files.
    /// </summary>
    event EventHandler<int> MatchFound;

    /// <summary>
    ///     Files that could not be read, like locked files, files without access or broken zip archives.
    /// </summary>
    int SkippedFiles { get; }

    /// <summary>
    ///     Directories that could not be enumerated, like directories without access.
    /// </summary>
    int SkippedDirectories { get; }
}
