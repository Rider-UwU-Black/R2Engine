using System.Numerics;

namespace R2Engine.Editor.Scene;

public enum MaterialLightingMode
{
    Lit,
    Unlit,
    ObjectSmooth
}

public enum MaterialSurfaceMode
{
    Opaque,
    Cutout,
    Transparent
}

public class Material
{
    public string Name { get; set; } =
        "Material";

    public Vector4 BaseColor =
        new Vector4(
            0.72f,
            0.74f,
            0.78f,
            1.0f);

    public string? TexturePath { get; set; }

    public Vector2 TextureTiling { get; set; } = Vector2.One;

    public Vector2 TextureOffset { get; set; } = Vector2.Zero;

    public MaterialLightingMode LightingMode { get; set; } =
        MaterialLightingMode.Lit;

    public MaterialSurfaceMode SurfaceMode { get; set; } =
        MaterialSurfaceMode.Opaque;

    public float AlphaCutoff { get; set; } =
        0.5f;

    public bool DoubleSided { get; set; }

    public bool ReceiveFog { get; set; } =
        true;

    public Material()
    {
    }

    public Material(
        string name)
    {
        Name =
            name;
    }

    public Material Clone()
    {
        return new Material(
            Name)
        {
            BaseColor =
                BaseColor,

            TexturePath =
                TexturePath,

            TextureTiling = TextureTiling,
            TextureOffset = TextureOffset,

            LightingMode = LightingMode,
            SurfaceMode = SurfaceMode,
            AlphaCutoff = AlphaCutoff,
            DoubleSided = DoubleSided,
            ReceiveFog = ReceiveFog
        };
    }

    public void CopyFrom(
        Material other)
    {
        Name =
            other.Name;

        BaseColor =
            other.BaseColor;

        TexturePath =
            other.TexturePath;

        TextureTiling = other.TextureTiling;
        TextureOffset = other.TextureOffset;

        LightingMode = other.LightingMode;
        SurfaceMode = other.SurfaceMode;
        AlphaCutoff = other.AlphaCutoff;
        DoubleSided = other.DoubleSided;
        ReceiveFog = other.ReceiveFog;
    }
}
