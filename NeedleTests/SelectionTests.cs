using System.ComponentModel;
using System.IO.Compression;
using Needle.Models;
using Needle.Services;
using NUnit.Framework;

namespace NeedleTests;

/// <summary>
///     Only selected matches are replaced. A file is selected if all its matches are selected.
/// </summary>
[TestFixture]
public class SelectionTests : TempDirectoryTestBase
{
    [Test]
    public async Task All_matches_are_selected_initially()
    {
        CreateFile("file.txt", "foo foo\nfoo");

        var result = (await SearchAsync("foo")).Single();

        Assert.That(result.IsSelected, Is.True);
        Assert.That(result.Matches.All(m => m.IsSelected), Is.True);
    }

    [Test]
    public async Task File_is_undetermined_if_some_matches_are_selected()
    {
        CreateFile("file.txt", "foo foo\nfoo");
        var result = (await SearchAsync("foo")).Single();
        var changed = RecordPropertyChanges(result);

        result.Matches[1].IsSelected = false;

        Assert.That(result.IsSelected, Is.Null);
        Assert.That(changed, Does.Contain(nameof(SearchResult.IsSelected)), "The file check box is updated");

        result.Matches[0].IsSelected = false;
        result.Matches[2].IsSelected = false;

        Assert.That(result.IsSelected, Is.False);
    }

    [Test]
    public async Task Selecting_the_file_selects_all_matches()
    {
        CreateFile("file.txt", "foo foo\nfoo");
        var result = (await SearchAsync("foo")).Single();
        var changedMatches = result.Matches.Select(RecordPropertyChanges).ToList();

        result.IsSelected = false;

        Assert.That(result.Matches.All(m => !m.IsSelected), Is.True);
        Assert.That(changedMatches.All(c => c.Contains(nameof(MatchLine.IsSelected))), Is.True,
            "The match check boxes are updated");

        // A click on an undetermined check box selects all.
        result.Matches[0].IsSelected = true;
        result.IsSelected = null;

        Assert.That(result.Matches.All(m => m.IsSelected), Is.True);
    }

    [Test]
    public async Task Deselected_file_is_not_replaced()
    {
        CreateFile("selected.txt", "foo");
        CreateFile("deselected.txt", "foo");
        var results = await SearchAsync("foo");

        results.Single(r => r.FilePath.EndsWith("deselected.txt")).IsSelected = false;
        var replaced = await ReplaceAsync(results, "bar");

        Assert.That(replaced.FilesModified, Is.EqualTo(1));
        Assert.That(File.ReadAllText(PathOf("selected.txt")), Is.EqualTo("bar"));
        Assert.That(File.ReadAllText(PathOf("deselected.txt")), Is.EqualTo("foo"));
    }

    [Test]
    public async Task Only_selected_matches_in_a_line_are_replaced()
    {
        CreateFile("file.txt", "foo foo foo");
        var result = (await SearchAsync("foo")).Single();

        result.Matches[1].IsSelected = false;
        await ReplaceAsync([result], "bar");

        Assert.That(File.ReadAllText(PathOf("file.txt")), Is.EqualTo("bar foo bar"));
    }

    [Test]
    public async Task Matches_in_archives_cannot_be_replaced()
    {
        using (var archive = ZipFile.Open(PathOf("archive.zip"), ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("inside.txt").Open()))
        {
            writer.Write("foo");
        }

        CreateFile("file.txt", "foo");

        var results = await SearchAsync("foo", fileMasks: "*.txt;*.zip");

        var archiveResult = results.Single(r => r.IsArchive);
        Assert.That(archiveResult.CanReplace, Is.False);
        Assert.That(archiveResult.Matches.Single().CanReplace, Is.False);

        var fileResult = results.Single(r => !r.IsArchive);
        Assert.That(fileResult.CanReplace, Is.True);
        Assert.That(fileResult.Matches.Single().CanReplace, Is.True);
    }

    private static List<string?> RecordPropertyChanges(INotifyPropertyChanged source)
    {
        var changed = new List<string?>();
        source.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        return changed;
    }
}
