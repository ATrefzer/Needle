using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using Needle.Models;
using Needle.Resources;
using Needle.Services;

namespace Needle.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly UserSettings _settings;
    private CancellationTokenSource? _cts;

    private string _fileMasks;
    private bool _includeSubdirectories;
    private bool _isBusy;
    private bool _isCaseSensitive;
    private bool _isRegex;

    private object _obj = new();
    private string _pattern;

    private string _progressMessage = string.Empty;
    private string _replacementText = string.Empty;
    private ObservableCollection<SearchResult> _result = [];
    private string _startDirectory;
    private string _statusMessage = "Ready";


    public MainViewModel()
    {
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

    private Regex CreateRegex()
    {
        var options = RegexOptions.Compiled | RegexOptions.Multiline;
        if (!IsCaseSensitive)
        {
            options |= RegexOptions.IgnoreCase;
        }

        return new Regex(Pattern, options, TimeSpan.FromSeconds(1));
    }

    private void SaveSettings()
    {
        _settings.StartDirectory = StartDirectory;
        _settings.FileMasks = FileMasks;
        _settings.Pattern = Pattern;
        _settings.IsRegex = IsRegex;
        _settings.IsCaseSensitive = IsCaseSensitive;
        _settings.IncludeSubdirectories = IncludeSubdirectories;
        _settings.Save();
    }

    private async void StartSearch()
    {
        IsBusy = true;
        ProgressMessage = "Searching...";
        StatusMessage = "Searching...";
        Results.Clear();

        List<SearchResult> searchResultsQueue = new(1000);
        var searchService = new FileSearchService();

        var timer = new DispatcherTimer
        {
            // If we have some hits but the large file gets not finished, update the progress.
            Interval = TimeSpan.FromSeconds(2)
        };
        timer.Stop();


        ulong finalHits = 0;
        ulong intermediateHits = 0;

        timer.Tick += (_, _) =>
        {
            lock (_obj)
            {
                timer.Stop();
                ProgressMessage = $"Found  {finalHits + intermediateHits} matches ({searchResultsQueue.Count} files completed)";
            }
        };

        searchService.FileCompleted += (_, result) =>
        {
            lock (_obj)
            {
                timer.Stop();
                searchResultsQueue.Add(result);
                finalHits += result.MatchCount;
                intermediateHits -= result.MatchCount;
                ProgressMessage = $"Found  {finalHits + intermediateHits} matches ({searchResultsQueue.Count} files completed)";
            }
        };
        searchService.MatchFound += (_, matchCount) =>
        {
            lock (_obj)
            {
                intermediateHits += matchCount;
                timer.Start();
            }
        };

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
            await searchService.SearchAsync(parameters, _cts.Token).ConfigureAwait(true);
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


    private void CancelSearch()
    {
        _cts?.Cancel();
    }

    private async void StartReplace()
    {
        IsBusy = true;
        StatusMessage = "Replacing...";

        // Give UI thread time to render overlay
        await Task.Delay(1);

        _cts = new CancellationTokenSource();


        var replaceService = new FileReplaceService();
        try
        {
            // Note: isRegex and isCaseSensitive are now stored in each SearchResult
            var result = await replaceService.ReplaceInFilesAsync(
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

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}