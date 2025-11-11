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
    }

    public string FilePath { get; }
    public IReadOnlyList<MatchLine> Matches { get; }
    public Encoding Encoding { get; }
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