using System.Numerics;

namespace R2Engine.Editor.Scene;

public abstract class Collider : Component
{
    public Vector3 Center = Vector3.Zero;
    public bool IsTrigger;
    public int Layer;
    public uint CollisionMask = uint.MaxValue;
}

public class BoxCollider : Collider
{
    public Vector3 Size = Vector3.One;
}

// A finite plane is stored as a very thin box so all collision paths retain
// stable two-sided contact while using the constant-time box tests.
public sealed class PlaneCollider : BoxCollider
{
    public const float Thickness = 0.02f;
}

public sealed class SphereCollider : Collider
{
    public float Radius = 0.5f;
}

public sealed class CapsuleCollider : Collider
{
    public float Radius = 0.5f;
    public float Height = 2.0f;
}

public sealed class MeshCollider : Collider
{
}
