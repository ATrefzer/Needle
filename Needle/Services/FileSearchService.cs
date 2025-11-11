using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Needle.Models;

namespace Needle.Services;

public class FileSearchService : ISearchService
{
    const int MaxDegreeOfParallelism = 8;
    const int BufferSize = 81920; // 80 KB buffer for file reading

    public Task SearchAsync(SearchParameters parameters, Action<SearchResult> onResult,
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

        return Task.Run(() => SearchInternalAsync(parameters, onResult, cancellationToken), cancellationToken);
    }

    async Task SearchInternalAsync(SearchParameters parameters, Action<SearchResult> onResult,
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
                foreach (var file in EnumerateFiles(parameters.StartDirectory, masks, parameters.IncludeSubdirectories,
                             cancellationToken))
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
                await ProcessSingleFileAsync(filePath, parameters, onResult, ct).ConfigureAwait(false)
        );

        await producerTask;
    }

    async Task ProcessSingleFileAsync(string filePath, SearchParameters parameters,
        Action<SearchResult> onResult, CancellationToken cancellationToken)
    {
        try
        {
            var searchResult = await SearchInFileAsync(filePath, parameters, cancellationToken);

            if (searchResult != null)
            {
                onResult(searchResult);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Skip files that can't be accessed
        }
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

    /// <summary>
    /// If the file has no preamble the preamble bytes in the returned encoding are empty.
    /// </summary>
    public static Encoding DetectEncoding(string filePath)
    {
        // Read BOM bytes
        var bom = new byte[4];

        var bomLength = 0;
        using (var file = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        {
            bomLength = file.Read(bom, 0, 4);
        }

        return DetectEncoding(bom, bomLength);
    }

    static Encoding DetectEncoding(FileStream stream)
    {
        var bom = new byte[4];
        var bomLength = stream.Read(bom, 0, 4);
        return DetectEncoding(bom, bomLength);
    }

    static Encoding DetectEncoding(byte[] bom, int bomLength)
    {
        // Detect bom
        if (bomLength >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
        {
            return new UTF8Encoding(true); // UTF-8 with BOM
        }

        if (bomLength >= 2 && bom[0] == 0xFF && bom[1] == 0xFE)
        {
            return Encoding.Unicode; // UTF-16 LE
        }

        if (bomLength >= 2 && bom[0] == 0xFE && bom[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode; // UTF-16 BE
        }

        if (bomLength >= 4 && bom[0] == 0xFF && bom[1] == 0xFE && bom[2] == 0x00 && bom[3] == 0x00)
        {
            return Encoding.UTF32;
        }

        // No BOM = UTF-8 without BOM (Standard for text files)
        return new UTF8Encoding(false);
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


    static async Task<SearchResult?> SearchInFileAsync(string filePath, SearchParameters parameters,
        CancellationToken cancellationToken)
    {
        var matches = new List<MatchLine>();

        try
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                BufferSize,
                FileOptions.SequentialScan | FileOptions.Asynchronous);

            // Extra step if I want to prevent writing a BOM when the original file did not have one.
            // Jump back to beginning after detecting encoding is faster than opening the file twice.
            var encoding = DetectEncoding(stream);
            stream.Seek(0, SeekOrigin.Begin);

            using var reader = new StreamReader(stream, encoding);

            var lineNumber = 0;

            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                lineNumber++;
                cancellationToken.ThrowIfCancellationRequested();

                if (parameters.Regex != null)
                {
                    // Regex: capture all matches with positions
                    var regexMatches = parameters.Regex.EnumerateMatches(line);

                    foreach (var match in regexMatches)
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
                    var pattern = parameters.Pattern;
                    // Simple string search: find all occurrences
                    var comparison = parameters.IsCaseSensitive
                        ? StringComparison.Ordinal
                        : StringComparison.OrdinalIgnoreCase;
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

            if (matches.Count > 0)
            {
                return new SearchResult(parameters, filePath, matches, encoding);
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

        return null;
    }
}