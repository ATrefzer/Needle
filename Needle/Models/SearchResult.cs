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
    }
    
    public SearchResult(SearchParameters parameters, string filePath, string archivePath, IReadOnlyList<MatchLine> matches)
    {
        Parameters = parameters;
        FilePath = filePath;
        ArchivePath = archivePath;
        Matches = matches;
        Encoding = Encoding.Default;
        IsArchive = true;
    }

    public string FilePath { get; }
    public string ArchivePath { get; }
    public IReadOnlyList<MatchLine> Matches { get; }
    public Encoding Encoding { get; }
    
    /// <summary>
    /// Matches in zip files cannot be replaced (yet).
    /// </summary>
    public bool IsArchive { get; }
    public int MatchCount => Matches?.Count ?? 0;
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