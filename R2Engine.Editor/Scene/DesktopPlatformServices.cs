using R2Engine.Runtime.Platform;
using R2Engine.Runtime.Rendering;
using R2Engine.Runtime.Audio;

namespace R2Engine.Editor.Scene;

public sealed class DesktopPlatformServices : IRuntimePlatformServices
{
    public string PlatformName { get; }
    public IRuntimeFileSystem Files { get; } = new ManagedFileSystem();
    public IRuntimeClock Clock { get; } = new StopwatchClock();
    public IRuntimeLogger Log { get; } = new ConsoleRuntimeLogger();
    public IRuntimeInputBackend? Input { get; }
    public IRuntimeAudioBackend? Audio { get; }
    public IRuntimeRenderBackend? Renderer { get; }

    public DesktopPlatformServices(
        string platformName,
        Action pollInput,
        IRuntimeSceneRenderer? sceneRenderer = null,
        IRuntimeAudioSystem? audioSystem = null)
    {
        PlatformName = platformName;
        Input = new DelegateInputBackend("Silk.NET Desktop Input", pollInput);
        Renderer = new NamedRenderBackend("OpenGL Desktop", sceneRenderer);
        Audio = new NamedAudioBackend("OpenAL Desktop", audioSystem);
        RuntimeAssetServices.MeshLoader = MeshAssetCache.Get;
        RuntimeAssetServices.MaterialLoader = MaterialAssetCache.Get;
        RuntimeAssetServices.SkeletalAssetLoader = MeshAssetCache.GetSkeletalAsset;
        RuntimeAssetServices.AssetPathResolver = AssetDatabase.ToAbsolutePath;
    }

    private sealed class DelegateInputBackend : IRuntimeInputBackend
    {
        private readonly Action _poll;
        public string Name { get; }
        public DelegateInputBackend(string name, Action poll) { Name = name; _poll = poll; }
        public void Poll() => _poll();
    }

    private sealed class NamedAudioBackend : IRuntimeAudioBackend
    {
        public string Name { get; }
        public IRuntimeAudioSystem? AudioSystem { get; private set; }
        public NamedAudioBackend(string name, IRuntimeAudioSystem? audioSystem)
        {
            Name = name;
            AudioSystem = audioSystem;
        }
        public void Attach(IRuntimeAudioSystem? audioSystem) => AudioSystem = audioSystem;
    }

    private sealed class NamedRenderBackend : IRuntimeRenderBackend
    {
        public string Name { get; }
        public IRuntimeSceneRenderer? SceneRenderer { get; }
        public NamedRenderBackend(string name, IRuntimeSceneRenderer? sceneRenderer)
        {
            Name = name;
            SceneRenderer = sceneRenderer;
        }
    }

    public void AttachAudioSystem(IRuntimeAudioSystem? audioSystem)
    {
        if (Audio is NamedAudioBackend backend)
            backend.Attach(audioSystem);
    }
}
