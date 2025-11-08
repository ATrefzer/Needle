using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Needle.Models;
using Needle.ViewModels;

namespace Needle;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.DataContext is SearchResult result)
        {
            result.IsExpanded = !result.IsExpanded;
            e.Handled = true;
        }
        
        // if (e.ClickCount == 2)
        //     if (sender is Border { DataContext: SearchResult result })
        //     {
        //         OpenFileInExplorer(result.FilePath);
        //         e.Handled = true;
        //     }
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select start directory",
            InitialDirectory = (DataContext as MainViewModel)?.StartDirectory ?? string.Empty
        };

        if (dialog.ShowDialog() == true)
            if (DataContext is MainViewModel viewModel)
                viewModel.StartDirectory = dialog.FolderName;
    }

    private void OpenInNotepadPlusPlus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement border } })
            // Border.DataContext = MatchLine
            // Border.Tag = SearchResult
            if (border is { DataContext: MatchLine matchLine, Tag: SearchResult searchResult })
                OpenFileInNotepadPlusPlus(searchResult.FilePath, matchLine.LineNumber);
    }

    private void OpenFileInNotepadPlusPlus(string filePath, int lineNumber)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                MessageBox.Show($"File not found:\n{filePath}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
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
            // Notepad++ not found in PATH
            MessageBox.Show("Notepad++ is not installed or not found in the system PATH.\n\n" +
                            "Please install Notepad++ or add it to your PATH environment variable.",
                "Notepad++ Not Found",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening file in Notepad++:\n{ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenFileInExplorer(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                // Opens explorer and selects the file
                Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            else
                MessageBox.Show($"File not found:\n{filePath}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening explorer:\n{ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void FindInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement border } })
            // Border.DataContext = MatchLine
            // Border.Tag = SearchResult
            if (border is { DataContext: MatchLine matchLine, Tag: SearchResult searchResult })
                OpenFileInExplorer(searchResult.FilePath);
        
       
    }
}