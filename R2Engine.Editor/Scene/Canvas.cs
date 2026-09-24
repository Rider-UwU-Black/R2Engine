using System.Numerics;

namespace R2Engine.Editor.Scene;

public sealed class Canvas : Component
{
    public Vector2 ReferenceResolution { get; set; } = new(640, 448);
    public bool StartsVisible { get; set; }
    public bool PauseGameplayWhenVisible { get; set; } = true;
    public bool ToggleWithMenuInput { get; set; } = true;
    public bool CloseWithCancelInput { get; set; } = true;
    public string NavigateSound { get; set; } = "";
    public string SubmitSound { get; set; } = "";
    public string CancelSound { get; set; } = "";
    public string ErrorSound { get; set; } = "";
    public string OpenSound { get; set; } = "";
    public bool PlayOpenSoundOnSceneStart { get; set; }
    private bool _isVisible;
    private bool _visibilityInitialized;
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            bool opening = value && !_isVisible;
            _isVisible = value;
            if (opening && (_visibilityInitialized || PlayOpenSoundOnSceneStart))
                PlayOpenSound();
            _visibilityInitialized = true;
        }
    }
    // Runtime/cooker-only: never written into authored scenes or prefabs.
    public bool IsLoadingScreen { get; set; }
    public bool IsInteractionPrompt { get; set; }

    public static Canvas? FindOwner(GameObject gameObject)
    {
        for (GameObject? current = gameObject; current != null; current = current.Parent)
            if (current.GetComponent<Canvas>() is { } canvas) return canvas;
        return null;
    }

    public static Canvas? FindByName(Scene scene, string name) => scene.GameObjects.FirstOrDefault(item =>
        item.IsActiveInHierarchy && item.GetComponent<Canvas>() != null &&
        string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))?.GetComponent<Canvas>();

    public void PlayNavigateSound() => PlayUiSound(NavigateSound);
    public void PlaySubmitSound() => PlayUiSound(SubmitSound);
    public void PlayCancelSound() => PlayUiSound(CancelSound);
    public void PlayErrorSound() => PlayUiSound(ErrorSound);
    public void PlayOpenSound() => PlayUiSound(OpenSound);

    private static void PlayUiSound(string path)
    {
        if (!string.IsNullOrWhiteSpace(path))
            R2Engine.Runtime.Platform.RuntimePlatform.Services.Audio?.AudioSystem?.PlayUiSound(path, true);
    }

    public static void ApplyCanvasAction(Scene scene, UIButton button)
    {
        if (button.Action == UIButtonAction.GoToScene)
        {
            if (!string.IsNullOrWhiteSpace(button.TargetScene)) SceneManager.LoadScene(button.TargetScene.Trim());
            return;
        }
        Canvas? owner = FindOwner(button.GameObject!);
        Canvas? target = FindByName(scene, button.TargetCanvas);
        if (button.Action == UIButtonAction.OpenSubmenu && owner != null && target != null)
        { scene.MenuNavigation.Open(owner, target, button); return; }
        if (button.Action == UIButtonAction.GoBack)
        { scene.MenuNavigation.Back(); return; }
        if (button.Action == UIButtonAction.CloseMenu && owner != null) owner.IsVisible = false;
        else if (button.Action == UIButtonAction.OpenCanvas && target != null) target.IsVisible = true;
        else if (button.Action == UIButtonAction.CloseCanvas && target != null) target.IsVisible = false;
        else if (button.Action == UIButtonAction.ToggleCanvas && target != null) target.IsVisible = !target.IsVisible;
    }
}

public enum UIButtonAction { None, CloseMenu, SaveCheckpoint, LoadCheckpoint, OpenCanvas, CloseCanvas, ToggleCanvas, OpenSubmenu, GoBack, GoToScene }

/// <summary>Transient navigation history owned by one runtime scene, never serialized.</summary>
public sealed class CanvasNavigation
{
    private readonly Stack<(Canvas Parent, Canvas Child, UIButton Button)> _history = new();
    public Canvas? Current => _history.Count == 0 ? null : _history.Peek().Child;
    private bool _focusPending;
    private UIButton? _returnFocus;

    public bool Open(Canvas parent, Canvas child, UIButton button)
    {
        if (parent == child || !parent.IsVisible || child.IsVisible || child.IsLoadingScreen ||
            (Current != null && parent != Current) ||
            _history.Count >= 31 || _history.Any(entry => entry.Parent == child)) return false;
        _history.Push((parent, child, button));
        parent.IsVisible = false;
        child.IsVisible = true;
        _returnFocus = null;
        _focusPending = true;
        return true;
    }

    public bool Back()
    {
        if (_history.Count == 0) return false;
        var entry = _history.Pop();
        entry.Child.IsVisible = false;
        entry.Parent.IsVisible = true;
        _returnFocus = entry.Button;
        _focusPending = true;
        return true;
    }

    public void Cancel(Canvas? owner)
    {
        Canvas? current = Current ?? owner;
        if (current?.CloseWithCancelInput != true) return;
        current.PlayCancelSound();
        if (!Back()) current.IsVisible = false;
    }

    public int ResolveFocus(UIButton[] buttons, int previous)
    {
        if (!_focusPending) return buttons.Length == 0 ? 0 : Math.Clamp(previous, 0, buttons.Length - 1);
        _focusPending = false;
        int index = _returnFocus == null ? -1 : Array.IndexOf(buttons, _returnFocus);
        _returnFocus = null;
        return Math.Max(0, index);
    }
}

public sealed class UIPanel : Component
{
    public int SortOrder { get; set; }
    public Vector2 Anchor { get; set; } = new(0.5f, 0.5f);
    public Vector2 Offset { get; set; }
    public Vector2 Size { get; set; } = new(240, 100);
    public Vector4 Color { get; set; } = new(0.03f, 0.08f, 0.18f, 0.9f);
}

public sealed class UIButton : Component
{
    public Vector4 SpriteBorders { get; set; }
    public int SortOrder { get; set; }
    public Vector2 Anchor { get; set; } = new(0.5f, 0.5f);
    public Vector2 Offset { get; set; }
    public Vector2 Size { get; set; } = new(180, 42);
    public Vector4 NormalColor { get; set; } = new(0.04f, 0.16f, 0.34f, 0.95f);
    public Vector4 HoverColor { get; set; } = new(0.04f, 0.35f, 0.7f, 1.0f);
    public Vector4 PressedColor { get; set; } = new(0.02f, 0.55f, 0.95f, 1.0f);
    public string Text { get; set; } = "Button";
    public string FontPath { get; set; } = "";
    public float FontSize { get; set; } = 16.0f;
    public bool Interactable { get; set; } = true;
    public UIButtonAction Action { get; set; }
    public string SaveSlot { get; set; } = "CHECKPOINT";
    public string TargetCanvas { get; set; } = "Canvas";
    public string TargetScene { get; set; } = "";
    public string NormalSprite { get; set; } = "";
    public string HoverSprite { get; set; } = "";
    public string PressedSprite { get; set; } = "";
    public bool IsHovered { get; set; }
    public bool IsPressed { get; set; }
    public bool WasClicked { get; set; }

    public bool ConsumeClick()
    {
        bool clicked = WasClicked;
        WasClicked = false;
        return clicked;
    }
}

public sealed class UIImage : Component
{
    // X, Y, width, height in original texture pixels, measured from top-left.
    // Zero width/height selects the complete texture.
    public Vector4 SourceRect { get; set; }
    public Vector4 SpriteBorders { get; set; }
    public int SortOrder { get; set; }
    public Vector2 Anchor { get; set; } = new(.5f);
    public Vector2 Offset { get; set; }
    public Vector2 Size { get; set; } = new(256, 128);
    public string TexturePath { get; set; } = "";
    public Vector4 Tint { get; set; } = Vector4.One;
    public bool PreserveAspect { get; set; } = true;
}

public sealed class UIText : Component
{
    public string FontPath { get; set; } = "";
    public bool Ps2SaveLoadFeedback { get; set; }
    public string SavedMessage { get; set; } = "Saved";
    public string SaveFailedMessage { get; set; } = "Save failed";
    public string LoadedMessage { get; set; } = "Loaded";
    public string LoadFailedMessage { get; set; } = "Load failed";
    public int SortOrder { get; set; }
    public Vector2 Anchor { get; set; } = new(0.5f, 0.5f);
    public Vector2 Offset { get; set; }
    public Vector2 Size { get; set; } = new(240, 36);
    public string Text { get; set; } = "Text";
    public Vector4 Color { get; set; } = Vector4.One;
    public float FontSize { get; set; } = 24.0f;
}
