using Needle.Models;
using Needle.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;

namespace Needle.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    readonly ISearchService _searchService;
    readonly IReplaceService _replaceService;
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
    string _replacementText = string.Empty;
    bool _isReplacing;

    public MainViewModel() : this(new FileSearchService(), new FileReplaceService())
    {
    }

    public MainViewModel(ISearchService searchService, IReplaceService replaceService)
    {
        _searchService = searchService;
        _replaceService = replaceService;
        _settings = UserSettings.Load();

        StartSearchCommand = new RelayCommand(_ => StartSearch(), _ => !IsSearching && !IsReplacing);
        CancelSearchCommand = new RelayCommand(_ => CancelSearch(), _ => IsSearching);
        ReplaceCommand = new RelayCommand(_ => StartReplace(), _ => !IsSearching && !IsReplacing && Results.Any());

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

    public string ReplacementText
    {
        get => _replacementText;
        set
        {
            _replacementText = value;
            OnPropertyChanged();
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
            ReplaceCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsReplacing
    {
        get => _isReplacing;
        private set
        {
            _isReplacing = value;
            OnPropertyChanged();
            StartSearchCommand.RaiseCanExecuteChanged();
            ReplaceCommand.RaiseCanExecuteChanged();
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
    public RelayCommand ReplaceCommand { get; }

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
            Regex = IsRegex ? CreateRegex() : null,
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

    Regex? CreateRegex()
    {
        var options = RegexOptions.Compiled | RegexOptions.Multiline;
        if (!IsCaseSensitive)
        {
            options |= RegexOptions.IgnoreCase;
        }

        return new Regex(Pattern, options, TimeSpan.FromSeconds(1));
    }

    void CancelSearch()
    {
        _cts?.Cancel();
    }

    /// <summary>
    /// Optional: Combine the two approaches.
    /// </summary>
    async void StartReplaceAllDirect()
    {
        IsReplacing = true;
        StatusMessage = "Searching and replacing...";
        Results.Clear();

        await Task.Delay(1);
        _cts = new CancellationTokenSource();

        var parameters = new SearchParameters
        {
            StartDirectory = StartDirectory,
            FileMasks = FileMasks,
            Pattern = Pattern,
            Regex = IsRegex ? CreateRegex() : null,
            IsCaseSensitive = IsCaseSensitive,
            IncludeSubdirectories = IncludeSubdirectories,
            ReplacementText = ReplacementText
        };

        try
        {
            var searchResults = new List<SearchResult>();

            // Search first
            await _searchService.SearchAsync(
                parameters,
                r => searchResults.Add(r),
                _cts.Token);

            if (searchResults.Count == 0)
            {
                StatusMessage = "No matches found";
                MessageBox.Show("No matches found for the specified pattern.",
                    "Replace All",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Show what will be replaced
            var totalMatches = searchResults.Sum(r => r.MatchCount);
            var confirmReplace = MessageBox.Show(
                $"Found {totalMatches} matches in {searchResults.Count} files.\n\n" +
                "Proceed with replacement?",
                "Confirm Replace",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmReplace != MessageBoxResult.Yes)
            {
                StatusMessage = "Replace canceled";
                return;
            }

            // Then replace immediately
            var result = await _replaceService.ReplaceInFilesAsync(
                searchResults,
                ReplacementText,
                _cts.Token);

            if (result.Success)
            {
                StatusMessage = $"Successfully replaced {result.TotalReplacements} occurrences in {result.FilesModified} files";
                MessageBox.Show(
                    $"Replacement completed successfully!\n\n" +
                    $"Files modified: {result.FilesModified}\n" +
                    $"Total replacements: {result.TotalReplacements}",
                    "Replace All Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                StatusMessage = $"Replacement completed with {result.Errors.Count} errors. {result.FilesModified} files modified";
                if (result.Errors.Count > 0)
                {
                    var errorSummary = string.Join("\n", result.Errors.Take(5));
                    if (result.Errors.Count > 5)
                        errorSummary += $"\n... and {result.Errors.Count - 5} more errors";

                    MessageBox.Show(
                        $"Errors occurred during replacement:\n\n{errorSummary}",
                        "Replacement Errors",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Canceled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            MessageBox.Show(
                $"Error during replacement:\n{ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsReplacing = false;
        }
    }

    async void StartReplace()
    {
        IsReplacing = true;
        StatusMessage = "Replacing...";

        // Give UI thread time to render overlay
        await Task.Delay(1);

        _cts = new CancellationTokenSource();

        try
        {
            // Note: isRegex and isCaseSensitive are now stored in each SearchResult
            var result = await _replaceService.ReplaceInFilesAsync(
                Results,
                ReplacementText,
                _cts.Token);

            if (result.Success)
            {
                StatusMessage = $"Replaced {result.TotalReplacements} occurrences in {result.FilesModified} files";
            }
            else
            {
                StatusMessage = $"Replacement completed with errors. {result.FilesModified} files modified, {result.Errors.Count} errors";
                if (result.Errors.Count > 0)
                {
                    // Show first few errors
                    var errorSummary = string.Join("\n", result.Errors.Take(3));
                    System.Windows.MessageBox.Show($"Errors occurred during replacement:\n\n{errorSummary}",
                        "Replacement Errors",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
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
            System.Windows.MessageBox.Show($"Error during replacement:\n{ex.Message}",
                "Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsReplacing = false;
        }
    }

    void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}