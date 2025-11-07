using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Needle.Models;
using SearchResult = Needle.Models.SearchResult;

namespace Needle.Services;

/*
   TODO Example
   Search Pattern: (\w+)@(\w+\.com)
   Replacement: Email: $1 at domain $2
   Input: john@example.com
   Output: Email: john at domain example.com
 */
public class FileReplaceService : IReplaceService
{
    const int MaxDegreeOfParallelism = 8;

    public async Task<ReplaceResult> ReplaceInFilesAsync(
        IEnumerable<SearchResult> searchResults,
        string replacementText,
        CancellationToken cancellationToken)
    {
        var result = new ReplaceResult { Success = true };
        var filesModified = 0;
        var totalReplacements = 0;

        await Parallel.ForEachAsync(
            searchResults,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxDegreeOfParallelism,
                CancellationToken = cancellationToken
            },
            async (searchResult, ct) =>
            {
                ct.ThrowIfCancellationRequested();

                // Only process selected matches
                var selectedMatches = searchResult.Matches.Where(m => m.IsSelected).ToList();
                if (selectedMatches.Count == 0)
                {
                    return;
                }

                try
                {
                    var replacementCount = await ReplaceInFileAsync(
                        searchResult.FilePath,
                        selectedMatches,
                        searchResult.Parameters,
                        ct);

                    if (replacementCount > 0)
                    {
                        Interlocked.Increment(ref filesModified);
                        Interlocked.Add(ref totalReplacements, replacementCount);
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{searchResult.FilePath}: {ex.Message}");
                }
            }
        );
    
        result.FilesModified = filesModified;
        result.TotalReplacements = totalReplacements;
        result.Success = result.Errors.Count == 0;
        
        return result;
    }

    static async Task<int> ReplaceInFileAsync(
        string filePath,
        List<MatchLine> selectedMatches,
        SearchParameters parameters,
        CancellationToken cancellationToken)
    {
        // Read entire file
        string[] lines;
        try
        {
            lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
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
            var newLine = originalLine;

            // Sort matches by StartIndex descending to replace from end to start
            // This preserves the positions of earlier matches
            var sortedMatches = lineGroup.OrderByDescending(m => m.StartIndex).ToList();

            foreach (var match in sortedMatches)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (parameters.Regex != null)
                {
                    // For regex, use the original pattern to support capture groups
                    newLine = ReplaceRegexInLine(newLine, parameters.Regex, parameters.ReplacementText,
                        match.StartIndex, match.Length);
                }
                else
                {
                    // Simple string replacement
                    if (match.StartIndex >= 0 && match.StartIndex + match.Length <= newLine.Length)
                    {
                        newLine = newLine.Remove(match.StartIndex, match.Length)
                            .Insert(match.StartIndex, parameters.ReplacementText);
                    }
                }

                replacementCount++;
            }

            lines[lineIndex] = newLine;
        }

        // Write back to file
        await File.WriteAllLinesAsync(filePath, lines, new UTF8Encoding(false), cancellationToken);

        return replacementCount;
    }

    static string ReplaceRegexInLine(string line, Regex regex, string replacement, int startIndex, int length)
    {
        // Extract the matched substring
        if (startIndex < 0 || startIndex + length > line.Length)
        {
            return line;
        }

        var substring = line.Substring(startIndex, length);

        // Check if the substring still matches (file might have changed)
        var match = regex.Match(substring);
        if (match is { Success: true })
        {
            // Perform replacement with support for capture groups ($1, $2, etc.)
            var replaced = regex.Replace(substring, replacement, 1);
            return line.Remove(startIndex, length).Insert(startIndex, replaced);
        }

        return line;
    }
}