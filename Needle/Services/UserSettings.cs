using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Needle.Services;

public class UserSettings
{
    private static readonly string DefaultFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Needle",
        "settings.json"
    );

    public string StartDirectory { get; set; } = string.Empty;
    public string FileMasks { get; set; } = "*.cs;*.xaml";
    public string Pattern { get; set; } = string.Empty;
    public bool IsRegex { get; set; }
    public bool IsCaseSensitive { get; set; }
    public bool IncludeSubdirectories { get; set; } = true;
    public SearchScope SearchScope { get; set; } = SearchScope.Content;
    public int EncodingWithoutBomCodePage { get; set; } = 65001; // UTF-8
    public List<string> FileMasksHistory { get; set; } = new();

    /// <summary>
    ///     The file the settings are saved to. Null for settings that were not loaded from a file,
    ///     then <see cref="Save" /> does nothing.
    /// </summary>
    [JsonIgnore]
    public string? FilePath { get; private set; }

    /// <summary>
    ///     Loads the settings of the current user. Returns default settings if there are none.
    /// </summary>
    public static UserSettings Load()
    {
        var settings = new UserSettings();
        try
        {
            if (File.Exists(DefaultFilePath))
            {
                var json = File.ReadAllText(DefaultFilePath);
                settings = JsonSerializer.Deserialize<UserSettings>(json) ?? settings;
            }
        }
        catch
        {
            // If loading fails, use default settings
        }

        settings.FilePath = DefaultFilePath;
        return settings;
    }

    public void Save()
    {
        if (FilePath == null)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Silently fail if saving is not possible
        }
    }
}
