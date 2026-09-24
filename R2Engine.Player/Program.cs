using R2Engine.Editor;
using R2Engine.Editor.Scene;
using R2Engine.Runtime.Platform;
using R2Engine.Runtime.Rendering;
using R2Engine.Runtime.Audio;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using System.Runtime.InteropServices;

namespace R2Engine.Player;

internal static class Program
{
    private static IWindow? _window;
    private static GL? _gl;
    private static IInputContext? _input;
    private static IKeyboard? _keyboard;
    private static IMouse? _mouse;
    private static IRuntimeSceneRenderer? _renderer;
    private static Scene? _scene;
    private static ProjectSettings? _settings;
    private static string _currentScenePath = "";
    private static readonly CollisionSystem CollisionSystem = new();
    private static IRuntimeAudioSystem? _audioSystem;
    private static float _physicsAccumulator;
    private static bool _previousMouseDown;
    private static bool _previousEscapeDown;
    private static bool _previousUiActivate;
    private static bool _previousUiCancel;
    private static bool _uiAxisLatched;
    private static int _uiFocusIndex;
    private static string? _loadingTarget;
    private static bool _loadingPresented;
    private const float PhysicsStep = 1.0f / 60.0f;

    private static void Main()
    {
        try
        {
            RunPlayer();
        }
        catch (Exception exception)
        {
            string details = exception.ToString();
            string logPath = Path.Combine(AppContext.BaseDirectory, "R2Game-crash.log");
            try { File.WriteAllText(logPath, details); } catch { }
            Console.Error.WriteLine(details);
            if (OperatingSystem.IsWindows())
                MessageBoxW(IntPtr.Zero,
                    $"R2Game could not start.\n\n{exception.GetBaseException().Message}\n\nA crash report was written to:\n{logPath}",
                    "R2Engine Player Error", 0x10);
        }
    }

    private static void RunPlayer()
    {
        string settingsPath = Path.Combine(AppContext.BaseDirectory, "game.json");
        if (!File.Exists(settingsPath))
            throw new FileNotFoundException("This build has no game.json settings file.", settingsPath);

        ProjectSettings settings = ProjectSettings.Load(settingsPath);
        RuntimeInput.Configure(settings.InputActions);

        WindowOptions options = WindowOptions.Default;
        options.Title = settings.ProductName;
        options.Size = new Silk.NET.Maths.Vector2D<int>(settings.Width, settings.Height);
        options.VSync = settings.VSync;
        if (settings.FrameRateLimit > 0)
        {
            options.FramesPerSecond = settings.FrameRateLimit;
            options.UpdatesPerSecond = settings.FrameRateLimit;
        }
        if (settings.WindowMode == GameWindowMode.Fullscreen)
            options.WindowState = WindowState.Fullscreen;
        else if (settings.WindowMode == GameWindowMode.Borderless)
            options.WindowBorder = WindowBorder.Hidden;
        _window = Window.Create(options);
        _window.Load += () => Load(settings);
        _window.Update += Update;
        _window.Render += Render;
        _window.Closing += Shutdown;
        _window.Run();
        _window.Dispose();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr window, string text, string caption, uint type);

    private static void Load(ProjectSettings settings)
    {
        _settings = settings;
        _gl = _window!.CreateOpenGL();
        _input = _window!.CreateInput();
        _keyboard = _input.Keyboards.FirstOrDefault();
        _mouse = _input.Mice.FirstOrDefault();
        SceneRenderer desktopRenderer = new(_gl);
        desktopRenderer.ConfigureRenderSettings(settings.RenderSettings);
        AudioSystem desktopAudio = new();
        RuntimePlatform.Configure(new DesktopPlatformServices(
            "Windows Player",
            PollPlayerInput,
            desktopRenderer,
            desktopAudio));
        _renderer = RuntimePlatform.Services.Renderer?.SceneRenderer
            ?? throw new InvalidOperationException("The desktop render backend did not provide a scene renderer.");
        _audioSystem = RuntimePlatform.Services.Audio?.AudioSystem
            ?? throw new InvalidOperationException("The desktop audio backend did not provide an audio system.");
        AssetDatabase.Initialize();

        LoadSceneRuntime(ResolveScenePath(settings.StartupScene));
    }

    private static void LoadSceneRuntime(string scenePath)
    {
        if (_settings == null)
            return;

        HashSet<GameObject> persistent = _scene == null
            ? new HashSet<GameObject>()
            : _scene.GameObjects.Where(SceneManager.IsPersistent).ToHashSet();

        if (_scene != null)
        {
            foreach (ScriptComponent script in _scene.GameObjects
                         .Where(item => !persistent.Contains(item))
                         .SelectMany(item => item.Components).OfType<ScriptComponent>())
                script.Shutdown();
        }

        Scene nextScene = SceneSerializer.Load(scenePath);
        LoadingScreen.Attach(nextScene, _settings.LoadingScreenPrefab);
        if (_scene != null)
        {
            foreach (GameObject gameObject in persistent)
            {
                _scene.GameObjects.Remove(gameObject);
                nextScene.AddGameObject(gameObject);
            }
        }

        _scene = nextScene;
        _currentScenePath = scenePath;
        CollisionSystem.Reset();
        _physicsAccumulator = 0.0f;
        ScriptRuntime.Begin(_scene, _settings.DevelopmentBuild ? RuntimePlatform.Log.Info : _ => { });

        ScriptCompilationResult compilation = ScriptCompiler.Compile(Path.Combine(AssetDatabase.AssetsRoot, "Scripts"));
        foreach (string message in compilation.Messages)
            if (_settings.DevelopmentBuild)
                RuntimePlatform.Log.Info(message);

        if (!compilation.Success)
            return;

        foreach (GameObject gameObject in _scene.GameObjects.Where(item => item.IsActiveInHierarchy))
        {
            gameObject.GetComponent<Animator>()?.StartRuntime();
            if (persistent.Contains(gameObject))
                continue;

            ScriptComponent? script = gameObject.GetComponent<ScriptComponent>();
            if (script == null || string.IsNullOrWhiteSpace(script.ScriptPath))
                continue;

            string className = Path.GetFileNameWithoutExtension(script.ScriptPath);
            if (compilation.Types.TryGetValue(className, out Type? type))
                script.Initialize(type);
        }
    }

    private static void PollPlayerInput()
    {
        InputDevicePoller.Poll(_input);
        SetKey(RuntimeKey.W, Key.W);
        SetKey(RuntimeKey.A, Key.A);
        SetKey(RuntimeKey.S, Key.S);
        SetKey(RuntimeKey.D, Key.D);
        SetKey(RuntimeKey.Q, Key.Q);
        SetKey(RuntimeKey.E, Key.E);
        SetKey(RuntimeKey.Space, Key.Space);
        SetKey(RuntimeKey.LeftShift, Key.ShiftLeft);
        SetKey(RuntimeKey.RightShift, Key.ShiftRight);
        SetKey(RuntimeKey.Up, Key.Up);
        SetKey(RuntimeKey.Down, Key.Down);
        SetKey(RuntimeKey.Left, Key.Left);
        SetKey(RuntimeKey.Right, Key.Right);
    }

    private static void Update(double elapsed)
    {
        if (_loadingTarget != null)
        {
            if (_loadingPresented)
            {
                string target = _loadingTarget;
                _loadingTarget = null;
                _loadingPresented = false;
                LoadSceneRuntime(target);
            }
            return;
        }
        if (_scene == null)
            return;

        RuntimeInput.BeginFrame();
        RuntimePlatform.Services.Input?.Poll();

        Canvas[] canvases = _scene.GameObjects.Where(item => item.IsActiveInHierarchy).Select(item => item.GetComponent<Canvas>()).Where(item => item != null).Cast<Canvas>().ToArray();
        Canvas? canvas = canvases.FirstOrDefault(item => !item.IsLoadingScreen && item.ToggleWithMenuInput && item.PauseGameplayWhenVisible) ?? canvases.FirstOrDefault(item => !item.IsLoadingScreen && item.ToggleWithMenuInput);
        bool escapeDown = _keyboard?.IsKeyPressed(Key.Escape) == true;
        if (escapeDown && !_previousEscapeDown)
        {
            if (Interactable.ActiveCanvas(_scene) is {} dialogue) _scene.MenuNavigation.Cancel(dialogue);
            else if (_scene.MenuNavigation.Current != null) _scene.MenuNavigation.Cancel(null);
            else if (canvas != null) canvas.IsVisible = !canvas.IsVisible;
        }
        _previousEscapeDown = escapeDown;
        Interactable.Update(_scene);
        UpdateUiNavigation();
        if (RuntimeInput.InteractionConsumed) _previousUiActivate = true;
        bool interactionActive = Interactable.ActiveCanvas(_scene) != null;
        canvases = _scene.GameObjects.Where(item => item.IsActiveInHierarchy).Select(item => item.GetComponent<Canvas>()).OfType<Canvas>().ToArray();

        float deltaTime = Math.Clamp((float)elapsed, 0.0f, 0.1f);
        bool pausedByUi = canvases.Any(item => item.IsVisible && item.PauseGameplayWhenVisible);
        SlidingDoor.Update(_scene, pausedByUi ? 0f : deltaTime);
        if (RuntimeInput.InteractionConsumed) _previousUiActivate = true;
        foreach (GameObject gameObject in (pausedByUi ? Array.Empty<GameObject>() : _scene.GameObjects.Where(item => item.IsActiveInHierarchy).ToArray()))
        {
            gameObject.GetComponent<Rotator>()?.Update(deltaTime);
            gameObject.GetComponent<Animator>()?.UpdateRuntime(deltaTime);
            PlayerController? controller = gameObject.GetComponent<PlayerController>();
            if (interactionActive) controller?.SuspendMovement();
            else if (!RuntimeInput.InteractionConsumed) controller?.Update(deltaTime, CollisionSystem, _scene);
            ScriptComponent? script = gameObject.GetComponent<ScriptComponent>();
            if (script != null && !script.HasRuntimeError)
                script.RuntimeUpdate(deltaTime);
        }

        if (!pausedByUi)
        {
            _physicsAccumulator = MathF.Min(_physicsAccumulator + deltaTime, PhysicsStep * 4.0f);
            while (_physicsAccumulator >= PhysicsStep)
            {
                foreach (Rigidbody body in _scene.GameObjects.Where(item => item.IsActiveInHierarchy)
                             .SelectMany(item => item.Components).OfType<Rigidbody>())
                    body.FixedUpdate(PhysicsStep, CollisionSystem, _scene);
                _physicsAccumulator -= PhysicsStep;
            }

            CollisionSystem.Update(_scene, RuntimePlatform.Log.Info);
            foreach (GameObject gameObject in ScriptRuntime.TakePendingDestroy())
                _scene.DeleteGameObject(gameObject);
        }

        _audioSystem?.Update(_scene);

        SceneLoadRequest? request = SceneManager.TakePendingRequest();
        if (request != null)
        {
            string target = request.Value.Kind == SceneLoadRequestKind.Restart
                ? _currentScenePath
                : ResolveScenePath(request.Value.SceneName);
            if (LoadingScreen.Show(_scene) != null)
            { _loadingTarget = target; _loadingPresented = false; }
            else LoadSceneRuntime(target);
        }
    }

    private static string ResolveScenePath(string sceneName)
    {
        if (_settings == null)
            throw new InvalidOperationException("Game settings have not loaded.");

        IEnumerable<string> candidates = _settings.BuildScenes.Append(_settings.StartupScene);
        string? relativePath = candidates.FirstOrDefault(path =>
            string.Equals(path, sceneName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(path), sceneName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileNameWithoutExtension(path), sceneName, StringComparison.OrdinalIgnoreCase));
        relativePath ??= sceneName;

        string absolutePath = Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.Combine(AppContext.BaseDirectory, relativePath);
        if (!File.Exists(absolutePath))
            throw new FileNotFoundException($"Scene '{sceneName}' is not included in this build.", absolutePath);
        return absolutePath;
    }

    private static void UpdateUiNavigation()
    {
        if (_scene == null) return;
        UIButton[] buttons = _scene.GameObjects.Where(item => item.IsActiveInHierarchy && Canvas.FindOwner(item)?.IsVisible == true)
            .Select(item => item.GetComponent<UIButton>()).Where(button => button?.Interactable == true)
            .Cast<UIButton>().OrderBy(button => button.SortOrder).ToArray();
        foreach (UIButton button in buttons) { button.IsHovered = false; button.IsPressed = false; }
        bool cancelPressed = RuntimeInput.ControllerButtons.Count > 1 && RuntimeInput.ControllerButtons[1];
        if (buttons.Length == 0)
        {
            if (cancelPressed && !_previousUiCancel) _scene.MenuNavigation.Cancel(Interactable.ActiveCanvas(_scene));
            _previousUiCancel = cancelPressed;
            _uiFocusIndex = 0;
            return;
        }
        _uiFocusIndex = _scene.MenuNavigation.ResolveFocus(buttons, _uiFocusIndex);

        float controllerY = RuntimeInput.ControllerAxes.Count > 1 ? RuntimeInput.ControllerAxes[1] : 0f;
        int direction = RuntimeInput.WasPressed(RuntimeKey.Up) || RuntimeInput.WasPressed(RuntimeKey.W) ? -1 :
            RuntimeInput.WasPressed(RuntimeKey.Down) || RuntimeInput.WasPressed(RuntimeKey.S) ? 1 : 0;
        if (!_uiAxisLatched && MathF.Abs(controllerY) > .55f) direction = controllerY > 0 ? 1 : -1;
        _uiAxisLatched = MathF.Abs(controllerY) > .35f;
        if (direction != 0)
        {
            _uiFocusIndex = (_uiFocusIndex + direction + buttons.Length) % buttons.Length;
            Canvas.FindOwner(buttons[_uiFocusIndex].GameObject!)?.PlayNavigateSound();
        }
        _uiFocusIndex = Math.Clamp(_uiFocusIndex, 0, buttons.Length - 1);

        bool controllerActivate = RuntimeInput.ControllerButtons.Count > 0 && RuntimeInput.ControllerButtons[0];
        bool controllerCancel = RuntimeInput.ControllerButtons.Count > 1 && RuntimeInput.ControllerButtons[1];
        bool activate = _keyboard?.IsKeyPressed(Key.Enter) == true || _keyboard?.IsKeyPressed(Key.Space) == true || controllerActivate;
        bool cancel = controllerCancel;
        UIButton focused = buttons[_uiFocusIndex];
        focused.IsHovered = true;
        focused.IsPressed = activate;
        if (activate && !_previousUiActivate)
        {
            focused.WasClicked = true;
            Canvas.FindOwner(focused.GameObject!)?.PlaySubmitSound();
        }
        if (cancel && !_previousUiCancel)
        {
            Canvas? owner = Canvas.FindOwner(focused.GameObject!);
            _scene.MenuNavigation.Cancel(owner);
        }
        _previousUiActivate = activate;
        _previousUiCancel = cancel;
    }

    private static void SetKey(RuntimeKey runtimeKey, Key key) =>
        RuntimeInput.SetKey(runtimeKey, _keyboard?.IsKeyPressed(key) == true);

    private static void Render(double _)
    {
        if (_scene == null || _renderer == null || _window == null)
            return;

        GameObject? cameraObject = _scene.GameObjects.FirstOrDefault(item =>
            item.IsActiveInHierarchy && item.GetComponent<Camera>()?.IsPrimary == true);
        Camera? camera = cameraObject?.GetComponent<Camera>();
        if (cameraObject == null || camera == null)
            return;

        int destinationWidth = Math.Max(1, _window.FramebufferSize.X);
        int destinationHeight = Math.Max(1, _window.FramebufferSize.Y);
        int renderWidth = Math.Max(1, _settings?.RenderSettings.TargetWidth ?? destinationWidth);
        int renderHeight = Math.Max(1, _settings?.RenderSettings.TargetHeight ?? destinationHeight);
        _renderer.RenderGame(_scene, cameraObject, camera, renderWidth, renderHeight);
        bool mouseDown = _mouse?.IsButtonPressed(MouseButton.Left) == true;
        float presentScale = MathF.Min(destinationWidth / (float)renderWidth, destinationHeight / (float)renderHeight);
        float presentWidth = renderWidth * presentScale;
        float presentHeight = renderHeight * presentScale;
        float presentX = (destinationWidth - presentWidth) * .5f;
        float presentY = (destinationHeight - presentHeight) * .5f;
        float mouseX = _mouse == null ? -1 : (_mouse.Position.X - presentX) / Math.Max(1f, presentWidth);
        float mouseY = _mouse == null ? -1 : (_mouse.Position.Y - presentY) / Math.Max(1f, presentHeight);
        _renderer.RenderRuntimeUi(_scene, renderWidth, renderHeight, mouseX, mouseY, mouseDown, !mouseDown && _previousMouseDown);
        _previousMouseDown = mouseDown;
        _renderer.PresentGame(renderWidth, renderHeight, destinationWidth, destinationHeight);
        if (_loadingTarget != null) _loadingPresented = true;
    }

    private static void Shutdown()
    {
        if (_scene != null)
        {
            foreach (ScriptComponent script in _scene.GameObjects.SelectMany(item => item.Components).OfType<ScriptComponent>())
                script.Shutdown();
        }

        ScriptRuntime.End();
        SceneManager.ClearRuntimeState();
        RuntimeInput.Clear();
        _renderer?.Dispose();
        _audioSystem?.Dispose();
        _input?.Dispose();
        _gl?.Dispose();
    }
}
