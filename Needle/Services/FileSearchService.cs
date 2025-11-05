using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Needle.Models;

namespace Needle.Services;

public class FileSearchService : ISearchService
{
    const int MaxDegreeOfParallelism = 8;
    const int BufferSize = 81920; // 80 KB buffer for file reading

    public async Task SearchAsync(SearchParameters parameters, Action<SearchResult> onResult,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(parameters.StartDirectory) || !Directory.Exists(parameters.StartDirectory))
        {
            throw new ArgumentException("Start directory is invalid or does not exist.");
        }

        if (string.IsNullOrWhiteSpace(parameters.Pattern))
        {
            throw new ArgumentException("Search pattern must not be empty.");
        }

        await Task.Run(async () => { await ExecuteSearchAsync(parameters, onResult, cancellationToken); }
        );
    }

    static async Task ExecuteSearchAsync(SearchParameters parameters, Action<SearchResult> onResult,
        CancellationToken cancellationToken)
    {
        var masks = ParseFileMasks(parameters.FileMasks);
        var matcher = CreateMatcher(parameters.Pattern, parameters.IsRegex, parameters.IsCaseSensitive);

        // Create channel for producer-consumer pattern
        var channel = Channel.CreateUnbounded<string>();

        var fileCount = 0;

        // Producer: Enumerate files in background
        var producerTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var file in EnumerateFiles(parameters.StartDirectory, masks, cancellationToken))
                {
                    await channel.Writer.WriteAsync(file, cancellationToken);
                    Interlocked.Increment(ref fileCount);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelled
            }
            finally
            {
                channel.Writer.Complete();
            }
        }, cancellationToken);

        // Consumer: Process files in parallel as they become available
        await Parallel.ForEachAsync(
            channel.Reader.ReadAllAsync(cancellationToken),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxDegreeOfParallelism,
                CancellationToken = cancellationToken
            },
            async (filePath, ct) =>
            {
                try
                {
                    var matches = await SearchInFileAsync(filePath, matcher, ct);

                    if (matches.Count > 0)
                    {
                        var result = new SearchResult
                        {
                            FilePath = filePath,
                            Matches = matches
                        };
                        onResult(result);
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    // Skip files that can't be accessed
                }
            });

        await producerTask;
    }

    static List<string> ParseFileMasks(string fileMasks)
    {
        if (string.IsNullOrWhiteSpace(fileMasks))
        {
            return ["*.*"];
        }

        return fileMasks
            .Split([';', ',', '|'], StringSplitOptions.RemoveEmptyEntries)
            .Select(m => m.Trim())
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .ToList();
    }

    static async IAsyncEnumerable<string> EnumerateFiles(string directory, List<string> masks,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var enumerationOptions = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.System
        };

        foreach (var mask in masks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, mask, enumerationOptions);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return file;
            }
        }
    }

    static Func<string, bool> CreateMatcher(string pattern, bool isRegex, bool isCaseSensitive)
    {
        if (isRegex)
        {
            var options = RegexOptions.Compiled | RegexOptions.Multiline;
            if (!isCaseSensitive)
            {
                options |= RegexOptions.IgnoreCase;
            }

            var regex = new Regex(pattern, options, TimeSpan.FromSeconds(1));
            return line => regex.IsMatch(line);
        }

        var comparison = isCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return line => line.Contains(pattern, comparison);
    }

    static async Task<List<MatchLine>> SearchInFileAsync(string filePath, Func<string, bool> matcher,
        CancellationToken cancellationToken)
    {
        var matches = new List<MatchLine>();

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferSize,
                FileOptions.SequentialScan | FileOptions.Asynchronous);
            using var reader = new StreamReader(stream, true);

            var lineNumber = 0;

            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                lineNumber++;
                cancellationToken.ThrowIfCancellationRequested();

                if (matcher(line))
                {
                    matches.Add(new MatchLine
                    {
                        LineNumber = lineNumber,
                        Text = line
                    });
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Skip files that can't be read
        }

        return matches;
    }
}