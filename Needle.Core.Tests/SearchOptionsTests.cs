using System.Text;
using System.Text.Json;
using Needle.Services;
using NUnit.Framework;

namespace Needle.Core.Tests;

[TestFixture]
public class SearchOptionsTests : TempDirectoryTestBase
{
    [Test]
    public void Options_are_loaded_with_comments_and_enum_names()
    {
        CreateFile("search.json", """
            {
              // Comments are allowed
              "Pattern": "TODO|FIXME",
              "IsRegex": true,
              "FileMasks": "*.cs",
              "SearchScope": "Both",
              "Encoding": "ansi",
            }
            """);

        var options = SearchOptions.Load(PathOf("search.json"));

        Assert.That(options.Pattern, Is.EqualTo("TODO|FIXME"));
        Assert.That(options.IsRegex, Is.True);
        Assert.That(options.FileMasks, Is.EqualTo("*.cs"));
        Assert.That(options.SearchScope, Is.EqualTo(SearchScope.Both));
        Assert.That(options.Encoding, Is.EqualTo("ansi"));
    }

    [Test]
    public void Missing_values_keep_their_defaults()
    {
        CreateFile("search.json", """{ "Pattern": "foo" }""");

        var options = SearchOptions.Load(PathOf("search.json"));

        Assert.That(options.FileMasks, Is.EqualTo("*"));
        Assert.That(options.IncludeSubdirectories, Is.True);
        Assert.That(options.SearchScope, Is.EqualTo(SearchScope.Content));
    }

    [Test]
    public void Unknown_key_is_an_error()
    {
        CreateFile("search.json", """{ "IsRegx": true }""");

        Assert.Throws<JsonException>(() => SearchOptions.Load(PathOf("search.json")));
    }

    [Test]
    public void Relative_start_directory_is_resolved_against_the_options_file()
    {
        CreateFile(Path.Combine("config", "search.json"), """{ "StartDirectory": "../src", "Pattern": "foo" }""");

        var parameters = SearchOptions.Load(PathOf(Path.Combine("config", "search.json"))).ToSearchParameters();

        Assert.That(parameters.StartDirectory, Is.EqualTo(PathOf("src")));
    }

    [Test]
    public void Missing_start_directory_is_the_directory_of_the_options_file()
    {
        CreateFile("search.json", """{ "Pattern": "foo" }""");

        var parameters = SearchOptions.Load(PathOf("search.json")).ToSearchParameters();

        Assert.That(parameters.StartDirectory, Is.EqualTo(Directory));
    }

    [Test]
    public void Saved_options_are_loaded_again()
    {
        var options = new SearchOptions
        {
            StartDirectory = "src",
            Pattern = @"\w+",
            IsCaseSensitive = true,
            SearchScope = SearchScope.FileName,
            Encoding = "1252"
        };

        options.Save(PathOf("search.json"));
        var loaded = SearchOptions.Load(PathOf("search.json"));

        // Readable: enum names, regex characters are not escaped.
        Assert.That(File.ReadAllText(PathOf("search.json")), Does.Contain("\"FileName\""));
        Assert.That(File.ReadAllText(PathOf("search.json")), Does.Contain(@"\w+"));
        Assert.That(loaded.StartDirectory, Is.EqualTo("src"));
        Assert.That(loaded.Pattern, Is.EqualTo(@"\w+"));
        Assert.That(loaded.IsCaseSensitive, Is.True);
        Assert.That(loaded.SearchScope, Is.EqualTo(SearchScope.FileName));
        Assert.That(loaded.Encoding, Is.EqualTo("1252"));
    }

    [TestCase("utf8", 65001)]
    [TestCase("UTF-8", 65001)]
    [TestCase("1252", 1252)]
    [TestCase("iso-8859-15", 28605)]
    public void Encodings_are_parsed(string name, int codePage)
    {
        Assert.That(SearchOptions.ParseEncoding(name).CodePage, Is.EqualTo(codePage));
    }

    [Test]
    public void Utf8_is_parsed_without_bom()
    {
        Assert.That(SearchOptions.ParseEncoding("utf8").GetPreamble(), Is.Empty);
    }

    [Test]
    public void Ansi_is_a_single_byte_code_page()
    {
        Assert.That(SearchOptions.ParseEncoding("ansi").IsSingleByte, Is.True);
    }

    [Test]
    public void Unknown_encoding_is_an_error()
    {
        Assert.Throws<ArgumentException>(() => SearchOptions.ParseEncoding("klingon"));
    }
}
