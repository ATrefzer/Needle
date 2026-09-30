using Needle.Services;
using NUnit.Framework;

namespace NeedleTests;

/// <summary>
///     Replacing uses the positions found by the search. If the file was modified in the meantime,
///     nothing must be replaced.
/// </summary>
[TestFixture]
public class ReplaceSafetyTests : TempDirectoryTestBase
{
    [Test]
    public async Task Replace_fails_if_line_was_inserted_after_search()
    {
        CreateFile("file.txt", "foo");
        var results = await SearchAsync("foo");

        CreateFile("file.txt", "inserted\nfoo");
        var replaced = await ReplaceAsync(results, "bar");

        AssertFileChangedError(replaced);
        Assert.That(File.ReadAllText(PathOf("file.txt")), Is.EqualTo("inserted\nfoo"));
    }

    [Test]
    public async Task Replace_fails_if_text_was_shifted_within_line_after_search()
    {
        CreateFile("file.txt", "a foo and foo");
        var results = await SearchAsync("foo");

        CreateFile("file.txt", "xx a foo and foo");
        var replaced = await ReplaceAsync(results, "bar");

        AssertFileChangedError(replaced);
        Assert.That(File.ReadAllText(PathOf("file.txt")), Is.EqualTo("xx a foo and foo"));
    }

    [Test]
    public async Task Replace_fails_if_lines_were_removed_after_search()
    {
        CreateFile("file.txt", "one\ntwo\nfoo");
        var results = await SearchAsync("foo");

        CreateFile("file.txt", "foo");
        var replaced = await ReplaceAsync(results, "bar");

        AssertFileChangedError(replaced);
        Assert.That(File.ReadAllText(PathOf("file.txt")), Is.EqualTo("foo"));
    }

    [Test]
    public async Task Replace_fails_if_regex_match_changed_after_search()
    {
        CreateFile("file.txt", "id=123");
        var results = await SearchAsync(@"id=\d+", isRegex: true);

        CreateFile("file.txt", "id=1234");
        var replaced = await ReplaceAsync(results, "id=0");

        AssertFileChangedError(replaced);
        Assert.That(File.ReadAllText(PathOf("file.txt")), Is.EqualTo("id=1234"));
    }

    [Test]
    public async Task Unchanged_files_are_replaced_even_if_other_file_changed()
    {
        CreateFile("changed.txt", "foo");
        CreateFile("unchanged.txt", "foo");
        var results = await SearchAsync("foo");

        CreateFile("changed.txt", "xfoo");
        var replaced = await ReplaceAsync(results, "bar");

        Assert.That(replaced.Errors, Has.Count.EqualTo(1));
        Assert.That(replaced.FilesModified, Is.EqualTo(1));
        Assert.That(File.ReadAllText(PathOf("changed.txt")), Is.EqualTo("xfoo"));
        Assert.That(File.ReadAllText(PathOf("unchanged.txt")).TrimEnd(), Is.EqualTo("bar"));
    }

    [Test]
    public async Task Content_is_not_replaced_and_file_not_renamed_if_content_changed()
    {
        CreateFile("foo.txt", "foo");
        var results = await SearchAsync("foo", SearchScope.Both);

        CreateFile("foo.txt", "changed foo");
        var replaced = await ReplaceAsync(results, "bar");

        AssertFileChangedError(replaced);
        Assert.That(FileNames(), Is.EquivalentTo(new[] { "foo.txt" }));
        Assert.That(File.ReadAllText(PathOf("foo.txt")), Is.EqualTo("changed foo"));
    }

    [Test]
    public async Task Rename_fails_if_file_was_renamed_after_search()
    {
        CreateFile("foo.txt", "content");
        var results = await SearchAsync("foo", SearchScope.FileName);

        File.Move(PathOf("foo.txt"), PathOf("other.txt"));
        var replaced = await ReplaceAsync(results, "bar");

        Assert.That(replaced.Success, Is.False);
        Assert.That(FileNames(), Is.EquivalentTo(new[] { "other.txt" }));
    }

    [Test]
    public async Task Regex_with_lookbehind_is_replaced()
    {
        // The lookbehind needs the text before the match. It is lost if only the matched part is searched again.
        CreateFile("file.txt", "ab cb ab");
        var results = await SearchAsync("(?<=a)b", isRegex: true);

        var replaced = await ReplaceAsync(results, "X");

        Assert.That(replaced.Success, Is.True, string.Join("\n", replaced.Errors));
        Assert.That(replaced.TotalReplacements, Is.EqualTo(2));
        Assert.That(File.ReadAllText(PathOf("file.txt")).TrimEnd(), Is.EqualTo("aX cb aX"));
    }

    [Test]
    public async Task Regex_with_word_boundary_is_replaced()
    {
        CreateFile("file.txt", "cat concat cat");
        var results = await SearchAsync(@"\bcat\b", isRegex: true);

        var replaced = await ReplaceAsync(results, "dog");

        Assert.That(replaced.Success, Is.True, string.Join("\n", replaced.Errors));
        Assert.That(File.ReadAllText(PathOf("file.txt")).TrimEnd(), Is.EqualTo("dog concat dog"));
    }

    private static void AssertFileChangedError(ReplaceResult replaced)
    {
        Assert.That(replaced.Success, Is.False);
        Assert.That(replaced.FilesModified, Is.EqualTo(0));
        Assert.That(replaced.Errors.Single(), Does.Contain(new FileChangedException().Message));
    }
}
