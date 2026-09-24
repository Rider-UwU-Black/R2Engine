namespace R2Engine.Editor.Scene;

public sealed class SavePoint : Component
{
    public string SlotName { get; set; } = "AUTOSAVE";
    public bool SaveOnTriggerEnter { get; set; } = true;
}
