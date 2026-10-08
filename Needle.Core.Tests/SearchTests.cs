using System.IO.Compression;
using NUnit.Framework;

namespace Needle.Core.Tests;

[TestFixture]
public class SearchTests : TempDirectoryTestBase
{
    [Test]
    public async Task Subdirectories_are_searched_if_enabled()
    {
        CreateFile("top.txt", "foo");
        CreateFile(Path.Combine("a", "nested.txt"), "foo");
        CreateFile(Path.Combine("a", "b", "deep.txt"), "foo");

        var results = await SearchAsync("foo", includeSubdirectories: true);

        Assert.That(results.Select(r => Path.GetFileName(r.FilePath)),
            Is.EquivalentTo(new[] { "top.txt", "nested.txt", "deep.txt" }));
    }

    [Test]
    public async Task Subdirectories_are_ignored_if_disabled()
    {
        CreateFile("top.txt", "foo");
        CreateFile(Path.Combine("a", "nested.txt"), "foo");

        var results = await SearchAsync("foo", includeSubdirectories: false);

        Assert.That(results.Select(r => Path.GetFileName(r.FilePath)), Is.EquivalentTo(new[] { "top.txt" }));
    }

    [Test]
    public async Task Git_directory_is_skipped_but_other_dot_directories_are_searched()
    {
        CreateFile(Path.Combine(".git", "config.txt"), "foo");
        CreateFile(Path.Combine(".github", "workflow.txt"), "foo");
        CreateFile(Path.Combine("src", "code.txt"), "foo");

        var results = await SearchAsync("foo", includeSubdirectories: true);

        Assert.That(results.Select(r => Path.GetFileName(r.FilePath)),
            Is.EquivalentTo(new[] { "workflow.txt", "code.txt" }));
    }

    [Test]
    public async Task Linked_directories_are_not_followed()
    {
        CreateFile(Path.Combine("a", "file.txt"), "foo");
        try
        {
            // A link to the parent directory would be an endless loop.
            System.IO.Directory.CreateSymbolicLink(PathOf(Path.Combine("a", "loop")), Directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Ignore("Creating symbolic links requires the developer mode on Windows.");
        }

        var results = await SearchAsync("foo", includeSubdirectories: true);

        Assert.That(results.Select(r => r.FilePath), Is.EquivalentTo(new[] { PathOf(Path.Combine("a", "file.txt")) }));
    }

    [Test]
    [Platform("Win", Reason = "Hidden and system are file attributes on Windows only.")]
    public async Task System_and_hidden_files_are_searched()
    {
        CreateFile("system.txt", "foo");
        CreateFile("hidden.txt", "foo");
        File.SetAttributes(PathOf("system.txt"), FileAttributes.System);
        File.SetAttributes(PathOf("hidden.txt"), FileAttributes.Hidden);

        var results = await SearchAsync("foo");

        Assert.That(results.Select(r => Path.GetFileName(r.FilePath)),
            Is.EquivalentTo(new[] { "system.txt", "hidden.txt" }));
    }

    [Test]
    public async Task Star_matches_all_files_on_all_platforms()
    {
        CreateFile("code.cs", "foo");
        CreateFile("Makefile", "foo");
        CreateFile(".bashrc", "foo");

        var results = await SearchAsync("foo", fileMasks: "*");

        Assert.That(results.Select(r => r.FileName), Is.EquivalentTo(new[] { "code.cs", "Makefile", ".bashrc" }));
    }

    [Test]
    public async Task Star_dot_star_requires_a_dot()
    {
        CreateFile("code.cs", "foo");
        CreateFile("Makefile", "foo");

        var results = await SearchAsync("foo", fileMasks: "*.*");

        Assert.That(results.Select(r => r.FileName), Is.EquivalentTo(new[] { "code.cs" }));
    }

    [Test]
    public async Task File_masks_are_applied_to_files_and_zip_entries()
    {
        CreateFile("match.txt", "foo");
        CreateFile("other.cs", "foo");
        CreateZip("archive.zip", ("inside.txt", "foo"), ("inside.cs", "foo"));
        CreateZip("second.zip", ("again.txt", "foo"));

        var results = await SearchAsync("foo", fileMasks: "*.txt;*.zip");

        Assert.That(results.Select(r => r.FileName),
            Is.EquivalentTo(new[] { "match.txt", "archive.zip/inside.txt", "second.zip/again.txt" }));
    }

    [Test]
    public async Task Locked_file_is_skipped_and_counted()
    {
        CreateFile("locked.txt", "foo");
        CreateFile("readable.txt", "foo");

        List<Needle.Models.SearchResult> results;
        using (new FileStream(PathOf("locked.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            results = await SearchAsync("foo");
        }

        Assert.That(results.Select(r => Path.GetFileName(r.FilePath)), Is.EquivalentTo(new[] { "readable.txt" }));
        Assert.That(LastSearchService!.SkippedFiles, Is.EqualTo(1));
        Assert.That(LastSearchService.SkippedDirectories, Is.EqualTo(0));
    }

    [Test]
    public async Task Broken_zip_is_skipped_and_counted()
    {
        CreateFile("broken.zip", "this is not a zip archive");
        CreateZip("valid.zip", ("inside.txt", "foo"));

        var results = await SearchAsync("foo", fileMasks: "*.txt;*.zip");

        Assert.That(results.Select(r => r.FileName), Is.EquivalentTo(new[] { "valid.zip/inside.txt" }));
        Assert.That(LastSearchService!.SkippedFiles, Is.EqualTo(1));
    }

    [Test]
    public async Task Nothing_is_skipped_normally()
    {
        CreateFile("file.txt", "foo");

        await SearchAsync("foo");

        Assert.That(LastSearchService!.SkippedFiles, Is.EqualTo(0));
        Assert.That(LastSearchService.SkippedDirectories, Is.EqualTo(0));
    }

    private void CreateZip(string name, params (string Entry, string Content)[] entries)
    {
        using var archive = ZipFile.Open(PathOf(name), ZipArchiveMode.Create);
        foreach (var (entry, content) in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(entry).Open());
            writer.Write(content);
        }
    }
}
