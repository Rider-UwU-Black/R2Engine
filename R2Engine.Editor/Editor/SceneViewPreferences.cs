using System.Text.Json;

namespace R2Engine.Editor;

// Workspace preferences, not scene content or runtime settings.
public sealed class SceneViewPreferences
{
    public bool ShowHelperGizmos { get; set; } = true;

    public static SceneViewPreferences Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<SceneViewPreferences>(File.ReadAllText(path)) ?? new();
        }
        catch (JsonException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return new();
    }

    public void Save(string path)
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this));
        File.Move(temporary, path, overwrite: true);
    }
}
