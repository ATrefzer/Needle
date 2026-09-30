using System.Text;
using Needle.Services;
using NUnit.Framework;

namespace NeedleTests;

[TestFixture]
public class EncodingTests : TempDirectoryTestBase
{
    private static List<(string, Encoding?, Encoding)> GetTestData()
    {
        return
        [
            // No BOM and not valid UTF-8, so the ANSI code page must be selected (see EncodingWithoutBomTests).
            ("file-latin9.txt", null, FileSearchService.AnsiEncoding),

            ("file-utf8-without-bom.txt", null, new UTF8Encoding(false)),

            // The selected encoding does not matter for files with BOM.
            ("file-utf8-with-bom.txt", new UTF8Encoding(true), FileSearchService.AnsiEncoding),
            ("file-utf16-with-bom.txt", Encoding.Unicode, FileSearchService.AnsiEncoding)
        ];
    }

    [TestCaseSource(nameof(GetTestData))]
    public async Task Encodings_are_preserved(
        (string file, Encoding? bomEncoding, Encoding encodingWithoutBom) testData)
    {
        // Work on a copy, so the test files stay unchanged.
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Files", testData.file), PathOf(testData.file));

        var results = await SearchAsync("byte", encodingWithoutBom: testData.encodingWithoutBom);
        Assert.That(results.Single().MatchCount, Is.EqualTo(1));

        var replaced = await ReplaceAsync(results, "foo");
        Assert.That(replaced.TotalReplacements, Is.EqualTo(1), string.Join("\n", replaced.Errors));

        // Assert the BOM is preserved after replace
        var encoding = FileSearchService.DetectBomEncoding(PathOf(testData.file));
        if (testData.bomEncoding == null)
        {
            Assert.That(encoding, Is.Null);
        }
        else
        {
            Assert.That(encoding, Is.Not.Null);
            Assert.That(encoding!.CodePage, Is.EqualTo(testData.bomEncoding.CodePage));
            Assert.That(encoding.GetPreamble(), Is.EqualTo(testData.bomEncoding.GetPreamble()));
        }
    }

    private static IEnumerable<Encoding> BomEncodings()
    {
        yield return new UTF8Encoding(true);
        yield return Encoding.Unicode; // UTF-16 LE
        yield return Encoding.BigEndianUnicode; // UTF-16 BE
        yield return Encoding.UTF32; // UTF-32 LE
        yield return new UTF32Encoding(true, true); // UTF-32 BE
    }

    [TestCaseSource(nameof(BomEncodings))]
    public async Task Files_with_bom_are_detected_and_preserved(Encoding bomEncoding)
    {
        // The UTF-32 LE BOM starts with the UTF-16 LE BOM (FF FE), so the order of detection matters.
        File.WriteAllBytes(PathOf("file.txt"), [.. bomEncoding.GetPreamble(), .. bomEncoding.GetBytes("Grüße byte\r\n")]);

        var results = await SearchAsync("ü", encodingWithoutBom: FileSearchService.AnsiEncoding);
        Assert.That(results.Single().Encoding.CodePage, Is.EqualTo(bomEncoding.CodePage));

        results = await SearchAsync("byte");
        var replaced = await ReplaceAsync(results, "foo");

        Assert.That(replaced.Success, Is.True, string.Join("\n", replaced.Errors));
        Assert.That(File.ReadAllBytes(PathOf("file.txt")),
            Is.EqualTo((byte[])[.. bomEncoding.GetPreamble(), .. bomEncoding.GetBytes("Grüße foo\r\n")]));
    }
}
