using System.Numerics;

namespace R2Engine.Editor.Scene;

public class Rotator : Component
{
    public Vector3 Axis =
        Vector3.UnitY;

    public float DegreesPerSecond =
        90.0f;

    public void Update(float deltaTime)
    {
        Vector3 axis =
            Axis;

        if (axis.LengthSquared() < 0.000001f)
            return;

        axis =
            Vector3.Normalize(axis);

        GameObject.Transform.Rotation +=
            axis *
            DegreesPerSecond *
            deltaTime;
    }
}
