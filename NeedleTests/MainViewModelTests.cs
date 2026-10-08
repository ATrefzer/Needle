using Needle.Core.Tests;
using Needle.Resources;
using Needle.Services;
using Needle.ViewModels;
using NUnit.Framework;

namespace NeedleTests;

[TestFixture]
public class MainViewModelTests : TempDirectoryTestBase
{
    private FakeDialogService _dialogs = null!;
    private MainViewModel _viewModel = null!;

    [SetUp]
    public void CreateViewModel()
    {
        _dialogs = new FakeDialogService();

        // Settings that are not loaded from a file are never saved, so the user's settings are not touched.
        _viewModel = new MainViewModel(new UserSettings(), () => new FileSearchService(), new FileReplaceService(),
            _dialogs)
        {
            StartDirectory = Directory,
            FileMasks = "*.txt",
            IncludeSubdirectories = false
        };
    }

    [Test]
    public async Task Search_shows_results()
    {
        CreateFile("a.txt", "foo");
        CreateFile("b.txt", "bar");
        _viewModel.Pattern = "foo";

        await _viewModel.SearchAsync();

        Assert.That(_viewModel.Results.Select(r => r.FileName), Is.EqualTo(new[] { "a.txt" }));
        Assert.That(_viewModel.StatusMessage, Is.EqualTo(Strings.Status_Finished));
        Assert.That(_viewModel.IsBusy, Is.False);
    }

    [Test]
    public async Task Invalid_regex_is_shown_in_status()
    {
        CreateFile("a.txt", "foo");
        _viewModel.Pattern = "(foo";
        _viewModel.IsRegex = true;

        await _viewModel.SearchAsync();

        Assert.That(_viewModel.StatusMessage, Does.StartWith(string.Format(Strings.Status_InvalidRegex, "")));
        Assert.That(_viewModel.Results, Is.Empty);
        Assert.That(_viewModel.IsBusy, Is.False);
    }

    [Test]
    public async Task Skipped_files_are_shown_in_status()
    {
        CreateFile("locked.txt", "foo");
        _viewModel.Pattern = "foo";

        using (new FileStream(PathOf("locked.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await _viewModel.SearchAsync();
        }

        Assert.That(_viewModel.StatusMessage,
            Is.EqualTo(Strings.Status_Finished + string.Format(Strings.Status_Skipped, 1, 0)));
    }

    [Test]
    public async Task File_mask_is_added_to_history()
    {
        _viewModel.Pattern = "foo";
        _viewModel.FileMasks = "*.cs";
        await _viewModel.SearchAsync();
        _viewModel.FileMasks = "*.txt";
        await _viewModel.SearchAsync();
        _viewModel.FileMasks = "*.cs";
        await _viewModel.SearchAsync();

        Assert.That(_viewModel.FileMasksHistory, Is.EqualTo(new[] { "*.cs", "*.txt" }));
    }

    [Test]
    public async Task Replace_clears_results()
    {
        CreateFile("a.txt", "foo");
        _viewModel.Pattern = "foo";
        _viewModel.ReplacementText = "bar";
        await _viewModel.SearchAsync();

        await _viewModel.ReplaceAsync();

        Assert.That(File.ReadAllText(PathOf("a.txt")), Is.EqualTo("bar"));
        Assert.That(_viewModel.Results, Is.Empty);
        Assert.That(_viewModel.StatusMessage, Is.EqualTo(string.Format(Strings.Status_Replaced, 1, 1)));
        Assert.That(_dialogs.Confirmations.Single(), Is.EqualTo(string.Format(Strings.Msg_ConfirmReplace, 1)));
    }

    [Test]
    public async Task Confirmation_mentions_renames()
    {
        CreateFile("foo.txt", "foo");
        _viewModel.Pattern = "foo";
        _viewModel.SearchScope = SearchScope.Both;
        await _viewModel.SearchAsync();
        _dialogs.ConfirmResult = false;

        await _viewModel.ReplaceAsync();

        Assert.That(_dialogs.Confirmations.Single(),
            Is.EqualTo(string.Format(Strings.Msg_ConfirmReplaceAndRename, 1, 1)));
    }

    [Test]
    public async Task Nothing_is_replaced_if_not_confirmed()
    {
        CreateFile("a.txt", "foo");
        _viewModel.Pattern = "foo";
        _viewModel.ReplacementText = "bar";
        await _viewModel.SearchAsync();
        _dialogs.ConfirmResult = false;

        await _viewModel.ReplaceAsync();

        Assert.That(File.ReadAllText(PathOf("a.txt")), Is.EqualTo("foo"));
        Assert.That(_viewModel.Results, Has.Count.EqualTo(1), "The result is still valid");
    }

    [Test]
    public async Task Nothing_selected_is_not_confirmed()
    {
        CreateFile("a.txt", "foo");
        _viewModel.Pattern = "foo";
        await _viewModel.SearchAsync();
        _viewModel.Results.Single().IsSelected = false;

        await _viewModel.ReplaceAsync();

        Assert.That(_dialogs.Confirmations, Is.Empty);
        Assert.That(_viewModel.StatusMessage, Is.EqualTo(Strings.Status_NothingSelected));
    }

    [Test]
    public async Task Replace_errors_are_shown_and_results_cleared()
    {
        CreateFile("a.txt", "foo");
        _viewModel.Pattern = "foo";
        await _viewModel.SearchAsync();
        CreateFile("a.txt", "changed foo");

        await _viewModel.ReplaceAsync();

        Assert.That(_dialogs.Warnings.Single(), Does.Contain(new FileChangedException().Message));
        Assert.That(_viewModel.StatusMessage, Is.EqualTo(string.Format(Strings.Status_ReplacedWithErrors, 0, 1)));
        Assert.That(_viewModel.Results, Is.Empty);
    }

    private sealed class FakeDialogService : IDialogService
    {
        public bool ConfirmResult { get; set; } = true;
        public List<string> Confirmations { get; } = [];
        public List<string> Warnings { get; } = [];
        public List<string> Errors { get; } = [];

        public bool Confirm(string message, string title)
        {
            Confirmations.Add(message);
            return ConfirmResult;
        }

        public void ShowWarning(string message, string title)
        {
            Warnings.Add(message);
        }

        public void ShowError(string message, string title)
        {
            Errors.Add(message);
        }
    }
}
