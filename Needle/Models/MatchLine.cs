using System.ComponentModel;
using Needle.Resources;

namespace Needle.Models;

public class MatchLine : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public const int MaxDisplayLength = 150;
    public const int ContextBeforeMatch = 40;

    public int LineNumber { get; set; }

    /// <summary>
    ///     The match is in the file name, not in the file content. Text holds the file name then.
    /// </summary>
    public bool IsFileName { get; set; }

    public string LineLabel => IsFileName ? Strings.Label_FileNameMatch : LineNumber.ToString();

    public string Text { get; set; } = string.Empty;
    public int StartIndex { get; set; }
    public int Length { get; set; }

    /// <summary>
    ///     Only selected matches are replaced.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetIsSelected(value))
            {
                Owner?.OnMatchSelectionChanged();
            }
        }
    }

    /// <summary>
    ///     The search result this match belongs to. Set by the search result.
    /// </summary>
    internal SearchResult? Owner { get; set; }

    public bool CanReplace => Owner?.CanReplace ?? false;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    ///     Does not notify the owner. Used by the owner to select all matches at once.
    /// </summary>
    internal bool SetIsSelected(bool value)
    {
        if (_isSelected == value)
        {
            return false;
        }

        _isSelected = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        return true;
    }

    /// <summary>
    /// Redundant with SearchResult.FilePath, but simplifies the binding.
    /// </summary>
    public string FilePath { get; init; } = string.Empty;

    // The display text is split into the text before the match, the match and the text after it, so the match
    // can be highlighted. Computed on demand: Only for displayed matches, not during the search.

    public string DisplayBefore => GetDisplayParts().Before;
    public string DisplayMatch => GetDisplayParts().Match;
    public string DisplayAfter => GetDisplayParts().After;

    /// <summary>
    ///     Long lines are cut around the match, with some context before it.
    /// </summary>
    internal (string Before, string Match, string After) GetDisplayParts()
    {
        const string ellipsis = "…";

        // A very long match (e.g. regex ".*") is cut, too.
        var matchLength = Math.Min(Length, MaxDisplayLength);
        var matchEnd = StartIndex + matchLength;

        var start = 0;
        var end = Text.Length;
        if (Text.Length > MaxDisplayLength)
        {
            start = Math.Max(0, StartIndex - ContextBeforeMatch);
            end = Math.Min(Text.Length, Math.Max(matchEnd, start + MaxDisplayLength));
        }

        var before = Text[start..StartIndex];
        var match = Text[StartIndex..matchEnd];
        var after = Text[matchEnd..end];

        if (start > 0)
        {
            before = ellipsis + before;
        }

        if (matchLength < Length)
        {
            match += ellipsis;
            after = string.Empty;
        }
        else if (end < Text.Length)
        {
            after += ellipsis;
        }

        return (before, match, after);
    }
}