using System.Numerics;

namespace R2Engine.Editor.Scene;

public sealed class ModelImportSettings
{
    public string SourcePath { get; set; } = "";
    public long SourceLastWriteUtcTicks { get; set; }
    public float Scale { get; set; } = 1.0f;
    public Vector3 Rotation { get; set; } = Vector3.Zero;
    public bool AutoCorrectOrientation { get; set; } = true;
    public int SkeletonLimit { get; set; } = 128;
}

public sealed class AnimationImportSettings
{
    public string SourcePath { get; set; } = "";
    public long SourceLastWriteUtcTicks { get; set; }
    public float SpeedScale { get; set; } = 1.0f;
}

public sealed class FbxImporterSettings
{
    public int Version { get; set; } = 1;
    public long SourceLastWriteUtcTicks { get; set; }
    public bool ImportModel { get; set; } = true;
    public bool ImportAnimations { get; set; } = true;
    public float Scale { get; set; } = 1.0f;
    public Vector3 Rotation { get; set; } = Vector3.Zero;
    public bool AutoCorrectOrientation { get; set; } = true;
    public int SkeletonLimit { get; set; } = 128;
    public float AnimationSpeedScale { get; set; } = 1.0f;
    public string GeneratedModelPath { get; set; } = "";
    public string GeneratedAnimationPath { get; set; } = "";
}

public enum Ps2TextureFormat
{
    Rgba32,
    Rgba16,
    Indexed8,
    Indexed4
}

public enum TextureFilterOverride
{
    ProjectDefault,
    Nearest,
    Bilinear
}

public sealed class TextureImportSettings
{
    public int MaxSize { get; set; }
    public Ps2TextureFormat Format { get; set; } = Ps2TextureFormat.Rgba32;
    public TextureFilterOverride Filtering { get; set; } = TextureFilterOverride.ProjectDefault;
    public bool GenerateMipmaps { get; set; }
}

public sealed class AudioImportSettings
{
    public int BitDepth { get; set; } = 16;
    public int MaximumSampleRate { get; set; } = 48000;
}
