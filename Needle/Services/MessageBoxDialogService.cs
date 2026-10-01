using System.Windows;

namespace Needle.Services;

public class MessageBoxDialogService : IDialogService
{
    public bool Confirm(string message, string title)
    {
        return Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    public void ShowWarning(string message, string title)
    {
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public void ShowError(string message, string title)
    {
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static MessageBoxResult Show(string message, string title, MessageBoxButton button, MessageBoxImage image)
    {
        var owner = Application.Current?.MainWindow;
        return owner != null
            ? MessageBox.Show(owner, message, title, button, image)
            : MessageBox.Show(message, title, button, image);
    }
}
