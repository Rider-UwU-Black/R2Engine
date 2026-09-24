using System.Text.Json;
using System.Text.Json.Serialization;
using R2Engine.Editor.Scene;
using R2Engine.Runtime.Platform;

namespace R2Engine.Editor;

public enum GameWindowMode
{
    Windowed,
    Borderless,
    Fullscreen
}

public enum Ps2DisplayAspect
{
    Standard4x3,
    Widescreen16x9
}

public enum RenderQualityPreset
{
    Performance,
    Balanced,
    Quality,
    Custom
}

public enum RenderTextureFiltering
{
    Nearest,
    Bilinear
}

public sealed class RenderSettings
{
    public RenderQualityPreset Preset { get; set; } = RenderQualityPreset.Balanced;
    public int TargetWidth { get; set; } = 640;
    public int TargetHeight { get; set; } = 448;
    public RenderTextureFiltering TextureFiltering { get; set; } = RenderTextureFiltering.Bilinear;
    public bool FrustumCulling { get; set; } = true;
    public bool EnableFog { get; set; } = true;
    public int MaxLocalLights { get; set; } = 2;

    public void ApplyPreset(RenderQualityPreset preset)
    {
        Preset = preset;
        switch (preset)
        {
            case RenderQualityPreset.Performance:
                TargetWidth = 512; TargetHeight = 384;
                TextureFiltering = RenderTextureFiltering.Nearest;
                FrustumCulling = true; EnableFog = false; MaxLocalLights = 1;
                break;
            case RenderQualityPreset.Balanced:
                TargetWidth = 640; TargetHeight = 448;
                TextureFiltering = RenderTextureFiltering.Bilinear;
                FrustumCulling = true; EnableFog = true; MaxLocalLights = 2;
                break;
            case RenderQualityPreset.Quality:
                TargetWidth = 640; TargetHeight = 480;
                TextureFiltering = RenderTextureFiltering.Bilinear;
                FrustumCulling = true; EnableFog = true; MaxLocalLights = 3;
                break;
        }
    }
}

public sealed class ProjectSettings
{
    public string ProductName { get; set; } = "R2 Game";
    public string StartupScene { get; set; } = "";
    public string LoadingScreenPrefab { get; set; } = "";
    public List<string> BuildScenes { get; set; } = new();
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public GameWindowMode WindowMode { get; set; } = GameWindowMode.Windowed;
    public bool VSync { get; set; } = true;
    public int FrameRateLimit { get; set; } = 60;
    public bool DevelopmentBuild { get; set; } = true;
    public bool Ps2ProgressiveScan { get; set; } = true;
    public Ps2DisplayAspect Ps2AspectRatio { get; set; } = Ps2DisplayAspect.Standard4x3;
    public bool Ps2BakeStaticVertexLighting { get; set; } = true;
    public string Ps2MemoryCardTitle { get; set; } = "";
    public string Ps2MemoryCardIcon { get; set; } = "";
    public string Ps2SaveId { get; set; } = "";
    public bool Ps2ImportLegacySaves { get; set; }
    public string Ps2NetworkBuildDirectory { get; set; } = "";
    public string BuildOutput { get; set; } = "Builds/Windows";
    public List<InputActionBinding> InputActions { get; set; } = InputActionBinding.CreateDefaults();
    public PerformanceBudgets PerformanceBudgets { get; set; } = new();
    public RenderSettings RenderSettings { get; set; } = new();
    public List<string> CollisionLayers { get; set; } = CreateDefaultCollisionLayers();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static ProjectSettings Load(string path)
    {
        if (!RuntimePlatform.Files.FileExists(path))
            return new ProjectSettings();

        ProjectSettings settings = JsonSerializer.Deserialize<ProjectSettings>(RuntimePlatform.Files.ReadAllText(path), JsonOptions)
            ?? new ProjectSettings();
        if (settings.InputActions == null || settings.InputActions.Count == 0)
            settings.InputActions = InputActionBinding.CreateDefaults();
        ApplyDefaultControllerBindings(settings.InputActions);
        settings.PerformanceBudgets ??= new PerformanceBudgets();
        settings.RenderSettings ??= new RenderSettings();
        settings.RenderSettings.TargetWidth = Math.Max(160, settings.RenderSettings.TargetWidth);
        settings.RenderSettings.TargetHeight = Math.Max(120, settings.RenderSettings.TargetHeight);
        settings.RenderSettings.MaxLocalLights = Math.Clamp(settings.RenderSettings.MaxLocalLights, 0, 3);
        if (settings.CollisionLayers == null || settings.CollisionLayers.Count == 0)
            settings.CollisionLayers = CreateDefaultCollisionLayers();
        if (settings.CollisionLayers.Count > 32)
            settings.CollisionLayers = settings.CollisionLayers.Take(32).ToList();
        return settings;
    }

    public void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));

    private static void ApplyDefaultControllerBindings(List<InputActionBinding> actions)
    {
        foreach (InputActionBinding action in actions)
        {
            if (string.Equals(action.Name, "Move", StringComparison.OrdinalIgnoreCase) &&
                action.ControllerAxisX < 0 && action.ControllerAxisY < 0)
            { action.ControllerAxisX = 0; action.ControllerAxisY = 1; }
            else if (string.Equals(action.Name, "Turn", StringComparison.OrdinalIgnoreCase) && action.ControllerAxis < 0)
                action.ControllerAxis = 2;
            else if (string.Equals(action.Name, "Sprint", StringComparison.OrdinalIgnoreCase) && action.ControllerButton < 0)
                action.ControllerButton = 1;
            else if (string.Equals(action.Name, "Jump", StringComparison.OrdinalIgnoreCase) && action.ControllerButton < 0)
                action.ControllerButton = 0;
        }
    }

    private static List<string> CreateDefaultCollisionLayers() => new()
    {
        "Default", "Player", "Environment", "Enemy",
        "Trigger", "Pickup", "Projectile", "Reserved"
    };
}

public sealed class PerformanceBudgets
{
    public int TargetFps { get; set; } = 60;
    public int MaxDrawCalls { get; set; } = 120;
    public int MaxVisibleTriangles { get; set; } = 100000;
    public int MaxVisibleObjects { get; set; } = 200;
    public int MaxTextures { get; set; } = 64;
    public int MaxTextureVramKilobytes { get; set; } = 2048;
    public int MaxActiveLights { get; set; } = 4;
    public int MaxSkinnedMeshes { get; set; } = 8;
}
