using System.Numerics;

namespace R2Engine.Editor;

// SceneRenderer still exposes editor-view rendering overloads. The standalone
// desktop backend only needs their camera state shape, not ImGui input handling.
public sealed class EditorCamera
{
    public Vector3 Position = new(4.0f, 3.0f, 6.0f);
    public float Yaw = -34.0f;
    public float Pitch = -18.0f;
}
