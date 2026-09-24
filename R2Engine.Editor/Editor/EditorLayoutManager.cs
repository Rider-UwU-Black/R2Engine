using ImGuiNET;

namespace R2Engine.Editor;

internal sealed record EditorLayout(string Name, string Path);

internal static class EditorLayoutManager
{
    public static string LayoutsDirectory => Path.Combine(R2UserSettings.UserDataRoot, "Layouts");

    public static IReadOnlyList<EditorLayout> GetCustomLayouts()
    {
        if (!Directory.Exists(LayoutsDirectory))
            return Array.Empty<EditorLayout>();

        return Directory.EnumerateFiles(LayoutsDirectory, "*.ini", SearchOption.TopDirectoryOnly)
            .Select(path => new EditorLayout(Path.GetFileNameWithoutExtension(path), path))
            .OrderBy(layout => layout.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static void LoadDefault() => LoadFrom(FindDefaultLayout());

    public static void Load(EditorLayout layout) => LoadFrom(layout.Path);

    public static void SaveNew(string name)
    {
        string path = GetCustomLayoutPath(name);
        if (File.Exists(path))
            throw new IOException($"A layout named '{Path.GetFileNameWithoutExtension(path)}' already exists.");
        SaveTo(path);
    }

    public static void Overwrite(EditorLayout layout) => SaveTo(layout.Path);

    public static void Delete(EditorLayout layout)
    {
        if (File.Exists(layout.Path))
            File.Delete(layout.Path);
    }

    private static void LoadFrom(string sourcePath)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The editor layout could not be found.", sourcePath);

        string projectLayout = Path.Combine(AssetDatabase.ProjectRoot, "imgui.ini");
        File.Copy(sourcePath, projectLayout, overwrite: true);
        ImGui.LoadIniSettingsFromDisk(projectLayout);
    }

    private static void SaveTo(string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        ImGui.SaveIniSettingsToDisk(destinationPath);
    }

    private static string GetCustomLayoutPath(string name)
    {
        string clean = name.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
            clean = clean.Replace(invalid, '_');
        clean = clean.Trim('.', ' ');
        if (string.IsNullOrWhiteSpace(clean))
            throw new ArgumentException("Enter a layout name.", nameof(name));
        if (string.Equals(clean, "Default", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Default is reserved for the built-in layout.", nameof(name));
        return Path.Combine(LayoutsDirectory, clean + ".ini");
    }

    private static string FindDefaultLayout()
    {
        DirectoryInfo? directory = new(Path.GetFullPath(AppContext.BaseDirectory));
        while (directory != null)
        {
            string candidate = Path.Combine(
                directory.FullName,
                "R2Engine.Editor",
                "Editor",
                "Defaults",
                "imgui.ini");
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("The built-in Default editor layout could not be found.");
    }
}
