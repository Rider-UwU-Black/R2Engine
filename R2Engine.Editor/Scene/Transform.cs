using System.Numerics;

namespace R2Engine.Editor.Scene;

public class Transform : Component
{
    public Vector3 Position = Vector3.Zero;
    public Vector3 Rotation = Vector3.Zero;
    public Vector3 Scale = Vector3.One;

    public Vector3 WorldScale => GameObject.Parent == null
        ? Scale
        : GameObject.Parent.Transform.WorldScale * Scale;

    public Vector3 WorldRotation => GameObject.Parent == null
        ? Rotation
        : GameObject.Parent.Transform.WorldRotation + Rotation;

    public Vector3 WorldPosition
    {
        get
        {
            if (GameObject.Parent == null)
                return Position;

            Transform parent = GameObject.Parent.Transform;
            Vector3 scaled = Position * parent.WorldScale;
            return parent.WorldPosition + Vector3.Transform(scaled, RotationQuaternion(parent.WorldRotation));
        }
    }

    public void SetWorldTransform(Vector3 position, Vector3 rotation, Vector3 scale)
    {
        if (GameObject.Parent == null)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
            return;
        }

        Transform parent = GameObject.Parent.Transform;
        Quaternion inverseRotation = Quaternion.Inverse(RotationQuaternion(parent.WorldRotation));
        Vector3 unrotated = Vector3.Transform(position - parent.WorldPosition, inverseRotation);
        Vector3 parentScale = parent.WorldScale;
        Position = new Vector3(
            parentScale.X == 0.0f ? 0.0f : unrotated.X / parentScale.X,
            parentScale.Y == 0.0f ? 0.0f : unrotated.Y / parentScale.Y,
            parentScale.Z == 0.0f ? 0.0f : unrotated.Z / parentScale.Z);
        Rotation = rotation - parent.WorldRotation;
        Scale = new Vector3(
            parentScale.X == 0.0f ? scale.X : scale.X / parentScale.X,
            parentScale.Y == 0.0f ? scale.Y : scale.Y / parentScale.Y,
            parentScale.Z == 0.0f ? scale.Z : scale.Z / parentScale.Z);
    }

    public void SetWorldPosition(Vector3 position) =>
        SetWorldTransform(position, WorldRotation, WorldScale);

    private static Quaternion RotationQuaternion(Vector3 degrees) =>
        Quaternion.CreateFromYawPitchRoll(
            degrees.Y * MathF.PI / 180.0f,
            degrees.X * MathF.PI / 180.0f,
            degrees.Z * MathF.PI / 180.0f);
}
