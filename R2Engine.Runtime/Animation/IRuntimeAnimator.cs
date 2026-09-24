namespace R2Engine.Editor.Scene;

/// <summary>Minimal parameter surface used by runtime gameplay components.</summary>
public interface IRuntimeAnimator
{
    bool MovementLocked { get; }
    void SetFloat(string name, float value);
    void SetBool(string name, bool value);
    void SetTrigger(string name);
}
