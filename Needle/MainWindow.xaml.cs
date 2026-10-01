using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Needle.Models;
using Needle.Resources;
using Needle.ViewModels;

namespace Needle;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (sender, e) => InitFocusControl.Focus();
        var viewModel = new MainViewModel();
        DataContext = viewModel;

        // Settings are not saved on every change, but on search and here.
        Closing += (_, _) => viewModel.SaveSettings();
    }

    void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { DataContext: SearchResult result })
        {
            result.IsExpanded = !result.IsExpanded;
            e.Handled = true;
        }
    }

    void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = Strings.Title_SelectStartDirectory,
            InitialDirectory = (DataContext as MainViewModel)?.StartDirectory ?? string.Empty
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (DataContext is MainViewModel viewModel)
        {
            viewModel.StartDirectory = dialog.FolderName;
        }
    }

    // Line-level context menu (match lines)
    void OpenInNotepadPlusPlus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem
            {
                Parent: ContextMenu { PlacementTarget: FrameworkElement { DataContext: MatchLine match } }
            })
        {
            OpenFileInNotepadPlusPlus(match.FilePath, match.LineNumber);
        }
    }

    void OpenFileInNotepadPlusPlus(string filePath, int lineNumber)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                MessageBox.Show(string.Format(Strings.Msg_FileNotFound, filePath),
                    Strings.Title_Error, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var notepadPlusPlus = FindNotepadPlusPlus();
            if (notepadPlusPlus == null)
            {
                MessageBox.Show(Strings.Msg_NotepadPlusPlusNotFound, Strings.Title_NotepadPlusPlusNotFound,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = notepadPlusPlus,
                Arguments = $"-n{lineNumber} \"{filePath}\"",
                UseShellExecute = false
            };

            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            MessageBox.Show(string.Format(Strings.Msg_OpenNotepadPlusPlusError, ex.Message),
                Strings.Title_Error, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    ///     Returns null if Notepad++ is not installed.
    /// </summary>
    static string? FindNotepadPlusPlus()
    {
        const string exe = "notepad++.exe";

        // Registered by the installer
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = root.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exe}");
            if (key?.GetValue(null) is string registered && File.Exists(registered))
            {
                return registered;
            }
        }

        var folders = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            }
            .Select(folder => Path.Combine(folder, "Notepad++"))
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));

        return folders
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Select(folder => Path.Combine(folder.Trim(), exe))
            .FirstOrDefault(File.Exists);
    }

    void OpenFileInExplorer(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            }
            else
            {
                MessageBox.Show(string.Format(Strings.Msg_FileNotFound, filePath),
                    Strings.Title_Error, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(string.Format(Strings.Msg_OpenInExplorerError, ex.Message),
                Strings.Title_Error, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Header-level context menu (file-level) handler
    void FindSearchResultInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem
            {
                Parent: ContextMenu { PlacementTarget: FrameworkElement { DataContext: SearchResult searchResult } }
            })
        {
            return;
        }

        OpenFileInExplorer(searchResult.FilePath);
    }
}