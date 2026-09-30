using Needle.Resources;

namespace Needle.Models;

public class MatchLine
{
    public const int MaxDisplayLength = 150;
    public int LineNumber { get; set; }

    /// <summary>
    ///     The match is in the file name, not in the file content. Text holds the file name then.
    /// </summary>
    public bool IsFileName { get; set; }

    public string LineLabel => IsFileName ? Strings.Label_FileNameMatch : LineNumber.ToString();

    public string Text { get; set; } = string.Empty;
    public int StartIndex { get; set; }
    public int Length { get; set; }
    public bool IsSelected { get; set; } = true; // For selective replacement

    public string SafeText => Text.Length < MaxDisplayLength ? Text : Truncate();

    /// <summary>
    /// Redundant with SearchResult.FilePath, but simplifies the binding.
    /// </summary>
    public string FilePath { get; init; } = string.Empty;

    private string Truncate()
    {
        var available = Text.Length - StartIndex;
        var length = Math.Min(MaxDisplayLength, available);

        var prefix = "(truncated) ... ";
        var postfix = string.Empty;
        if (length != available)
        {
            postfix = " ...";
        }

        var truncated = Text.AsSpan(StartIndex, length);
        return string.Concat(prefix, truncated, postfix);
    }
}