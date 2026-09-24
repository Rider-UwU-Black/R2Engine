using System.Numerics;

namespace R2Engine.Editor.Scene;

/// <summary>A passive UI prefab, cloned into runtime scenes without changing their source files.</summary>
public static class LoadingScreen
{
    public static string ConfiguredPath() => ProjectSettings.Load(
        Path.Combine(AssetDatabase.ProjectRoot, "ProjectSettings.json")).LoadingScreenPrefab;

    public static void Attach(Scene scene, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || scene.GameObjects.Any(o => o.GetComponent<Canvas>()?.IsLoadingScreen == true)) return;
        Scene prefab = SceneSerializer.Load(AssetDatabase.ToAbsolutePath(path));
        Canvas[] canvases = prefab.GameObjects.Where(o => o.IsActiveInHierarchy)
            .Select(o => o.GetComponent<Canvas>()).OfType<Canvas>().ToArray();
        if (canvases.Length != 1)
            throw new InvalidDataException("Loading screen prefab must contain exactly one active Canvas.");
        foreach (GameObject item in prefab.GameObjects)
        {
            if (item.Components.Any(c => c is not Transform and not Canvas and not UIPanel and not UIImage and not UIText and not UIButton))
                throw new InvalidDataException("Loading screen prefabs support only Canvas, Panel, Image, Text and Button components.");
            if (Canvas.FindOwner(item) != canvases[0])
                throw new InvalidDataException("All loading screen objects must belong to its Canvas.");
        }
        Canvas canvas = canvases[0];
        canvas.IsLoadingScreen = true;
        canvas.ToggleWithMenuInput = canvas.CloseWithCancelInput = false;
        canvas.StartsVisible = canvas.IsVisible = false;
        canvas.PauseGameplayWhenVisible = false; // transition state handles pausing; never a Start-button menu
        foreach (GameObject item in prefab.GameObjects.ToArray())
        {
            item.Name = scene.GetUniqueName("[Loading] " + item.Name);
            item.PrefabPath = path;
            if (item.GetComponent<UIButton>() is { } button)
            { button.Interactable = false; button.Action = UIButtonAction.None; }
            if (item.GetComponent<UIText>() is { } text) text.Ps2SaveLoadFeedback = false;
            scene.AddGameObject(item);
        }
    }

    public static Dictionary<Canvas, bool>? Show(Scene scene)
    {
        Canvas[] canvases = scene.GameObjects.Where(o => o.IsActiveInHierarchy)
            .Select(o => o.GetComponent<Canvas>()).OfType<Canvas>().ToArray();
        if (!canvases.Any(c => c.IsLoadingScreen)) return null;
        var previous = canvases.ToDictionary(c => c, c => c.IsVisible);
        foreach (Canvas canvas in canvases) canvas.IsVisible = canvas.IsLoadingScreen;
        return previous;
    }

    public static void Restore(Dictionary<Canvas, bool>? previous)
    {
        if (previous != null) foreach (var entry in previous) entry.Key.IsVisible = entry.Value;
    }

    public static Scene CreateTemplate()
    {
        Scene scene = new("Loading Screen");
        GameObject root = scene.CreateGameObject("Loading Screen");
        root.AddComponent<Canvas>().StartsVisible = true;
        UIPanel panel = root.AddComponent<UIPanel>();
        panel.Size = new(640, 448);
        panel.Color = new(.025f, .035f, .07f, 1);
        GameObject label = scene.CreateGameObject("Loading Label");
        label.SetParent(root, false);
        UIText text = label.AddComponent<UIText>();
        text.Text = "LOADING...";
        text.FontSize = 24;
        text.Size = new(400, 60);
        text.Color = Vector4.One;
        return scene;
    }
}
