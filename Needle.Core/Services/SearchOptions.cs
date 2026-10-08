using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Needle.Services;

/// <summary>
///     A search that can be saved to and loaded from a JSON file. Used by the command line tool.
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
    ///     Relative to the directory of the options file, or to the current directory if empty.
    /// </summary>
    public string StartDirectory { get; set; } = string.Empty;

    public string FileMasks { get; set; } = "*";
    public string Pattern { get; set; } = string.Empty;
    public bool IsRegex { get; set; }
    public bool IsCaseSensitive { get; set; }
    public bool IncludeSubdirectories { get; set; } = true;
    public SearchScope SearchScope { get; set; } = SearchScope.Content;

    /// <summary>
    ///     Encoding for files without BOM: "utf8", "ansi", a code page number or an encoding name.
    /// </summary>
    public string Encoding { get; set; } = "utf8";

    /// <summary>
    ///     The directory relative paths are resolved against. The directory of the options file after
    ///     <see cref="Load" />.
    /// </summary>
    [JsonIgnore]
    public string BaseDirectory { get; set; } = string.Empty;

    /// <summary>
    ///     Throws <see cref="JsonException" /> for invalid files, including unknown keys, so typos are noticed.
    /// </summary>
    public static SearchOptions Load(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        using var stream = File.OpenRead(fullPath);
        var options = JsonSerializer.Deserialize(stream, Json.SearchOptions)
                      ?? throw new JsonException("The options file is empty.");
        options.BaseDirectory = Path.GetDirectoryName(fullPath)!;
        return options;
    }

    public void Save(string filePath)
    {
        using var stream = File.Create(filePath);
        JsonSerializer.Serialize(stream, this, Json.SearchOptions);
    }

    /// <summary>
    ///     Throws <see cref="ArgumentException" /> for an unknown encoding.
    /// </summary>
    public SearchParameters ToSearchParameters()
    {
        var baseDirectory = string.IsNullOrEmpty(BaseDirectory) ? Environment.CurrentDirectory : BaseDirectory;
        return new SearchParameters
        {
            StartDirectory = Path.GetFullPath(StartDirectory, baseDirectory),
            FileMasks = FileMasks,
            Pattern = Pattern,
            IsRegex = IsRegex,
            IsCaseSensitive = IsCaseSensitive,
            IncludeSubdirectories = IncludeSubdirectories,
            Scope = SearchScope,
            EncodingWithoutBom = ParseEncoding(Encoding)
        };
    }

    public static Encoding ParseEncoding(string encoding)
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
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    PropertyNameCaseInsensitive = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(SearchOptions))]
internal partial class SearchOptionsJsonContext : JsonSerializerContext;
