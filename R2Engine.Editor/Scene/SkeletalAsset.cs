using System.Numerics;
using System.Text.Json;
using R2Engine.Runtime.Platform;

namespace R2Engine.Editor.Scene;

public sealed class SkeletalAsset
{
    public string Name { get; set; } = "Character";
    public ModelImportSettings ImportSettings { get; set; } = new();
    public int BonePaletteLimit { get; set; } = 40;
    public int DeformBoneCount { get; set; }
    public List<SkeletalBone> Bones { get; set; } = new();
    public Dictionary<string, string> BoneMappings { get; set; } = new(StringComparer.Ordinal);
    public List<SkinnedVertex> Vertices { get; set; } = new();
    public List<uint> Indices { get; set; } = new();
    public List<SkeletalAnimationClip> Animations { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        IncludeFields = true
    };

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
    }

    public static SkeletalAsset Load(string path) =>
        JsonSerializer.Deserialize<SkeletalAsset>(RuntimePlatform.Files.ReadAllText(path), Options)
        ?? throw new InvalidDataException("Could not read skeletal asset.");
}

public sealed class SkeletalBone
{
    public string Name { get; set; } = "Bone";
    public int ParentIndex { get; set; } = -1;
    public Matrix4x4 LocalBindTransform { get; set; } = Matrix4x4.Identity;
    public Matrix4x4 InverseBindTransform { get; set; } = Matrix4x4.Identity;
}

public sealed class SkinnedVertex
{
    public Vector3 Position { get; set; }
    public Vector3 Normal { get; set; }
    public Vector2 UV { get; set; }
    public int[] BoneIndices { get; set; } = { 0, 0, 0, 0 };
    public float[] BoneWeights { get; set; } = { 1.0f, 0.0f, 0.0f, 0.0f };
}

public sealed class SkeletalAnimationClip
{
    public string Name { get; set; } = "Animation";
    public float Duration { get; set; }
    public bool ApplyRootMotion { get; set; }
    public List<BoneAnimationTrack> Tracks { get; set; } = new();
    public List<AnimationEventMarker> Events { get; set; } = new();
}

public sealed class AnimationEventMarker
{
    public string Name { get; set; } = "Event";
    public float Time { get; set; }
}

public sealed class BoneAnimationTrack
{
    public string BoneName { get; set; } = "";
    public List<VectorKeyframe> Positions { get; set; } = new();
    public List<QuaternionKeyframe> Rotations { get; set; } = new();
    public List<VectorKeyframe> Scales { get; set; } = new();
}

public sealed class VectorKeyframe
{
    public float Time { get; set; }
    public Vector3 Value { get; set; }
}

public sealed class QuaternionKeyframe
{
    public float Time { get; set; }
    public Quaternion Value { get; set; } = Quaternion.Identity;
}
