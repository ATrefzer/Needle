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
            async (searchResult, ct) => await ProcessSingleFileAsync(searchResult, replacementText, resultToFill, ct)
                .ConfigureAwait(false)
        );

        return resultToFill;
    }

    /// <summary>
    ///     Returns the number of replacements
    /// </summary>
    private async Task ProcessSingleFileAsync(SearchResult searchResult, string replacementText, ReplaceResult result,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Only process selected matches
        var selectedMatches = searchResult.Matches.Where(m => m.IsSelected).ToList();
        if (selectedMatches.Count == 0)
        {
            return;
        }

        try
        {
            var replacementCount = await ReplaceInFileAsync(
                searchResult,
                selectedMatches,
                replacementText,
                cancellationToken);

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

    private static async Task<int> ReplaceInFileAsync(
        SearchResult searchResult,
        List<MatchLine> selectedMatches,
        string replacementText,
        CancellationToken cancellationToken)
    {
        // Read entire file
        string[] lines;
        try
        {
            lines = await File.ReadAllLinesAsync(searchResult.FilePath, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Cannot read file: {ex.Message}", ex);
        }

        var replacementCount = 0;

        // Group matches by line number
        var matchesByLine = selectedMatches
            .GroupBy(m => m.LineNumber)
            .OrderByDescending(g => g.Key); // Process in reverse to maintain positions

        foreach (var lineGroup in matchesByLine)
        {
            var lineIndex = lineGroup.Key - 1; // Convert to 0-based index
            if (lineIndex < 0 || lineIndex >= lines.Length)
            {
                continue;
            }

            var originalLine = lines[lineIndex];

            // Sort matches by StartIndex descending to replace from end to start
            // This preserves the positions of earlier matches
            // Sort matches by StartIndex ASCENDING - natural for left-to-right processing
            var sortedMatches = lineGroup.OrderBy(m => m.StartIndex).ToList();

            var regex = searchResult.Parameters.Regex;
            string newLine;
            if (regex != null)
            {
                newLine = ReplaceMultipleRegexInLine(originalLine, sortedMatches,
                    regex, replacementText);
            }
            else
            {
                newLine = ReplaceMultipleInLine(originalLine, sortedMatches,
                    replacementText);
            }


            lines[lineIndex] = newLine;
            replacementCount += sortedMatches.Count;
        }

        // Write back to file
        await File.WriteAllLinesAsync(searchResult.FilePath, lines, searchResult.Encoding, cancellationToken);

        return replacementCount;
    }

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
                if (match.StartIndex < 0 || match.StartIndex + match.Length > sourceSpan.Length)
                {
                    continue;
                }

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
            if (matchLine.StartIndex < 0 || matchLine.StartIndex + matchLine.Length > line.Length)
            {
                continue;
            }

            // Match on the original line at the exact position
            var match = regex.Match(line, matchLine.StartIndex, matchLine.Length);

            if (match.Success && match.Index == matchLine.StartIndex && match.Length == matchLine.Length)
            {
                // Perform replacement with capture group support
                var replacedText = match.Result(replacement);
                replacements.Add((matchLine.StartIndex, matchLine.Length, replacedText));
                totalLengthDelta += replacedText.Length - matchLine.Length;
            }
        }

        if (replacements.Count == 0)
        {
            return line;
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