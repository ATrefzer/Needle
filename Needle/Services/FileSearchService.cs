using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Needle.Models;

namespace Needle.Services;

public class FileSearchService : ISearchService
{
    private const int MaxDegreeOfParallelism = 8;
    private const int BufferSize = 81920; // 80 KB buffer for file reading

    public Task SearchAsync(SearchParameters parameters, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(parameters.StartDirectory) || !Directory.Exists(parameters.StartDirectory))
        {
            throw new ArgumentException("Start directory is invalid or does not exist.");
        }

        if (string.IsNullOrWhiteSpace(parameters.Pattern))
        {
            throw new ArgumentException("Search pattern must not be empty.");
        }

        return Task.Run(() => SearchInternalAsync(parameters, cancellationToken), cancellationToken);
    }

    public event EventHandler<SearchResult>? FileCompleted;
    public event EventHandler<ulong>? MatchFound;

    private async Task SearchInternalAsync(SearchParameters parameters, CancellationToken cancellationToken)
    {

        // Create channel for producer-consumer pattern
        var channel = Channel.CreateUnbounded<string>();

        var filePatterns = parameters.CreateFilePatterns();

        // Producer: Enumerate files in background
        var producerTask = Task.Run(async () =>
        {
            try
            {
                foreach (var file in EnumerateFiles(parameters.StartDirectory, filePatterns,
                             parameters.IncludeSubdirectories,
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
                await ProcessSingleFileAsync(filePath, parameters, ct).ConfigureAwait(false)
        );

        await producerTask;
    }

    private async Task ProcessSingleFileAsync(string filePath, SearchParameters parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            if (IsZip(filePath))
            {
                await SearchInArchiveAsync(filePath, parameters, cancellationToken);
                return;
            }

            await SearchInFileAsync(filePath, parameters, cancellationToken);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Skip files that can't be accessed
        }
    }

    private async Task SearchInArchiveAsync(string zipFilePath, SearchParameters parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            var filePatterns = parameters.CreateFilePatterns();

            await using var archive = await ZipFile.OpenReadAsync(zipFilePath, cancellationToken);

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Use the same search masks inside the zip
                if (IsZip(entry.FullName) || !filePatterns.Any(p => p.IsMatch(entry.FullName)))
                {
                    // Don't search recursive in archives for the moment.
                    continue;
                }


                var lineNumber = 0;

                await using var entryStream = await entry.OpenAsync(cancellationToken);
                using var reader = new StreamReader(entryStream);
                
                var matches = new List<MatchLine>();
                while (await reader.ReadLineAsync(cancellationToken) is { } line)
                {
                    lineNumber++;
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var matchesCount = SearchInLine(zipFilePath, line, parameters, lineNumber, matches);
                    if (matchesCount > 0)
                    {
                        // Intermediate result for large files
                        MatchFound?.Invoke(this, matchesCount);
                    }
                }

                if (matches.Count > 0)
                {
                    // File is complete
                    var result = new SearchResult(parameters, zipFilePath, entry.FullName, matches);
                    FileCompleted?.Invoke(this, result);
                }
            }
        }
        catch (Exception ex)
        {
            // Skip files that can't be read
            Trace.WriteLine(ex.ToString());
        }
    }

    private async Task SearchInFileAsync(string filePath, SearchParameters parameters,
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
                var matchesCount = SearchInLine(filePath, line, parameters, lineNumber, matches);
                if (matchesCount > 0)
                {
                    // Intermediate result for large files
                    MatchFound?.Invoke(this, matchesCount);
                }
            }

            if (matches.Count > 0)
            {
                var result = new SearchResult(parameters, filePath, matches, encoding);
                FileCompleted?.Invoke(this, result);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Skip files that can't be read
            Trace.WriteLine(ex.ToString());
        }
    }

    private static bool IsZip(string filePath)
    {
        return Path.GetExtension(filePath).Equals(".zip", StringComparison.InvariantCultureIgnoreCase);
    }


    /// <summary>
    ///     If the file has no preamble the preamble bytes in the returned encoding are empty.
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

    private static Encoding DetectEncoding(FileStream stream)
    {
        var bom = new byte[4];
        var bomLength = stream.Read(bom, 0, 4);
        return DetectEncoding(bom, bomLength);
    }

    private static Encoding DetectEncoding(byte[] bom, int bomLength)
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


    private static IEnumerable<string> EnumerateFiles(string directory, List<Regex> filePatterns,
        bool includeSubdirectories,
        CancellationToken cancellationToken)
    {
        var enumerationOptions = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = includeSubdirectories,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.System
        };


        cancellationToken.ThrowIfCancellationRequested();

        var files = Directory.EnumerateFiles(directory, "*", enumerationOptions);

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            if (filePatterns.Any(pattern => pattern.IsMatch(fileName)))
            {
                yield return file;
            }
        }
    }


    private static ulong SearchInLine(string filePath, string line, SearchParameters parameters, int lineNumber,
        List<MatchLine> matches)
    {
        if (parameters.Regex != null)
        {
            return SearchInLineRegex(filePath, line, parameters.Regex, lineNumber, matches);
        }
        else
        {
            return SearchInLineText(filePath, line, parameters, lineNumber, matches);
        }
    }

    private static ulong SearchInLineText(string filePath, string line, SearchParameters parameters, int lineNumber,
        List<MatchLine> matches)
    {
        var pattern = parameters.Pattern;
        // Simple string search: find all occurrences
        var comparison = parameters.IsCaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        var index = 0;

        ulong matchesCount = 0;
        while ((index = line.IndexOf(pattern, index, comparison)) != -1)
        {
            matchesCount++;
            matches.Add(new MatchLine
            {
                FilePath = filePath,
                LineNumber = lineNumber,
                Text = line,
                StartIndex = index,
                Length = pattern.Length,
                IsSelected = true
            });
            index += pattern.Length; // Move past this match
        }

        return matchesCount;
    }

    private static ulong SearchInLineRegex(string filePath, string line, Regex regex, int lineNumber,
        List<MatchLine> matches)
    {
        // Regex: capture all matches with positions
        var regexMatches = regex.EnumerateMatches(line);

        ulong matchesCount = 0;
        foreach (var match in regexMatches)
        {
            matchesCount++;
            matches.Add(new MatchLine
            {
                FilePath = filePath,
                LineNumber = lineNumber,
                Text = line,
                StartIndex = match.Index,
                Length = match.Length,
                IsSelected = true
            });
        }

        return matchesCount;
    }
}