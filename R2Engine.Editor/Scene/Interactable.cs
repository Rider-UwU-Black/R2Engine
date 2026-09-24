using System.Numerics;
using System.Runtime.CompilerServices;

namespace R2Engine.Editor.Scene;

public sealed class Interactable : Component
{
    public string Prompt { get; set; } = "Talk";
    public string InputAction { get; set; } = "Interact";
    public string TargetCanvas { get; set; } = "";
    public string DialoguePages { get; set; } = "";
    public string PromptFontPath { get; set; } = "";
    public float PromptFontSize { get; set; } = 18.0f;
    private readonly HashSet<GameObject> _playersInside = new();

    public void NotifyTrigger(GameObject other, bool entering)
    {
        if (other.GetComponent<PlayerController>() == null || GameObject.GetComponent<BoxCollider>()?.IsTrigger != true) return;
        if (entering) _playersInside.Add(other); else _playersInside.Remove(other);
    }

    private sealed class State
    {
        public Canvas? PromptCanvas; public UIText? Label; public Canvas? ActiveRoot;
        public Interactable? ActiveInteraction; public UIText? DialogueLabel;
        public string? OriginalText; public string[] Pages = Array.Empty<string>(); public int Page;
    }
    private static readonly ConditionalWeakTable<Scene, State> States = new();
    public static Canvas? ActiveCanvas(Scene scene)
    {
        var state = States.GetOrCreateValue(scene);
        if (state.ActiveRoot == null)
        {
            // Preserve sessions opened by older callers that directly showed the
            // interaction canvas instead of going through Update.
            state.ActiveRoot = scene.GameObjects.Where(o => o.IsActiveInHierarchy)
                .Select(o => o.GetComponent<Interactable>()).Where(i => i != null)
                .Select(i => Canvas.FindByName(scene, i!.TargetCanvas)).LastOrDefault(c => c?.IsVisible == true);
        }
        if (scene.MenuNavigation.Current is { IsVisible: true } nested && state.ActiveRoot != null)
            return nested;
        if (state.ActiveRoot?.IsVisible == true) return state.ActiveRoot;
        state.ActiveRoot = null;
        return null;
    }

    public static Interactable? FindNearest(Scene scene)
    {
        if (ActiveCanvas(scene) != null) return null;
        if (scene.GameObjects.Any(o => o.IsActiveInHierarchy && o.GetComponent<Canvas>() is { IsVisible: true, IsInteractionPrompt: false } c &&
            (c.PauseGameplayWhenVisible || scene.GameObjects.Any(child => Canvas.FindOwner(child) == c && child.GetComponent<UIButton>() != null)))) return null;
        var player = scene.GameObjects.FirstOrDefault(o => o.IsActiveInHierarchy && o.GetComponent<PlayerController>() != null);
        if (player == null) return null;
        Interactable? best = null; float nearest = float.PositiveInfinity;
        foreach (var o in scene.GameObjects.Where(o => o.IsActiveInHierarchy))
        {
            var item = o.GetComponent<Interactable>();
            if (item == null || !item._playersInside.Contains(player) || Canvas.FindByName(scene,item.TargetCanvas) is not { IsVisible: false }) continue;
            float distance = Vector3.DistanceSquared(player.Transform.WorldPosition,o.Transform.WorldPosition);
            if (distance < nearest) { best=item; nearest=distance; }
        }
        return best;
    }

    public static void Update(Scene scene)
    {
        var state = States.GetOrCreateValue(scene);
        if (state.PromptCanvas != null) state.PromptCanvas.IsVisible=false;
        Canvas? active = ActiveCanvas(scene);
        if (active != null)
        {
            if (RuntimeInput.WasActionPressed(state.ActiveInteraction?.InputAction ?? "Interact"))
            {
                RuntimeInput.InteractionConsumed = true;
                if (state.Pages.Length > 0 && state.Page + 1 < state.Pages.Length)
                {
                    state.Page++;
                    if (state.DialogueLabel != null) state.DialogueLabel.Text = state.Pages[state.Page];
                }
                else End(scene, state);
            }
            return;
        }
        if(state.ActiveInteraction!=null)
        {
            if(state.DialogueLabel!=null && state.OriginalText!=null) state.DialogueLabel.Text=state.OriginalText;
            state.ActiveInteraction=null; state.DialogueLabel=null; state.OriginalText=null;
            state.Pages=Array.Empty<string>(); state.Page=0;
        }
        var nearby = FindNearest(scene);
        if (nearby == null) return;
        if (RuntimeInput.WasActionPressed(nearby.InputAction))
        {
            Canvas target = Canvas.FindByName(scene,nearby.TargetCanvas)!;
            target.IsVisible=true;
            state.ActiveRoot=target;
            state.ActiveInteraction=nearby;
            state.Pages=nearby.DialoguePages.Replace("\r", "").Split(new[]{'\n'}, StringSplitOptions.RemoveEmptyEntries)
                .Select(page=>page.Trim()).Where(page=>page.Length>0).ToArray();
            state.Page=0;
            state.DialogueLabel=scene.GameObjects.FirstOrDefault(o => o.IsActiveInHierarchy && Canvas.FindOwner(o)==target && o.GetComponent<UIText>()!=null)?.GetComponent<UIText>();
            state.OriginalText=state.DialogueLabel?.Text;
            if(state.Pages.Length>0 && state.DialogueLabel!=null) state.DialogueLabel.Text=state.Pages[0];
            RuntimeInput.InteractionConsumed=true;
            return;
        }
        if (state.PromptCanvas == null)
        {
            var prompt = scene.CreateGameObject("__InteractionPrompt");
            state.PromptCanvas=prompt.AddComponent<Canvas>();
            state.PromptCanvas.IsInteractionPrompt=true;
            state.PromptCanvas.PauseGameplayWhenVisible=false;
            state.PromptCanvas.ToggleWithMenuInput=false;
            state.PromptCanvas.CloseWithCancelInput=false;
            state.Label=prompt.AddComponent<UIText>();
            state.Label.Anchor=new Vector2(.5f,.9f); state.Label.FontSize=18;
        }
        state.Label!.Text="[" + RuntimeInput.ActionHint(nearby.InputAction) + "] " + nearby.Prompt;
        state.Label.FontPath=nearby.PromptFontPath;
        state.Label.FontSize=MathF.Max(4,nearby.PromptFontSize);
        state.PromptCanvas.IsVisible=true;
    }

    private static void End(Scene scene, State state)
    {
        while(scene.MenuNavigation.Current != null) scene.MenuNavigation.Back();
        if(state.ActiveRoot!=null) state.ActiveRoot.IsVisible=false;
        if(state.DialogueLabel!=null && state.OriginalText!=null) state.DialogueLabel.Text=state.OriginalText;
        state.ActiveRoot=null; state.ActiveInteraction=null; state.DialogueLabel=null;
        state.OriginalText=null; state.Pages=Array.Empty<string>(); state.Page=0;
    }
}
