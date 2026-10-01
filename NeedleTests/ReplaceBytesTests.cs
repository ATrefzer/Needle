using System.Text;
using Needle.Services;
using NUnit.Framework;

namespace NeedleTests;

/// <summary>
///     Replacing modifies only the matched text. All other bytes, including line breaks, stay unchanged.
/// </summary>
[TestFixture]
public class ReplaceBytesTests : TempDirectoryTestBase
{
    // Same as the buffer size of the LineRewriter, to test line breaks at the buffer boundary.
    private const int BufferSize = 81920;

    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    [TestCase("a foo\nb\r\nc foo\rd foo", "a bar\nb\r\nc bar\rd bar", TestName = "Mixed line breaks")]
    [TestCase("foo\n", "bar\n", TestName = "Line break at end")]
    [TestCase("foo", "bar", TestName = "No line break at end")]
    [TestCase("\r\r\n\nfoo\r", "\r\r\n\nbar\r", TestName = "Empty lines")]
    [TestCase("x\rfoo\r\n", "x\rbar\r\n", TestName = "Single CR counts as line break")]
    [TestCase("Grüße foo 😀\nfoo", "Grüße bar 😀\nbar", TestName = "Multibyte characters")]
    public async Task Only_matched_text_is_replaced(string original, string expected)
    {
        File.WriteAllBytes(PathOf("file.txt"), Utf8.GetBytes(original));

        var replaced = await ReplaceAsync(await SearchAsync("foo"), "bar");

        Assert.That(replaced.Success, Is.True, string.Join("\n", replaced.Errors));
        Assert.That(File.ReadAllBytes(PathOf("file.txt")), Is.EqualTo(Utf8.GetBytes(expected)));
    }

    private static IEnumerable<Encoding> MultiByteUnitEncodings()
    {
        yield return Encoding.Unicode;
        yield return Encoding.BigEndianUnicode;
        yield return Encoding.UTF32;
        yield return new UTF32Encoding(true, true);
    }

    [TestCaseSource(nameof(MultiByteUnitEncodings))]
    public async Task Line_breaks_are_preserved_in_utf16_and_utf32(Encoding encoding)
    {
        // U+0A0D and U+0D0A contain the bytes of CR and LF, but are no line breaks.
        const string original = "a foo\n਍ഊ\r\nc foo\rd foo";
        File.WriteAllBytes(PathOf("file.txt"), [.. encoding.GetPreamble(), .. encoding.GetBytes(original)]);

        var results = await SearchAsync("foo");
        Assert.That(results.Single().Matches.Select(m => m.LineNumber), Is.EqualTo(new[] { 1, 3, 4 }));

        var replaced = await ReplaceAsync(results, "bar");

        Assert.That(replaced.Success, Is.True, string.Join("\n", replaced.Errors));
        Assert.That(File.ReadAllBytes(PathOf("file.txt")),
            Is.EqualTo((byte[])[.. encoding.GetPreamble(), .. encoding.GetBytes(original.Replace("foo", "bar"))]));
    }

    [TestCase("\r\n", TestName = "CR LF split by buffer boundary")]
    [TestCase("\r", TestName = "Single CR at buffer boundary")]
    [TestCase("\n", TestName = "LF at buffer boundary")]
    public async Task Line_break_at_buffer_boundary(string lineBreak)
    {
        // The first character of the line break is the last byte of the first buffer.
        var original = new string('x', BufferSize - 1) + lineBreak + "foo" + lineBreak + "foo";
        File.WriteAllBytes(PathOf("file.txt"), Utf8.GetBytes(original));

        var results = await SearchAsync("foo");
        Assert.That(results.Single().Matches.Select(m => m.LineNumber), Is.EqualTo(new[] { 2, 3 }));

        var replaced = await ReplaceAsync(results, "bar");

        Assert.That(replaced.Success, Is.True, string.Join("\n", replaced.Errors));
        Assert.That(File.ReadAllBytes(PathOf("file.txt")), Is.EqualTo(Utf8.GetBytes(original.Replace("foo", "bar"))));
    }

    [Test]
    public async Task Matched_line_spanning_several_buffers_is_replaced()
    {
        var original = "start\n" + new string('x', BufferSize - 10) + "foo" + new string('y', 3 * BufferSize) +
                       "foo\nend";
        File.WriteAllBytes(PathOf("file.txt"), Utf8.GetBytes(original));

        var replaced = await ReplaceAsync(await SearchAsync("foo"), "bar");

        Assert.That(replaced.TotalReplacements, Is.EqualTo(2), string.Join("\n", replaced.Errors));
        Assert.That(File.ReadAllBytes(PathOf("file.txt")), Is.EqualTo(Utf8.GetBytes(original.Replace("foo", "bar"))));
    }

    [Test]
    public async Task Large_file_is_replaced()
    {
        var builder = new StringBuilder();
        for (var i = 0; i < 200_000; i++)
        {
            builder.Append("Line ").Append(i).Append(i % 2 == 0 ? "\n" : "\r\n");
        }

        builder.Append("foo");
        var original = builder.ToString();
        File.WriteAllBytes(PathOf("file.txt"), Utf8.GetBytes(original));

        var replaced = await ReplaceAsync(await SearchAsync("foo"), "bar");

        Assert.That(replaced.Success, Is.True, string.Join("\n", replaced.Errors));
        Assert.That(File.ReadAllBytes(PathOf("file.txt")), Is.EqualTo(Utf8.GetBytes(original.Replace("foo", "bar"))));
    }

    [Test]
    public async Task Binary_file_keeps_all_other_bytes()
    {
        byte[] original = [0x00, 0xFF, 0x0A, 0x0D, 0x0D, 0x0A, 0x80, .. "Version 1.0"u8, 0x00, 0x0D, 0x9D, 0x81, 0x0A, 0xFE];
        File.WriteAllBytes(PathOf("file.bin"), original);

        var results = await SearchAsync("Version 1.0", encodingWithoutBom: FileSearchService.AnsiEncoding);
        var replaced = await ReplaceAsync(results, "Version 2.0");

        Assert.That(replaced.Success, Is.True, string.Join("\n", replaced.Errors));
        byte[] expected = [0x00, 0xFF, 0x0A, 0x0D, 0x0D, 0x0A, 0x80, .. "Version 2.0"u8, 0x00, 0x0D, 0x9D, 0x81, 0x0A, 0xFE];
        Assert.That(File.ReadAllBytes(PathOf("file.bin")), Is.EqualTo(expected));
    }

    [Test]
    public async Task Binary_file_searched_as_utf8_is_not_replaced()
    {
        byte[] original = [0xFF, 0x80, .. "Version 1.0"u8, 0xC3];
        File.WriteAllBytes(PathOf("file.bin"), original);

        var results = await SearchAsync("Version 1.0", encodingWithoutBom: Utf8);
        var replaced = await ReplaceAsync(results, "Version 2.0");

        Assert.That(replaced.Success, Is.False);
        Assert.That(File.ReadAllBytes(PathOf("file.bin")), Is.EqualTo(original));
    }

    [Test]
    public async Task No_temporary_file_is_left_behind()
    {
        CreateFile("ok.txt", "foo");
        CreateFile("changed.txt", "foo");
        var results = await SearchAsync("foo");

        CreateFile("changed.txt", "xfoo");
        var replaced = await ReplaceAsync(results, "bar");

        Assert.That(replaced.Errors, Has.Count.EqualTo(1));
        Assert.That(FileNames(), Is.EquivalentTo(new[] { "ok.txt", "changed.txt" }));
    }

    [Test]
    public async Task File_attributes_are_preserved()
    {
        CreateFile("file.txt", "foo");
        File.SetAttributes(PathOf("file.txt"), FileAttributes.Hidden);

        var replaced = await ReplaceAsync(await SearchAsync("foo"), "bar");

        Assert.That(replaced.Success, Is.True, string.Join("\n", replaced.Errors));
        Assert.That(File.GetAttributes(PathOf("file.txt")).HasFlag(FileAttributes.Hidden), Is.True);
        Assert.That(File.ReadAllText(PathOf("file.txt")), Is.EqualTo("bar"));
    }
}
