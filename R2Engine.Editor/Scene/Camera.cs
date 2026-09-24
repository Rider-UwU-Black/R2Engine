namespace R2Engine.Editor.Scene;

public class Camera : Component
{
    public bool IsPrimary;
    public float FieldOfView = 60.0f;
    public float NearClip = 0.1f;
    public float FarClip = 1000.0f;
    public GameObject? AudioListenerOverride;
}
