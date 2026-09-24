using System.Numerics;
using System.Text.Json;
using R2Engine.Runtime.Platform;

namespace R2Engine.Editor.Scene;

public sealed class AnimatorController
{
    public string Name { get; set; } = "Animator Controller";
    public string DefaultState { get; set; } = "";
    public string SkeletonPath { get; set; } = "";
    public List<AnimatorState> States { get; set; } = new();
    public List<AnimatorTransition> Transitions { get; set; } = new();
    public List<AnimatorParameter> Parameters { get; set; } = new();

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
            IncludeFields = true
        }));
    }

    public static AnimatorController Load(string path) =>
        JsonSerializer.Deserialize<AnimatorController>(RuntimePlatform.Files.ReadAllText(path), new JsonSerializerOptions
        {
            IncludeFields = true
        }) ?? throw new InvalidDataException("Could not read Animator Controller.");
}

public sealed class AnimatorState
{
    public string Name { get; set; } = "State";
    public string AnimationTake { get; set; } = "";
    public string AnimationPath { get; set; } = "";
    public Vector2 Position { get; set; }
    public bool Loop { get; set; } = true;
    public float Speed { get; set; } = 1.0f;
    public bool Reverse { get; set; }
    public bool LockMovement { get; set; }
}

public sealed class AnimatorTransition
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Parameter { get; set; } = "";
    public AnimatorCondition Condition { get; set; }
    public float Threshold { get; set; }
    public float BlendDuration { get; set; } = 0.15f;
}

public enum AnimatorCondition { IsTrue, IsFalse, Greater, Less, Equals, Triggered, Finished, RandomFinished }
public enum AnimatorParameterType { Float, Integer, Boolean, Trigger }

public sealed class AnimatorParameter
{
    public string Name { get; set; } = "Parameter";
    public AnimatorParameterType Type { get; set; }
    public float FloatValue { get; set; }
    public int IntegerValue { get; set; }
    public bool BoolValue { get; set; }
}
