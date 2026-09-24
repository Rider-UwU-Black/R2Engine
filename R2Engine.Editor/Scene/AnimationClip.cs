using System.Numerics;
using System.Text.Json;
using R2Engine.Runtime.Platform;

namespace R2Engine.Editor.Scene;

public sealed class AnimationKeyframe
{
    public float Time { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Rotation { get; set; }
    public Vector3 Scale { get; set; } = Vector3.One;
}

public sealed class AnimationClip
{
    public string Name { get; set; } = "Animation";
    public float Duration { get; set; } = 1.0f;
    public List<AnimationKeyframe> Keyframes { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        IncludeFields = true
    };

    public static AnimationClip Load(string path) =>
        JsonSerializer.Deserialize<AnimationClip>(RuntimePlatform.Files.ReadAllText(path), Options)
        ?? throw new InvalidDataException("Could not read animation clip.");

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
    }

    public void Sample(float time, out Vector3 position, out Vector3 rotation, out Vector3 scale)
    {
        if (Keyframes.Count == 0)
        {
            position = Vector3.Zero;
            rotation = Vector3.Zero;
            scale = Vector3.One;
            return;
        }

        List<AnimationKeyframe> frames = Keyframes.OrderBy(frame => frame.Time).ToList();
        if (time <= frames[0].Time)
        {
            position = frames[0].Position;
            rotation = frames[0].Rotation;
            scale = frames[0].Scale;
            return;
        }

        AnimationKeyframe last = frames[^1];
        if (time >= last.Time)
        {
            position = last.Position;
            rotation = last.Rotation;
            scale = last.Scale;
            return;
        }

        for (int index = 0; index < frames.Count - 1; index++)
        {
            AnimationKeyframe from = frames[index];
            AnimationKeyframe to = frames[index + 1];
            if (time > to.Time)
                continue;
            float amount = Math.Clamp((time - from.Time) / Math.Max(0.0001f, to.Time - from.Time), 0.0f, 1.0f);
            position = Vector3.Lerp(from.Position, to.Position, amount);
            rotation = Vector3.Lerp(from.Rotation, to.Rotation, amount);
            scale = Vector3.Lerp(from.Scale, to.Scale, amount);
            return;
        }

        position = last.Position;
        rotation = last.Rotation;
        scale = last.Scale;
    }
}
