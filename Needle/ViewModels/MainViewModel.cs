using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Needle.Models;
using Needle.Services;

namespace Needle.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    readonly ISearchService _searchService;
    readonly UserSettings _settings;
    CancellationTokenSource? _cts;

    string _fileMasks;
    bool _isCaseSensitive;
    bool _isRegex;
    bool _isSearching;
    string _pattern;
    string _startDirectory;
    string _statusMessage = "Ready";
    bool _includeSubdirectories;

    public MainViewModel() : this(new FileSearchService())
    {
    }

    public MainViewModel(ISearchService searchService)
    {
        _searchService = searchService;
        _settings = UserSettings.Load();

        StartSearchCommand = new RelayCommand(_ => StartSearch(), _ => !IsSearching);
        CancelSearchCommand = new RelayCommand(_ => CancelSearch(), _ => IsSearching);

        // Load saved settings
        _startDirectory = _settings.StartDirectory;
        _fileMasks = _settings.FileMasks;
        _pattern = _settings.Pattern;
        _isRegex = _settings.IsRegex;
        _isCaseSensitive = _settings.IsCaseSensitive;
        _includeSubdirectories = _settings.IncludeSubdirectories;
    }

    public ObservableCollection<SearchResult> Results { get; } = new();

    public string StartDirectory
    {
        get => _startDirectory;
        set
        {
            _startDirectory = value;
            OnPropertyChanged();
            SaveSettings();
        }
    }

    public string FileMasks
    {
        get => _fileMasks;
        set
        {
            _fileMasks = value;
            OnPropertyChanged();
            SaveSettings();
        }
    }

    public string Pattern
    {
        get => _pattern;
        set
        {
            _pattern = value;
            OnPropertyChanged();
            SaveSettings();
        }
    }

    public bool IsRegex
    {
        get => _isRegex;
        set
        {
            _isRegex = value;
            OnPropertyChanged();
            SaveSettings();
        }
    }

    public bool IsCaseSensitive
    {
        get => _isCaseSensitive;
        set
        {
            _isCaseSensitive = value;
            OnPropertyChanged();
            SaveSettings();
        }
    }

    public bool IncludeSubdirectories
    {
        get => _includeSubdirectories;
        set
        {
            _includeSubdirectories = value;
            OnPropertyChanged();
            SaveSettings();
        }
    }

    public bool IsSearching
    {
        get => _isSearching;
        private set
        {
            _isSearching = value;
            OnPropertyChanged();
            StartSearchCommand.RaiseCanExecuteChanged();
            CancelSearchCommand.RaiseCanExecuteChanged();
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

    public RelayCommand StartSearchCommand { get; }
    public RelayCommand CancelSearchCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    void SaveSettings()
    {
        _settings.StartDirectory = StartDirectory;
        _settings.FileMasks = FileMasks;
        _settings.Pattern = Pattern;
        _settings.IsRegex = IsRegex;
        _settings.IsCaseSensitive = IsCaseSensitive;
        _settings.IncludeSubdirectories = IncludeSubdirectories;
        _settings.Save();
    }

    async void StartSearch()
    {
        IsSearching = true;
        StatusMessage = "Searching...";
        Results.Clear();

        // Give UI thread time to render overlay
        await Task.Delay(1);

        _cts = new CancellationTokenSource();

        var parameters = new SearchParameters
        {
            StartDirectory = StartDirectory,
            FileMasks = FileMasks,
            Pattern = Pattern,
            IsRegex = IsRegex,
            IsCaseSensitive = IsCaseSensitive,
            IncludeSubdirectories = IncludeSubdirectories
        };

        try
        {
            await _searchService.SearchAsync(parameters, r => App.Current.Dispatcher.Invoke(() => Results.Add(r)),
                _cts.Token);
            StatusMessage = "Finished";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Canceled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    void CancelSearch()
    {
        _cts?.Cancel();
    }

    void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}