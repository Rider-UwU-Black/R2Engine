using System.Diagnostics;
using R2Engine.Runtime.Rendering;
using R2Engine.Runtime.Audio;

namespace R2Engine.Runtime.Platform;

public interface IRuntimeFileSystem
{
    bool FileExists(string path);
    string ReadAllText(string path);
    byte[] ReadAllBytes(string path);
    Stream OpenRead(string path);
}

public interface IRuntimeClock
{
    double TotalSeconds { get; }
}

public interface IRuntimeLogger
{
    void Info(string message);
    void Warning(string message);
    void Error(string message);
}

public interface IRuntimeInputBackend
{
    string Name { get; }
    void Poll();
}

public interface IRuntimeAudioBackend
{
    string Name { get; }
    IRuntimeAudioSystem? AudioSystem { get; }
}

public interface IRuntimeRenderBackend
{
    string Name { get; }
    IRuntimeSceneRenderer? SceneRenderer { get; }
}

public interface IRuntimePlatformServices
{
    string PlatformName { get; }
    IRuntimeFileSystem Files { get; }
    IRuntimeClock Clock { get; }
    IRuntimeLogger Log { get; }
    IRuntimeInputBackend? Input { get; }
    IRuntimeAudioBackend? Audio { get; }
    IRuntimeRenderBackend? Renderer { get; }
}

public static class RuntimePlatform
{
    private static IRuntimePlatformServices _services = new ManagedDesktopPlatformServices();

    public static IRuntimePlatformServices Services => _services;
    public static IRuntimeFileSystem Files => _services.Files;
    public static IRuntimeClock Clock => _services.Clock;
    public static IRuntimeLogger Log => _services.Log;

    public static void Configure(IRuntimePlatformServices services) =>
        _services = services ?? throw new ArgumentNullException(nameof(services));
}

public sealed class ManagedDesktopPlatformServices : IRuntimePlatformServices
{
    public string PlatformName => "Managed Desktop";
    public IRuntimeFileSystem Files { get; } = new ManagedFileSystem();
    public IRuntimeClock Clock { get; } = new StopwatchClock();
    public IRuntimeLogger Log { get; } = new ConsoleRuntimeLogger();
    public IRuntimeInputBackend? Input => null;
    public IRuntimeAudioBackend? Audio => null;
    public IRuntimeRenderBackend? Renderer => null;
}

public sealed class ManagedFileSystem : IRuntimeFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
    public string ReadAllText(string path) => File.ReadAllText(path);
    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);
    public Stream OpenRead(string path) => File.OpenRead(path);
}

public sealed class StopwatchClock : IRuntimeClock
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    public double TotalSeconds => _stopwatch.Elapsed.TotalSeconds;
}

public sealed class ConsoleRuntimeLogger : IRuntimeLogger
{
    public void Info(string message) => Console.WriteLine(message);
    public void Warning(string message) => Console.WriteLine($"Warning: {message}");
    public void Error(string message) => Console.Error.WriteLine(message);
}
