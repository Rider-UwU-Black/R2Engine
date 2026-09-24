using System.Numerics;

namespace R2Engine.Editor.Scene;

public enum LightType
{
    Directional,
    Point,
    Spot
}

public enum LightBakeMode
{
    Realtime,
    Mixed,
    Baked
}

public class Light : Component
{
    public LightType Type =
        LightType.Directional;

    public Vector3 Color =
        Vector3.One;

    public float Intensity =
        1.0f;

    public float Range =
        10.0f;

    public float SpotAngle =
        45.0f;

    public LightBakeMode Mode = LightBakeMode.Realtime;
    public bool BakeOnExport
    {
        get => Mode != LightBakeMode.Realtime;
        set => Mode = value
            ? RealtimeEnabled ? LightBakeMode.Mixed : LightBakeMode.Baked
            : RealtimeEnabled ? LightBakeMode.Realtime : LightBakeMode.Baked;
    }
    public bool RealtimeEnabled
    {
        get => Mode != LightBakeMode.Baked;
        set => Mode = value
            ? BakeOnExport ? LightBakeMode.Mixed : LightBakeMode.Realtime
            : BakeOnExport ? LightBakeMode.Baked : LightBakeMode.Realtime;
    }
    public uint RealtimeLayerMask = uint.MaxValue;
}
