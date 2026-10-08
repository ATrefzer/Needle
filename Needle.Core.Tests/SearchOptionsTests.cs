using System.Text;
using System.Text.Json;
using Needle.Services;
using NUnit.Framework;

namespace Needle.Core.Tests;

[TestFixture]
public class SearchOptionsTests : TempDirectoryTestBase
{
    private static readonly string TemplatePath = Path.Combine(AppContext.BaseDirectory, "Files", "needle-cli.template.json");

    [Test]
    public void Template_of_the_command_line_tool_contains_all_options()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(TemplatePath),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        var keys = json.RootElement.EnumerateObject().Select(p => p.Name);

        Assert.That(keys, Is.EquivalentTo(typeof(SearchOptions).GetProperties().Select(p => p.Name)));
    }

    [Test]
    public void Template_of_the_command_line_tool_has_no_effect()
    {
        var template = SearchOptions.Load(TemplatePath);
        var fromTemplate = template.ToSearchParameters();
        var defaults = new SearchOptions().ToSearchParameters();

        Assert.That(fromTemplate.StartDirectory, Is.EqualTo(defaults.StartDirectory));
        Assert.That(fromTemplate.FileMasks, Is.EqualTo(defaults.FileMasks));
        Assert.That(fromTemplate.Pattern, Is.EqualTo(defaults.Pattern));
        Assert.That(fromTemplate.IsRegex, Is.EqualTo(defaults.IsRegex));
        Assert.That(fromTemplate.IsCaseSensitive, Is.EqualTo(defaults.IsCaseSensitive));
        Assert.That(fromTemplate.IncludeSubdirectories, Is.EqualTo(defaults.IncludeSubdirectories));
        Assert.That(fromTemplate.Scope, Is.EqualTo(defaults.Scope));
        Assert.That(fromTemplate.EncodingWithoutBom.CodePage, Is.EqualTo(defaults.EncodingWithoutBom.CodePage));
        Assert.That(template.MaxColumns, Is.EqualTo(0));
    }

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
    public void Missing_values_are_not_set()
    {
        CreateFile("search.json", """{ "Pattern": "foo" }""");

        var options = SearchOptions.Load(PathOf("search.json"));

        Assert.That(options.FileMasks, Is.Null);
        Assert.That(options.IncludeSubdirectories, Is.Null);
        Assert.That(options.SearchScope, Is.Null);
        Assert.That(options.MaxColumns, Is.Null);
    }

    [Test]
    public void Defaults_are_used_for_options_that_are_not_set()
    {
        var parameters = new SearchOptions { Pattern = "foo" }.ToSearchParameters();

        Assert.That(parameters.StartDirectory, Is.EqualTo(Environment.CurrentDirectory));
        Assert.That(parameters.FileMasks, Is.EqualTo("*"));
        Assert.That(parameters.IsRegex, Is.False);
        Assert.That(parameters.IsCaseSensitive, Is.False);
        Assert.That(parameters.IncludeSubdirectories, Is.True);
        Assert.That(parameters.Scope, Is.EqualTo(SearchScope.Content));
        Assert.That(parameters.EncodingWithoutBom.CodePage, Is.EqualTo(65001));
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

        var options = SearchOptions.Load(PathOf(Path.Combine("config", "search.json")));

        Assert.That(options.StartDirectory, Is.EqualTo(PathOf("src")));
    }

    [Test]
    public void Set_options_override_and_others_are_kept()
    {
        var lower = new SearchOptions { Pattern = "foo", FileMasks = "*.cs", IsRegex = true, MaxColumns = 200 };
        var higher = new SearchOptions { Pattern = "bar", IsRegex = false };

        var merged = lower.Merge(higher);

        Assert.That(merged.Pattern, Is.EqualTo("bar"));
        Assert.That(merged.IsRegex, Is.False);
        Assert.That(merged.FileMasks, Is.EqualTo("*.cs"));
        Assert.That(merged.MaxColumns, Is.EqualTo(200));
        Assert.That(merged.StartDirectory, Is.Null);
    }

    [Test]
    public void Files_are_layered()
    {
        CreateFile(Path.Combine("tool", "needle-cli.json"), """{ "FileMasks": "*.cs", "MaxColumns": 300 }""");
        CreateFile(Path.Combine("repo", "search.json"), """{ "StartDirectory": "src", "Pattern": "foo" }""");

        var options = SearchOptions.Load(PathOf(Path.Combine("tool", "needle-cli.json")))
            .Merge(SearchOptions.Load(PathOf(Path.Combine("repo", "search.json"))))
            .Merge(new SearchOptions { Pattern = "bar" });

        Assert.That(options.FileMasks, Is.EqualTo("*.cs"));
        Assert.That(options.MaxColumns, Is.EqualTo(300));
        Assert.That(options.StartDirectory, Is.EqualTo(PathOf(Path.Combine("repo", "src"))));
        Assert.That(options.Pattern, Is.EqualTo("bar"));
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
            Encoding = "1252",
            MaxColumns = 120
        };

        options.Save(PathOf("search.json"));
        var loaded = SearchOptions.Load(PathOf("search.json"));

        // Readable: enum names, regex characters are not escaped, options that are not set are left out.
        var json = File.ReadAllText(PathOf("search.json"));
        Assert.That(json, Does.Contain("\"FileName\""));
        Assert.That(json, Does.Contain(@"\w+"));
        Assert.That(json, Does.Not.Contain("FileMasks"));
        Assert.That(loaded.StartDirectory, Is.EqualTo(PathOf("src")));
        Assert.That(loaded.Pattern, Is.EqualTo(@"\w+"));
        Assert.That(loaded.IsCaseSensitive, Is.True);
        Assert.That(loaded.SearchScope, Is.EqualTo(SearchScope.FileName));
        Assert.That(loaded.Encoding, Is.EqualTo("1252"));
        Assert.That(loaded.MaxColumns, Is.EqualTo(120));
        Assert.That(loaded.FileMasks, Is.Null);
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
