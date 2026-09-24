using System.Numerics;
using System.Runtime.CompilerServices;

namespace R2Engine.Editor.Scene;

public sealed class SlidingDoor : Component
{
    public float Lift = 3.3f;
    public float Speed = 2f;
    public float Range = 3f;
    public bool Opening { get; private set; }
    public float Progress { get; private set; }
    private Vector3? _closed;
    public Vector3 ClosedPosition => _closed ?? GameObject.Transform.WorldPosition;
    public void Open() { _closed ??= GameObject.Transform.WorldPosition; Opening = true; }
    public void Tick(float dt)
    {
        _closed ??= GameObject.Transform.WorldPosition;
        if (!Opening || !float.IsFinite(dt) || dt <= 0 || !float.IsFinite(Speed) || Speed <= 0 || !float.IsFinite(Lift) || Lift <= 0) return;
        Progress = MathF.Min(Lift, Progress + Speed * dt);
        GameObject.Transform.SetWorldPosition(_closed.Value + Vector3.UnitY * Progress);
    }
    private sealed class PromptState { public Canvas? Canvas; public UIText? Text; }
    private static readonly ConditionalWeakTable<Scene, PromptState> Prompts = new();
    public static SlidingDoor? FindNearest(Scene scene)
    {
        if (Interactable.ActiveCanvas(scene) != null || scene.GameObjects.Any(o => o.IsActiveInHierarchy &&
            o.GetComponent<Canvas>() is {IsVisible:true,IsInteractionPrompt:false} c &&
            (c.PauseGameplayWhenVisible || scene.GameObjects.Any(child => Canvas.FindOwner(child)==c && child.GetComponent<UIButton>()!=null)))) return null;
        var player=scene.GameObjects.FirstOrDefault(o=>o.IsActiveInHierarchy && o.GetComponent<PlayerController>()!=null);
        if(player==null)return null;
        return scene.GameObjects.Where(o=>o.IsActiveInHierarchy).Select(o=>o.GetComponent<SlidingDoor>())
            .Where(d=>d!=null && !d.Opening && float.IsFinite(d.Range) && d.Range>0 && Vector3.DistanceSquared(player.Transform.WorldPosition,d.ClosedPosition)<=d.Range*d.Range)
            .OrderBy(d=>Vector3.DistanceSquared(player.Transform.WorldPosition,d!.ClosedPosition)).FirstOrDefault();
    }
    public static void Update(Scene scene,float dt)
    {
        var state=Prompts.GetOrCreateValue(scene);
        if(state.Canvas!=null)state.Canvas.IsVisible=false;
        // Dialogue/sign interactions take precedence where their ranges overlap.
        var door=RuntimeInput.InteractionConsumed || Interactable.FindNearest(scene)!=null ? null : FindNearest(scene);
        if(door!=null)
        {
            if(RuntimeInput.WasActionPressed("Interact")){door.Open();RuntimeInput.InteractionConsumed=true;}
            else
            {
                if(state.Canvas==null){var o=scene.CreateGameObject("__DoorPrompt");state.Canvas=o.AddComponent<Canvas>();state.Canvas.IsInteractionPrompt=true;state.Canvas.PauseGameplayWhenVisible=false;state.Canvas.ToggleWithMenuInput=false;state.Canvas.CloseWithCancelInput=false;state.Text=o.AddComponent<UIText>();state.Text.Anchor=new Vector2(.5f,.9f);}
                state.Text!.Text="["+RuntimeInput.ActionHint("Interact")+"] Open";state.Canvas.IsVisible=true;
            }
        }
        foreach(var o in scene.GameObjects.Where(o=>o.IsActiveInHierarchy))o.GetComponent<SlidingDoor>()?.Tick(dt);
    }
    public static void BuildDoorway(GameObject root)
    {
        var scene=root.Scene ?? throw new InvalidOperationException("Doorway needs a scene.");
        GameObject Part(string name,Vector3 pos,Vector3 scale,Vector4 color)
        {
            var o=scene.CreateGameObject(name);o.SetParent(root,false);o.Transform.Position=pos;o.Transform.Scale=scale;
            o.AddComponent<MeshRenderer>().LocalMaterial.BaseColor=color;o.AddComponent<BoxCollider>();return o;
        }
        var dark=new Vector4(.16f,.2f,.25f,1);
        Part("Left jamb",new(-1.35f,1.75f,0),new(.35f,3.5f,.6f),dark);
        Part("Right jamb",new(1.35f,1.75f,0),new(.35f,3.5f,.6f),dark);
        Part("Header",new(0,3.4f,0),new(3.05f,.4f,.6f),dark);
        Part("Door panel",new(0,1.5f,0),new(2.35f,3,.25f),new(.24f,.48f,.58f,1)).AddComponent<SlidingDoor>();
    }
}
