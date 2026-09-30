using Needle.Services;
using NUnit.Framework;

namespace NeedleTests;

[TestFixture]
public class FileNameTests : TempDirectoryTestBase
{
    [Test]
    public async Task FileName_scope_finds_matches_in_file_name_only()
    {
        CreateFile("report.txt", "report in content");
        CreateFile("other.txt", "report in content");

        var results = await SearchAsync("report", SearchScope.FileName);

        Assert.That(results, Has.Count.EqualTo(1));
        var result = results[0];
        Assert.That(Path.GetFileName(result.FilePath), Is.EqualTo("report.txt"));
        Assert.That(result.Matches, Has.Count.EqualTo(1));
        Assert.That(result.Matches[0].IsFileName, Is.True);
        Assert.That(result.Matches[0].Text, Is.EqualTo("report.txt"));
        Assert.That(result.Matches[0].StartIndex, Is.EqualTo(0));
        Assert.That(result.Matches[0].Length, Is.EqualTo(6));
    }

    [Test]
    public async Task Content_scope_ignores_file_name()
    {
        CreateFile("report.txt", "nothing here");

        var results = await SearchAsync("report", SearchScope.Content);

        Assert.That(results, Is.Empty);
    }

    [Test]
    public async Task Both_scope_combines_name_and_content_matches_in_one_result()
    {
        CreateFile("report.txt", "first report\nsecond report");
        CreateFile("name-only-report.txt", "nothing here");
        CreateFile("content-only.txt", "a report");

        var results = await SearchAsync("report", SearchScope.Both);
        var byName = results.ToDictionary(r => Path.GetFileName(r.FilePath));

        Assert.That(byName.Keys, Is.EquivalentTo(new[] { "report.txt", "name-only-report.txt", "content-only.txt" }));

        var both = byName["report.txt"];
        Assert.That(both.MatchCount, Is.EqualTo(3));
        Assert.That(both.Matches[0].IsFileName, Is.True, "File name match is listed first");
        Assert.That(both.Matches.Skip(1).Select(m => m.LineNumber), Is.EqualTo(new[] { 1, 2 }));
        Assert.That(both.Matches.Skip(1).All(m => !m.IsFileName), Is.True);

        Assert.That(byName["name-only-report.txt"].Matches.Single().IsFileName, Is.True);
        Assert.That(byName["content-only.txt"].Matches.Single().IsFileName, Is.False);
    }

    [Test]
    public async Task Replace_renames_file()
    {
        CreateFile("old-name.txt", "content");

        var results = await SearchAsync("old", SearchScope.FileName);
        var replaced = await ReplaceAsync(results, "new");

        Assert.That(replaced.Success, Is.True, string.Join("\n", replaced.Errors));
        Assert.That(replaced.FilesModified, Is.EqualTo(1));
        Assert.That(replaced.TotalReplacements, Is.EqualTo(1));
        Assert.That(FileNames(), Is.EquivalentTo(new[] { "new-name.txt" }));
        Assert.That(File.ReadAllText(PathOf("new-name.txt")), Is.EqualTo("content"));
    }

    [Test]
    public async Task Replace_renames_all_occurrences_in_file_name()
    {
        CreateFile("ab-ab-ab.txt", "content");

        var results = await SearchAsync("ab", SearchScope.FileName);
        var replaced = await ReplaceAsync(results, "xyz");

        Assert.That(replaced.TotalReplacements, Is.EqualTo(3));
        Assert.That(FileNames(), Is.EquivalentTo(new[] { "xyz-xyz-xyz.txt" }));
    }

    [Test]
    public async Task Replace_with_regex_supports_capture_groups_in_file_name()
    {
        CreateFile("img_2024_05.png", "content");

        var results = await SearchAsync(@"img_(\d+)_(\d+)", SearchScope.FileName, isRegex: true);
        var replaced = await ReplaceAsync(results, "photo-$2-$1");

        Assert.That(replaced.Success, Is.True, string.Join("\n", replaced.Errors));
        Assert.That(FileNames(), Is.EquivalentTo(new[] { "photo-05-2024.png" }));
    }

    [Test]
    public async Task Replace_in_both_scope_changes_content_and_renames_file()
    {
        CreateFile("foo.txt", "foo line\nother line\nfoo again");

        var results = await SearchAsync("foo", SearchScope.Both);
        var replaced = await ReplaceAsync(results, "bar");

        Assert.That(replaced.Success, Is.True, string.Join("\n", replaced.Errors));
        Assert.That(replaced.FilesModified, Is.EqualTo(1));
        Assert.That(replaced.TotalReplacements, Is.EqualTo(3));
        Assert.That(FileNames(), Is.EquivalentTo(new[] { "bar.txt" }));
        Assert.That(File.ReadAllLines(PathOf("bar.txt")), Is.EqualTo(new[] { "bar line", "other line", "bar again" }));
    }

    [Test]
    public async Task Replace_does_not_overwrite_existing_file()
    {
        CreateFile("a.txt", "from a");
        CreateFile("b.txt", "from b");

        var results = await SearchAsync("a.txt", SearchScope.FileName);
        var replaced = await ReplaceAsync(results, "b.txt");

        Assert.That(replaced.Success, Is.False);
        Assert.That(replaced.Errors, Has.Count.EqualTo(1));
        Assert.That(FileNames(), Is.EquivalentTo(new[] { "a.txt", "b.txt" }));
        Assert.That(File.ReadAllText(PathOf("b.txt")), Is.EqualTo("from b"));
    }

    [Test]
    public async Task Replace_allows_case_only_rename()
    {
        CreateFile("readme.txt", "content");

        var results = await SearchAsync("readme", SearchScope.FileName, isCaseSensitive: true);
        var replaced = await ReplaceAsync(results, "README");

        Assert.That(replaced.Success, Is.True, string.Join("\n", replaced.Errors));
        Assert.That(FileNames(), Is.EquivalentTo(new[] { "README.txt" }));
    }

    [Test]
    public async Task Replace_rejects_invalid_file_name()
    {
        CreateFile("name.txt", "content");

        var results = await SearchAsync("name", SearchScope.FileName);
        var replaced = await ReplaceAsync(results, "in/valid");

        Assert.That(replaced.Success, Is.False);
        Assert.That(FileNames(), Is.EquivalentTo(new[] { "name.txt" }));
    }

    [Test]
    public async Task Replace_skips_deselected_file_name_match()
    {
        CreateFile("foo.txt", "foo");

        var results = await SearchAsync("foo", SearchScope.Both);
        results.Single().Matches.Single(m => m.IsFileName).IsSelected = false;
        var replaced = await ReplaceAsync(results, "bar");

        Assert.That(replaced.TotalReplacements, Is.EqualTo(1));
        Assert.That(FileNames(), Is.EquivalentTo(new[] { "foo.txt" }));
        Assert.That(File.ReadAllText(PathOf("foo.txt")).TrimEnd(), Is.EqualTo("bar"));
    }
}
