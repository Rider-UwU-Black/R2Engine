namespace R2Engine.Editor.Scene;

public sealed class PersistentObject : Component
{
    public string SaveId { get; set; } = "OBJECT";
    public bool SaveTransform { get; set; } = true;
    public bool SaveActiveState { get; set; } = true;
    public bool SaveInteger { get; set; }
    public int IntegerValue { get; set; }
    public bool SaveBoolean { get; set; }
    public bool BooleanValue { get; set; }

    public void SetInteger(int value) => IntegerValue = value;
    public int GetInteger() => IntegerValue;
    public void SetBoolean(bool value) => BooleanValue = value;
    public bool GetBoolean() => BooleanValue;
}
