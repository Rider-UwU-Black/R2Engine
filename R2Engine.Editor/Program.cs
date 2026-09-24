using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Core;
using StbImageSharp;
using R2Engine.Editor.Scene;
using R2Engine.Runtime.Platform;
using System.Runtime.InteropServices;

namespace R2Engine.Editor;

internal static class Program
{
    private static IWindow? _window;
    private static GL? _gl;
    private static IInputContext? _input;

    private static ImGuiController? _imgui;
    private static EditorUI? _editorUI;
    private static bool _normalWindowRestored;
    private static nint _nativeWindowHandle;

    private static int Main(string[] args)
    {
        int projectArgument = Array.FindIndex(args,
            argument => string.Equals(argument, "--project", StringComparison.OrdinalIgnoreCase));
        if (projectArgument >= 0)
        {
            if (projectArgument + 1 >= args.Length)
            {
                Console.Error.WriteLine("--project requires a project folder.");
                return 1;
            }

            try
            {
                string projectRoot = Path.GetFullPath(args[projectArgument + 1]);
                AssetDatabase.ConfigureProjectRoot(projectRoot);
                Directory.SetCurrentDirectory(projectRoot);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Could not open project: {exception.Message}");
                return 1;
            }
        }

        if(args.Length==3 && args[0]=="--import-font")
        {
            try { Console.WriteLine(FontAssetImporter.Import(args[1],args[2])); return 0; }
            catch(Exception exception) { Console.Error.WriteLine(exception); return 1; }
        }
        if (args.Length == 3 && args[0] == "--convert-wav")
        {
            try
            {
                WavAssetImporter.ConvertToRuntimeWav(args[1], args[2]);
                Console.WriteLine($"Converted WAV: {args[2]}");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }
        if (args.Length >= 4 && args[0] == "--cook-ps2-build")
        {
            try
            {
                RuntimePlatform.Configure(new DesktopPlatformServices("PS2 Cooker", () => { }));
                string buildPath = Path.GetFullPath(args[1]);
                string[] scenePaths = args.Skip(2).Select(Path.GetFullPath).ToArray();
                BuildAssetCollection assets = BuildAssetCollector.Collect(scenePaths);
                if (assets.MissingReferences.Count > 0)
                    throw new InvalidOperationException(
                        "PS2 cook asset validation failed: " + string.Join(" | ", assets.MissingReferences));
                MeshPackageExporter.Export(assets, buildPath);
                TexturePackageExporter.Export(scenePaths, buildPath);
                Ps2AnimationPackageExportResult result = Ps2AnimationPackageExporter.Export(scenePaths, buildPath);
                ScenePackageExportResult scenes = ScenePackageExporter.Export(scenePaths, buildPath);
                Console.WriteLine($"Cooked {scenes.SceneCount} PS2 scene(s), {scenes.InstanceCount} instance(s), " +
                    $"and {result.AnimationCount} animation package(s).");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        if (args.Length == 3 && args[0] == "--cook-ps2-animation")
        {
            try
            {
                RuntimePlatform.Configure(new DesktopPlatformServices("PS2 Cooker", () => { }));
                string scenePath = Path.GetFullPath(args[1]);
                string buildPath = Path.GetFullPath(args[2]);
                BuildAssetCollection assets = BuildAssetCollector.Collect(new[] { scenePath });
                if (assets.MissingReferences.Count > 0)
                    throw new InvalidOperationException(
                        "PS2 cook asset validation failed: " + string.Join(" | ", assets.MissingReferences));
                MeshPackageExporter.Export(assets, buildPath);
                TexturePackageExporter.Export(new[] { scenePath }, buildPath);
                Ps2AnimationPackageExportResult result = Ps2AnimationPackageExporter.Export(
                    new[] { scenePath }, buildPath);
                ScenePackageExporter.Export(
                    new[] { scenePath }, buildPath);
                Console.WriteLine($"Cooked {result.AnimationCount} PS2 animation package(s), {result.TotalBytes} bytes.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        if (args.Length == 3 && args[0] == "--import-skeletal")
        {
            try
            {
                SkeletalAssetImporter.Import(args[1]).Save(args[2]);
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        if (args.Length == 2 && args[0] == "--reimport-humanoid")
        {
            try
            {
                AssetDatabase.ReimportHumanoidAnimation(args[1]);
                Console.WriteLine($"Reimported humanoid animation: {args[1]}");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        var options =
            WindowOptions.Default;

        options.Title =
            "R2Engine";

        options.Size =
            new Silk.NET.Maths.Vector2D<int>(
                960,
                600);
        options.WindowBorder = WindowBorder.Hidden;
        options.Position = CenterOnCurrentMonitor(options.Size);

        _window =
            Window.Create(
                options);

        _window.Load +=
            OnLoad;

        _window.Update +=
            OnUpdate;

        _window.Render +=
            OnRender;

        _window.Closing +=
            OnClosing;

        // Keep the GL context alive until editor cleanup finishes. The default
        // Run extension resets the window/context before returning.
        try
        {
            _window.Initialize();
            _window.Run(() =>
            {
                _window.DoEvents();
                if (!_window.IsClosing) _window.DoUpdate();
                if (!_window.IsClosing) _window.DoRender();
            });
        }
        finally
        {
            DisposeEditorResources();
            _window.Dispose();
        }
        return 0;
    }

    private static void OnLoad()
    {
        IWindow window =
            _window!;

        _nativeWindowHandle = GetActiveWindow();
        ApplySplashWindowShape(window.Size);

        _gl =
            window.CreateOpenGL();

        _gl.ClearColor(
            0.018f,
            0.028f,
            0.052f,
            1.0f);

        _input =
            window.CreateInput();

        RuntimePlatform.Configure(new DesktopPlatformServices(
            "Windows Editor",
            () => InputDevicePoller.Poll(_input)));

        SetApplicationIcons(window);

        _imgui =
            new ImGuiController(
                _gl,
                window,
                _input);

        _editorUI =
            new EditorUI(
                window,
                _gl);

        window.FileDrop += paths => _editorUI?.QueueExternalFileDrop(paths);
    }

    private static void SetApplicationIcons(IWindow window)
    {
        try
        {
            string projectRoot = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(projectRoot, "R2Engine.Editor.csproj")))
            {
                DirectoryInfo? parent = Directory.GetParent(projectRoot);
                if (parent == null)
                    return;
                projectRoot = parent.FullName;
            }

            RawImage[] icons =
            {
                LoadIcon(Path.Combine(projectRoot, "Editor", "Icons", "r2-titlebar.png"), 32),
                LoadIcon(Path.Combine(projectRoot, "Editor", "Icons", "r2-taskbar.png"), 256)
            };

            window.SetWindowIcon(icons);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Could not set application icon: {exception.Message}");
        }
    }

    private static RawImage LoadIcon(string path, int targetSize)
    {
        ImageResult source = ImageResult.FromMemory(
            File.ReadAllBytes(path),
            ColorComponents.RedGreenBlueAlpha);
        byte[] resized = new byte[targetSize * targetSize * 4];

        for (int y = 0; y < targetSize; y++)
        for (int x = 0; x < targetSize; x++)
        {
            int sourceX = x * source.Width / targetSize;
            int sourceY = y * source.Height / targetSize;
            int sourceIndex = (sourceY * source.Width + sourceX) * 4;
            int targetIndex = (y * targetSize + x) * 4;
            System.Buffer.BlockCopy(source.Data, sourceIndex, resized, targetIndex, 4);
        }

        return new RawImage(targetSize, targetSize, resized);
    }

    private static void OnUpdate(
        double deltaTime)
    {
        if (!_normalWindowRestored && _editorUI?.StartupFinished == true && _window != null)
        {
            var newSize = new Silk.NET.Maths.Vector2D<int>(1280, 720);
            if (_nativeWindowHandle != 0)
                SetWindowRgn(_nativeWindowHandle, 0, true);
            _window.WindowBorder = WindowBorder.Resizable;
            _window.Size = newSize;
            _window.Position = CenterOnCurrentMonitor(newSize);
            _normalWindowRestored = true;
        }
        _imgui?.Update(
            (float)deltaTime);
    }

    private static void OnRender(
        double deltaTime)
    {
        if (_gl == null ||
            _window == null)
        {
            return;
        }

        var framebufferSize =
            _window.FramebufferSize;

        _gl.Viewport(
            0,
            0,
            (uint)framebufferSize.X,
            (uint)framebufferSize.Y);

        _gl.ClearColor(
            0.018f,
            0.028f,
            0.052f,
            1.0f);

        _gl.Clear(
            (uint)
            ClearBufferMask.ColorBufferBit);

        _editorUI?.Render();

        framebufferSize =
            _window.FramebufferSize;

        _gl.Viewport(
            0,
            0,
            (uint)framebufferSize.X,
            (uint)framebufferSize.Y);

        _imgui?.Render();
    }

    private static void OnClosing()
    {
        if (_window == null) return;
        // Native X / Alt+F4 requests are cancellable. Do not dispose anything
        // while the unsaved-changes dialog still needs to render.
        _window.IsClosing = _editorUI?.ConfirmWindowClose() ?? true;
    }

    private static void DisposeEditorResources()
    {
        _editorUI?.Dispose();

        _imgui?.Dispose();

        _input?.Dispose();

        _gl?.Dispose();
    }

    private static Silk.NET.Maths.Vector2D<int> CenterOnCurrentMonitor(
        Silk.NET.Maths.Vector2D<int> size)
    {
        if (!GetCursorPos(out NativePoint cursor))
            return new Silk.NET.Maths.Vector2D<int>(100, 100);
        nint monitor = MonitorFromPoint(cursor, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info))
            return new Silk.NET.Maths.Vector2D<int>(100, 100);
        int width = info.Work.Right - info.Work.Left;
        int height = info.Work.Bottom - info.Work.Top;
        return new Silk.NET.Maths.Vector2D<int>(
            info.Work.Left + Math.Max(0, (width - size.X) / 2),
            info.Work.Top + Math.Max(0, (height - size.Y) / 2));
    }

    private static void ApplySplashWindowShape(Silk.NET.Maths.Vector2D<int> size)
    {
        if (_nativeWindowHandle == 0) return;
        nint region = CreateRoundRectRgn(0, 0, size.X + 1, size.Y + 1, 24, 24);
        if (region == 0) return;
        if (SetWindowRgn(_nativeWindowHandle, region, true) == 0)
            DeleteObject(region);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern nint GetActiveWindow();

    [DllImport("gdi32.dll")]
    private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom,
        int ellipseWidth, int ellipseHeight);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(nint window, nint region, bool redraw);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint value);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
