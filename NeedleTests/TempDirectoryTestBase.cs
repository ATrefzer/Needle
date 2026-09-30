using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Needle.Models;
using Needle.Services;
using NUnit.Framework;

namespace NeedleTests;

/// <summary>
///     Each test gets its own temporary directory that is deleted afterwards.
/// </summary>
public abstract class TempDirectoryTestBase
{
    protected string Directory { get; private set; } = string.Empty;

    [SetUp]
    public void CreateDirectory()
    {
        Directory = Path.Combine(Path.GetTempPath(), "NeedleTests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
    }

    [TearDown]
    public void DeleteDirectory()
    {
        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, true);
        }
    }

    protected async Task<List<SearchResult>> SearchAsync(string pattern, SearchScope scope = SearchScope.Content,
        bool isRegex = false, bool isCaseSensitive = false, bool includeSubdirectories = false,
        string fileMasks = "*.*", Encoding? encodingWithoutBom = null)
    {
        var parameters = new SearchParameters
        {
            Scope = scope,
            StartDirectory = Directory,
            FileMasks = fileMasks,
            Pattern = pattern,
            Regex = isRegex
                ? new Regex(pattern, isCaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase)
                : null,
            IsCaseSensitive = isCaseSensitive,
            IncludeSubdirectories = includeSubdirectories,
            EncodingWithoutBom = encodingWithoutBom ?? new UTF8Encoding(false)
        };

        // Events are raised from parallel workers.
        var results = new ConcurrentBag<SearchResult>();
        var service = new FileSearchService();
        service.FileCompleted += (_, result) => results.Add(result);

        await service.SearchAsync(parameters, CancellationToken.None);
        return results.ToList();
    }

    protected static Task<ReplaceResult> ReplaceAsync(IEnumerable<SearchResult> results, string replacement)
    {
        return new FileReplaceService().ReplaceInFilesAsync(results, replacement, CancellationToken.None);
    }

    /// <param name="name">Relative path, subdirectories are created.</param>
    protected void CreateFile(string name, string content)
    {
        var path = PathOf(name);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    protected string PathOf(string name)
    {
        return Path.Combine(Directory, name);
    }

    protected string[] FileNames()
    {
        return System.IO.Directory.GetFiles(Directory).Select(Path.GetFileName).ToArray()!;
    }
}
