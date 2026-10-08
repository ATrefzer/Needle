using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Needle.Services;

/// <summary>
///     Options of the command line tool, saved to and loaded from a JSON file.
///     Null means not set, so several sources can be layered with <see cref="Merge" />.
///     The defaults are applied at the end, in <see cref="ToSearchParameters" />.
/// </summary>
public class SearchOptions
{
    // Regex characters like + are not escaped, so the file stays readable.
    private static readonly SearchOptionsJsonContext Json = new(
        new JsonSerializerOptions(SearchOptionsJsonContext.Default.Options)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

    /// <summary>
    ///     In a file relative to the file. Absolute after <see cref="Load" />. Default: the current directory.
    /// </summary>
    public string? StartDirectory { get; set; }

    /// <summary>
    ///     Default: all files.
    /// </summary>
    public string? FileMasks { get; set; }

    public string? Pattern { get; set; }
    public bool? IsRegex { get; set; }
    public bool? IsCaseSensitive { get; set; }

    /// <summary>
    ///     Default: true.
    /// </summary>
    public bool? IncludeSubdirectories { get; set; }

    /// <summary>
    ///     Default: content.
    /// </summary>
    public SearchScope? SearchScope { get; set; }

    /// <summary>
    ///     Encoding for files without BOM: "utf8", "ansi", a code page number or an encoding name. Default: utf8.
    /// </summary>
    public string? Encoding { get; set; }

    /// <summary>
    ///     Longer lines are cut in the output. Default: 0, lines are not cut.
    /// </summary>
    public int? MaxColumns { get; set; }

    /// <summary>
    ///     Throws <see cref="JsonException" /> for invalid files, including unknown keys, so typos are noticed.
    /// </summary>
    public static SearchOptions Load(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        using var stream = File.OpenRead(fullPath);
        var options = JsonSerializer.Deserialize(stream, Json.SearchOptions)
                      ?? throw new JsonException("The options file is empty.");

        if (options.StartDirectory != null)
        {
            // Resolved now, because the options of several files are merged.
            options.StartDirectory = Path.GetFullPath(options.StartDirectory, Path.GetDirectoryName(fullPath)!);
        }

        return options;
    }

    /// <summary>
    ///     Writes only the options that are set.
    /// </summary>
    public void Save(string filePath)
    {
        using var stream = File.Create(filePath);
        JsonSerializer.Serialize(stream, this, Json.SearchOptions);
    }

    /// <summary>
    ///     The options that are set in <paramref name="overrides" /> replace these.
    /// </summary>
    public SearchOptions Merge(SearchOptions overrides)
    {
        return new SearchOptions
        {
            StartDirectory = overrides.StartDirectory ?? StartDirectory,
            FileMasks = overrides.FileMasks ?? FileMasks,
            Pattern = overrides.Pattern ?? Pattern,
            IsRegex = overrides.IsRegex ?? IsRegex,
            IsCaseSensitive = overrides.IsCaseSensitive ?? IsCaseSensitive,
            IncludeSubdirectories = overrides.IncludeSubdirectories ?? IncludeSubdirectories,
            SearchScope = overrides.SearchScope ?? SearchScope,
            Encoding = overrides.Encoding ?? Encoding,
            MaxColumns = overrides.MaxColumns ?? MaxColumns
        };
    }

    /// <summary>
    ///     Applies the defaults for options that are not set.
    ///     Throws <see cref="ArgumentException" /> for an unknown encoding.
    /// </summary>
    public SearchParameters ToSearchParameters()
    {
        return new SearchParameters
        {
            StartDirectory = Path.GetFullPath(StartDirectory ?? Environment.CurrentDirectory),
            FileMasks = FileMasks ?? "*",
            Pattern = Pattern ?? string.Empty,
            IsRegex = IsRegex ?? false,
            IsCaseSensitive = IsCaseSensitive ?? false,
            IncludeSubdirectories = IncludeSubdirectories ?? true,
            Scope = SearchScope ?? Services.SearchScope.Content,
            EncodingWithoutBom = ParseEncoding(Encoding)
        };
    }

    public static Encoding ParseEncoding(string? encoding)
    {
        if (string.IsNullOrWhiteSpace(encoding) ||
            encoding.Replace("-", "").Equals("utf8", StringComparison.OrdinalIgnoreCase))
        {
            return new UTF8Encoding(false);
        }

        if (encoding.Equals("ansi", StringComparison.OrdinalIgnoreCase))
        {
            return FileSearchService.AnsiEncoding;
        }

        // Registers the code pages, too.
        _ = FileSearchService.AnsiEncoding;
        try
        {
            return int.TryParse(encoding, out var codePage)
                ? System.Text.Encoding.GetEncoding(codePage)
                : System.Text.Encoding.GetEncoding(encoding);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            throw new ArgumentException($"Unknown encoding '{encoding}'.", ex);
        }
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    PropertyNameCaseInsensitive = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(SearchOptions))]
internal partial class SearchOptionsJsonContext : JsonSerializerContext;
