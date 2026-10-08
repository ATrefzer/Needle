namespace Needle.Models;

/// <summary>
///     The part of a long line that is shown, so a line with many matches or a file without line breaks
///     (e.g. minified JavaScript) does not flood the output. Starts shortly before the first match and
///     includes the following matches as far as they fit.
/// </summary>
/// <param name="HiddenMatches">Matches that start after the shown part.</param>
public readonly record struct LineWindow(int Start, int End, int HiddenMatches)
{
    /// <param name="matches">The matches in the line, not empty.</param>
    /// <param name="maxColumns">0 for the whole line.</param>
    public static LineWindow Create(string text, IReadOnlyCollection<MatchLine> matches, int maxColumns)
    {
        if (maxColumns <= 0 || text.Length <= maxColumns)
        {
            return new LineWindow(0, text.Length, 0);
        }

        var firstMatch = matches.Min(m => m.StartIndex);
        var context = Math.Min(MatchLine.ContextBeforeMatch, maxColumns / 4);
        var start = Math.Max(0, firstMatch - context);
        var end = Math.Min(text.Length, start + maxColumns);

        // Near the end of the line, more context before the match fits.
        start = Math.Max(0, end - maxColumns);

        // Do not split a character that consists of two chars (surrogate pair).
        if (start > 0 && char.IsLowSurrogate(text[start]))
        {
            start++;
        }

        if (end < text.Length && char.IsLowSurrogate(text[end]))
        {
            end--;
        }

        var hidden = matches.Count(m => m.StartIndex >= end);
        return new LineWindow(start, end, hidden);
    }
}
