using System.Text.Json;
using System.Text.Json.Nodes;

namespace R2Engine.Editor;

internal sealed class R2UserSettings
{
    public bool DarkTheme { get; set; }
    public string Pcsx2ExecutablePath { get; set; } = "";
    public bool Pcsx2HostFsNoticeAcknowledged { get; set; }

    internal static string UserDataRoot { get; } = FindUserDataRoot();

    private static string SettingsPath => Path.Combine(UserDataRoot, "user-settings.json");

    private static string FindUserDataRoot()
    {
        DirectoryInfo? directory = new(Path.GetFullPath(AppContext.BaseDirectory));
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, ".r2portable")))
                return Path.Combine(directory.FullName, "UserData");
            directory = directory.Parent;
        }
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "R2Engine");
    }

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

    public static void AcknowledgePcsx2HostFsNotice()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        JsonObject settings;
        try
        {
            settings = File.Exists(SettingsPath)
                ? JsonNode.Parse(File.ReadAllText(SettingsPath)) as JsonObject ?? new JsonObject()
                : new JsonObject();
        }
        catch
        {
            settings = new JsonObject();
        }

        settings[nameof(Pcsx2HostFsNoticeAcknowledged)] = true;
        File.WriteAllText(SettingsPath, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
