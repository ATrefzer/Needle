using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using Needle.Models;
using Needle.Resources;
using Needle.Services;

namespace Needle.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private const int MaxFileMasksHistory = 10;

    private readonly Func<ISearchService> _createSearchService;
    private readonly IDialogService _dialogs;
    private readonly object _progressLock = new();
    private readonly IReplaceService _replaceService;
    private readonly UserSettings _settings;
    private CancellationTokenSource? _cts;

    private int _encodingWithoutBomCodePage;
    private string _fileMasks;
    private bool _includeSubdirectories;
    private bool _isBusy;
    private bool _isCaseSensitive;
    private bool _isRegex;
    private string _pattern;
    private string _progressMessage = string.Empty;
    private string _replacementText = string.Empty;
    private ObservableCollection<SearchResult> _results = [];
    private SearchScope _searchScope;
    private string _startDirectory;
    private string _statusMessage = Strings.Status_Ready;

    public MainViewModel()
        : this(UserSettings.Load(), () => new FileSearchService(), new FileReplaceService(),
            new MessageBoxDialogService())
    {
    }

    /// <param name="createSearchService">A search service is used for one search only.</param>
    public MainViewModel(UserSettings settings, Func<ISearchService> createSearchService,
        IReplaceService replaceService, IDialogService dialogs)
    {
        _settings = settings;
        _createSearchService = createSearchService;
        _replaceService = replaceService;
        _dialogs = dialogs;

        StartSearchCommand = new RelayCommand(_ => StartSearch(), _ => !IsBusy);
        CancelSearchCommand = new RelayCommand(_ => CancelSearch(), _ => IsBusy);
        ReplaceCommand = new RelayCommand(_ => StartReplace(), _ => !IsBusy && Results.Any());

        // Load saved settings
        _startDirectory = _settings.StartDirectory;
        _fileMasks = _settings.FileMasks;
        _pattern = _settings.Pattern;
        _isRegex = _settings.IsRegex;
        _isCaseSensitive = _settings.IsCaseSensitive;
        _includeSubdirectories = _settings.IncludeSubdirectories;
        _searchScope = Enum.IsDefined(_settings.SearchScope) ? _settings.SearchScope : SearchScope.Content;

        // The ANSI code page may differ if the settings were written on another system.
        _encodingWithoutBomCodePage = EncodingsWithoutBom.Any(e => e.Key == _settings.EncodingWithoutBomCodePage)
            ? _settings.EncodingWithoutBomCodePage
            : Encoding.UTF8.CodePage;

        FileMasksHistory = new ObservableCollection<string>(_settings.FileMasksHistory);
    }

    public ObservableCollection<SearchResult> Results
    {
        get => _results;
        set
        {
            _results = value;
            OnPropertyChanged();
            ReplaceCommand.RaiseCanExecuteChanged();
        }
    }

    public ObservableCollection<string> FileMasksHistory { get; }

    public string StartDirectory
    {
        get => _startDirectory;
        set
        {
            _startDirectory = value;
            OnPropertyChanged();
        }
    }

    public string FileMasks
    {
        get => _fileMasks;
        set
        {
            _fileMasks = value;
            OnPropertyChanged();
        }
    }

    public string Pattern
    {
        get => _pattern;
        set
        {
            _pattern = value;
            OnPropertyChanged();
        }
    }

    public bool IsRegex
    {
        get => _isRegex;
        set
        {
            _isRegex = value;
            OnPropertyChanged();
        }
    }

    public bool IsCaseSensitive
    {
        get => _isCaseSensitive;
        set
        {
            _isCaseSensitive = value;
            OnPropertyChanged();
        }
    }

    public bool IncludeSubdirectories
    {
        get => _includeSubdirectories;
        set
        {
            _includeSubdirectories = value;
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<KeyValuePair<SearchScope, string>> SearchScopes { get; } =
    [
        new(SearchScope.Content, Strings.SearchScope_Content),
        new(SearchScope.FileName, Strings.SearchScope_FileName),
        new(SearchScope.Both, Strings.SearchScope_Both)
    ];

    public SearchScope SearchScope
    {
        get => _searchScope;
        set
        {
            _searchScope = value;
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<KeyValuePair<int, string>> EncodingsWithoutBom { get; } =
    [
        new(Encoding.UTF8.CodePage, "UTF-8"),
        new(FileSearchService.AnsiEncoding.CodePage, $"ANSI ({FileSearchService.AnsiEncoding.WebName})")
    ];

    public int EncodingWithoutBomCodePage
    {
        get => _encodingWithoutBomCodePage;
        set
        {
            _encodingWithoutBomCodePage = value;
            OnPropertyChanged();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (value == _isBusy)
            {
                return;
            }

            _isBusy = value;
            OnPropertyChanged();

            StartSearchCommand.RaiseCanExecuteChanged();
            CancelSearchCommand.RaiseCanExecuteChanged();
            ReplaceCommand.RaiseCanExecuteChanged();
        }
    }

    public string ReplacementText
    {
        get => _replacementText;
        set
        {
            _replacementText = value;
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public string ProgressMessage
    {
        get => _progressMessage;
        private set
        {
            _progressMessage = value;
            OnPropertyChanged();
        }
    }

    public RelayCommand StartSearchCommand { get; }
    public RelayCommand CancelSearchCommand { get; }
    public RelayCommand ReplaceCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    ///     Called when starting a search and when the window is closed.
    /// </summary>
    public void SaveSettings()
    {
        _settings.StartDirectory = StartDirectory;
        _settings.FileMasks = FileMasks;
        _settings.Pattern = Pattern;
        _settings.IsRegex = IsRegex;
        _settings.IsCaseSensitive = IsCaseSensitive;
        _settings.IncludeSubdirectories = IncludeSubdirectories;
        _settings.SearchScope = SearchScope;
        _settings.EncodingWithoutBomCodePage = EncodingWithoutBomCodePage;
        _settings.FileMasksHistory = FileMasksHistory.ToList();
        _settings.Save();
    }

    private void AddFileMaskToHistory(string fileMask)
    {
        if (string.IsNullOrWhiteSpace(fileMask))
        {
            return;
        }

        // Remove if already exists to move it to the top
        FileMasksHistory.Remove(fileMask);
        FileMasksHistory.Insert(0, fileMask);

        while (FileMasksHistory.Count > MaxFileMasksHistory)
        {
            FileMasksHistory.RemoveAt(FileMasksHistory.Count - 1);
        }

        // Restore FileMasks because modifying the ObservableCollection
        // causes the editable ComboBox to reset its Text binding.
        FileMasks = fileMask;
    }

    private async void StartSearch()
    {
        await SearchAsync();
    }

    /// <summary>
    ///     Does not throw. Errors are shown in the status message.
    /// </summary>
    public async Task SearchAsync()
    {
        AddFileMaskToHistory(FileMasks);
        SaveSettings();

        IsBusy = true;
        ProgressMessage = Strings.Status_Searching;
        StatusMessage = Strings.Status_Searching;
        Results = [];

        List<SearchResult> searchResultsQueue = new(1000);
        var searchService = _createSearchService();

        var timer = new DispatcherTimer
        {
            // If we have some hits but the large file gets not finished, update the progress.
            Interval = TimeSpan.FromSeconds(2)
        };

        long finalHits = 0;
        long intermediateHits = 0;

        timer.Tick += (_, _) =>
        {
            lock (_progressLock)
            {
                timer.Stop();
                ProgressMessage = string.Format(Strings.Progress_Found, finalHits + intermediateHits,
                    searchResultsQueue.Count);
            }
        };

        searchService.FileCompleted += (_, result) =>
        {
            lock (_progressLock)
            {
                timer.Stop();
                searchResultsQueue.Add(result);
                finalHits += result.MatchCount;
                intermediateHits -= result.MatchCount;
                ProgressMessage = string.Format(Strings.Progress_Found, finalHits + intermediateHits,
                    searchResultsQueue.Count);
            }
        };
        searchService.MatchFound += (_, matchCount) =>
        {
            lock (_progressLock)
            {
                intermediateHits += matchCount;
                timer.Start();
            }
        };

        // Give UI thread time to render overlay
        await Task.Delay(1);

        _cts = new CancellationTokenSource();

        try
        {
            var parameters = new SearchParameters
            {
                Scope = SearchScope,
                StartDirectory = StartDirectory,
                FileMasks = FileMasks,
                Pattern = Pattern,
                IsRegex = IsRegex,
                IsCaseSensitive = IsCaseSensitive,
                IncludeSubdirectories = IncludeSubdirectories,
                EncodingWithoutBom = EncodingWithoutBomCodePage == Encoding.UTF8.CodePage
                    ? new UTF8Encoding(false)
                    : FileSearchService.AnsiEncoding
            };

            await searchService.SearchAsync(parameters, _cts.Token).ConfigureAwait(true);
            StatusMessage = Strings.Status_Finished;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Strings.Status_Canceled;
        }
        catch (RegexParseException ex)
        {
            StatusMessage = string.Format(Strings.Status_InvalidRegex, ex.Message);
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(Strings.Status_Error, ex.Message);
        }

        timer.Stop();

        // Otherwise, the user does not notice that files are missing in the result.
        if (searchService.SkippedFiles > 0 || searchService.SkippedDirectories > 0)
        {
            StatusMessage += string.Format(Strings.Status_Skipped, searchService.SkippedFiles,
                searchService.SkippedDirectories);
        }

        // Regardless if we canceled or completed, update available results
        ProgressMessage = Strings.Progress_UpdatingUi;

        // WPF renders within frames around every 16ms!
        await Task.Delay(60);

        Results = new ObservableCollection<SearchResult>(searchResultsQueue);
        IsBusy = false;
    }

    private void CancelSearch()
    {
        _cts?.Cancel();
    }

    private async void StartReplace()
    {
        await ReplaceAsync();
    }

    /// <summary>
    ///     Does not throw. Errors are shown in a message box and in the status message.
    /// </summary>
    public async Task ReplaceAsync()
    {
        // Matches in archives cannot be replaced.
        var selected = Results.Where(r => r.CanReplace && r.Matches.Any(m => m.IsSelected)).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = Strings.Status_NothingSelected;
            return;
        }

        var renameCount = selected.Count(r => r.Matches.Any(m => m.IsFileName && m.IsSelected));
        var question = renameCount > 0
            ? string.Format(Strings.Msg_ConfirmReplaceAndRename, selected.Count, renameCount)
            : string.Format(Strings.Msg_ConfirmReplace, selected.Count);
        if (!_dialogs.Confirm(question, Strings.Title_Replace))
        {
            return;
        }

        IsBusy = true;
        StatusMessage = Strings.Status_Replacing;

        // Give UI thread time to render overlay
        await Task.Delay(1);

        _cts = new CancellationTokenSource();

        try
        {
            var result = await _replaceService.ReplaceInFilesAsync(selected, ReplacementText, _cts.Token);

            if (result.Success)
            {
                StatusMessage = string.Format(Strings.Status_Replaced, result.TotalReplacements, result.FilesModified);
            }
            else
            {
                StatusMessage = string.Format(Strings.Status_ReplacedWithErrors, result.FilesModified,
                    result.Errors.Count);

                // Show first few errors
                var errorSummary = string.Join("\n", result.Errors.Take(3));
                _dialogs.ShowWarning(string.Format(Strings.Msg_ReplaceErrors, errorSummary),
                    Strings.Title_ReplaceErrors);
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Strings.Status_ReplaceCanceled;
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(Strings.Status_Error, ex.Message);
            _dialogs.ShowError(string.Format(Strings.Msg_ReplaceError, ex.Message), Strings.Title_Error);
        }
        finally
        {
            // The positions and paths in the result are outdated now, even if replacing failed or was canceled.
            Results = [];
            IsBusy = false;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
