using System.Text;
using Needle.Models;
using Needle.Services;
using NUnit.Framework;

namespace NeedleTests;


[TestFixture]
public class EncodingTests
{
    static List<(string, Encoding, bool)> GetTestData()
    {
        // Most code pages are no longer supported by default. We have to register them.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        //var latin9 = Encoding.GetEncoding("ISO-8859-15");

        return
        [
            // Codepages are not detected. We map it to utf8 when we write the file.
            // File has no preamble and starts with the text.
            ("file-latin9.txt", Encoding.UTF8,  false),

            // File has no preamble and starts with the text.
            ("file-utf8-without-bom.txt", Encoding.UTF8, false),

            ("file-utf8-with-bom.txt", Encoding.UTF8, true),
            ("file-utf16-with-bom.txt", Encoding.Unicode, true)
        ];
    }

    [TestCaseSource(nameof(GetTestData))]
    public async Task Encodings_are_preserved((string file, Encoding encoding, bool shouldHavePreamble) testData)
    {
        var startDirectory = Path.Combine(AppContext.BaseDirectory, "Files");
        var filePath = Path.Combine(startDirectory, testData.file);

        var fss = new FileSearchService();
        var parameters = new SearchParameters
        {
            StartDirectory = startDirectory,
            FileMasks = testData.file,
            Pattern = "byte",
            IncludeSubdirectories = false
        };

        // Search text
        SearchResult? result = null;
        await fss.SearchAsync(parameters, r => result = r, CancellationToken.None);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.MatchCount, Is.EqualTo(1));

        // Replace text
        var frs = new FileReplaceService();
        var replaced = await frs.ReplaceInFilesAsync([result], "foo", CancellationToken.None);
        Assert.That(replaced.TotalReplacements, Is.EqualTo(1));


        // Assert encoding is preserved after replace
        var encoding = FileSearchService.DetectEncoding(filePath);
        Assert.That(encoding.EncodingName, Is.EqualTo(testData.encoding.EncodingName));
        Assert.That(encoding.CodePage, Is.EqualTo(testData.encoding.CodePage));

        if (testData.shouldHavePreamble)
        {
            Assert.That(encoding.GetPreamble(), Is.EquivalentTo(testData.encoding.GetPreamble()));
        }
        else
        {
            Assert.That(encoding.GetPreamble().Length, Is.EqualTo(0));
        }
    }

}