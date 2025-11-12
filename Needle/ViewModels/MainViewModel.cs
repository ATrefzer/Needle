using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using Needle.Models;
using Needle.Resources;
using Needle.Services;

namespace Needle.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    readonly IReplaceService _replaceService;
    readonly ISearchService _searchService;
    readonly UserSettings _settings;
    CancellationTokenSource? _cts;

    string _fileMasks;
    bool _includeSubdirectories;
    bool _isBusy;
    bool _isCaseSensitive;
    bool _isRegex;
    string _pattern;

    string _progressMessage;
    string _replacementText = string.Empty;
    ObservableCollection<SearchResult> _result = [];
    string _startDirectory;
    string _statusMessage = "Ready";


    public MainViewModel() : this(new FileSearchService(), new FileReplaceService())
    {
    }

    public MainViewModel(ISearchService searchService, IReplaceService replaceService)
    {
        _searchService = searchService;
        _replaceService = replaceService;
        _settings = UserSettings.Load();

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
    }


    public ObservableCollection<SearchResult> Results
    {
        get => _result;
        set
        {
            _result = value;
            OnPropertyChanged();
        }
    }

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

    public bool IsBusy
    {
        get => _isBusy;
        set
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

    Regex CreateRegex()
    {
        var options = RegexOptions.Compiled | RegexOptions.Multiline;
        if (!IsCaseSensitive)
        {
            options |= RegexOptions.IgnoreCase;
        }

        return new Regex(Pattern, options, TimeSpan.FromSeconds(1));
    }

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
        IsBusy = true;
        ProgressMessage = Strings.Label_Busy;
        StatusMessage = "Searching...";
        Results.Clear();


        ConcurrentQueue<SearchResult> searchResultsQueue = new();


        // Give UI thread time to render overlay
        await Task.Delay(1);

        _cts = new CancellationTokenSource();

        var parameters = new SearchParameters
        {
            StartDirectory = StartDirectory,
            FileMasks = FileMasks,
            Pattern = Pattern,
            Regex = IsRegex ? CreateRegex() : null,
            IsCaseSensitive = IsCaseSensitive,
            IncludeSubdirectories = IncludeSubdirectories
        };

        try
        {
            await _searchService.SearchAsync(parameters, r =>
                {
                    searchResultsQueue.Enqueue(r);

                    // Marshal UI update to the dispatcher
                    ProgressMessage = $"Found matches in {searchResultsQueue.Count} files.";
                }
                , _cts.Token).ConfigureAwait(true);


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

        // Regardless if we canceled or completed, update available results

        ProgressMessage = "Updating Ui";

        // WPF renders within frames around every 16ms!
        await Task.Delay(60);

        Results = new ObservableCollection<SearchResult>(searchResultsQueue);

        ProgressMessage = "Results loaded";
        IsBusy = false;
    }


    void CancelSearch()
    {
        _cts?.Cancel();
    }

    async void StartReplace()
    {
        IsBusy = true;
        StatusMessage = "Replacing...";

        // Give UI thread time to render overlay
        await Task.Delay(1);

        _cts = new CancellationTokenSource();

        try
        {
            // Note: isRegex and isCaseSensitive are now stored in each SearchResult
            var result = await _replaceService.ReplaceInFilesAsync(
                Results, ReplacementText,
                _cts.Token);

            if (result.Success)
            {
                StatusMessage = $"Replaced {result.TotalReplacements} occurrences in {result.FilesModified} files";
            }
            else
            {
                StatusMessage =
                    $"Replacement completed with errors. {result.FilesModified} files modified, {result.Errors.Count} errors";
                if (result.Errors.Count > 0)
                {
                    // Show first few errors
                    var errorSummary = string.Join("\n", result.Errors.Take(3));
                    MessageBox.Show($"Errors occurred during replacement:\n\n{errorSummary}",
                        "Replacement Errors",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }

            // Optionally clear results after successful replace
            // Results.Clear();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Replacement canceled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            MessageBox.Show($"Error during replacement:\n{ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}