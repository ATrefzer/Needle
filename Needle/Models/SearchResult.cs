using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Needle.Services;

namespace Needle.Models;

public class SearchResult : INotifyPropertyChanged
{
    private bool _isExpanded;

    public SearchResult(SearchParameters parameters, string filePath, IReadOnlyList<MatchLine> matches)
    {
        Parameters = parameters;
        FilePath = filePath;
        Matches = matches;
    }

    public string FilePath { get; }
    public IReadOnlyList<MatchLine> Matches { get; }
    public int MatchCount => Matches?.Count ?? 0;
    public string FileName => Path.GetFileName(FilePath);

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