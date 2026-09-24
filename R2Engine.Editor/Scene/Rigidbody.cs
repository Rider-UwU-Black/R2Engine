using System.Numerics;

namespace R2Engine.Editor.Scene;

public enum RigidbodyBodyType
{
    Static,
    Kinematic,
    Dynamic
}

public sealed class Rigidbody : Component
{
    public RigidbodyBodyType BodyType = RigidbodyBodyType.Dynamic;
    public Vector3 Velocity = Vector3.Zero;
    public float GravityScale = 1.0f;
    public float LinearDrag;
    public float Mass = 1.0f;
    public float Restitution;
    public float Friction = 0.5f;
    public bool IsGrounded { get; internal set; }

    public void FixedUpdate(float fixedDeltaTime, CollisionSystem collisionSystem, Scene scene)
    {
        IsGrounded = false;

        if (BodyType == RigidbodyBodyType.Static)
        {
            Velocity = Vector3.Zero;
            return;
        }

        if (BodyType == RigidbodyBodyType.Dynamic)
            Velocity.Y += -9.81f * GravityScale * fixedDeltaTime;

        if (LinearDrag > 0.0f)
            Velocity *= 1.0f / (1.0f + LinearDrag * fixedDeltaTime);

        Vector3 requested = Velocity * fixedDeltaTime;
        Vector3 moved = collisionSystem.Move(GameObject, requested, scene);

        const float blockedTolerance = 0.0001f;

        if (MathF.Abs(moved.X - requested.X) > blockedTolerance)
            collisionSystem.ResolveBlockedVelocity(this, Vector3.UnitX * MathF.Sign(requested.X), scene);

        if (MathF.Abs(moved.Y - requested.Y) > blockedTolerance)
        {
            IsGrounded = requested.Y < 0.0f;
            collisionSystem.ResolveBlockedVelocity(this, Vector3.UnitY * MathF.Sign(requested.Y), scene);
        }

        if (MathF.Abs(moved.Z - requested.Z) > blockedTolerance)
            collisionSystem.ResolveBlockedVelocity(this, Vector3.UnitZ * MathF.Sign(requested.Z), scene);
    }
}
