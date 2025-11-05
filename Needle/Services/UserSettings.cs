using System.IO;
using System.Text.Json;

namespace Needle.Services;

public class UserSettings
{
    static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Needle",
        "settings.json"
    );

    public string StartDirectory { get; set; } = string.Empty;
    public string FileMasks { get; set; } = "*.cs;*.xaml";
    public string Pattern { get; set; } = string.Empty;
    public bool IsRegex { get; set; }
    public bool IsCaseSensitive { get; set; }

    public static UserSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                return JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
            }
        }
        catch
        {
            // If loading fails, return default settings
        }

        return new UserSettings();
    }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(SettingsFilePath, json);
        }
        catch
        {
            // Silently fail if saving is not possible
        }
    }
}