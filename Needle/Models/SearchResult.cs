using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Needle.Services;

namespace Needle.Models;

public class SearchResult : INotifyPropertyChanged
{
    private bool _isExpanded;

    public SearchResult(SearchParameters parameters, string filePath, IReadOnlyList<MatchLine> matches,
        Encoding encoding)
    {
        Parameters = parameters;
        FilePath = filePath;
        Matches = matches;
        Encoding = encoding;
        IsArchive = false;
        ArchivePath = string.Empty;
        SetOwner(matches);
    }

    public SearchResult(SearchParameters parameters, string filePath, string archivePath, IReadOnlyList<MatchLine> matches)
    {
        Parameters = parameters;
        FilePath = filePath;
        ArchivePath = archivePath;
        Matches = matches;
        Encoding = Encoding.Default;
        IsArchive = true;
        SetOwner(matches);
    }

    /// <summary>
    ///     True if all matches are selected, false if none, null if some.
    ///     Setting it selects or deselects all matches.
    /// </summary>
    public bool? IsSelected
    {
        get
        {
            var selected = Matches.Count(m => m.IsSelected);
            return selected == Matches.Count ? true : selected == 0 ? false : null;
        }
        set
        {
            // A click on an undetermined check box selects all.
            var isSelected = value ?? true;
            foreach (var match in Matches)
            {
                // Without notifying this owner for each match.
                match.SetIsSelected(isSelected);
            }

            OnPropertyChanged();
        }
    }

    internal void OnMatchSelectionChanged()
    {
        OnPropertyChanged(nameof(IsSelected));
    }

    private void SetOwner(IReadOnlyList<MatchLine> matches)
    {
        foreach (var match in matches)
        {
            match.Owner = this;
        }
    }

    public string FilePath { get; }
    public string ArchivePath { get; }
    public IReadOnlyList<MatchLine> Matches { get; }
    public Encoding Encoding { get; }
    
    /// <summary>
    /// Matches in zip files cannot be replaced (yet).
    /// </summary>
    public bool IsArchive { get; }
    public bool CanReplace => !IsArchive;
    public ulong MatchCount => (ulong)Matches.Count;
    public string FileName => IsArchive ? Path.GetFileName(FilePath) + "/" + ArchivePath : Path.GetFileName(FilePath) ;

    /// <summary>
    ///     Used search parameters for this search result.
    /// </summary>
    public SearchParameters Parameters { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}