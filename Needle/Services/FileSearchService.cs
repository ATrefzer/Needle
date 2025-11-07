using System.IO;
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
            , CancellationToken.None);
    }

    static async Task ExecuteSearchAsync(SearchParameters parameters, Action<SearchResult> onResult,
        CancellationToken cancellationToken)
    {
        var masks = ParseFileMasks(parameters.FileMasks);

        // Create channel for producer-consumer pattern
        var channel = Channel.CreateUnbounded<string>();


        // Producer: Enumerate files in background
        var producerTask = Task.Run(async () =>
        {
            try
            {
                foreach (var file in EnumerateFiles(parameters.StartDirectory, masks, parameters.IncludeSubdirectories, cancellationToken))
                {
                    await channel.Writer.WriteAsync(file, cancellationToken).ConfigureAwait(false);
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
                  
                    var matches = await SearchInFileAsync(filePath, parameters.Pattern, parameters.Regex, parameters.IsCaseSensitive, ct);

                    if (matches.Count > 0)
                    {
                        var result = new SearchResult(parameters, filePath, matches);
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


    static IEnumerable<string> EnumerateFiles(string directory, List<string> masks, bool includeSubdirectories,
        CancellationToken cancellationToken)
    {
        var enumerationOptions = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = includeSubdirectories,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.System
        };

        var patterns = masks.Select(mask =>

            // \* because of the Regex.Escape
            new Regex(
                "^" + Regex.Escape(mask).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled
            )).ToList();

        cancellationToken.ThrowIfCancellationRequested();

        var files = Directory.EnumerateFiles(directory, "*", enumerationOptions);

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            if (patterns.Any(pattern => pattern.IsMatch(fileName)))
            {
                yield return file;
            }
        }
    }


    static async Task<List<MatchLine>> SearchInFileAsync(string filePath, string pattern, Regex? regex, bool isCaseSensitive,
        CancellationToken cancellationToken)
    {
        var matches = new List<MatchLine>();

        try
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferSize,
                FileOptions.SequentialScan | FileOptions.Asynchronous);
            using var reader = new StreamReader(stream, true);

            var lineNumber = 0;

            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                lineNumber++;
                cancellationToken.ThrowIfCancellationRequested();

                if (regex != null)
                {
                    // Regex: capture all matches with positions
                    var regexMatches = regex.Matches(line);

                    foreach (Match match in regexMatches)
                    {
                        matches.Add(new MatchLine
                        {
                            LineNumber = lineNumber,
                            Text = line,
                            StartIndex = match.Index,
                            Length = match.Length,
                            IsSelected = true
                        });
                    }
                }
                else
                {
                    // Simple string search: find all occurrences
                    var comparison = isCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                    var index = 0;
                    
                    while ((index = line.IndexOf(pattern, index, comparison)) != -1)
                    {
                        matches.Add(new MatchLine
                        {
                            LineNumber = lineNumber,
                            Text = line,
                            StartIndex = index,
                            Length = pattern.Length,
                            IsSelected = true
                        });
                        index += pattern.Length; // Move past this match
                    }
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