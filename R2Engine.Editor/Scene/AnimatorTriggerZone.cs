namespace R2Engine.Editor.Scene;

/// <summary>Fires a named Animator trigger when a player enters this object's trigger collider.</summary>
public sealed class AnimatorTriggerZone : Component
{
    public string TargetAnimator { get; set; } = "";
    public string TriggerName { get; set; } = "Wave";

    public void NotifyEntry(GameObject other)
    {
        if (other.GetComponent<PlayerController>() == null || GameObject.GetComponent<BoxCollider>()?.IsTrigger != true)
            return;
        var target = GameObject.Scene?.GameObjects.FirstOrDefault(o => o.Name == TargetAnimator && o.IsActiveInHierarchy);
        target?.GetComponent<Animator>()?.SetTrigger(TriggerName);
    }
}
