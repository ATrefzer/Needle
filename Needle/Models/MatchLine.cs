using System.ComponentModel;
using Needle.Resources;

namespace Needle.Models;

public class MatchLine : INotifyPropertyChanged
{
    private bool _isSelected = true;

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