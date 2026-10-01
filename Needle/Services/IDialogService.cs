namespace Needle.Services;

/// <summary>
///     Message boxes, so the view model can be tested without UI.
/// </summary>
public interface IDialogService
{
    bool Confirm(string message, string title);
    void ShowWarning(string message, string title);
    void ShowError(string message, string title);
}
