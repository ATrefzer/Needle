using System.Text;
using Needle.Services;
using NUnit.Framework;

namespace Needle.Core.Tests;

/// <summary>
///     Files without BOM are read with the selected encoding (UTF-8 or the system's ANSI code page).
///     Replacing must never corrupt a file, even if the wrong encoding was selected.
///     The tests assume a western system (Windows-1252).
/// </summary>
[TestFixture]
public class EncodingWithoutBomTests : TempDirectoryTestBase
{
    private static readonly Encoding Ansi = FileSearchService.AnsiEncoding;
    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    [Test]
    public async Task Non_ascii_characters_in_ansi_file_are_preserved_on_replace()
    {
        File.WriteAllBytes(PathOf("file.txt"), Ansi.GetBytes("Grüße byte\r\nStraße €\r\n"));

        var results = await SearchAsync("byte", encodingWithoutBom: Ansi);
        Assert.That(results.Single().Encoding.CodePage, Is.EqualTo(Ansi.CodePage));

        var replaced = await ReplaceAsync(results, "foo");

        Assert.That(replaced.Success, Is.True, string.Join("\n", replaced.Errors));
        Assert.That(File.ReadAllBytes(PathOf("file.txt")), Is.EqualTo(Ansi.GetBytes("Grüße foo\r\nStraße €\r\n")));
    }

    [Test]
    public async Task Non_ascii_characters_in_ansi_file_can_be_searched()
    {
        File.WriteAllBytes(PathOf("file.txt"), Ansi.GetBytes("Straße"));

        var results = await SearchAsync("ß", encodingWithoutBom: Ansi);

        Assert.That(results.Single().Matches.Single().StartIndex, Is.EqualTo(4));
    }

    [Test]
    public async Task All_bytes_of_ansi_file_survive_replace()
    {
        // Every byte above ASCII, including those that are undefined in Windows-1252.
        var nonAscii = Enumerable.Range(0x80, 0x80).Select(b => (byte)b).ToArray();
        File.WriteAllBytes(PathOf("file.bin"), [.. "byte "u8, .. nonAscii]);

        var results = await SearchAsync("byte", encodingWithoutBom: Ansi);
        var replaced = await ReplaceAsync(results, "foo");

        Assert.That(replaced.Success, Is.True, string.Join("\n", replaced.Errors));
        Assert.That(File.ReadAllBytes(PathOf("file.bin")), Is.EqualTo((byte[])[.. "foo "u8, .. nonAscii]));
    }

    [Test]
    public async Task Ansi_file_can_be_searched_as_utf8_but_is_not_replaced()
    {
        var original = Ansi.GetBytes("Grüße byte");
        File.WriteAllBytes(PathOf("file.txt"), original);

        var results = await SearchAsync("byte", encodingWithoutBom: Utf8);
        Assert.That(results, Has.Count.EqualTo(1), "ASCII text is found despite the wrong encoding");

        var replaced = await ReplaceAsync(results, "foo");

        Assert.That(replaced.Success, Is.False);
        Assert.That(replaced.Errors.Single(), Does.Contain("not valid utf-8"));
        Assert.That(File.ReadAllBytes(PathOf("file.txt")), Is.EqualTo(original));
    }

    [Test]
    public async Task Utf8_file_replaced_as_ansi_keeps_its_bytes()
    {
        // Every UTF-8 byte is a valid character in Windows-1252, so the wrong encoding is not detected.
        // But the bytes are written back unchanged.
        File.WriteAllBytes(PathOf("file.txt"), Utf8.GetBytes("Grüße byte\r\n"));

        var results = await SearchAsync("byte", encodingWithoutBom: Ansi);
        var replaced = await ReplaceAsync(results, "foo");

        Assert.That(replaced.Success, Is.True, string.Join("\n", replaced.Errors));
        Assert.That(File.ReadAllBytes(PathOf("file.txt")), Is.EqualTo(Utf8.GetBytes("Grüße foo\r\n")));
    }

    [Test]
    public async Task Utf8_file_without_bom_stays_utf8()
    {
        File.WriteAllBytes(PathOf("file.txt"), Utf8.GetBytes("Grüße byte → ok\r\n"));

        var results = await SearchAsync("byte", encodingWithoutBom: Utf8);
        Assert.That(results.Single().Encoding.CodePage, Is.EqualTo(Encoding.UTF8.CodePage));

        var replaced = await ReplaceAsync(results, "foo");

        Assert.That(replaced.Success, Is.True, string.Join("\n", replaced.Errors));
        Assert.That(File.ReadAllBytes(PathOf("file.txt")), Is.EqualTo(Utf8.GetBytes("Grüße foo → ok\r\n")));
    }

    [Test]
    public async Task File_with_bom_ignores_selected_encoding()
    {
        var utf8WithBom = new UTF8Encoding(true);
        File.WriteAllBytes(PathOf("file.txt"), [.. utf8WithBom.GetPreamble(), .. utf8WithBom.GetBytes("Grüße byte\r\n")]);

        var results = await SearchAsync("ü", encodingWithoutBom: Ansi);

        Assert.That(results.Single().Encoding.GetPreamble(), Is.EqualTo(utf8WithBom.GetPreamble()));
    }

    [Test]
    public async Task Replacement_that_cannot_be_stored_in_ansi_file_is_rejected()
    {
        var original = Ansi.GetBytes("Grüße byte");
        File.WriteAllBytes(PathOf("file.txt"), original);

        var results = await SearchAsync("byte", encodingWithoutBom: Ansi);
        var replaced = await ReplaceAsync(results, "→");

        Assert.That(replaced.Success, Is.False);
        Assert.That(File.ReadAllBytes(PathOf("file.txt")), Is.EqualTo(original));
    }
}
