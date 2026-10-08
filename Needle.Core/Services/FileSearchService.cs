using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.IO.Enumeration;
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

        // Throws RegexParseException before the search starts, if the regex is invalid.
        _ = parameters.Regex;

        return Task.Run(() => SearchInternalAsync(parameters, cancellationToken), cancellationToken);
    }

    private int _skippedDirectories;
    private int _skippedFiles;

    public event EventHandler<SearchResult>? FileCompleted;
    public event EventHandler<int>? MatchFound;
    public int SkippedFiles => _skippedFiles;
    public int SkippedDirectories => _skippedDirectories;

    private async Task SearchInternalAsync(SearchParameters parameters, CancellationToken cancellationToken)
    {
        // Create channel for producer-consumer pattern
        var channel = Channel.CreateUnbounded<string>();

        // Created once per search, because the regexes are compiled.
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
                await ProcessSingleFileAsync(filePath, parameters, filePatterns, ct).ConfigureAwait(false)
        );

        await producerTask;
    }

    private async Task ProcessSingleFileAsync(string filePath, SearchParameters parameters,
        List<Regex> filePatterns, CancellationToken cancellationToken)
    {
        try
        {
            if (IsZip(filePath))
            {
                await SearchInArchiveAsync(filePath, parameters, filePatterns, cancellationToken);
                return;
            }

            var matches = SearchInFileName(filePath, Path.GetFileName(filePath), parameters);

            if (parameters.SearchInContent)
            {
                await SearchInFileAsync(filePath, parameters, matches, cancellationToken);
            }
            else if (matches.Count > 0)
            {
                // Content is not touched, so the encoding does not matter.
                var result = new SearchResult(parameters, filePath, matches, new UTF8Encoding(false));
                FileCompleted?.Invoke(this, result);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Skip files that can't be accessed
            Interlocked.Increment(ref _skippedFiles);
            Trace.WriteLine(ex.ToString());
        }
    }

    private async Task SearchInArchiveAsync(string zipFilePath, SearchParameters parameters,
        List<Regex> filePatterns, CancellationToken cancellationToken)
    {
        try
        {
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


                var matches = SearchInFileName(zipFilePath, Path.GetFileName(entry.FullName), parameters);

                if (parameters.SearchInContent)
                {
                    var lineNumber = 0;

                    await using var entryStream = await entry.OpenAsync(cancellationToken);
                    using var reader = new StreamReader(entryStream, parameters.EncodingWithoutBom);

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
                }

                if (matches.Count > 0)
                {
                    // File is complete
                    var result = new SearchResult(parameters, zipFilePath, entry.FullName, matches);
                    FileCompleted?.Invoke(this, result);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Skip archives that can't be read, like broken ones
            Interlocked.Increment(ref _skippedFiles);
            Trace.WriteLine(ex.ToString());
        }
    }

    /// <summary>
    ///     Returns the matches in the file name (empty if file names are not searched).
    ///     The returned list is used to collect further content matches.
    /// </summary>
    private List<MatchLine> SearchInFileName(string filePath, string fileName, SearchParameters parameters)
    {
        var matches = new List<MatchLine>();
        if (!parameters.SearchInFileName)
        {
            return matches;
        }

        var matchesCount = SearchInLine(filePath, fileName, parameters, 0, matches);
        foreach (var match in matches)
        {
            match.IsFileName = true;
        }

        if (matchesCount > 0)
        {
            MatchFound?.Invoke(this, matchesCount);
        }

        return matches;
    }

    /// <param name="matches">Already found matches (i.e. in the file name). Content matches are appended.</param>
    private async Task SearchInFileAsync(string filePath, SearchParameters parameters, List<MatchLine> matches,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            BufferSize,
            FileOptions.SequentialScan | FileOptions.Asynchronous);

        // Extra step if I want to prevent writing a BOM when the original file did not have one.
        // Jump back to beginning after detecting encoding is faster than opening the file twice.
        // Invalid bytes are decoded as replacement characters here. Replacing is strict and refuses such files.
        var encoding = DetectBomEncoding(stream) ?? parameters.EncodingWithoutBom;
        stream.Seek(0, SeekOrigin.Begin);

        using var reader = new StreamReader(stream, encoding, false, BufferSize);

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

    private static bool IsZip(string filePath)
    {
        return Path.GetExtension(filePath).Equals(".zip", StringComparison.InvariantCultureIgnoreCase);
    }


    /// <summary>
    ///     Returns null if the file has no BOM.
    /// </summary>
    public static Encoding? DetectBomEncoding(string filePath)
    {
        using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        return DetectBomEncoding(file);
    }

    private static Encoding? DetectBomEncoding(FileStream stream)
    {
        var bom = new byte[4];
        var bomLength = stream.ReadAtLeast(bom, bom.Length, false);
        return DetectBomEncoding(bom, bomLength);
    }

    /// <summary>
    ///     Returns null if there is no BOM.
    /// </summary>
    private static Encoding? DetectBomEncoding(byte[] bom, int bomLength)
    {
        // UTF-32 LE before UTF-16 LE, because both BOMs start with FF FE.
        if (bomLength >= 4 && bom[0] == 0xFF && bom[1] == 0xFE && bom[2] == 0x00 && bom[3] == 0x00)
        {
            return Encoding.UTF32; // UTF-32 LE
        }

        if (bomLength >= 4 && bom[0] == 0x00 && bom[1] == 0x00 && bom[2] == 0xFE && bom[3] == 0xFF)
        {
            return new UTF32Encoding(true, true); // UTF-32 BE
        }

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

        return null;
    }

    /// <summary>
    ///     The ANSI code page of the system, like Windows-1252 for western languages.
    ///     Can be selected for files without BOM.
    /// </summary>
    public static Encoding AnsiEncoding { get; } = CreateAnsiEncoding();

    private const int WesternEuropeanCodePage = 1252;

    private static Encoding CreateAnsiEncoding()
    {
        // Code pages are not available in .NET by default. We have to register them.
        // Afterwards, code page 0 is the system's ANSI code page.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var systemAnsi = Encoding.GetEncoding(0);

        // With the Windows option "Use Unicode UTF-8 for worldwide language support" the ANSI code page is UTF-8.
        // It would be the same as the UTF-8 option then.
        return systemAnsi.CodePage == Encoding.UTF8.CodePage
            ? Encoding.GetEncoding(WesternEuropeanCodePage)
            : systemAnsi;
    }


    private IEnumerable<string> EnumerateFiles(string directory, List<Regex> filePatterns,
        bool includeSubdirectories,
        CancellationToken cancellationToken)
    {
        var files = SafeEnumerateFiles(directory, includeSubdirectories, cancellationToken);

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            if (filePatterns.Any(pattern => pattern.IsMatch(fileName)))
            {
                yield return file;
            }
        }
    }

    /// <summary>
    ///     Hidden and system files are searched, too. Inaccessible directories throw, so they can be counted.
    /// </summary>
    private static readonly EnumerationOptions AllEntries = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = false
    };

    private static bool IsSkippedDirectory(string path, bool isReparsePoint)
    {
        // The git repository contains only internal data, but a lot of it.
        var folderName = Path.GetFileName(path);
        if (folderName.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
            folderName.Equals(".vs", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Symbolic links and junctions are not followed, they can form endless loops.
        // Other reparse points, like OneDrive folders, are normal directories.
        return isReparsePoint && new DirectoryInfo(path).LinkTarget != null;
    }

    /// <summary>
    ///     Enumerates files recursively while tolerating inaccessible subdirectories,
    ///     instead of letting one bad folder abort the entire scan.
    ///     Files are yielded per directory, so the search can start before the whole tree is enumerated.
    /// </summary>
    private IEnumerable<string> SafeEnumerateFiles(string path, bool includeSubdirectories,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(path);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();

            var files = new List<string>();

            try
            {
                // One pass for files and directories. The entry provides the attributes without extra calls.
                var entries = new FileSystemEnumerable<(string Path, bool IsDirectory, bool IsReparsePoint)>(
                    current,
                    (ref entry) => (entry.ToFullPath(), entry.IsDirectory,
                        entry.Attributes.HasFlag(FileAttributes.ReparsePoint)),
                    AllEntries);

                foreach (var entry in entries)
                {
                    if (!entry.IsDirectory)
                    {
                        files.Add(entry.Path);
                    }
                    else if (includeSubdirectories && !IsSkippedDirectory(entry.Path, entry.IsReparsePoint))
                    {
                        pending.Push(entry.Path);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Interlocked.Increment(ref _skippedDirectories);
                Trace.WriteLine(ex.ToString());
                continue;
            }

            // Not possible inside the try block above.
            foreach (var file in files)
            {
                yield return file;
            }
        }
    }

    private static int SearchInLine(string filePath, string line, SearchParameters parameters, int lineNumber,
        List<MatchLine> matches)
    {
        if (parameters.Regex != null)
        {
            return SearchInLineRegex(filePath, line, parameters.Regex, lineNumber, matches);
        }

        return SearchInLineText(filePath, line, parameters, lineNumber, matches);
    }

    private static int SearchInLineText(string filePath, string line, SearchParameters parameters, int lineNumber,
        List<MatchLine> matches)
    {
        var pattern = parameters.Pattern;
        // Simple string search: find all occurrences
        var comparison = parameters.IsCaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        var index = 0;

        var matchesCount = 0;
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

    private static int SearchInLineRegex(string filePath, string line, Regex regex, int lineNumber,
        List<MatchLine> matches)
    {
        // Regex: capture all matches with positions
        var regexMatches = regex.EnumerateMatches(line);

        var matchesCount = 0;
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