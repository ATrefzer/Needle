using System.ComponentModel;
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
        DataContext = new MainViewModel();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { DataContext: SearchResult result })
        {
            result.IsExpanded = !result.IsExpanded;
            e.Handled = true;
        }
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
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

    private void OpenInNotepadPlusPlus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement border } })
            // Border.DataContext = MatchLine
            // Border.Tag = SearchResult
        {
            if (border is { DataContext: MatchLine matchLine, Tag: SearchResult searchResult })
            {
                OpenFileInNotepadPlusPlus(searchResult.FilePath, matchLine.LineNumber);
            }
        }
    }

    private void OpenFileInNotepadPlusPlus(string filePath, int lineNumber)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                MessageBox.Show(string.Format(Strings.Msg_FileNotFound, filePath),
                    Strings.Title_Error, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Notepad++ command line syntax: notepad++.exe -n<lineNumber> "filepath"
            var startInfo = new ProcessStartInfo
            {
                FileName = "C:\\Program Files\\Notepad++\\notepad++.exe",
                Arguments = $"-n{lineNumber} \"{filePath}\"",
                UseShellExecute = false
            };

            Process.Start(startInfo);
        }
        catch (Win32Exception)
        {
            MessageBox.Show(Strings.Msg_NotepadPlusPlusNotFound, Strings.Title_NotepadPlusPlusNotFound,
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(string.Format(Strings.Msg_OpenNotepadPlusPlusError, ex.Message),
                Strings.Title_Error, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenFileInExplorer(string filePath)
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

    private void FindInExplorer_Click(object sender, RoutedEventArgs e)
    {
        // Border.DataContext = MatchLine
        // Border.Tag = SearchResult
        if (sender is not MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement border } })
        {
            return;
        }

        if (border is { DataContext: MatchLine matchLine, Tag: SearchResult searchResult })
        {
            OpenFileInExplorer(searchResult.FilePath);
        }
    }
}