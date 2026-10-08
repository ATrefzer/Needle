using Needle.Models;

namespace Needle.Services;

public interface IReplaceService
{
    /// <summary>
    ///     Replaces the selected matches. Files that cannot be replaced safely are not modified, see
    ///     <see cref="ReplaceResult.Errors" />.
    /// </summary>
    Task<ReplaceResult> ReplaceInFilesAsync(IEnumerable<SearchResult> searchResults,
        string replacementText,
        CancellationToken cancellationToken);
}
