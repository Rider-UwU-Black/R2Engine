using R2Engine.Editor.Scene;

namespace R2Engine.Editor;

/// <summary>Editor-only preview selection. Never changes Canvas.IsVisible or scene data.</summary>
public sealed class CanvasPreviewState
{
    public bool Isolate = true;
    // Deliberately session-only: opening the editor always starts with an unobstructed Scene view.
    public bool ShowInScene;
    private Canvas? _focused;

    public void LoadPreferences(string path)
    {
        ShowInScene = false;
        if (!System.IO.File.Exists(path)) return;
        try
        {
            using var data = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
            if (data.RootElement.TryGetProperty("ShowOnlyFocusedCanvas", out var value) &&
                value.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
                Isolate = value.GetBoolean();
        }
        catch (System.Text.Json.JsonException) { }
        catch (System.IO.IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void SavePreferences(string path) => System.IO.File.WriteAllText(path,
        System.Text.Json.JsonSerializer.Serialize(new { ShowOnlyFocusedCanvas = Isolate }));

    public Canvas? Resolve(Scene.Scene scene, GameObject? selected)
    {
        Canvas? selectedCanvas = selected == null ? null : Canvas.FindOwner(selected);
        if (selectedCanvas?.GameObject is { } owner && owner.Scene == scene &&
            owner.IsActiveInHierarchy && scene.GameObjects.Contains(owner))
            _focused = selectedCanvas;
        if (_focused?.GameObject is not { } focusedObject || focusedObject.Scene != scene ||
            !focusedObject.IsActiveInHierarchy || !scene.GameObjects.Contains(focusedObject))
        {
            var canvases = scene.GameObjects.Where(o => o.IsActiveInHierarchy)
                .Select(o => o.GetComponent<Canvas>()).OfType<Canvas>().ToArray();
            _focused = canvases.FirstOrDefault(c => c.StartsVisible) ?? canvases.FirstOrDefault();
        }
        return _focused;
    }
}
