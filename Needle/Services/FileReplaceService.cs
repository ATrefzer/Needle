using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Needle.Models;
using SearchResult = Needle.Models.SearchResult;

namespace Needle.Services;

/*
   Example
   Search Pattern: (\w+)@(\w+\.com)
   Replacement: Email: $1 at domain $2
   Input: john@example.com
   Output: Email: john at domain example.com
 */
public class FileReplaceService : IReplaceService
{
    private const int MaxDegreeOfParallelism = 8;
    private const int BufferSize = 81920;

    public Task<ReplaceResult> ReplaceInFilesAsync(IEnumerable<SearchResult> searchResults,
        string replacementText,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => ReplaceInFilesInternalAsync(searchResults, replacementText, cancellationToken),
            cancellationToken);
    }

    private async Task<ReplaceResult> ReplaceInFilesInternalAsync(IEnumerable<SearchResult> searchResults,
        string replacementText,
        CancellationToken cancellationToken)
    {
        var resultToFill = new ReplaceResult();

        await Parallel.ForEachAsync(
            searchResults,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxDegreeOfParallelism,
                CancellationToken = cancellationToken
            },
            (searchResult, ct) =>
            {
                ProcessSingleFile(searchResult, replacementText, resultToFill, ct);
                return ValueTask.CompletedTask;
            });

        return resultToFill;
    }

    private static void ProcessSingleFile(SearchResult searchResult, string replacementText, ReplaceResult result,
        CancellationToken cancellationToken)
    {
        if (searchResult.IsArchive)
        {
            // Cannot replace in archive
            return;
        }
        
        cancellationToken.ThrowIfCancellationRequested();

        // Only process selected matches
        var selectedMatches = searchResult.Matches.Where(m => m.IsSelected).ToList();
        if (selectedMatches.Count == 0)
        {
            return;
        }

        var contentMatches = selectedMatches.Where(m => !m.IsFileName).ToList();
        var fileNameMatches = selectedMatches.Where(m => m.IsFileName).ToList();

        try
        {
            var replacementCount = 0;

            // Content first because the rename invalidates the file path.
            if (contentMatches.Count > 0)
            {
                replacementCount += ReplaceInFile(
                    searchResult,
                    contentMatches,
                    replacementText,
                    cancellationToken);
            }

            if (fileNameMatches.Count > 0)
            {
                RenameFile(searchResult, fileNameMatches, replacementText);
                replacementCount += fileNameMatches.Count;
            }

            if (replacementCount > 0)
            {
                result.IncrementFilesModified();
                result.AddToTotalReplacements(replacementCount);
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"{searchResult.FilePath}: {ex.Message}");
        }
    }

    private static void RenameFile(SearchResult searchResult, List<MatchLine> fileNameMatches, string replacementText)
    {
        var filePath = searchResult.FilePath;
        var oldName = Path.GetFileName(filePath);
        var sortedMatches = fileNameMatches.OrderBy(m => m.StartIndex).ToList();
        EnsureUnchanged(oldName, sortedMatches, searchResult.Parameters);

        var regex = searchResult.Parameters.Regex;
        var newName = regex != null
            ? ReplaceMultipleRegexInLine(oldName, sortedMatches, regex, replacementText)
            : ReplaceMultipleInLine(oldName, sortedMatches, replacementText);

        if (newName == oldName)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(newName) || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException($"Invalid new file name '{newName}'");
        }

        var newPath = Path.Combine(Path.GetDirectoryName(filePath)!, newName);

        // Allow case-only renames, but never overwrite another file.
        if (!string.Equals(newPath, filePath, StringComparison.OrdinalIgnoreCase) && File.Exists(newPath))
        {
            throw new InvalidOperationException($"Cannot rename to '{newName}', the file already exists");
        }

        File.Move(filePath, newPath, false);
    }

    /// <summary>
    ///     Only the lines with matches are decoded and modified. All other bytes, including all line breaks,
    ///     are copied unchanged. The result is written to a temporary file that replaces the original at the end,
    ///     so the original is untouched if anything fails.
    /// </summary>
    private static int ReplaceInFile(
        SearchResult searchResult,
        List<MatchLine> selectedMatches,
        string replacementText,
        CancellationToken cancellationToken)
    {
        // The search decodes invalid bytes as replacement characters. Writing them back would corrupt the file.
        // So we decode strict here: If the file does not fit the encoding, we don't touch it.
        var strictEncoding = CreateStrictEncoding(searchResult.Encoding);
        EnsureEncodable(strictEncoding, replacementText);

        // Matches are replaced left to right within a line.
        var matchesByLine = selectedMatches
            .GroupBy(m => m.LineNumber)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.StartIndex).ToList());

        var rewrittenLines = 0;

        byte[] RewriteLine(int lineNumber, ReadOnlyMemory<byte> bytes)
        {
            rewrittenLines++;
            var line = DecodeLine(bytes.Span, strictEncoding, searchResult.Encoding);
            var sortedMatches = matchesByLine[lineNumber];

            // The positions are taken from the search. If the file was modified in the meantime, we would
            // replace the wrong text.
            EnsureUnchanged(line, sortedMatches, searchResult.Parameters);

            var regex = searchResult.Parameters.Regex;
            var newLine = regex != null
                ? ReplaceMultipleRegexInLine(line, sortedMatches, regex, replacementText)
                : ReplaceMultipleInLine(line, sortedMatches, replacementText);

            return strictEncoding.GetBytes(newLine);
        }

        var filePath = searchResult.FilePath;
        var tempPath = Path.Combine(Path.GetDirectoryName(filePath)!,
            $"{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.needle.tmp");

        try
        {
            using (var input = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                       BufferSize, FileOptions.SequentialScan))
            using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       BufferSize))
            {
                CopyPreamble(input, output, searchResult.Encoding);
                LineRewriter.Rewrite(input, output, searchResult.Encoding, matchesByLine.ContainsKey, RewriteLine,
                    cancellationToken);
            }

            // Lines behind the end of the file were not visited.
            if (rewrittenLines != matchesByLine.Count)
            {
                throw new FileChangedException();
            }

            // Keeps the attributes and permissions of the original file.
            File.Replace(tempPath, filePath, null);
        }
        finally
        {
            File.Delete(tempPath);
        }

        return selectedMatches.Count;
    }

    /// <summary>
    ///     The BOM is copied unchanged. The line numbers of the search start after it.
    /// </summary>
    private static void CopyPreamble(Stream input, Stream output, Encoding encoding)
    {
        var preamble = encoding.GetPreamble();
        if (preamble.Length == 0)
        {
            return;
        }

        var bytes = new byte[preamble.Length];
        var read = input.ReadAtLeast(bytes, bytes.Length, false);
        if (read != bytes.Length || !bytes.AsSpan().SequenceEqual(preamble))
        {
            throw new FileChangedException();
        }

        output.Write(bytes);
    }

    private static string DecodeLine(ReadOnlySpan<byte> bytes, Encoding strictEncoding, Encoding encoding)
    {
        string line;
        try
        {
            line = strictEncoding.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new InvalidOperationException(
                $"The file is not valid {encoding.WebName}. Select the file's encoding and search again.");
        }

        // The unchanged parts of the line must be written back exactly. Some code pages cannot guarantee this.
        if (!strictEncoding.GetBytes(line).AsSpan().SequenceEqual(bytes))
        {
            throw new InvalidOperationException(
                $"The file cannot be written back unchanged in {encoding.WebName}.");
        }

        return line;
    }

    /// <summary>
    ///     Same code page, but throws on invalid bytes and on characters that cannot be encoded.
    ///     Used for single lines only, the BOM is copied separately.
    /// </summary>
    private static Encoding CreateStrictEncoding(Encoding encoding)
    {
        return Encoding.GetEncoding(encoding.CodePage, EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);
    }

    /// <summary>
    ///     A file in the ANSI code page cannot store every character. Without this check, such
    ///     characters would silently be written as '?'.
    /// </summary>
    private static void EnsureEncodable(Encoding strictEncoding, string text)
    {
        try
        {
            strictEncoding.GetByteCount(text);
        }
        catch (EncoderFallbackException)
        {
            throw new InvalidOperationException(
                $"The replacement text contains characters that cannot be stored in the file's encoding ({strictEncoding.WebName})");
        }
    }

    /// <summary>
    ///     Throws if the text at the matches' positions does no longer match the search pattern.
    /// </summary>
    private static void EnsureUnchanged(string line, List<MatchLine> matches, SearchParameters parameters)
    {
        if (!matches.All(match => IsUnchanged(line, match, parameters)))
        {
            throw new FileChangedException();
        }
    }

    private static bool IsUnchanged(string line, MatchLine match, SearchParameters parameters)
    {
        if (match.StartIndex < 0 || match.StartIndex + match.Length > line.Length)
        {
            return false;
        }

        if (parameters.Regex != null)
        {
            // Search the whole line (not only the matched part), so lookarounds and anchors behave as in the search.
            var regexMatch = parameters.Regex.Match(line, match.StartIndex);
            return regexMatch.Success && regexMatch.Index == match.StartIndex && regexMatch.Length == match.Length;
        }

        var comparison = parameters.IsCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return line.AsSpan(match.StartIndex, match.Length).Equals(parameters.Pattern, comparison);
    }

    /// <summary>
    ///     All matches must be verified by <see cref="EnsureUnchanged" /> before.
    /// </summary>
    private static string ReplaceMultipleInLine(
        string line,
        List<MatchLine> sortedMatches,
        string replacement)
    {
        if (sortedMatches.Count == 0)
        {
            return line;
        }

        // Calculate final string length
        var lengthDelta = replacement.Length - sortedMatches[0].Length;
        var finalLength = line.Length + lengthDelta * sortedMatches.Count;

        return string.Create(finalLength, (line, sortedMatches, replacement), (span, state) =>
        {
            var sourceSpan = state.line.AsSpan();
            var destPos = 0;
            var sourcePos = 0;

            // Process matches from start to end (already sorted ascending)
            foreach (var match in state.sortedMatches)
            {
                // Copy everything before the match
                var beforeLength = match.StartIndex - sourcePos;
                if (beforeLength > 0)
                {
                    sourceSpan.Slice(sourcePos, beforeLength).CopyTo(span.Slice(destPos));
                    destPos += beforeLength;
                }

                // Copy replacement text
                state.replacement.AsSpan().CopyTo(span.Slice(destPos));
                destPos += state.replacement.Length;

                // Skip the matched text in source
                sourcePos = match.StartIndex + match.Length;
            }

            // Copy remaining text after last match
            if (sourcePos < sourceSpan.Length)
            {
                sourceSpan.Slice(sourcePos).CopyTo(span.Slice(destPos));
            }
        });
    }


    /// <summary>
    ///     All matches must be verified by <see cref="EnsureUnchanged" /> before.
    /// </summary>
    private static string ReplaceMultipleRegexInLine(
        string line,
        List<MatchLine> sortedMatches,
        Regex regex,
        string replacement)
    {
        if (sortedMatches.Count == 0)
        {
            return line;
        }

        // First pass: compute all replacements
        var replacements = new List<(int startIndex, int length, string replacedText)>();
        var totalLengthDelta = 0;

        foreach (var matchLine in sortedMatches)
        {
            // Match on the whole line starting at the verified position (see IsUnchanged).
            var match = regex.Match(line, matchLine.StartIndex);

            // Perform replacement with capture group support
            var replacedText = match.Result(replacement);
            replacements.Add((matchLine.StartIndex, matchLine.Length, replacedText));
            totalLengthDelta += replacedText.Length - matchLine.Length;
        }

        // Second pass: build the new string with all replacements
        var finalLength = line.Length + totalLengthDelta;

        return string.Create(finalLength, (line, replacements), (span, state) =>
        {
            var sourceSpan = state.line.AsSpan();
            var destPos = 0;
            var sourcePos = 0;

            foreach (var (startIndex, length, replacedText) in state.replacements)
            {
                // Copy everything before the match
                var beforeLength = startIndex - sourcePos;
                if (beforeLength > 0)
                {
                    sourceSpan.Slice(sourcePos, beforeLength).CopyTo(span.Slice(destPos));
                    destPos += beforeLength;
                }

                // Copy replacement text
                replacedText.AsSpan().CopyTo(span.Slice(destPos));
                destPos += replacedText.Length;

                // Skip the matched text in source
                sourcePos = startIndex + length;
            }

            // Copy remaining text after last match
            if (sourcePos < sourceSpan.Length)
            {
                sourceSpan.Slice(sourcePos).CopyTo(span.Slice(destPos));
            }
        });
    }
}