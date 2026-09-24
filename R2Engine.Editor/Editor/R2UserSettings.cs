using System.Text.Json;

namespace R2Engine.Editor;

internal sealed class R2UserSettings
{
    public bool DarkTheme { get; set; }
    public string Pcsx2ExecutablePath { get; set; } = "";

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "R2Engine", "user-settings.json");

    public static R2UserSettings Load()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<R2UserSettings>(File.ReadAllText(SettingsPath)) ?? new R2UserSettings()
                : new R2UserSettings();
        }
        catch
        {
            return new R2UserSettings();
        }
    }
}
