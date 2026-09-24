using System.Text.Json;
using R2Engine.Runtime.Platform;

namespace R2Engine.Editor.Scene;

public sealed class HumanoidAnimationAsset
{
    public int RetargetVersion { get; set; }
    public string Name { get; set; } = "Animation";
    public AnimationImportSettings ImportSettings { get; set; } = new();
    public List<string> BoneNames { get; set; } = new();
    public Dictionary<string, string> BoneMappings { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, System.Numerics.Quaternion> SourceBindRotations { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, System.Numerics.Vector3> SourceBindPositions { get; set; } = new(StringComparer.Ordinal);
    public List<SkeletalAnimationClip> Clips { get; set; } = new();

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

    public static HumanoidAnimationAsset Load(string path) =>
        JsonSerializer.Deserialize<HumanoidAnimationAsset>(RuntimePlatform.Files.ReadAllText(path), Options)
        ?? throw new InvalidDataException("Could not read humanoid animation asset.");
}
