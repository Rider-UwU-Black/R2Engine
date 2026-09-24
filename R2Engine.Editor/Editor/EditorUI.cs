using System.Numerics;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Collections.Concurrent;
using ImGuiNET;
using R2Engine.Editor.Scene;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using R2Engine.Runtime.Platform;

namespace R2Engine.Editor;

public class EditorUI : IDisposable
{
    private sealed record TextureBudgetEntry(
        string Source,
        int Width,
        int Height,
        string Format,
        int MipLevels,
        long VramBytes);

    private enum PendingSceneAction
    {
        None,
        NewScene,
        OpenScene,
        CloseEditor
    }

    private enum HierarchyDropMode
    {
        Before,
        Parent,
        After
    }

    private enum SelectionArea
    {
        Scene,
        Assets
    }

    private enum UiWidgetDragMode { None, Move, Resize, Anchor }

    private readonly IWindow _window;

    private Scene.Scene _scene;
    private Scene.Scene? _runtimeScene;
    private string? _runtimeScenePath;
    private bool _runtimePaused;
    private bool _runtimeStepRequested;

    private Dictionary<string, Type> _compiledScriptTypes =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<string> _scriptMessages =
        new();

    private float _physicsAccumulator;
    private bool _gameViewHasInputFocus;
    private const float PhysicsStep = 1.0f / 60.0f;

    private bool _editorScriptsReady;

    private readonly SceneRenderer _sceneRenderer;
    private readonly SceneRenderer _gameRenderer;
    private readonly SceneRenderer _cameraPreviewRenderer;
    private readonly EditorCamera _editorCamera;
    private readonly TransformGizmo _transformGizmo;
    private SceneViewPreferences _sceneViewPreferences = new();
    private readonly UndoRedoManager _undoRedo;
    private readonly AssetThumbnailCache _thumbnailCache;
    private readonly AssetThumbnailCache _splashTextureCache;
    private readonly CollisionSystem _collisionSystem = new();
    private AudioSystem? _audioSystem;

    private readonly Task<ScriptCompilationResult> _startupScriptCompilation;
    private readonly System.Diagnostics.Stopwatch _startupTimer =
        System.Diagnostics.Stopwatch.StartNew();
    private bool _startupCompilationApplied;
    private bool _startupFinished;
    public bool StartupFinished => _startupFinished;
    private const float MinimumSplashSeconds = 3.0f;
    private readonly ProjectSettings _projectSettings;
    private bool _showProjectSettings;
    private bool _focusProjectSettings;
    private bool _openSaveLayoutPopup;
    private string _layoutNameBuffer = "";
    private EditorLayout? _layoutToDelete;
    private Task<string>? _gameBuildTask;

    private GameObject? _selectedObject;
    private readonly HashSet<GameObject> _selectedObjects = new();
    private GameObject? _hierarchySelectionAnchor;
    private SelectionArea _selectionArea = SelectionArea.Scene;

    private string? _currentScenePath;

    private string _statusMessageText = "";
    private long _statusMessageStarted;
    private string _statusMessage
    {
        get => _statusMessageText;
        set
        {
            _statusMessageText = value;
            // Reset even when consecutive actions produce the same message.
            _statusMessageStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        }
    }

    private string _savedSceneSnapshot =
        "";

    private PendingSceneAction _pendingSceneAction =
        PendingSceneAction.None;
    private string? _pendingOpenScenePath;

    private bool _openUnsavedChangesPopup;

    private string? _activeInspectorEditSnapshot;
    private bool _inspectorLocked;
    private GameObject? _lockedInspectorObject;
    private (Vector3 Position, Vector3 Rotation, Vector3 Scale)? _copiedTransform;
    private string _addComponentSearch = "";

    private string? _activeGizmoEditSnapshot;

    private bool _wasGizmoDragging;
    private bool _uiWidgetDragging;
    private Vector2 _uiWidgetDragStartMouse;
    private Vector2 _uiWidgetDragStartOffset;
    private Vector2 _uiWidgetDragStartSize;
    private Vector2 _uiWidgetDragStartAnchor;
    private UiWidgetDragMode _uiWidgetDragMode;
    private bool _uiAnchorEditing;
    private int _runtimeUiFocusIndex;
    private bool _runtimeUiAxisLatched;
    private bool _runtimeUiPreviousActivate;
    private bool _runtimeUiPreviousCancel;
    private string? _uiWidgetEditSnapshot;

    // =========================================================
    // Project Browser
    // =========================================================

    private string _projectBrowserPath;

    private string? _selectedAssetPath;
    private string? _draggedAssetPath;
    private GameObject? _draggedHierarchyObject;
    private readonly CanvasPreviewState _canvasPreview = new();
    private GameObject? _hierarchyDropTarget;
    private HierarchyDropMode _hierarchyDropMode;
    private bool _hierarchyDragActive;
    private string _hierarchySearch = "";
    private bool _focusHierarchySearch;
    private GameObject? _inlineRenameHierarchyObject;
    private string _inlineRenameHierarchyBuffer = "";
    private bool _focusInlineHierarchyRename;
    private bool _boxSelectionCandidate;
    private bool _boxSelectionActive;
    private Vector2 _boxSelectionStart;
    private readonly HashSet<GameObject> _expandedHierarchyObjects = new();
    private bool _hierarchySceneExpanded = true;
    private string? _copiedHierarchyScene;
    private int _copiedHierarchyRootIndex = -1;
    private string? _copiedHierarchyParentName;
    private string? _pendingRenameAssetPath;
    private string? _pendingDeleteAssetPath;
    private readonly ConcurrentQueue<string> _externalAssetDrops = new();
    private readonly HashSet<string> _pendingTextureInvalidations = new(StringComparer.OrdinalIgnoreCase);
    private bool _openNewFolderPopup;
    private string _newFolderName = "New Folder";
    private string _assetRenameBuffer = "";
    private string? _inlineRenameAssetPath;
    private string _inlineRenameBuffer = "";
    private bool _focusInlineRename;
    private string _projectSearch = "";
    private bool _focusProjectSearch;
    private bool _openRenameAssetPopup;
    private bool _openDeleteAssetPopup;
    private Dictionary<string, string>? _copiedScriptFieldValues;
    private float _newAnimationKeyTime;
    private bool _showAnimationWindow;
    private string? _animationEditorPath;
    private GameObject? _animationPreviewObject;
    private bool _animationPreviewPlaying;
    private float _animationPreviewTime;
    private int _animationSelectedKeyframe = -1;
    private AnimationKeyframe? _copiedAnimationKeyframe;
    private Vector3 _animationPreviewOriginalPosition;
    private Vector3 _animationPreviewOriginalRotation;
    private Vector3 _animationPreviewOriginalScale;
    private bool _hasAnimationPreviewOriginal;
    private bool _showAnimatorControllerWindow;
    private string? _animatorControllerPath;
    private int _selectedAnimatorState = -1;
    private Vector2 _animatorNewStatePosition;
    private Vector2 _animatorGraphPan = new(40.0f, 40.0f);
    private float _animatorGraphZoom = 1.0f;
    private bool _inspectAnimatorController;
    private string? _inspectedAssetPath;
    private int _transitionSourceState = -1;
    private bool _showHumanoidBoneMappingWindow;
    private string? _humanoidBoneMappingPath;
    private bool _showSkeletonConfigurationWindow;
    private string? _skeletonConfigurationPath;
    private bool _showPerformanceWindow = true;
    private bool _showTextureBudgetWindow;
    private DateTime _textureBudgetManifestWriteTimeUtc;
    private readonly List<TextureBudgetEntry> _textureBudgetEntries = new();
    private long _textureBudgetTotalBytes;
    private bool _showEnvironmentWindow;
    private bool _performanceUseGameView = true;
    private float _smoothedFrameTime = 1.0f / 60.0f;
    private HashSet<string> _currentPrefabOverrides = new(StringComparer.Ordinal);
    private int _currentPrefabObjectIndex = -1;
    private int _selectedMeshMaterialSlot;

    public EditorUI(
        IWindow window,
        GL gl)
    {
        _window =
            window;
        ApplyEditorTheme(R2UserSettings.Load().DarkTheme);

        AssetDatabase.Initialize();

        _projectSettings = ProjectSettings.Load(
            Path.Combine(AssetDatabase.ProjectRoot, "ProjectSettings.json"));
        if (string.IsNullOrWhiteSpace(_projectSettings.Ps2SaveId))
        {
            _projectSettings.Ps2SaveId = Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();
            SaveProjectSettings();
        }
        RuntimeInput.Configure(_projectSettings.InputActions);
        _canvasPreview.LoadPreferences(Path.Combine(AssetDatabase.ProjectRoot, "CanvasPreview.editor.json"));
        _sceneViewPreferences = SceneViewPreferences.Load(Path.Combine(AssetDatabase.ProjectRoot, "SceneView.editor.json"));

        _projectBrowserPath =
            AssetDatabase.AssetsRoot;

        _sceneRenderer =
            new SceneRenderer(
                gl);

        _gameRenderer =
            new SceneRenderer(
                gl);

        _cameraPreviewRenderer =
            new SceneRenderer(
                gl);

        _sceneRenderer.ConfigureRenderSettings(_projectSettings.RenderSettings);
        _gameRenderer.ConfigureRenderSettings(_projectSettings.RenderSettings);
        _cameraPreviewRenderer.ConfigureRenderSettings(_projectSettings.RenderSettings);

        _editorCamera =
            new EditorCamera();

        _transformGizmo =
            new TransformGizmo();

        _undoRedo =
            new UndoRedoManager();

        _thumbnailCache =
            new AssetThumbnailCache(
                gl);

        _splashTextureCache =
            new AssetThumbnailCache(
                gl,
                2048);

        _scene =
            CreateDefaultScene();

        _savedSceneSnapshot =
            SceneSerializer.Serialize(
                _scene);

        _startupScriptCompilation = Task.Run(() =>
            ScriptCompiler.Compile(
                Path.Combine(AssetDatabase.AssetsRoot, "Scripts")));

        UpdateWindowTitle();
    }

    private static void ApplyEditorTheme(bool dark)
    {
        ImGuiStylePtr style = ImGui.GetStyle();
        style.WindowPadding = new Vector2(6.0f, 5.0f);
        style.FramePadding = new Vector2(5.0f, 2.0f);
        style.ItemSpacing = new Vector2(5.0f, 3.0f);
        style.ItemInnerSpacing = new Vector2(4.0f, 3.0f);
        style.IndentSpacing = 16.0f;
        style.ScrollbarSize = 13.0f;
        style.GrabMinSize = 8.0f;
        style.WindowBorderSize = 1.0f;
        style.ChildBorderSize = 1.0f;
        style.PopupBorderSize = 1.0f;
        style.FrameBorderSize = 1.0f;
        style.TabBorderSize = 1.0f;
        style.WindowRounding = 0.0f;
        style.ChildRounding = 0.0f;
        style.FrameRounding = 1.0f;
        style.PopupRounding = 1.0f;
        style.ScrollbarRounding = 1.0f;
        style.GrabRounding = 1.0f;
        style.TabRounding = 1.0f;

        if (dark)
        {
            ImGui.StyleColorsDark();
            var darkColors = style.Colors;
            darkColors[(int)ImGuiCol.Text] = new Vector4(0.91f, 0.91f, 0.91f, 1.0f);
            darkColors[(int)ImGuiCol.TextDisabled] = new Vector4(0.52f, 0.52f, 0.52f, 1.0f);
            darkColors[(int)ImGuiCol.WindowBg] = new Vector4(0.15f, 0.15f, 0.15f, 1.0f);
            darkColors[(int)ImGuiCol.ChildBg] = new Vector4(0.13f, 0.13f, 0.13f, 1.0f);
            darkColors[(int)ImGuiCol.PopupBg] = new Vector4(0.18f, 0.18f, 0.18f, 0.99f);
            darkColors[(int)ImGuiCol.Border] = new Vector4(0.30f, 0.30f, 0.30f, 1.0f);
            darkColors[(int)ImGuiCol.BorderShadow] = new Vector4(0.02f, 0.02f, 0.02f, 0.50f);
            darkColors[(int)ImGuiCol.FrameBg] = new Vector4(0.22f, 0.22f, 0.22f, 1.0f);
            darkColors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.29f, 0.29f, 0.29f, 1.0f);
            darkColors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.25f, 0.34f, 0.45f, 1.0f);
            darkColors[(int)ImGuiCol.TitleBg] = new Vector4(0.12f, 0.12f, 0.12f, 1.0f);
            darkColors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.18f, 0.18f, 0.18f, 1.0f);
            darkColors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(0.11f, 0.11f, 0.11f, 1.0f);
            darkColors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.19f, 0.19f, 0.19f, 1.0f);
            darkColors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.11f, 0.11f, 0.11f, 1.0f);
            darkColors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.34f, 0.34f, 0.34f, 1.0f);
            darkColors[(int)ImGuiCol.Button] = new Vector4(0.25f, 0.25f, 0.25f, 1.0f);
            darkColors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.33f, 0.33f, 0.33f, 1.0f);
            darkColors[(int)ImGuiCol.ButtonActive] = new Vector4(0.25f, 0.36f, 0.49f, 1.0f);
            darkColors[(int)ImGuiCol.Header] = new Vector4(0.25f, 0.38f, 0.53f, 1.0f);
            darkColors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.31f, 0.46f, 0.63f, 1.0f);
            darkColors[(int)ImGuiCol.HeaderActive] = new Vector4(0.22f, 0.34f, 0.48f, 1.0f);
            darkColors[(int)ImGuiCol.Tab] = new Vector4(0.20f, 0.20f, 0.20f, 1.0f);
            darkColors[(int)ImGuiCol.TabHovered] = new Vector4(0.31f, 0.42f, 0.56f, 1.0f);
            darkColors[(int)ImGuiCol.TabSelected] = new Vector4(0.28f, 0.28f, 0.28f, 1.0f);
            darkColors[(int)ImGuiCol.TabDimmed] = new Vector4(0.16f, 0.16f, 0.16f, 1.0f);
            darkColors[(int)ImGuiCol.TabDimmedSelected] = new Vector4(0.23f, 0.23f, 0.23f, 1.0f);
            darkColors[(int)ImGuiCol.TableHeaderBg] = new Vector4(0.22f, 0.22f, 0.22f, 1.0f);
            darkColors[(int)ImGuiCol.DockingEmptyBg] = new Vector4(0.09f, 0.09f, 0.09f, 1.0f);
            return;
        }

        var colors = style.Colors;
        colors[(int)ImGuiCol.Text] = new Vector4(0.12f, 0.12f, 0.12f, 1.0f);
        colors[(int)ImGuiCol.TextDisabled] = new Vector4(0.42f, 0.42f, 0.42f, 1.0f);
        colors[(int)ImGuiCol.WindowBg] = new Vector4(0.72f, 0.72f, 0.72f, 1.0f);
        colors[(int)ImGuiCol.ChildBg] = new Vector4(0.70f, 0.70f, 0.70f, 1.0f);
        colors[(int)ImGuiCol.PopupBg] = new Vector4(0.84f, 0.84f, 0.84f, 0.99f);
        colors[(int)ImGuiCol.Border] = new Vector4(0.37f, 0.37f, 0.37f, 1.0f);
        colors[(int)ImGuiCol.BorderShadow] = new Vector4(0.94f, 0.94f, 0.94f, 0.45f);
        colors[(int)ImGuiCol.FrameBg] = new Vector4(0.80f, 0.80f, 0.80f, 1.0f);
        colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.88f, 0.88f, 0.88f, 1.0f);
        colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.66f, 0.72f, 0.80f, 1.0f);
        colors[(int)ImGuiCol.TitleBg] = new Vector4(0.62f, 0.62f, 0.62f, 1.0f);
        colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.73f, 0.73f, 0.73f, 1.0f);
        colors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(0.60f, 0.60f, 0.60f, 1.0f);
        colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.72f, 0.72f, 0.72f, 1.0f);
        colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.65f, 0.65f, 0.65f, 1.0f);
        colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.48f, 0.48f, 0.48f, 1.0f);
        colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.40f, 0.40f, 0.40f, 1.0f);
        colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(0.31f, 0.31f, 0.31f, 1.0f);
        colors[(int)ImGuiCol.CheckMark] = new Vector4(0.18f, 0.38f, 0.67f, 1.0f);
        colors[(int)ImGuiCol.SliderGrab] = new Vector4(0.45f, 0.45f, 0.45f, 1.0f);
        colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(0.22f, 0.43f, 0.72f, 1.0f);
        colors[(int)ImGuiCol.Button] = new Vector4(0.73f, 0.73f, 0.73f, 1.0f);
        colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.84f, 0.84f, 0.84f, 1.0f);
        colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.60f, 0.67f, 0.76f, 1.0f);
        colors[(int)ImGuiCol.Header] = new Vector4(0.52f, 0.67f, 0.84f, 1.0f);
        colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.62f, 0.75f, 0.90f, 1.0f);
        colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.42f, 0.60f, 0.80f, 1.0f);
        colors[(int)ImGuiCol.Separator] = new Vector4(0.42f, 0.42f, 0.42f, 1.0f);
        colors[(int)ImGuiCol.SeparatorHovered] = new Vector4(0.28f, 0.48f, 0.73f, 1.0f);
        colors[(int)ImGuiCol.SeparatorActive] = new Vector4(0.18f, 0.38f, 0.67f, 1.0f);
        colors[(int)ImGuiCol.ResizeGrip] = new Vector4(0.40f, 0.40f, 0.40f, 0.30f);
        colors[(int)ImGuiCol.ResizeGripHovered] = new Vector4(0.30f, 0.50f, 0.76f, 0.75f);
        colors[(int)ImGuiCol.ResizeGripActive] = new Vector4(0.18f, 0.38f, 0.67f, 1.0f);
        colors[(int)ImGuiCol.Tab] = new Vector4(0.64f, 0.64f, 0.64f, 1.0f);
        colors[(int)ImGuiCol.TabHovered] = new Vector4(0.82f, 0.82f, 0.82f, 1.0f);
        colors[(int)ImGuiCol.TabSelected] = new Vector4(0.80f, 0.80f, 0.80f, 1.0f);
        colors[(int)ImGuiCol.TabDimmed] = new Vector4(0.59f, 0.59f, 0.59f, 1.0f);
        colors[(int)ImGuiCol.TabDimmedSelected] = new Vector4(0.70f, 0.70f, 0.70f, 1.0f);
        colors[(int)ImGuiCol.DockingPreview] = new Vector4(0.25f, 0.48f, 0.80f, 0.65f);
        colors[(int)ImGuiCol.DockingEmptyBg] = new Vector4(0.22f, 0.22f, 0.22f, 1.0f);
        colors[(int)ImGuiCol.TableHeaderBg] = new Vector4(0.64f, 0.64f, 0.64f, 1.0f);
        colors[(int)ImGuiCol.TableBorderStrong] = new Vector4(0.38f, 0.38f, 0.38f, 1.0f);
        colors[(int)ImGuiCol.TableBorderLight] = new Vector4(0.58f, 0.58f, 0.58f, 1.0f);
        colors[(int)ImGuiCol.TextSelectedBg] = new Vector4(0.30f, 0.50f, 0.78f, 0.55f);
        colors[(int)ImGuiCol.DragDropTarget] = new Vector4(0.14f, 0.40f, 0.78f, 1.0f);
        colors[(int)ImGuiCol.NavCursor] = new Vector4(0.18f, 0.38f, 0.67f, 1.0f);
    }

    // =========================================================
    // Scene State
    // =========================================================

    private bool IsSceneDirty =>
        SceneSerializer.Serialize(
            _scene) !=
        _savedSceneSnapshot;

    private bool IsPlaying =>
        _runtimeScene != null;

    private void EnterPlayMode()
    {
        if (IsPlaying)
            return;

        _runtimeScene =
            SceneSerializer.Deserialize(
                SceneSerializer.Serialize(
                    _scene));
        _runtimeScenePath = _currentScenePath;
        _runtimePaused = false;
        _runtimeStepRequested = false;
        _gameViewHasInputFocus = true;

        try
        {
            _audioSystem = new AudioSystem();
            if (RuntimePlatform.Services is DesktopPlatformServices desktopServices)
                desktopServices.AttachAudioSystem(_audioSystem);
        }
        catch (Exception exception)
        {
            _scriptMessages.Add($"Audio initialization failed: {exception.GetBaseException().Message}");
        }

        PrepareRuntimeScripts();

        _statusMessage =
            "Play Mode started.";
    }

    private void ExitPlayMode()
    {
        if (!IsPlaying)
            return;

        if (_runtimeScene != null)
        {
            foreach (ScriptComponent script in _runtimeScene.GameObjects
                         .Where(gameObject => gameObject.IsActiveInHierarchy)
                         .SelectMany(gameObject => gameObject.Components)
                         .OfType<ScriptComponent>())
            {
                try
                {
                    script.Shutdown();
                }
                catch (Exception exception)
                {
                    _scriptMessages.Add($"OnDestroy: {exception.GetBaseException().Message}");
                }
            }
        }

        _runtimeScene =
            null;
        _runtimeScenePath = null;
        _runtimePaused = false;
        _runtimeStepRequested = false;

        RuntimeInput.Clear();
        _gameViewHasInputFocus = false;
        ScriptRuntime.End();
        SceneManager.ClearRuntimeState();
        _loadingRequest = null;
        _loadingVisibility = null;
        _loadingPresented = false;
        _collisionSystem.Reset();
        if (RuntimePlatform.Services is DesktopPlatformServices desktopServices)
            desktopServices.AttachAudioSystem(null);
        _audioSystem?.Dispose();
        _audioSystem = null;
        _physicsAccumulator = 0.0f;

        _statusMessage =
            "Play Mode stopped. Runtime changes were discarded.";
    }

    private void PrepareRuntimeScripts()
    {
        if (_runtimeScene == null)
            return;

        try { LoadingScreen.Attach(_runtimeScene, _projectSettings.LoadingScreenPrefab); }
        catch (Exception exception)
        { _scriptMessages.Add($"Loading screen unavailable: {exception.GetBaseException().Message}"); }

        ScriptRuntime.Begin(
            _runtimeScene,
            message => _scriptMessages.Add(message));

        string scriptsRoot =
            Path.Combine(
                AssetDatabase.AssetsRoot,
                "Scripts");

        ScriptCompilationResult compilation =
            ScriptCompiler.Compile(scriptsRoot);

        _compiledScriptTypes =
            compilation.Types;

        _editorScriptsReady =
            compilation.Success;

        _scriptMessages.AddRange(compilation.Messages);

        if (!compilation.Success)
        {
            _statusMessage =
                "Script compilation failed. See Console.";
            return;
        }

        foreach (GameObject gameObject in _runtimeScene.GameObjects.ToArray())
        {
            if (!gameObject.IsActiveInHierarchy)
                continue;

            gameObject.GetComponent<Animator>()?.StartRuntime();

            ScriptComponent? script =
                gameObject.GetComponent<ScriptComponent>();

            if (script == null || string.IsNullOrWhiteSpace(script.ScriptPath))
                continue;

            if (script.IsInitialized)
                continue;

            string className =
                Path.GetFileNameWithoutExtension(script.ScriptPath);

            if (!_compiledScriptTypes.TryGetValue(className, out Type? scriptType))
            {
                _scriptMessages.Add(
                    $"{gameObject.Name}: no ScriptBehaviour class named {className} was found.");
                continue;
            }

            try
            {
                script.Initialize(scriptType);
                _scriptMessages.Add($"Started {className} on {gameObject.Name}.");
            }
            catch (Exception exception)
            {
                script.HasRuntimeError = true;
                _scriptMessages.Add($"{className}.Start: {exception.GetBaseException().Message}");
            }
        }

    }

    // =========================================================
    // Default Scene
    // =========================================================

    private Scene.Scene CreateDefaultScene()
    {
        Scene.Scene scene =
            new Scene.Scene(
                "Untitled Scene");

        GameObject mainCamera =
            scene.CreateGameObject(
                "Main Camera");

        Camera defaultCamera =
            mainCamera
                .AddComponent<Camera>();

        defaultCamera.IsPrimary =
            true;

        mainCamera.Transform.Position =
            new Vector3(
                0.0f,
                1.0f,
                5.0f);

        GameObject directionalLight =
            scene.CreateGameObject(
                "Directional Light");

        Light defaultDirectionalLight =
            directionalLight
                .AddComponent<Light>();

        defaultDirectionalLight.Type =
            LightType.Directional;

        directionalLight.Transform.Rotation =
            new Vector3(
                50.0f,
                -30.0f,
                0.0f);

        _selectedObject =
            null;

        return scene;
    }

    // =========================================================
    // Render
    // =========================================================

    public void Render()
    {
        // Process these before any ImGui draw commands or scene draws reference
        // the old GL textures. Deleting them directly from the inspector can
        // invalidate a texture that was already queued earlier in the frame.
        ProcessPendingTextureInvalidations();

        if (DrawStartupSplashIfNeeded())
            return;

        if (_runtimeScene == null)
            RuntimePlatform.Services.Input?.Poll();

        HandleKeyboardShortcuts();

        UpdateRuntime();

        UpdateWindowTitle();

        UpdateGameBuildStatus();

        DrawMainMenu();

        const float toolbarHeight = 28.0f;
        ImGuiViewportPtr mainViewport = ImGui.GetMainViewport();
        Vector2 originalWorkPos = mainViewport.WorkPos;
        Vector2 originalWorkSize = mainViewport.WorkSize;
        DrawMainToolbar(mainViewport, toolbarHeight);

        // Keep the toolbar outside the docking area so it owns a real row instead
        // of covering the Scene/Hierarchy tabs. Restore the viewport immediately
        // after creating the dockspace; ImGui rebuilds its work area each frame.
        unsafe
        {
            mainViewport.NativePtr->WorkPos = originalWorkPos + new Vector2(0.0f, toolbarHeight);
            mainViewport.NativePtr->WorkSize = new Vector2(
                originalWorkSize.X,
                Math.Max(1.0f, originalWorkSize.Y - toolbarHeight));
        }
        ImGui.DockSpaceOverViewport();
        unsafe
        {
            mainViewport.NativePtr->WorkPos = originalWorkPos;
            mainViewport.NativePtr->WorkSize = originalWorkSize;
        }

        DrawHierarchy();
        DrawInspector();
        // Inspector reference fields must see the hierarchy drag on its release frame.
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            _hierarchyDragActive = false;
            _draggedHierarchyObject = null;
            _hierarchyDropTarget = null;
        }
        DrawProject();
        DrawSceneView();
        DrawGameView();
        DrawConsole();
        DrawPerformanceWindow();
        DrawTextureBudgetWindow();
        DrawEnvironmentWindow();
        DrawProjectSettings();
        DrawAnimationWindow();
        DrawAnimatorControllerWindow();
        DrawHumanoidBoneMappingWindow();
        DrawSkeletonConfigurationWindow();

        DrawUnsavedChangesPopup();
        DrawLayoutPopups();

        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            // A drag must keep the destination Inspector visible; only a click selects an asset.
            if (_draggedAssetPath != null && ImGui.GetMouseDragDelta(ImGuiMouseButton.Left, 0).LengthSquared() < 36)
            {
                _inspectedAssetPath = _draggedAssetPath;
                if (!string.Equals(_animatorControllerPath, _draggedAssetPath, StringComparison.OrdinalIgnoreCase))
                    _inspectAnimatorController = false;
            }
            _draggedAssetPath = null;
        }
    }

    private bool DrawStartupSplashIfNeeded()
    {
        if (_startupFinished)
            return false;

        bool compilationFinished = _startupScriptCompilation.IsCompleted;

        if (compilationFinished && !_startupCompilationApplied)
        {
            _startupCompilationApplied = true;

            if (_startupScriptCompilation.IsCompletedSuccessfully)
            {
                ScriptCompilationResult result = _startupScriptCompilation.Result;
                _compiledScriptTypes = result.Types;
                _editorScriptsReady = result.Success;
                _scriptMessages.Clear();
                _scriptMessages.AddRange(result.Messages);
            }
            else
            {
                _editorScriptsReady = false;
                _scriptMessages.Add(
                    $"Startup script compilation failed: {_startupScriptCompilation.Exception?.GetBaseException().Message}");
            }
        }

        float elapsed = (float)_startupTimer.Elapsed.TotalSeconds;
        if (compilationFinished && elapsed >= MinimumSplashSeconds)
        {
            _startupFinished = true;
            _startupTimer.Stop();
            return false;
        }

        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);
        ImGui.SetNextWindowViewport(viewport.ID);

        ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoBringToFrontOnFocus |
            ImGuiWindowFlags.NoNav |
            ImGuiWindowFlags.NoDocking;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.Begin("##R2StartupSplash", flags);

        string splashPath = Path.Combine(
            FindEngineRoot(),
            "R2Engine.Editor",
            "Editor",
            "Icons",
            "r2-splash.png");

        if (_splashTextureCache.TryGetThumbnail(
                splashPath,
                out uint splashTexture,
                out int splashWidth,
                out int splashHeight))
        {
            Vector2 available = ImGui.GetContentRegionAvail();
            float scale = MathF.Min(available.X / splashWidth, available.Y / splashHeight);
            Vector2 imageSize = new(splashWidth * scale, splashHeight * scale);
            ImGui.SetCursorPos((available - imageSize) * 0.5f);
            ImGui.Image((nint)splashTexture, imageSize, Vector2.Zero, Vector2.One);
        }

        const float closeSize = 30.0f;
        ImGui.SetCursorScreenPos(new Vector2(
            viewport.Pos.X + viewport.Size.X - closeSize - 8.0f,
            viewport.Pos.Y + 8.0f));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.04f, 0.04f, 0.055f, 0.82f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.72f, 0.12f, 0.14f, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.55f, 0.07f, 0.09f, 1.0f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2.0f);
        if (ImGui.Button("X##CloseStartup", new Vector2(closeSize, closeSize)))
            _window.IsClosing = true;
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);

        float timeProgress = Math.Clamp(elapsed / MinimumSplashSeconds, 0.0f, 1.0f);
        float progress = compilationFinished
            ? MathF.Max(0.92f, timeProgress)
            : MathF.Min(0.85f, timeProgress * 0.85f);
        string loadingText = compilationFinished
            ? "Finalizing editor..."
            : "Compiling project scripts...";

        const float footerHeight = 78.0f;
        Vector2 footerMin = new(viewport.Pos.X, viewport.Pos.Y + viewport.Size.Y - footerHeight);
        Vector2 footerMax = viewport.Pos + viewport.Size;
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(footerMin, footerMax, ImGui.ColorConvertFloat4ToU32(
            new Vector4(0.20f, 0.21f, 0.23f, 0.98f)));
        drawList.AddLine(footerMin, new Vector2(footerMax.X, footerMin.Y),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.42f, 0.43f, 0.46f, 1.0f)));

        string projectName = new DirectoryInfo(AssetDatabase.ProjectRoot).Name;
        Version? version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
        string versionText = version == null
            ? "R2Engine Editor"
            : $"R2Engine Editor {version.Major}.{version.Minor}.{version.Build}";
        drawList.AddText(footerMin + new Vector2(20.0f, 14.0f), 0xffeeeeee, versionText);
        drawList.AddText(footerMin + new Vector2(20.0f, 39.0f), 0xffbfc3c9,
            $"{projectName}  -  {loadingText}");

        Vector2 barSize = new(MathF.Min(430.0f, viewport.Size.X * 0.46f), 18.0f);
        ImGui.SetCursorScreenPos(new Vector2(
            footerMax.X - barSize.X - 22.0f,
            footerMin.Y + (footerHeight - barSize.Y) * 0.5f));
        ImGui.ProgressBar(progress, barSize, $"{progress * 100.0f:0}%");

        ImGui.End();
        ImGui.PopStyleVar();
        return true;
    }

    private void UpdateRuntime()
    {
        if (_runtimeScene == null)
            return;
        if (_loadingRequest != null)
        {
            if (_loadingPresented) HandleRuntimeSceneRequest();
            return;
        }
        if (_runtimePaused && !_runtimeStepRequested)
        {
            _audioSystem?.Update(_runtimeScene);
            return;
        }

        RuntimeInput.BeginFrame();
        RuntimePlatform.Services.Input?.Poll();
        RuntimeInput.SetInputEnabled(_gameViewHasInputFocus);
        RuntimeInput.SetKey(RuntimeKey.W, ImGui.IsKeyDown(ImGuiKey.W));
        RuntimeInput.SetKey(RuntimeKey.A, ImGui.IsKeyDown(ImGuiKey.A));
        RuntimeInput.SetKey(RuntimeKey.S, ImGui.IsKeyDown(ImGuiKey.S));
        RuntimeInput.SetKey(RuntimeKey.D, ImGui.IsKeyDown(ImGuiKey.D));
        RuntimeInput.SetKey(RuntimeKey.Q, ImGui.IsKeyDown(ImGuiKey.Q));
        RuntimeInput.SetKey(RuntimeKey.E, ImGui.IsKeyDown(ImGuiKey.E));
        RuntimeInput.SetKey(RuntimeKey.Space, ImGui.IsKeyDown(ImGuiKey.Space));
        RuntimeInput.SetKey(RuntimeKey.LeftShift, ImGui.IsKeyDown(ImGuiKey.LeftShift));
        RuntimeInput.SetKey(RuntimeKey.RightShift, ImGui.IsKeyDown(ImGuiKey.RightShift));
        RuntimeInput.SetKey(RuntimeKey.Up, ImGui.IsKeyDown(ImGuiKey.UpArrow));
        RuntimeInput.SetKey(RuntimeKey.Down, ImGui.IsKeyDown(ImGuiKey.DownArrow));
        RuntimeInput.SetKey(RuntimeKey.Left, ImGui.IsKeyDown(ImGuiKey.LeftArrow));
        RuntimeInput.SetKey(RuntimeKey.Right, ImGui.IsKeyDown(ImGuiKey.RightArrow));

        Interactable.Update(_runtimeScene);
        bool interactionActive = Interactable.ActiveCanvas(_runtimeScene) != null;

        float deltaTime = _runtimeStepRequested
            ? 1.0f / 60.0f
            : Math.Clamp(ImGui.GetIO().DeltaTime, 0.0f, 0.1f);
        if (_runtimeScene.GameObjects.Where(item => item.IsActiveInHierarchy).Select(item => item.GetComponent<Canvas>())
            .Any(canvas => canvas?.IsVisible == true && canvas.PauseGameplayWhenVisible))
            deltaTime = 0.0f;

        SlidingDoor.Update(_runtimeScene, deltaTime);
        foreach (GameObject gameObject in _runtimeScene.GameObjects)
        {
            if (!gameObject.IsActiveInHierarchy)
                continue;

            Rotator? rotator =
                gameObject.GetComponent<Rotator>();

            rotator?.Update(deltaTime);

            gameObject.GetComponent<Animator>()?.UpdateRuntime(deltaTime);

            PlayerController? playerController =
                gameObject.GetComponent<PlayerController>();

            if (interactionActive) playerController?.SuspendMovement();
            else if (!RuntimeInput.InteractionConsumed) playerController?.Update(deltaTime, _collisionSystem, _runtimeScene);

            ScriptComponent? script =
                gameObject.GetComponent<ScriptComponent>();

            if (script != null && !script.HasRuntimeError)
            {
                try
                {
                    script.RuntimeUpdate(deltaTime);
                }
                catch (Exception exception)
                {
                    script.HasRuntimeError = true;
                    _scriptMessages.Add(
                        $"{gameObject.Name} script Update: {exception.GetBaseException().Message}");
                }
            }
        }

        _physicsAccumulator = MathF.Min(_physicsAccumulator + deltaTime, PhysicsStep * 4.0f);

        while (_physicsAccumulator >= PhysicsStep)
        {
            foreach (Rigidbody rigidbody in _runtimeScene.GameObjects
                         .Where(gameObject => gameObject.IsActiveInHierarchy)
                         .SelectMany(gameObject => gameObject.Components)
                         .OfType<Rigidbody>())
            {
                rigidbody.FixedUpdate(PhysicsStep, _collisionSystem, _runtimeScene);
            }

            _physicsAccumulator -= PhysicsStep;
        }

        foreach (GameObject gameObject in ScriptRuntime.TakePendingDestroy())
        {
            foreach (ScriptComponent script in gameObject.Components.OfType<ScriptComponent>())
            {
                try
                {
                    script.Shutdown();
                }
                catch (Exception exception)
                {
                    _scriptMessages.Add($"{gameObject.Name}.OnDestroy: {exception.GetBaseException().Message}");
                }
            }

            _runtimeScene.DeleteGameObject(gameObject);
        }

        try
        {
            _collisionSystem.Update(
                _runtimeScene,
                message => _scriptMessages.Add(message));
        }
        catch (Exception exception)
        {
            _scriptMessages.Add($"Collision callback: {exception.GetBaseException().Message}");
        }

        HandleRuntimeSceneRequest();
        _audioSystem?.Update(_runtimeScene);
        _runtimeStepRequested = false;
    }

    private SceneLoadRequest? _loadingRequest;
    private Dictionary<Canvas, bool>? _loadingVisibility;
    private bool _loadingPresented;

    private void HandleRuntimeSceneRequest()
    {
        SceneLoadRequest? request = _loadingRequest ?? SceneManager.TakePendingRequest();
        if (request == null || _runtimeScene == null)
            return;
        if (_loadingRequest == null && (_loadingVisibility = LoadingScreen.Show(_runtimeScene)) != null)
        {
            _loadingRequest = request;
            _loadingPresented = false;
            return;
        }

        try
        {
            string scenePath = request.Value.Kind == SceneLoadRequestKind.Restart
                ? _runtimeScenePath ?? throw new InvalidOperationException("The current scene has not been saved.")
                : ResolveRuntimeScenePath(request.Value.SceneName);

            HashSet<GameObject> persistent = _runtimeScene.GameObjects
                .Where(SceneManager.IsPersistent).ToHashSet();
            foreach (ScriptComponent script in _runtimeScene.GameObjects
                         .Where(item => !persistent.Contains(item))
                         .SelectMany(item => item.Components).OfType<ScriptComponent>())
                script.Shutdown();

            Scene.Scene nextScene = SceneSerializer.Load(scenePath);
            foreach (GameObject gameObject in persistent)
            {
                _runtimeScene.GameObjects.Remove(gameObject);
                nextScene.AddGameObject(gameObject);
            }

            if (!string.IsNullOrWhiteSpace(request.Value.DestinationMarker))
            {
                GameObject marker = nextScene.GameObjects.FirstOrDefault(item =>
                    string.Equals(item.Name, request.Value.DestinationMarker, StringComparison.Ordinal))
                    ?? throw new InvalidOperationException(
                        $"Destination marker '{request.Value.DestinationMarker}' was not found in '{Path.GetFileNameWithoutExtension(scenePath)}'.");
                GameObject player = nextScene.GameObjects.FirstOrDefault(item =>
                    !string.IsNullOrWhiteSpace(request.Value.PlayerObjectName) &&
                    string.Equals(item.Name, request.Value.PlayerObjectName, StringComparison.Ordinal))
                    ?? nextScene.GameObjects.FirstOrDefault(item => item.GetComponent<PlayerController>() != null)
                    ?? throw new InvalidOperationException("No destination player object was found.");
                player.Transform.SetWorldTransform(marker.Transform.WorldPosition,
                    marker.Transform.WorldRotation, player.Transform.WorldScale);
            }

            _runtimeScene = nextScene;
            _runtimeScenePath = scenePath;
            _collisionSystem.Reset();
            _physicsAccumulator = 0.0f;
            PrepareRuntimeScripts();
            _scriptMessages.Add($"Loaded scene {Path.GetFileNameWithoutExtension(scenePath)}.");
        }
        catch (Exception exception)
        {
            LoadingScreen.Restore(_loadingVisibility);
            _scriptMessages.Add($"Scene load failed: {exception.GetBaseException().Message}");
        }
        finally
        {
            _loadingRequest = null;
            _loadingVisibility = null;
            _loadingPresented = false;
        }
    }

    private string ResolveRuntimeScenePath(string sceneName)
    {
        IEnumerable<string> candidates = _projectSettings.BuildScenes.Append(_projectSettings.StartupScene);
        string? relative = candidates.FirstOrDefault(path =>
            string.Equals(path, sceneName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(path), sceneName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileNameWithoutExtension(path), sceneName, StringComparison.OrdinalIgnoreCase));
        relative ??= sceneName;
        string absolute = Path.IsPathRooted(relative)
            ? relative
            : Path.Combine(AssetDatabase.ProjectRoot, relative);
        // Editor play mode can visit saved scenes that have not been added to a build yet.
        if (!File.Exists(absolute) && !Path.IsPathRooted(sceneName))
        {
            string scenesRoot = Path.Combine(AssetDatabase.ProjectRoot, "Scenes");
            string fileName = Path.GetFileName(sceneName);
            if (!fileName.EndsWith(".r2scene", StringComparison.OrdinalIgnoreCase)) fileName += ".r2scene";
            string[] matches = Directory.Exists(scenesRoot)
                ? Directory.EnumerateFiles(scenesRoot, "*.r2scene", SearchOption.AllDirectories)
                    .Where(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase)).ToArray()
                : Array.Empty<string>();
            if (matches.Length > 1)
                throw new InvalidOperationException($"Scene '{sceneName}' matches multiple saved scenes. Use its project-relative path.");
            if (matches.Length == 1) absolute = matches[0];
        }
        if (!File.Exists(absolute))
            throw new FileNotFoundException($"Scene '{sceneName}' was not found.", absolute);
        return absolute;
    }

    private void DrawConsole()
    {
        ImGui.Begin("Console");

        if (ImGui.Button("Copy All"))
        {
            ImGui.SetClipboardText(
                string.Join(Environment.NewLine, _scriptMessages));
        }

        ImGui.SameLine();

        if (ImGui.Button("Clear"))
            _scriptMessages.Clear();

        ImGui.Separator();

        if (_scriptMessages.Count == 0)
        {
            ImGui.TextDisabled("Script compiler and runtime messages appear here.");
        }
        else
        {
            string consoleText =
                string.Join(
                    Environment.NewLine,
                    _scriptMessages);

            ImGui.InputTextMultiline(
                "##ConsoleOutput",
                ref consoleText,
                (uint)Math.Max(1024, consoleText.Length + 1),
                ImGui.GetContentRegionAvail(),
                ImGuiInputTextFlags.ReadOnly);
        }

        ImGui.End();
    }

    // =========================================================
    // Window Title
    // =========================================================

    private void UpdateWindowTitle()
    {
        string sceneName =
            _currentScenePath != null
                ? Path.GetFileNameWithoutExtension(
                    _currentScenePath)
                : _scene.Name;

        string dirtyMarker =
            IsSceneDirty
                ? "*"
                : "";

        _window.Title =
            $"R2Engine - {sceneName}{dirtyMarker}";
    }

    // =========================================================
    // Keyboard
    // =========================================================

    private void HandleKeyboardShortcuts()
    {
        var io =
            ImGui.GetIO();

        if (io.WantTextInput)
            return;

        if (ImGui.IsKeyPressed(ImGuiKey.F2))
        {
            if (_selectionArea == SelectionArea.Assets && _selectedAssetPath != null &&
                (File.Exists(_selectedAssetPath) || Directory.Exists(_selectedAssetPath)))
                BeginInlineAssetRename(_selectedAssetPath);
            else if (_selectedObject != null)
                BeginInlineHierarchyRename(_selectedObject);
            return;
        }

        if (_selectionArea == SelectionArea.Assets && ImGui.IsKeyPressed(ImGuiKey.Backspace))
        {
            NavigateProjectToParent();
            return;
        }

        if (_selectionArea == SelectionArea.Assets && ImGui.IsKeyPressed(ImGuiKey.Enter) &&
            _selectedAssetPath != null)
        {
            OpenSelectedProjectItem();
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Delete))
        {
            if (_selectionArea == SelectionArea.Assets &&
                _selectedAssetPath != null &&
                (File.Exists(_selectedAssetPath) || Directory.Exists(_selectedAssetPath)))
            {
                _pendingDeleteAssetPath = _selectedAssetPath;
                _openDeleteAssetPopup = true;
            }
            else if (_selectedObjects.Count > 0)
            {
                DeleteSelectedSceneObjects();
            }

            return;
        }

        bool controlHeld =
            ImGui.IsKeyDown(
                ImGuiKey.LeftCtrl) ||
            ImGui.IsKeyDown(
                ImGuiKey.RightCtrl);

        bool shiftHeld = ImGui.IsKeyDown(ImGuiKey.LeftShift) || ImGui.IsKeyDown(ImGuiKey.RightShift);
        bool altHeld = ImGui.IsKeyDown(ImGuiKey.LeftAlt) || ImGui.IsKeyDown(ImGuiKey.RightAlt);

        if (!controlHeld)
            return;

        if (ImGui.IsKeyPressed(ImGuiKey.F))
        {
            if (_selectionArea == SelectionArea.Assets)
            {
                _focusProjectSearch = true;
                ImGui.SetWindowFocus("Project");
            }
            else
            {
                _focusHierarchySearch = true;
                ImGui.SetWindowFocus("Hierarchy");
            }
            return;
        }

        if (ImGui.IsKeyPressed(
                ImGuiKey.S))
        {
            if (shiftHeld) SaveSceneAs();
            else SaveScene();

            return;
        }

        if (ImGui.IsKeyPressed(
                ImGuiKey.Z))
        {
            Undo();

            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Y) ||
            (shiftHeld && ImGui.IsKeyPressed(ImGuiKey.Z)))
        {
            Redo();

            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.C) && _selectedObject != null)
        {
            CopyHierarchyObject(_selectedObject);

            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.V) && _copiedHierarchyScene != null)
        {
            PasteHierarchyObject();

            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.D) && _selectionArea == SelectionArea.Assets &&
            _selectedAssetPath != null && (File.Exists(_selectedAssetPath) || Directory.Exists(_selectedAssetPath)))
        {
            DuplicateAsset(_selectedAssetPath);
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.D) && _selectedObject != null)
        {
            DuplicateHierarchyObject(_selectedObject);
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.P))
        {
            if (shiftHeld && IsPlaying)
                _runtimePaused = !_runtimePaused;
            else if (altHeld && IsPlaying)
            {
                _runtimePaused = true;
                _runtimeStepRequested = true;
            }
            else if (IsPlaying) ExitPlayMode();
            else EnterPlayMode();
        }
    }

    private void DeleteSelectedSceneObjects()
    {
        HashSet<GameObject> selected = _selectedObjects
            .Where(gameObject => _scene.GameObjects.Contains(gameObject))
            .ToHashSet();
        GameObject[] roots = selected
            .Where(gameObject =>
            {
                for (GameObject? parent = gameObject.Parent; parent != null; parent = parent.Parent)
                {
                    if (selected.Contains(parent))
                        return false;
                }
                return true;
            })
            .ToArray();

        if (roots.Length == 0)
            return;

        RecordImmediateEdit(() =>
        {
            foreach (GameObject gameObject in roots)
                _scene.DeleteGameObject(gameObject);
            _selectedObjects.Clear();
            _selectedObject = null;
        });

        _statusMessage = roots.Length == 1
            ? $"Deleted {roots[0].Name}."
            : $"Deleted {roots.Length} scene objects.";
    }

    private void NavigateProjectToParent()
    {
        string scenesRoot = Path.Combine(AssetDatabase.ProjectRoot, "Scenes");
        string current = Path.GetFullPath(_projectBrowserPath);
        string scenes = Path.GetFullPath(scenesRoot);
        string root = current.Equals(scenes, StringComparison.OrdinalIgnoreCase) ||
                      current.StartsWith(scenes + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? scenes
            : Path.GetFullPath(AssetDatabase.AssetsRoot);
        if (current.Equals(root, StringComparison.OrdinalIgnoreCase)) return;
        DirectoryInfo? parent = Directory.GetParent(current);
        if (parent == null) return;
        _projectBrowserPath = parent.FullName.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? parent.FullName
            : root;
        _selectedAssetPath = null;
    }

    private void OpenSelectedProjectItem()
    {
        if (_selectedAssetPath == null) return;
        if (Directory.Exists(_selectedAssetPath))
        {
            _projectBrowserPath = _selectedAssetPath;
            _selectedAssetPath = null;
            return;
        }

        string extension = Path.GetExtension(_selectedAssetPath).ToLowerInvariant();
        if (extension == ".r2anim") OpenAnimationEditor(_selectedAssetPath);
        else if (extension == ".r2controller")
        {
            _animatorControllerPath = _selectedAssetPath;
            _showAnimatorControllerWindow = true;
            _inspectAnimatorController = true;
            _selectedAnimatorState = -1;
        }
        else if (extension == ".r2scene") RequestOpenScene(_selectedAssetPath);
    }

    private void BeginInlineHierarchyRename(GameObject gameObject)
    {
        _inlineRenameHierarchyObject = gameObject;
        _inlineRenameHierarchyBuffer = gameObject.Name;
        _focusInlineHierarchyRename = true;
    }

    // =========================================================
    // Undo / Redo
    // =========================================================

    private void RecordUndoSnapshot(
        string snapshot)
    {
        _undoRedo.RecordUndo(
            snapshot);
    }

    private void Undo()
    {
        string currentSnapshot =
            SceneSerializer.Serialize(
                _scene);

        string? snapshot =
            _undoRedo.Undo(
                currentSnapshot);

        if (snapshot == null)
            return;

        RestoreSnapshot(
            snapshot);

        _statusMessage =
            "Undo";
    }

    private void Redo()
    {
        string currentSnapshot =
            SceneSerializer.Serialize(
                _scene);

        string? snapshot =
            _undoRedo.Redo(
                currentSnapshot);

        if (snapshot == null)
            return;

        RestoreSnapshot(
            snapshot);

        _statusMessage =
            "Redo";
    }

    private void RestoreSnapshot(
        string snapshot)
    {
        string? selectedName =
            _selectedObject?
                .Name;
        HashSet<string> expandedNames = _expandedHierarchyObjects
            .Where(gameObject => gameObject.Scene == _scene)
            .Select(gameObject => gameObject.Name)
            .ToHashSet(StringComparer.Ordinal);

        _scene =
            SceneSerializer.Deserialize(
                snapshot);

        _expandedHierarchyObjects.Clear();
        foreach (GameObject gameObject in _scene.GameObjects)
        {
            if (expandedNames.Contains(gameObject.Name))
                _expandedHierarchyObjects.Add(gameObject);
        }

        _selectedObject =
            null;
        _selectedObjects.Clear();

        if (selectedName != null)
        {
            _selectedObject =
                _scene.GameObjects
                    .FirstOrDefault(
                        gameObject =>
                            gameObject.Name ==
                            selectedName);
        }

        if (_selectedObject == null &&
            _scene.GameObjects.Count >
            0)
        {
            _selectedObject =
                _scene.GameObjects[0];
        }

        if (_selectedObject != null)
            _selectedObjects.Add(_selectedObject);

        _activeInspectorEditSnapshot =
            null;

        _activeGizmoEditSnapshot =
            null;

        _wasGizmoDragging =
            false;
    }

    private void RecordImmediateEdit(
        Action edit)
    {
        string before =
            SceneSerializer.Serialize(
                _scene);

        edit();

        string after =
            SceneSerializer.Serialize(
                _scene);

        if (before !=
            after)
        {
            RecordUndoSnapshot(
                before);
        }
    }

    // =========================================================
    // Inspector Undo Helpers
    // =========================================================

    private void BeginInspectorEditIfNeeded()
    {
        if (ImGui.IsItemActivated())
        {
            _activeInspectorEditSnapshot =
                SceneSerializer.Serialize(
                    _scene);
        }
    }

    private void FinishInspectorEditIfNeeded()
    {
        if (!ImGui.IsItemDeactivatedAfterEdit())
            return;

        if (_activeInspectorEditSnapshot ==
            null)
        {
            return;
        }

        string current =
            SceneSerializer.Serialize(
                _scene);

        if (current !=
            _activeInspectorEditSnapshot)
        {
            RecordUndoSnapshot(
                _activeInspectorEditSnapshot);
        }

        _activeInspectorEditSnapshot =
            null;
    }

    // =========================================================
    // Main Menu
    // =========================================================

    private void DrawMainMenu()
    {
        if (!ImGui.BeginMainMenuBar())
            return;

        if (ImGui.BeginMenu(
                "File"))
        {
            if (ImGui.MenuItem(
                    "New Scene"))
            {
                RequestSceneAction(
                    PendingSceneAction.NewScene);
            }

            if (ImGui.MenuItem(
                    "Open Scene..."))
            {
                _pendingOpenScenePath = null;
                RequestSceneAction(
                    PendingSceneAction.OpenScene);
            }

            ImGui.Separator();

            if (ImGui.MenuItem(
                    "Save Scene",
                    "Ctrl+S"))
            {
                SaveScene();
            }

            if (ImGui.MenuItem(
                    "Save Scene As...",
                    "Ctrl+Shift+S"))
            {
                SaveSceneAs();
            }

            ImGui.Separator();

            bool canBuild = _gameBuildTask == null;
            if (!canBuild) ImGui.BeginDisabled();

            if (ImGui.BeginMenu("Build"))
            {
                if (ImGui.MenuItem("Windows Build"))
                    BuildStandaloneGame(runAfterBuild: false);
                if (ImGui.MenuItem("Windows Build and Run"))
                    BuildStandaloneGame(runAfterBuild: true);
                ImGui.Separator();
                if (ImGui.MenuItem("PS2 Emulator Build and Run"))
                    BuildAndRunPs2();
                if (ImGui.MenuItem("PS2 Final ISO"))
                    BuildAndRunPs2(finalIso: true);
                if (ImGui.MenuItem("PS2 Network Development Build"))
                    BuildAndRunPs2(finalIso: true, deployToNetwork: true);
                ImGui.EndMenu();
            }

            if (!canBuild) ImGui.EndDisabled();

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu(
                "Edit"))
        {
            if (!_undoRedo.CanUndo)
                ImGui.BeginDisabled();

            if (ImGui.MenuItem(
                    "Undo",
                    "Ctrl+Z"))
            {
                Undo();
            }

            if (!_undoRedo.CanUndo)
                ImGui.EndDisabled();

            if (!_undoRedo.CanRedo)
                ImGui.BeginDisabled();

            if (ImGui.MenuItem(
                    "Redo",
                    "Ctrl+Y"))
            {
                Redo();
            }

            if (!_undoRedo.CanRedo)
                ImGui.EndDisabled();

            ImGui.Separator();

            bool hasSelection = _selectionArea == SelectionArea.Assets
                ? _selectedAssetPath != null && (File.Exists(_selectedAssetPath) || Directory.Exists(_selectedAssetPath))
                : _selectedObject != null;
            if (!hasSelection) ImGui.BeginDisabled();
            if (ImGui.MenuItem("Duplicate", "Ctrl+D"))
            {
                if (_selectionArea == SelectionArea.Assets && _selectedAssetPath != null) DuplicateAsset(_selectedAssetPath);
                else if (_selectedObject != null) DuplicateHierarchyObject(_selectedObject);
            }
            if (ImGui.MenuItem("Rename", "F2"))
            {
                if (_selectionArea == SelectionArea.Assets && _selectedAssetPath != null) BeginInlineAssetRename(_selectedAssetPath);
                else if (_selectedObject != null) BeginInlineHierarchyRename(_selectedObject);
            }
            if (ImGui.MenuItem("Delete", "Delete"))
            {
                if (_selectionArea == SelectionArea.Assets && _selectedAssetPath != null)
                { _pendingDeleteAssetPath = _selectedAssetPath; _openDeleteAssetPopup = true; }
                else DeleteSelectedSceneObjects();
            }
            if (!hasSelection) ImGui.EndDisabled();

            bool canFrame = _selectedObject != null;
            if (!canFrame) ImGui.BeginDisabled();
            if (ImGui.MenuItem("Frame Selected", "F")) _editorCamera.FocusObject(_selectedObject!);
            if (!canFrame) ImGui.EndDisabled();

            ImGui.Separator();

            if (ImGui.MenuItem("Project Settings..."))
            {
                _showProjectSettings = true;
                _focusProjectSettings = true;
            }

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Assets"))
        {
            if (ImGui.BeginMenu("Create"))
            {
                if (ImGui.MenuItem("Folder"))
                {
                    _newFolderName = "New Folder";
                    _openNewFolderPopup = true;
                }
                ImGui.Separator();
                if (ImGui.MenuItem("C# Script")) CreateScriptAsset(_projectBrowserPath);
                if (ImGui.MenuItem("Material")) CreateMaterialAsset(_projectBrowserPath);
                if (ImGui.MenuItem("Animation Clip")) CreateBlankAnimationAsset(_projectBrowserPath);
                if (ImGui.MenuItem("Animator Controller")) CreateAnimatorControllerAsset(_projectBrowserPath);
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Import New Asset"))
            {
                if (ImGui.MenuItem("Texture...")) ImportAssetForCurrentFolder("textures", _projectBrowserPath);
                if (ImGui.MenuItem("Audio...")) ImportAssetForCurrentFolder("audio", _projectBrowserPath);
                if (ImGui.MenuItem("Script...")) ImportAssetForCurrentFolder("scripts", _projectBrowserPath);
                if (ImGui.MenuItem("Font...")) ImportAssetForCurrentFolder("fonts", _projectBrowserPath);
                ImGui.Separator();
                if (ImGui.MenuItem("OBJ Model...")) ImportModelAsset("OBJ", _projectBrowserPath);
                if (ImGui.MenuItem("FBX Model...")) ImportModelAsset("FBX", _projectBrowserPath);
                if (ImGui.MenuItem("GLB Model...")) ImportModelAsset("GLB", _projectBrowserPath);
                if (ImGui.MenuItem("glTF Model...")) ImportModelAsset("glTF", _projectBrowserPath);
                if (ImGui.MenuItem("Humanoid Animation FBX...")) ImportHumanoidAnimationAsset(_projectBrowserPath);
                ImGui.EndMenu();
            }

            ImGui.Separator();
            if (ImGui.MenuItem("Show in File Explorer")) OpenProjectFolder(_projectBrowserPath);
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("GameObject"))
        {
            DrawObjectCreationMenu()?.Invoke();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Component"))
        {
            bool canAddComponent = _selectedObject != null && _runtimeScene == null;
            if (!canAddComponent) ImGui.BeginDisabled();
            GameObject? target = _selectedObject;
            void AddComponentItem<T>(string label) where T : Component, new()
            {
                bool missing = target?.GetComponent<T>() == null;
                if (!missing) ImGui.BeginDisabled();
                if (ImGui.MenuItem(label) && target != null)
                    RecordImmediateEdit(() => target.AddComponent<T>());
                if (!missing) ImGui.EndDisabled();
            }
            if (ImGui.BeginMenu("Rendering"))
            {
                AddComponentItem<MeshRenderer>("Mesh Renderer");
                AddComponentItem<Camera>("Camera");
                AddComponentItem<Light>("Light");
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("Physics"))
            {
                AddComponentItem<Rigidbody>("Rigidbody");
                AddComponentItem<BoxCollider>("Box Collider");
                AddComponentItem<SphereCollider>("Sphere Collider");
                AddComponentItem<CapsuleCollider>("Capsule Collider");
                AddComponentItem<MeshCollider>("Mesh Collider");
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("Audio"))
            {
                AddComponentItem<AudioSource>("Audio Source");
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("Animation"))
            {
                AddComponentItem<Animator>("Animator");
                AddComponentItem<Rotator>("Rotator");
                AddComponentItem<AnimatorTriggerZone>("Animator Trigger Zone");
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("UI"))
            {
                AddComponentItem<Canvas>("Canvas");
                AddComponentItem<UIPanel>("Panel");
                AddComponentItem<UIButton>("Button");
                AddComponentItem<UIText>("Text");
                AddComponentItem<UIImage>("Image");
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("Gameplay"))
            {
                AddComponentItem<PlayerController>("Player Controller");
                AddComponentItem<Interactable>("Interactable");
                AddComponentItem<SlidingDoor>("Sliding Door");
                AddComponentItem<SavePoint>("Save Point");
                AddComponentItem<PersistentObject>("Persistent Object");
                ImGui.EndMenu();
            }
            AddComponentItem<ScriptComponent>("Script");
            if (!canAddComponent) ImGui.EndDisabled();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu(
                "Window"))
        {
            if (ImGui.MenuItem("Hierarchy"))
                ImGui.SetWindowFocus("Hierarchy");

            if (ImGui.MenuItem("Inspector"))
                ImGui.SetWindowFocus("Inspector");

            if (ImGui.MenuItem("Project"))
                ImGui.SetWindowFocus("Project");

            ImGui.Separator();

            if (ImGui.MenuItem("Animation")) _showAnimationWindow = true;
            if (ImGui.MenuItem("Environment")) _showEnvironmentWindow = true;
            if (ImGui.MenuItem("Performance")) _showPerformanceWindow = true;
            if (ImGui.MenuItem("Texture Budget")) _showTextureBudgetWindow = true;

            ImGui.Separator();
            if (ImGui.BeginMenu("Layouts"))
            {
                if (ImGui.MenuItem("Default"))
                    TryLoadLayout(null);

                IReadOnlyList<EditorLayout> layouts = EditorLayoutManager.GetCustomLayouts();
                if (layouts.Count > 0)
                {
                    ImGui.Separator();
                    foreach (EditorLayout layout in layouts)
                    {
                        if (!ImGui.BeginMenu(layout.Name))
                            continue;
                        if (ImGui.MenuItem("Load"))
                            TryLoadLayout(layout);
                        if (ImGui.MenuItem("Overwrite With Current"))
                            TryOverwriteLayout(layout);
                        ImGui.Separator();
                        if (ImGui.MenuItem("Delete..."))
                            _layoutToDelete = layout;
                        ImGui.EndMenu();
                    }
                }

                ImGui.Separator();
                if (ImGui.MenuItem("Save Current Layout..."))
                {
                    _layoutNameBuffer = "";
                    _openSaveLayoutPopup = true;
                }
                ImGui.EndMenu();
            }

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Help"))
        {
            if (ImGui.MenuItem("R2Engine Documentation"))
                OpenProjectFolder(FindEngineDocumentationPath());
            ImGui.Separator();
            if (ImGui.MenuItem("About R2Engine"))
                _statusMessage = "R2Engine Editor";
            ImGui.EndMenu();
        }

        ImGui.EndMainMenuBar();
    }

    private void TryLoadLayout(EditorLayout? layout)
    {
        try
        {
            if (layout == null)
            {
                EditorLayoutManager.LoadDefault();
                _statusMessage = "Loaded the Default editor layout.";
            }
            else
            {
                EditorLayoutManager.Load(layout);
                _statusMessage = $"Loaded editor layout '{layout.Name}'.";
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _statusMessage = $"Could not load the editor layout: {exception.Message}";
        }
    }

    private void TryOverwriteLayout(EditorLayout layout)
    {
        try
        {
            EditorLayoutManager.Overwrite(layout);
            _statusMessage = $"Updated editor layout '{layout.Name}'.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _statusMessage = $"Could not update the editor layout: {exception.Message}";
        }
    }

    private void DrawLayoutPopups()
    {
        if (_openSaveLayoutPopup)
        {
            ImGui.OpenPopup("Save Editor Layout");
            _openSaveLayoutPopup = false;
        }

        bool saveOpen = true;
        if (ImGui.BeginPopupModal("Save Editor Layout", ref saveOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted("Save the current arrangement for use in every project.");
            ImGui.SetNextItemWidth(320.0f);
            bool submit = ImGui.InputText("Name", ref _layoutNameBuffer, 80,
                ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);

            bool canSave = !string.IsNullOrWhiteSpace(_layoutNameBuffer);
            if (!canSave) ImGui.BeginDisabled();
            if ((ImGui.Button("Save", new Vector2(90.0f, 0.0f)) || submit) && canSave)
            {
                try
                {
                    EditorLayoutManager.SaveNew(_layoutNameBuffer);
                    _statusMessage = $"Saved editor layout '{_layoutNameBuffer.Trim()}'.";
                    ImGui.CloseCurrentPopup();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    _statusMessage = $"Could not save the editor layout: {exception.Message}";
                }
            }
            if (!canSave) ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(90.0f, 0.0f)))
                ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }

        if (_layoutToDelete != null)
            ImGui.OpenPopup("Delete Editor Layout");

        bool deleteOpen = true;
        if (ImGui.BeginPopupModal("Delete Editor Layout", ref deleteOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            EditorLayout layout = _layoutToDelete!;
            ImGui.TextWrapped($"Delete the saved layout '{layout.Name}'?");
            if (ImGui.Button("Delete", new Vector2(90.0f, 0.0f)))
            {
                try
                {
                    EditorLayoutManager.Delete(layout);
                    _statusMessage = $"Deleted editor layout '{layout.Name}'.";
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    _statusMessage = $"Could not delete the editor layout: {exception.Message}";
                }
                _layoutToDelete = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(90.0f, 0.0f)))
            {
                _layoutToDelete = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        else if (!deleteOpen)
        {
            _layoutToDelete = null;
        }
    }

    private static string FindEngineDocumentationPath()
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            DirectoryInfo? directory = new(Path.GetFullPath(start));
            while (directory != null)
            {
                string docs = Path.Combine(directory.FullName, "Docs");
                if (Directory.Exists(Path.Combine(docs, "Manual")))
                    return docs;
                directory = directory.Parent;
            }
        }
        return Path.Combine(Environment.CurrentDirectory, "Docs");
    }

    private void DrawMainToolbar(ImGuiViewportPtr viewport, float height)
    {
        ImGui.SetNextWindowPos(viewport.WorkPos);
        ImGui.SetNextWindowSize(new Vector2(viewport.WorkSize.X, height));
        ImGui.SetNextWindowViewport(viewport.ID);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4.0f, 3.0f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);
        ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoDocking |
            ImGuiWindowFlags.NoNavFocus |
            ImGuiWindowFlags.NoBringToFrontOnFocus;
        ImGui.Begin("##MainToolbar", flags);

        // Unity-style global tool strip: transform tools stay left and runtime
        // controls remain centered in a dedicated row beneath the menus.
        if (SceneToolButton("View", "View / Hand Tool (Q)", _transformGizmo.Mode == TransformGizmoMode.View))
            _transformGizmo.Mode = TransformGizmoMode.View;
        ImGui.SameLine(0.0f, 2.0f);
        if (SceneToolButton("Move", "Move Tool (W)", _transformGizmo.Mode == TransformGizmoMode.Move))
            _transformGizmo.Mode = TransformGizmoMode.Move;
        ImGui.SameLine(0.0f, 2.0f);
        if (SceneToolButton("Rotate", "Rotate Tool (E)", _transformGizmo.Mode == TransformGizmoMode.Rotate))
            _transformGizmo.Mode = TransformGizmoMode.Rotate;
        ImGui.SameLine(0.0f, 2.0f);
        if (SceneToolButton("Scale", "Scale Tool (R)", _transformGizmo.Mode == TransformGizmoMode.Scale))
            _transformGizmo.Mode = TransformGizmoMode.Scale;
        ImGui.SameLine(0.0f, 2.0f);
        if (SceneToolButton("Rect", "Rect Tool (T)", _transformGizmo.Mode == TransformGizmoMode.Rect))
        {
            _transformGizmo.Mode = TransformGizmoMode.Rect;
            _canvasPreview.ShowInScene = true;
        }
        ImGui.SameLine(0.0f, 2.0f);
        if (SceneToolButton("Transform", "Transform Tool (Y)", _transformGizmo.Mode == TransformGizmoMode.Transform))
            _transformGizmo.Mode = TransformGizmoMode.Transform;
        ImGui.SameLine(0.0f, 9.0f);
        bool pivot = _transformGizmo.UsePivotPosition;
        if (ImGui.SmallButton(pivot ? "Pivot" : "Center"))
            _transformGizmo.UsePivotPosition = !pivot;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Toggle Pivot / Center handle position");
        ImGui.SameLine(0.0f, 2.0f);
        bool global = _transformGizmo.Orientation == TransformGizmoOrientation.Global;
        if (ImGui.SmallButton(global ? "Global" : "Local"))
            _transformGizmo.Orientation = global
                ? TransformGizmoOrientation.Local
                : TransformGizmoOrientation.Global;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Toggle Global / Local handle orientation");

        float runtimeButtonSize = Math.Max(22.0f, ImGui.GetFrameHeight());
        float runtimeControlsWidth = runtimeButtonSize * 3.0f + 6.0f;
        float runtimeControlsX = (ImGui.GetWindowWidth() - runtimeControlsWidth) * 0.5f;
        if (ImGui.GetCursorPosX() < runtimeControlsX)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosX(runtimeControlsX);
        }
        bool playing = IsPlaying;
        if (RuntimeToolButton("Play", "Play / Stop (Ctrl+P)", playing, runtimeButtonSize))
        {
            if (playing) ExitPlayMode();
            else EnterPlayMode();
        }

        ImGui.SameLine(0.0f, 3.0f);
        if (!playing) ImGui.BeginDisabled();
        if (RuntimeToolButton("Pause", "Pause Play Mode", _runtimePaused, runtimeButtonSize))
            _runtimePaused = !_runtimePaused;
        ImGui.SameLine(0.0f, 3.0f);
        if (RuntimeToolButton("Step", "Advance one frame", false, runtimeButtonSize))
        {
            _runtimePaused = true;
            _runtimeStepRequested = true;
        }
        if (!playing) ImGui.EndDisabled();

        ImGui.End();
        ImGui.PopStyleVar(2);
    }

    private void DrawPerformanceWindow()
    {
        if (!_showPerformanceWindow) return;
        bool open = _showPerformanceWindow;
        if (!ImGui.Begin("Performance", ref open))
        {
            ImGui.End();
            _showPerformanceWindow = open;
            return;
        }
        _showPerformanceWindow = open;

        float currentFrameTime = Math.Max(0.000001f, ImGui.GetIO().DeltaTime);
        _smoothedFrameTime += (currentFrameTime - _smoothedFrameTime) * 0.08f;
        float fps = 1.0f / Math.Max(0.000001f, _smoothedFrameTime);
        ImGui.Text($"Editor: {fps:0.0} FPS  |  {_smoothedFrameTime * 1000.0f:0.00} ms");

        ImGui.Checkbox("Use Game View statistics", ref _performanceUseGameView);
        SceneRenderer.RendererFrameStats stats = _performanceUseGameView
            ? _gameRenderer.LastFrameStats
            : _sceneRenderer.LastFrameStats;
        ImGui.TextDisabled(_performanceUseGameView ? "Game renderer" : "Scene renderer (includes selection outline)");
        ImGui.Separator();

        PerformanceBudgets budgets = _projectSettings.PerformanceBudgets;
        int activeLights = stats.DirectionalLights + stats.PointLights + stats.SpotLights;
        DrawPerformanceMetric("Draw Calls", stats.DrawCalls, budgets.MaxDrawCalls);
        DrawPerformanceMetric("Visible Objects", stats.VisibleObjects, budgets.MaxVisibleObjects);
        ImGui.Text($"Culled Mesh Objects: {stats.CulledObjects:N0}");
        DrawPerformanceMetric("Triangles", stats.Triangles, budgets.MaxVisibleTriangles);
        DrawPerformanceMetric("Textures", stats.Textures, budgets.MaxTextures);
        DrawPerformanceMetric("Texture VRAM (KB)", (stats.TextureVramBytes + 1023) / 1024,
            budgets.MaxTextureVramKilobytes);
        DrawPerformanceMetric("Active Lights", activeLights, budgets.MaxActiveLights);
        DrawPerformanceMetric("Skinned Meshes", stats.SkinnedMeshes, budgets.MaxSkinnedMeshes);
        ImGui.Text($"Submitted Vertices: {stats.Vertices:N0}");
        ImGui.Text($"Renderer CPU Time: {stats.CpuRenderMilliseconds:0.000} ms");
        ImGui.TextDisabled($"Lights: {stats.DirectionalLights} directional, {stats.PointLights} point, {stats.SpotLights} spot");

        ImGui.Separator();
        if (ImGui.CollapsingHeader("PS2-oriented Budgets"))
        {
            bool changed = false;
            int targetFps = budgets.TargetFps;
            int maxDrawCalls = budgets.MaxDrawCalls;
            int maxTriangles = budgets.MaxVisibleTriangles;
            int maxObjects = budgets.MaxVisibleObjects;
            int maxTextures = budgets.MaxTextures;
            int maxTextureVram = budgets.MaxTextureVramKilobytes;
            int maxLights = budgets.MaxActiveLights;
            int maxSkinnedMeshes = budgets.MaxSkinnedMeshes;
            if (ImGui.DragInt("Target FPS", ref targetFps, 1.0f, 1, 120)) { budgets.TargetFps = targetFps; changed = true; }
            if (ImGui.DragInt("Max Draw Calls", ref maxDrawCalls, 1.0f, 1, 10000)) { budgets.MaxDrawCalls = maxDrawCalls; changed = true; }
            if (ImGui.DragInt("Max Triangles", ref maxTriangles, 100.0f, 100, 10000000)) { budgets.MaxVisibleTriangles = maxTriangles; changed = true; }
            if (ImGui.DragInt("Max Visible Objects", ref maxObjects, 1.0f, 1, 100000)) { budgets.MaxVisibleObjects = maxObjects; changed = true; }
            if (ImGui.DragInt("Max Textures", ref maxTextures, 1.0f, 1, 10000)) { budgets.MaxTextures = maxTextures; changed = true; }
            if (ImGui.DragInt("Texture VRAM Budget (KB)", ref maxTextureVram, 16.0f, 64, 4096)) { budgets.MaxTextureVramKilobytes = maxTextureVram; changed = true; }
            if (ImGui.DragInt("Max Active Lights", ref maxLights, 1.0f, 0, 1000)) { budgets.MaxActiveLights = maxLights; changed = true; }
            if (ImGui.DragInt("Max Skinned Meshes", ref maxSkinnedMeshes, 1.0f, 0, 1000)) { budgets.MaxSkinnedMeshes = maxSkinnedMeshes; changed = true; }
            if (changed)
                _projectSettings.Save(Path.Combine(AssetDatabase.ProjectRoot, "ProjectSettings.json"));
            ImGui.TextDisabled("These are planning targets, not claims about final PS2 performance.");
        }

        ImGui.End();
    }

    private void DrawTextureBudgetWindow()
    {
        if (!_showTextureBudgetWindow) return;
        bool open = _showTextureBudgetWindow;
        if (!ImGui.Begin("Texture Budget", ref open))
        {
            ImGui.End();
            _showTextureBudgetWindow = open;
            return;
        }
        _showTextureBudgetWindow = open;

        string manifestPath = GetTextureManifestPath();
        DateTime writeTime = File.Exists(manifestPath)
            ? File.GetLastWriteTimeUtc(manifestPath)
            : DateTime.MinValue;
        if (writeTime != _textureBudgetManifestWriteTimeUtc)
            ReloadTextureBudgetManifest(manifestPath, writeTime);

        if (ImGui.Button("Refresh Report"))
            ReloadTextureBudgetManifest(manifestPath, writeTime);
        ImGui.SameLine();
        ImGui.TextDisabled("Latest standalone build");

        if (!File.Exists(manifestPath))
        {
            ImGui.TextWrapped("No texture build report exists yet. Build the game once to generate it.");
            ImGui.End();
            return;
        }

        long budgetBytes = (long)_projectSettings.PerformanceBudgets.MaxTextureVramKilobytes * 1024;
        bool overBudget = _textureBudgetTotalBytes > budgetBytes;
        if (overBudget)
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.35f, 0.25f, 1.0f));
        ImGui.Text($"Estimated VRAM: {FormatBytes(_textureBudgetTotalBytes)} / {FormatBytes(budgetBytes)}" +
                   (overBudget ? "  OVER" : ""));
        if (overBudget) ImGui.PopStyleColor();
        ImGui.TextDisabled($"{_textureBudgetEntries.Count} texture package(s), largest first");
        ImGui.Separator();

        ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH |
                                ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY;
        if (ImGui.BeginTable("##textureBudgetTable", 6, flags, new Vector2(0.0f, -1.0f)))
        {
            ImGui.TableSetupColumn("Texture", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthFixed, 90.0f);
            ImGui.TableSetupColumn("Format", ImGuiTableColumnFlags.WidthFixed, 95.0f);
            ImGui.TableSetupColumn("Mips", ImGuiTableColumnFlags.WidthFixed, 45.0f);
            ImGui.TableSetupColumn("VRAM", ImGuiTableColumnFlags.WidthFixed, 80.0f);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 105.0f);
            ImGui.TableHeadersRow();

            foreach (TextureBudgetEntry entry in _textureBudgetEntries)
            {
                ImGui.PushID(entry.Source);
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(entry.Source);
                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted($"{entry.Width} x {entry.Height}");
                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted(entry.Format);
                ImGui.TableSetColumnIndex(3);
                ImGui.TextUnformatted(entry.MipLevels.ToString());
                ImGui.TableSetColumnIndex(4);
                ImGui.TextUnformatted(FormatBytes(entry.VramBytes));
                ImGui.TableSetColumnIndex(5);
                string absolutePath = AssetDatabase.ToAbsolutePath(entry.Source);
                if (ImGui.SmallButton("Select"))
                {
                    _selectedAssetPath = absolutePath;
                    _projectBrowserPath = Path.GetDirectoryName(absolutePath) ?? AssetDatabase.TexturesDirectory;
                    _selectionArea = SelectionArea.Assets;
                    ImGui.SetWindowFocus("Project");
                }
                ImGui.SameLine();
                if (ImGui.SmallButton("Edit"))
                {
                    _selectedAssetPath = absolutePath;
                    _projectBrowserPath = Path.GetDirectoryName(absolutePath) ?? AssetDatabase.TexturesDirectory;
                    _selectionArea = SelectionArea.Assets;
                    _inspectedAssetPath = absolutePath;
                    ImGui.SetWindowFocus("Inspector");
                }
                ImGui.PopID();
            }
            ImGui.EndTable();
        }

        ImGui.End();
    }

    private string GetTextureManifestPath()
    {
        string buildDirectory = Path.IsPathRooted(_projectSettings.BuildOutput)
            ? _projectSettings.BuildOutput
            : Path.Combine(AssetDatabase.ProjectRoot, _projectSettings.BuildOutput);
        return Path.Combine(buildDirectory, "R2Data", "texture-manifest.json");
    }

    private void ReloadTextureBudgetManifest(string manifestPath, DateTime writeTime)
    {
        _textureBudgetEntries.Clear();
        _textureBudgetTotalBytes = 0;
        _textureBudgetManifestWriteTimeUtc = writeTime;
        if (!File.Exists(manifestPath)) return;

        try
        {
            JsonNode? root = JsonNode.Parse(File.ReadAllText(manifestPath));
            _textureBudgetTotalBytes = root?["TotalEstimatedVramBytes"]?.GetValue<long>() ?? 0;
            if (root?["Textures"] is not JsonArray textures) return;
            foreach (JsonNode? item in textures)
            {
                if (item == null) continue;
                _textureBudgetEntries.Add(new TextureBudgetEntry(
                    item["Source"]?.GetValue<string>() ?? "Unknown",
                    item["Width"]?.GetValue<int>() ?? 0,
                    item["Height"]?.GetValue<int>() ?? 0,
                    item["Format"]?.GetValue<string>() ?? "Unknown",
                    item["MipLevels"]?.GetValue<int>() ?? 1,
                    item["EstimatedVramBytes"]?.GetValue<long>() ?? 0));
            }
            _textureBudgetEntries.Sort((left, right) => right.VramBytes.CompareTo(left.VramBytes));
        }
        catch (Exception exception)
        {
            _scriptMessages.Add($"Could not read texture build report: {exception.Message}");
        }
    }

    private void DrawEnvironmentWindow()
    {
        if (!_showEnvironmentWindow)
            return;

        bool open = _showEnvironmentWindow;
        if (!ImGui.Begin("Environment", ref open))
        {
            ImGui.End();
            _showEnvironmentWindow = open;
            return;
        }
        _showEnvironmentWindow = open;

        EnvironmentSettings environment = _scene.Environment;

        Vector3 background = environment.BackgroundColor;
        ImGui.ColorEdit3("Background", ref background);
        BeginInspectorEditIfNeeded();
        environment.BackgroundColor = background;
        FinishInspectorEditIfNeeded();

        Vector3 ambient = environment.AmbientColor;
        ImGui.ColorEdit3("Ambient Light", ref ambient);
        BeginInspectorEditIfNeeded();
        environment.AmbientColor = ambient;
        FinishInspectorEditIfNeeded();

        ImGui.Separator();
        bool fogEnabled = environment.FogEnabled;
        if (ImGui.Checkbox("Enable Fog", ref fogEnabled))
            RecordImmediateEdit(() => environment.FogEnabled = fogEnabled);

        if (!environment.FogEnabled)
            ImGui.BeginDisabled();

        Vector3 fogColor = environment.FogColor;
        ImGui.ColorEdit3("Fog Color", ref fogColor);
        BeginInspectorEditIfNeeded();
        environment.FogColor = fogColor;
        FinishInspectorEditIfNeeded();

        float fogStart = environment.FogStart;
        ImGui.DragFloat("Fog Start", ref fogStart, 0.25f, 0.0f, 10000.0f);
        BeginInspectorEditIfNeeded();
        environment.FogStart = Math.Max(0.0f, fogStart);
        environment.FogEnd = Math.Max(environment.FogEnd, environment.FogStart + 0.1f);
        FinishInspectorEditIfNeeded();

        float fogEnd = environment.FogEnd;
        ImGui.DragFloat("Fog End", ref fogEnd, 0.25f, environment.FogStart + 0.1f, 10000.0f);
        BeginInspectorEditIfNeeded();
        environment.FogEnd = Math.Max(environment.FogStart + 0.1f, fogEnd);
        FinishInspectorEditIfNeeded();

        if (!environment.FogEnabled)
            ImGui.EndDisabled();

        ImGui.TextDisabled("Linear fog designed for the PS2 render path.");
        ImGui.End();
    }

    private static void DrawPerformanceMetric(string label, long value, long budget)
    {
        bool exceeded = budget >= 0 && value > budget;
        if (exceeded) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.35f, 0.25f, 1.0f));
        ImGui.Text($"{label}: {value:N0} / {budget:N0}{(exceeded ? "  OVER" : "")}");
        if (exceeded) ImGui.PopStyleColor();
    }

    private static string FormatBytes(long bytes) => bytes >= 1024 * 1024
        ? $"{bytes / (1024.0 * 1024.0):0.00} MB"
        : $"{bytes / 1024.0:0.0} KB";

    private void BuildStandaloneGame(bool runAfterBuild)
    {
        if (_gameBuildTask != null)
        {
            _statusMessage = "A game build is already running.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_currentScenePath) &&
            string.IsNullOrWhiteSpace(_projectSettings.StartupScene) &&
            !SaveScene())
        {
            _statusMessage = "Build cancelled because the scene was not saved.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(_currentScenePath) && !SaveScene())
            return;

        _statusMessage = "Building game...";
        _scriptMessages.Add(_statusMessage);
        _gameBuildTask = Task.Run(() => BuildStandaloneGameWorker(runAfterBuild));
    }

    private void BuildAndRunPs2(bool finalIso = false, bool deployToNetwork = false)
    {
        if (_gameBuildTask != null)
        {
            _statusMessage = "A game build is already running.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_currentScenePath) &&
            string.IsNullOrWhiteSpace(_projectSettings.StartupScene) &&
            !SaveScene())
        {
            _statusMessage = "Build cancelled because the scene was not saved.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(_currentScenePath) && !SaveScene())
            return;

        if (deployToNetwork && string.IsNullOrWhiteSpace(_projectSettings.Ps2NetworkBuildDirectory))
        {
            _statusMessage = "Choose the Network PS2 DVD Folder in Project Settings before building.";
            return;
        }

        _statusMessage = deployToNetwork ? "Building PS2 ISO for network boot..." :
            finalIso ? "Building self-contained PS2 ISO..." : "Building game for PS2 and launching PCSX2...";
        _scriptMessages.Add(_statusMessage);
        _gameBuildTask = Task.Run(() => BuildAndRunPs2Worker(finalIso, deployToNetwork));
    }

    private string BuildAndRunPs2Worker(bool finalIso = false, bool deployToNetwork = false)
    {
        string gameBuildResult = BuildStandaloneGameWorker(runAfterBuild: false);
        if (gameBuildResult.StartsWith("Game build failed:", StringComparison.Ordinal))
            return gameBuildResult;

        try
        {
            string solutionRoot = FindEngineRoot();
            string ps2Script = Path.Combine(solutionRoot, "R2Engine.PS2", finalIso ? "build-final.ps1" : "run-pcsx2.ps1");
            if (!File.Exists(ps2Script))
                throw new FileNotFoundException("The PS2 launch script could not be found.", ps2Script);

            string buildDirectory = Path.IsPathRooted(_projectSettings.BuildOutput)
                ? _projectSettings.BuildOutput
                : Path.Combine(AssetDatabase.ProjectRoot, _projectSettings.BuildOutput);
            string sceneName = Path.GetFileNameWithoutExtension(_projectSettings.StartupScene);
            if (string.IsNullOrWhiteSpace(sceneName))
                throw new InvalidOperationException("The startup scene does not have a valid filename.");

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                WorkingDirectory = solutionRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(ps2Script);
            startInfo.ArgumentList.Add("-CookedBuildDirectory");
            startInfo.ArgumentList.Add(buildDirectory);
            startInfo.ArgumentList.Add("-SceneName");
            startInfo.ArgumentList.Add(sceneName);
            if (_projectSettings.Ps2ProgressiveScan)
                startInfo.ArgumentList.Add("-ProgressiveScan");
            if (_projectSettings.DevelopmentBuild)
                startInfo.ArgumentList.Add("-DevelopmentBuild");
            string pcsx2Path = R2UserSettings.Load().Pcsx2ExecutablePath;
            if (!finalIso && !string.IsNullOrWhiteSpace(pcsx2Path))
            {
                startInfo.ArgumentList.Add("-Pcsx2Path");
                startInfo.ArgumentList.Add(Environment.ExpandEnvironmentVariables(pcsx2Path));
            }

            using System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start the PS2 build process.");
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(finalIso ? 600_000 : 180_000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("The PS2 build exceeded its time limit and was stopped.");
            }

            string output = outputTask.GetAwaiter().GetResult();
            string errors = errorTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
                throw new InvalidOperationException(SummarizeBuildFailure(output, errors));

            string launchSummary = output.Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault(line => line.StartsWith(finalIso ? "Exported PS2 ISO:" : "Launched ", StringComparison.OrdinalIgnoreCase))
                ?? (finalIso ? "PS2 ISO export completed." : "Launched the PS2 build in PCSX2.");
            if (deployToNetwork)
            {
                string networkDirectory = Environment.ExpandEnvironmentVariables(
                    _projectSettings.Ps2NetworkBuildDirectory.Trim());
                if (!Path.IsPathRooted(networkDirectory))
                    networkDirectory = Path.Combine(AssetDatabase.ProjectRoot, networkDirectory);
                Directory.CreateDirectory(networkDirectory);

                string isoName = string.Concat((_projectSettings.ProductName ?? "R2Game")
                    .Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character))
                    .Trim();
                if (string.IsNullOrWhiteSpace(isoName)) isoName = "R2Game";
                string sourceIso = Path.Combine(buildDirectory, "PS2-Final",
                    $"R2GM_000.01.{sceneName}.iso");
                if (!File.Exists(sourceIso))
                    throw new FileNotFoundException("The PS2 build completed without producing its expected ISO.", sourceIso);
                string timestamp = DateTime.Now.ToString("MM-dd-yy HH-mm", System.Globalization.CultureInfo.InvariantCulture);
                string destinationIso = Path.Combine(networkDirectory, $"{isoName} [{timestamp}].iso");
                foreach (string existingIso in Directory.EnumerateFiles(networkDirectory, "*.iso"))
                {
                    string existingName = Path.GetFileName(existingIso);
                    if (string.Equals(existingName, isoName + ".iso", StringComparison.OrdinalIgnoreCase) ||
                        (existingName.StartsWith(isoName + " [", StringComparison.OrdinalIgnoreCase) &&
                         existingName.EndsWith("].iso", StringComparison.OrdinalIgnoreCase)))
                        File.Delete(existingIso);
                }
                File.Copy(sourceIso, destinationIso, overwrite: true);
                launchSummary = $"Network PS2 ISO updated: {destinationIso}";
            }
            return $"{gameBuildResult} {launchSummary}";
        }
        catch (Exception exception)
        {
            return $"PS2 build/run failed: {exception.GetBaseException().Message}";
        }
    }

    private string BuildStandaloneGameWorker(bool runAfterBuild)
    {
        try
        {
            string solutionRoot = FindEngineRoot();
            string playerProject = Path.Combine(solutionRoot, "R2Engine.Player", "R2Engine.Player.csproj");
            string playerTemplate = Path.Combine(solutionRoot, "PlayerTemplate");
            string isolatedBuildCache = Path.Combine(solutionRoot, ".standalone-build-cache");
            string buildLogPath = Path.Combine(isolatedBuildCache, "game-build.log");
            Directory.CreateDirectory(isolatedBuildCache);
            string buildDirectory = Path.IsPathRooted(_projectSettings.BuildOutput)
                ? _projectSettings.BuildOutput
                : Path.Combine(AssetDatabase.ProjectRoot, _projectSettings.BuildOutput);
            Directory.CreateDirectory(buildDirectory);

            if (string.IsNullOrWhiteSpace(_projectSettings.StartupScene))
            {
                _projectSettings.StartupScene = Path.GetRelativePath(AssetDatabase.ProjectRoot, _currentScenePath!);
                SaveProjectSettings();
            }

            string startupScenePath = Path.Combine(AssetDatabase.ProjectRoot, _projectSettings.StartupScene);
            if (!File.Exists(startupScenePath))
                throw new FileNotFoundException("The configured startup scene could not be found.", startupScenePath);

            if (File.Exists(Path.Combine(playerTemplate, "R2Game.exe")))
            {
                CopyDirectory(playerTemplate, buildDirectory);
            }
            else
            {
                string configuration = _projectSettings.DevelopmentBuild ? "Debug" : "Release";
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "dotnet",
                    WorkingDirectory = solutionRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add("publish");
                startInfo.ArgumentList.Add(playerProject);
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add(configuration);
                startInfo.ArgumentList.Add($"-p:BaseOutputPath={isolatedBuildCache}{Path.DirectorySeparatorChar}");
                startInfo.ArgumentList.Add("-o");
                startInfo.ArgumentList.Add(buildDirectory);
                startInfo.ArgumentList.Add("--nologo");
                startInfo.ArgumentList.Add($"-flp:logfile={buildLogPath};verbosity=minimal;append=false");

                using System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Could not start the game build process.");

                if (!process.WaitForExit(120_000))
                {
                    process.Kill(entireProcessTree: true);
                    throw new TimeoutException("The game build exceeded two minutes and was stopped.");
                }

                if (process.ExitCode != 0)
                {
                    string log = File.Exists(buildLogPath)
                        ? File.ReadAllText(buildLogPath)
                        : "";
                    throw new InvalidOperationException(SummarizeBuildFailure(log, ""));
                }
            }

            string[] buildScenes = _projectSettings.BuildScenes
                         .Append(_projectSettings.StartupScene)
                         .Where(path => !string.IsNullOrWhiteSpace(path))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .ToArray();
            List<string> buildScenePaths = new();
            foreach (string scene in buildScenes)
            {
                string sourceScene = Path.Combine(AssetDatabase.ProjectRoot, scene);
                if (!File.Exists(sourceScene))
                    throw new FileNotFoundException("A scene in the build list could not be found.", sourceScene);
                buildScenePaths.Add(sourceScene);
                string destinationScene = Path.Combine(buildDirectory, scene);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationScene)!);
                File.Copy(sourceScene, destinationScene, overwrite: true);
            }
            BuildAssetCollection assetCollection = BuildAssetCollector.Collect(buildScenePaths);
            if (assetCollection.MissingReferences.Count > 0)
                throw new InvalidOperationException(
                    "Build asset validation failed: " + string.Join(" | ", assetCollection.MissingReferences));
            BuildAssetCollector.CopyToBuild(assetCollection, buildDirectory);
            BuildAssetCollector.WriteManifest(assetCollection, buildDirectory);
            TexturePackageExportResult textureExport = TexturePackageExporter.Export(buildScenePaths, buildDirectory);
            MeshPackageExportResult meshExport = MeshPackageExporter.Export(assetCollection, buildDirectory);
            Ps2AnimationPackageExportResult animationExport = Ps2AnimationPackageExporter.Export(buildScenePaths, buildDirectory);
            ScenePackageExportResult sceneExport = ScenePackageExporter.Export(buildScenePaths, buildDirectory);
            Ps2SavePresentation.Export(_projectSettings, AssetDatabase.ProjectRoot, buildDirectory);
            _projectSettings.Save(Path.Combine(buildDirectory, "game.json"));

            if (runAfterBuild)
            {
                string executable = Path.Combine(buildDirectory, "R2Game.exe");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = executable,
                    WorkingDirectory = buildDirectory,
                    UseShellExecute = true
                });
            }

            long textureBudgetBytes = (long)_projectSettings.PerformanceBudgets.MaxTextureVramKilobytes * 1024;
            string textureSummary =
                $" Exported {textureExport.TextureCount} referenced texture package(s), " +
                $"estimated VRAM {FormatBytes(textureExport.TotalVramBytes)}.";
            if (textureExport.TotalVramBytes > textureBudgetBytes)
                textureSummary += $" WARNING: texture budget exceeded by " +
                                  $"{FormatBytes(textureExport.TotalVramBytes - textureBudgetBytes)}.";
            string assetSummary =
                $" Included {assetCollection.Assets.Count} referenced asset(s), " +
                $"{FormatBytes(assetCollection.TotalBytes)} source data.";
            string meshSummary =
                $" Exported {meshExport.MeshCount} PS2 mesh package(s), " +
                $"{FormatBytes(meshExport.TotalBytes)} cooked data.";
            string sceneSummary = $" Cooked {sceneExport.SceneCount} PS2 scene package(s) with " +
                                  $"{sceneExport.InstanceCount} mesh instance(s).";
            if (sceneExport.ScriptWarnings.Count > 0)
                sceneSummary += " WARNING: " + string.Join(" | ", sceneExport.ScriptWarnings) +
                    " See R2Data/ps2-script-report.json in the build folder.";
            return $"Game built to {buildDirectory}.{assetSummary}{textureSummary}{meshSummary}{sceneSummary}";
        }
        catch (Exception exception)
        {
            return $"Game build failed: {exception.GetBaseException().Message}";
        }
    }

    private void UpdateGameBuildStatus()
    {
        if (_gameBuildTask == null || !_gameBuildTask.IsCompleted)
            return;

        _statusMessage = _gameBuildTask.GetAwaiter().GetResult();
        _scriptMessages.Add(_statusMessage);
        _gameBuildTask = null;
    }

    private static string SummarizeBuildFailure(string output, string errors)
    {
        string combined = $"{output}{Environment.NewLine}{errors}";
        string[] lines = combined.Split(
            new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? errorLine = lines.LastOrDefault(line =>
            line.Contains(": error ", StringComparison.OrdinalIgnoreCase));
        return errorLine ?? lines.LastOrDefault() ?? "The build process failed without an error message.";
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string destinationFile = Path.Combine(destination, Path.GetRelativePath(source, file));
            File.Copy(file, destinationFile, overwrite: true);
        }
    }

    private void DrawProjectSettings()
    {
        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        if (_focusProjectSettings)
        {
            ImGui.OpenPopup("Project Settings");
            _focusProjectSettings = false;
        }

        if (!_showProjectSettings)
            return;

        ImGui.SetNextWindowSize(new Vector2(520.0f, 560.0f), ImGuiCond.Appearing);
        ImGui.SetNextWindowPos(
            viewport.WorkPos + viewport.WorkSize * 0.5f,
            ImGuiCond.Appearing,
            new Vector2(0.5f, 0.5f));

        bool keepOpen = true;
        if (!ImGui.BeginPopupModal(
                "Project Settings",
                ref keepOpen,
                ImGuiWindowFlags.NoDocking))
            return;

        bool changed = false;
        ImGui.SeparatorText("Game");

        string productName = _projectSettings.ProductName;
        if (ImGui.InputText("Product Name", ref productName, 128))
        {
            _projectSettings.ProductName = productName;
            changed = true;
        }

        ImGui.Spacing();
        ImGui.SeparatorText("Startup & Loading");
        ImGui.Text("Startup Scene");
        ImGui.SameLine();
        ImGui.TextDisabled(string.IsNullOrWhiteSpace(_projectSettings.StartupScene)
            ? "None selected"
            : _projectSettings.StartupScene);

        if (ImGui.Button("Use Current Scene") && _currentScenePath != null)
        {
            _projectSettings.StartupScene = Path.GetRelativePath(AssetDatabase.ProjectRoot, _currentScenePath);
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("Choose Scene..."))
        {
            string? selectedScene = WindowsFileDialog.OpenScene();
            if (selectedScene != null)
            {
                _projectSettings.StartupScene = Path.GetRelativePath(AssetDatabase.ProjectRoot, selectedScene);
                changed = true;
            }
        }

        string loadingPrefab = _projectSettings.LoadingScreenPrefab;
        if (ImGui.InputText("Loading Screen Prefab", ref loadingPrefab, 512))
        { _projectSettings.LoadingScreenPrefab = loadingPrefab; changed = true; }
        string? droppedLoading = AcceptAssetDrop(".r2prefab");
        if (droppedLoading != null)
        { _projectSettings.LoadingScreenPrefab = AssetDatabase.ToProjectRelativePath(droppedLoading); changed = true; }
        if (ImGui.Button("Use Selected Prefab") && _selectedAssetPath != null &&
            Path.GetExtension(_selectedAssetPath).Equals(".r2prefab", StringComparison.OrdinalIgnoreCase))
        { _projectSettings.LoadingScreenPrefab = AssetDatabase.ToProjectRelativePath(_selectedAssetPath); changed = true; }
        ImGui.SameLine();
        if (ImGui.Button("Create Loading Screen Prefab"))
        {
            Directory.CreateDirectory(AssetDatabase.PrefabsDirectory);
            string path = Path.Combine(AssetDatabase.PrefabsDirectory, "Loading Screen.r2prefab");
            for (int suffix = 2; File.Exists(path); suffix++)
                path = Path.Combine(AssetDatabase.PrefabsDirectory, $"Loading Screen ({suffix}).r2prefab");
            File.WriteAllText(path, SceneSerializer.Serialize(LoadingScreen.CreateTemplate()));
            _projectSettings.LoadingScreenPrefab = AssetDatabase.ToProjectRelativePath(path);
            _projectBrowserPath = AssetDatabase.PrefabsDirectory;
            _selectedAssetPath = path;
            changed = true;
        }
        if (ImGui.Button("Disable Loading Screen"))
        { _projectSettings.LoadingScreenPrefab = ""; changed = true; }
        ImGui.TextWrapped("One passive Canvas prefab, automatically shown between scenes. Edit an instance and Apply Prefab. PS2 displays a static screen during blocking loads; no animation or progress yet.");
        ImGui.Spacing();
        ImGui.SeparatorText("Scenes In Build");
        int removeSceneIndex = -1;
        for (int index = 0; index < _projectSettings.BuildScenes.Count; index++)
        {
            ImGui.PushID(index);
            ImGui.TextUnformatted($"{index + 1}. {_projectSettings.BuildScenes[index]}");
            ImGui.SameLine();
            if (ImGui.SmallButton("X"))
                removeSceneIndex = index;
            ImGui.PopID();
        }

        if (removeSceneIndex >= 0)
        {
            _projectSettings.BuildScenes.RemoveAt(removeSceneIndex);
            changed = true;
        }

        if (ImGui.Button("Add Current Scene") && _currentScenePath != null)
        {
            string relative = Path.GetRelativePath(AssetDatabase.ProjectRoot, _currentScenePath);
            if (!_projectSettings.BuildScenes.Contains(relative, StringComparer.OrdinalIgnoreCase))
            {
                _projectSettings.BuildScenes.Add(relative);
                changed = true;
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Add Scene..."))
        {
            string? selectedScene = WindowsFileDialog.OpenScene();
            if (selectedScene != null)
            {
                string relative = Path.GetRelativePath(AssetDatabase.ProjectRoot, selectedScene);
                if (!_projectSettings.BuildScenes.Contains(relative, StringComparer.OrdinalIgnoreCase))
                {
                    _projectSettings.BuildScenes.Add(relative);
                    changed = true;
                }
            }
        }

        ImGui.Spacing();
        ImGui.SeparatorText("Collision Layers");
        for (int layerIndex = 0; layerIndex < _projectSettings.CollisionLayers.Count; layerIndex++)
        {
            string layerName = _projectSettings.CollisionLayers[layerIndex];
            ImGui.PushID($"CollisionLayer{layerIndex}");
            ImGui.Text($"{layerIndex}");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(220.0f);
            if (ImGui.InputText("##LayerName", ref layerName, 48) && !string.IsNullOrWhiteSpace(layerName))
            {
                _projectSettings.CollisionLayers[layerIndex] = layerName.Trim();
                changed = true;
            }
            ImGui.PopID();
        }
        if (_projectSettings.CollisionLayers.Count < 32 && ImGui.Button("Add Collision Layer"))
        {
            _projectSettings.CollisionLayers.Add($"Layer {_projectSettings.CollisionLayers.Count}");
            changed = true;
        }
        ImGui.SameLine();
        if (_projectSettings.CollisionLayers.Count <= 1) ImGui.BeginDisabled();
        if (ImGui.Button("Remove Last Layer"))
        {
            _projectSettings.CollisionLayers.RemoveAt(_projectSettings.CollisionLayers.Count - 1);
            changed = true;
        }
        if (_projectSettings.CollisionLayers.Count <= 1) ImGui.EndDisabled();
        ImGui.TextDisabled("Removing a layer only removes its name; check existing collider assignments afterward.");

        ImGui.Spacing();
        ImGui.SeparatorText("Input Actions");
        int removeActionIndex = -1;
        for (int index = 0; index < _projectSettings.InputActions.Count; index++)
        {
            InputActionBinding action = _projectSettings.InputActions[index];
            ImGui.PushID(index);
            string actionName = action.Name;
            if (ImGui.InputText("Name", ref actionName, 64)) { action.Name = actionName; changed = true; }
            int actionType = (int)action.Type;
            string[] actionTypes = Enum.GetNames<InputActionType>();
            if (ImGui.Combo("Type", ref actionType, actionTypes, actionTypes.Length))
            {
                action.Type = (InputActionType)actionType;
                changed = true;
            }
            if (action.Type == InputActionType.Button)
            {
                changed |= DrawRuntimeKeyCombo("Primary", action.Primary, value => action.Primary = value);
                changed |= DrawRuntimeKeyCombo("Secondary", action.Secondary, value => action.Secondary = value);
                int controllerButton = action.ControllerButton;
                if (ImGui.InputInt("Controller Button", ref controllerButton))
                {
                    action.ControllerButton = Math.Max(-1, controllerButton);
                    changed = true;
                }
            }
            else if (action.Type == InputActionType.Axis1D)
            {
                changed |= DrawRuntimeKeyCombo("Negative", action.Negative, value => action.Negative = value);
                changed |= DrawRuntimeKeyCombo("Positive", action.Positive, value => action.Positive = value);
                int controllerAxis = action.ControllerAxis;
                if (ImGui.InputInt("Controller Axis", ref controllerAxis))
                {
                    action.ControllerAxis = Math.Max(-1, controllerAxis);
                    changed = true;
                }
            }
            else
            {
                changed |= DrawRuntimeKeyCombo("Up", action.Up, value => action.Up = value);
                changed |= DrawRuntimeKeyCombo("Down", action.Down, value => action.Down = value);
                changed |= DrawRuntimeKeyCombo("Left", action.Left, value => action.Left = value);
                changed |= DrawRuntimeKeyCombo("Right", action.Right, value => action.Right = value);
                int axisX = action.ControllerAxisX;
                int axisY = action.ControllerAxisY;
                if (ImGui.InputInt("Controller X Axis", ref axisX)) { action.ControllerAxisX = Math.Max(-1, axisX); changed = true; }
                if (ImGui.InputInt("Controller Y Axis", ref axisY)) { action.ControllerAxisY = Math.Max(-1, axisY); changed = true; }
                bool invertX = action.InvertControllerX;
                bool invertY = action.InvertControllerY;
                if (ImGui.Checkbox("Invert Controller X", ref invertX)) { action.InvertControllerX = invertX; changed = true; }
                if (ImGui.Checkbox("Invert Controller Y", ref invertY)) { action.InvertControllerY = invertY; changed = true; }
            }
            if (action.Type != InputActionType.Button)
            {
                float deadZone = action.DeadZone;
                if (ImGui.DragFloat("Dead Zone", ref deadZone, 0.01f, 0.0f, 0.95f))
                {
                    action.DeadZone = Math.Clamp(deadZone, 0.0f, 0.95f);
                    changed = true;
                }
            }
            if (ImGui.SmallButton("Remove Action")) removeActionIndex = index;
            ImGui.Separator();
            ImGui.PopID();
        }
        if (removeActionIndex >= 0)
        {
            _projectSettings.InputActions.RemoveAt(removeActionIndex);
            changed = true;
        }
        if (ImGui.Button("Add Input Action"))
        {
            _projectSettings.InputActions.Add(new InputActionBinding());
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("Reset Input Defaults"))
        {
            _projectSettings.InputActions = InputActionBinding.CreateDefaults();
            changed = true;
        }

        ImGui.TextDisabled($"Connected Controller: {RuntimeInput.ControllerName}");
        for (int axisIndex = 0; axisIndex < RuntimeInput.ControllerAxes.Count; axisIndex++)
            ImGui.Text($"Axis {axisIndex}: {RuntimeInput.ControllerAxes[axisIndex]:0.000}");
        string pressedButtons = string.Join(", ", RuntimeInput.ControllerButtons
            .Select((pressed, index) => (pressed, index))
            .Where(item => item.pressed)
            .Select(item => item.index));
        ImGui.Text($"Pressed Buttons: {(pressedButtons.Length == 0 ? "None" : pressedButtons)}");

        ImGui.Spacing();
        ImGui.SeparatorText("Rendering");

        RenderSettings renderSettings = _projectSettings.RenderSettings;
        int renderPreset = (int)renderSettings.Preset;
        string[] renderPresets = Enum.GetNames<RenderQualityPreset>();
        if (ImGui.Combo("Render Preset", ref renderPreset, renderPresets, renderPresets.Length))
        {
            renderSettings.ApplyPreset((RenderQualityPreset)renderPreset);
            changed = true;
        }

        int targetWidth = renderSettings.TargetWidth;
        int targetHeight = renderSettings.TargetHeight;
        if (ImGui.InputInt("Render Width", ref targetWidth))
        {
            renderSettings.TargetWidth = Math.Max(160, targetWidth);
            renderSettings.Preset = RenderQualityPreset.Custom;
            changed = true;
        }
        if (ImGui.InputInt("Render Height", ref targetHeight))
        {
            renderSettings.TargetHeight = Math.Max(120, targetHeight);
            renderSettings.Preset = RenderQualityPreset.Custom;
            changed = true;
        }

        int filtering = (int)renderSettings.TextureFiltering;
        string[] filteringModes = Enum.GetNames<RenderTextureFiltering>();
        if (ImGui.Combo("Texture Filtering", ref filtering, filteringModes, filteringModes.Length))
        {
            renderSettings.TextureFiltering = (RenderTextureFiltering)filtering;
            renderSettings.Preset = RenderQualityPreset.Custom;
            changed = true;
        }

        bool frustumCulling = renderSettings.FrustumCulling;
        if (ImGui.Checkbox("Frustum Culling", ref frustumCulling))
        {
            renderSettings.FrustumCulling = frustumCulling;
            renderSettings.Preset = RenderQualityPreset.Custom;
            changed = true;
        }

        bool renderFog = renderSettings.EnableFog;
        if (ImGui.Checkbox("Allow Scene Fog", ref renderFog))
        {
            renderSettings.EnableFog = renderFog;
            renderSettings.Preset = RenderQualityPreset.Custom;
            changed = true;
        }

        int maxLocalLights = renderSettings.MaxLocalLights;
        if (ImGui.DragInt("Max Point/Spot Lights", ref maxLocalLights, 1.0f, 0, 3))
        {
            renderSettings.MaxLocalLights = Math.Clamp(maxLocalLights, 0, 3);
            renderSettings.Preset = RenderQualityPreset.Custom;
            changed = true;
        }

        ImGui.TextDisabled("Render resolution is presented with its aspect ratio preserved.");
        ImGui.Spacing();
        ImGui.SeparatorText("Desktop Window");

        int width = _projectSettings.Width;
        int height = _projectSettings.Height;
        if (ImGui.InputInt("Width", ref width))
        {
            _projectSettings.Width = Math.Max(320, width);
            changed = true;
        }
        if (ImGui.InputInt("Height", ref height))
        {
            _projectSettings.Height = Math.Max(240, height);
            changed = true;
        }

        int windowMode = (int)_projectSettings.WindowMode;
        string[] modes = { "Windowed", "Borderless", "Fullscreen" };
        if (ImGui.Combo("Window Mode", ref windowMode, modes, modes.Length))
        {
            _projectSettings.WindowMode = (GameWindowMode)windowMode;
            changed = true;
        }

        bool vsync = _projectSettings.VSync;
        if (ImGui.Checkbox("VSync", ref vsync))
        {
            _projectSettings.VSync = vsync;
            changed = true;
        }

        int frameLimit = _projectSettings.FrameRateLimit;
        if (ImGui.InputInt("Frame Rate Limit (0 = unlimited)", ref frameLimit))
        {
            _projectSettings.FrameRateLimit = Math.Max(0, frameLimit);
            changed = true;
        }

        ImGui.Spacing();
        ImGui.SeparatorText("Build");

        bool development = _projectSettings.DevelopmentBuild;
        if (ImGui.Checkbox("Development Build", ref development))
        {
            _projectSettings.DevelopmentBuild = development;
            changed = true;
        }

        ImGui.Spacing();
        ImGui.SeparatorText("PS2 Display & Lighting");
        bool progressiveScan = _projectSettings.Ps2ProgressiveScan;
        int ps2Aspect = (int)_projectSettings.Ps2AspectRatio;
        string[] ps2Aspects = { "4:3", "16:9 (Anamorphic)" };
        if (ImGui.Combo("PS2 Aspect Ratio", ref ps2Aspect, ps2Aspects, ps2Aspects.Length))
        {
            _projectSettings.Ps2AspectRatio = (Ps2DisplayAspect)ps2Aspect;
            changed = true;
        }
        ImGui.TextWrapped("Match the TV or PCSX2 aspect ratio to this setting. Widescreen uses the same framebuffer resolution and shows more horizontally. UI anchors follow the screen edges; element proportions are preserved.");
        if (ImGui.Checkbox("PS2 Progressive Scan (480p)", ref progressiveScan))
        {
            _projectSettings.Ps2ProgressiveScan = progressiveScan;
            changed = true;
        }
        ImGui.TextDisabled("Non-interlaced output for PCSX2 and compatible modern displays.");
        bool bakeStaticLighting = _projectSettings.Ps2BakeStaticVertexLighting;
        if (ImGui.Checkbox("Bake Static Vertex Lighting on PS2 Export", ref bakeStaticLighting))
        {
            _projectSettings.Ps2BakeStaticVertexLighting = bakeStaticLighting;
            changed = true;
        }
        ImGui.TextWrapped("Bakes ambient and directional light into static mesh vertices. Uses main RAM, not texture VRAM; animated and scripted objects keep dynamic lighting.");
        ImGui.Spacing();
        ImGui.SeparatorText("PS2 Memory Card");
        string cardTitle = _projectSettings.Ps2MemoryCardTitle ?? "";
        string saveId = _projectSettings.Ps2SaveId ?? "";
        if (ImGui.InputText("PS2 Save ID", ref saveId, 64))
        { _projectSettings.Ps2SaveId = saveId; changed = true; }
        ImGui.TextWrapped("Stable game identity: 1-20 uppercase letters, digits, underscore or hyphen. Keep it unchanged after release. Changing it selects a different save folder; changing the title does not.");
        bool importLegacy = _projectSettings.Ps2ImportLegacySaves;
        if (ImGui.Checkbox("Import Legacy R2ENGINE Saves", ref importLegacy))
        { _projectSettings.Ps2ImportLegacySaves = importLegacy; changed = true; }
        ImGui.TextWrapped("Enable only for the game that owns your old shared saves. If no save exists in the new folder, Load may read the old folder. Save then writes the new folder; originals are never deleted. Leave off for new games.");
        if (ImGui.InputText("PS2 Memory Card Title", ref cardTitle, 256))
        { _projectSettings.Ps2MemoryCardTitle = cardTitle; changed = true; }
        ImGui.TextDisabled("Blank uses Product Name. Up to 32 full-width / Japanese characters.");
        string cardIcon = _projectSettings.Ps2MemoryCardIcon ?? "";
        if (ImGui.InputText("PS2 Memory Card Icon", ref cardIcon, 512))
        { _projectSettings.Ps2MemoryCardIcon = cardIcon; changed = true; }
        string? droppedCardIcon = AcceptAssetDrop(".icn", ".ico", ".icon");
        if (droppedCardIcon != null)
        { _projectSettings.Ps2MemoryCardIcon = AssetDatabase.ToProjectRelativePath(droppedCardIcon); changed = true; }
        if (ImGui.BeginCombo("Choose Memory Card Icon", string.IsNullOrEmpty(cardIcon) ? "Built-in teal gem" : Path.GetFileName(cardIcon)))
        {
            if (ImGui.Selectable("Built-in teal gem", string.IsNullOrEmpty(cardIcon)))
            { _projectSettings.Ps2MemoryCardIcon = ""; changed = true; }
            string assets = Path.Combine(AssetDatabase.ProjectRoot, "Assets");
            if (Directory.Exists(assets)) foreach (string path in Directory.EnumerateFiles(assets, "*", SearchOption.AllDirectories)
                .Where(p => new[] { ".icn", ".ico", ".icon" }.Contains(Path.GetExtension(p).ToLowerInvariant())).OrderBy(p => p))
            {
                string relative = AssetDatabase.ToProjectRelativePath(path);
                if (ImGui.Selectable(relative, relative == cardIcon))
                { _projectSettings.Ps2MemoryCardIcon = relative; changed = true; }
            }
            ImGui.EndCombo();
        }
        ImGui.TextWrapped("Native PS2 3D icon (.icn/.ico/.icon), not a PNG or Windows icon. Put it in Assets, choose it above, or drag it onto the field. Blank uses the built-in gem. Rebuild and save in-game to update the card entry; existing progress is retained.");

        ImGui.Spacing();
        ImGui.SeparatorText("Build Locations");
        string output = _projectSettings.BuildOutput;
        if (ImGui.InputText("Output Folder", ref output, 260))
        {
            _projectSettings.BuildOutput = output;
            changed = true;
        }

        string networkOutput = _projectSettings.Ps2NetworkBuildDirectory ?? "";
        if (ImGui.InputText("Network PS2 DVD Folder", ref networkOutput, 512))
        {
            _projectSettings.Ps2NetworkBuildDirectory = networkOutput;
            changed = true;
        }
        ImGui.TextWrapped("Folder shared with OPL for ETH Games (the share's DVD folder). Each network build replaces the previous ISO and adds its export time to the PS2-visible filename (MM-DD-YY HH-mm). Windows filenames cannot contain / or :.");

        if (changed)
        {
            SaveProjectSettings();
            RuntimeInput.Configure(_projectSettings.InputActions);
            _sceneRenderer.ConfigureRenderSettings(_projectSettings.RenderSettings);
            _gameRenderer.ConfigureRenderSettings(_projectSettings.RenderSettings);
            _cameraPreviewRenderer.ConfigureRenderSettings(_projectSettings.RenderSettings);
        }

        if (ImGui.Button("Close"))
        {
            keepOpen = false;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();

        if (!keepOpen)
            _showProjectSettings = false;
    }

    private void SaveProjectSettings() =>
        _projectSettings.Save(Path.Combine(AssetDatabase.ProjectRoot, "ProjectSettings.json"));

    private static string FindEngineRoot()
    {
        DirectoryInfo? directory = new(Path.GetFullPath(AppContext.BaseDirectory));
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "R2Engine.Player", "R2Engine.Player.csproj")) &&
                Directory.Exists(Path.Combine(directory.FullName, "R2Engine.PS2")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the R2Engine installation folder.");
    }

    private static bool DrawRuntimeKeyCombo(string label, RuntimeKey key, Action<RuntimeKey> apply)
    {
        int selected = (int)key;
        string[] names = Enum.GetNames<RuntimeKey>();
        if (!ImGui.Combo(label, ref selected, names, names.Length))
            return false;
        apply((RuntimeKey)selected);
        return true;
    }

    private void CreateAnimationClip(GameObject target, Animator animator)
    {
        Directory.CreateDirectory(AssetDatabase.AnimationsDirectory);
        string safeName = target.Name;
        foreach (char invalid in Path.GetInvalidFileNameChars())
            safeName = safeName.Replace(invalid, '_');
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "Animation";

        string path = Path.Combine(AssetDatabase.AnimationsDirectory, safeName + ".r2anim");
        int suffix = 1;
        while (File.Exists(path))
            path = Path.Combine(AssetDatabase.AnimationsDirectory, $"{safeName} ({suffix++}).r2anim");

        AnimationClip clip = new()
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Duration = 1.0f,
            Keyframes =
            {
                new AnimationKeyframe
                {
                    Time = 0.0f,
                    Position = target.Transform.Position,
                    Rotation = target.Transform.Rotation,
                    Scale = target.Transform.Scale
                }
            }
        };
        clip.Save(path);
        string relative = AssetDatabase.ToProjectRelativePath(path);
        RecordImmediateEdit(() => animator.ClipPath = relative);
        _projectBrowserPath = AssetDatabase.AnimationsDirectory;
        _statusMessage = $"Created animation clip: {Path.GetFileName(path)}";
    }

    private void DrawAnimationClipEditor(GameObject target, Animator animator)
    {
        try
        {
            string path = AssetDatabase.ToAbsolutePath(animator.ClipPath);
            AnimationClip clip = AnimationClip.Load(path);
            bool changed = false;

            ImGui.SeparatorText("Clip Keyframes");
            float duration = clip.Duration;
            if (ImGui.DragFloat("Duration", ref duration, 0.05f, 0.01f, 3600.0f))
            {
                clip.Duration = Math.Max(0.01f, duration);
                changed = true;
            }

            int removeIndex = -1;
            for (int index = 0; index < clip.Keyframes.Count; index++)
            {
                AnimationKeyframe frame = clip.Keyframes[index];
                ImGui.PushID(index);
                float time = frame.Time;
                if (ImGui.DragFloat("Time", ref time, 0.01f, 0.0f, clip.Duration))
                {
                    frame.Time = Math.Clamp(time, 0.0f, clip.Duration);
                    changed = true;
                }
                Vector3 position = frame.Position;
                Vector3 rotation = frame.Rotation;
                Vector3 scale = frame.Scale;
                if (ImGui.DragFloat3("Position", ref position, 0.05f)) { frame.Position = position; changed = true; }
                if (ImGui.DragFloat3("Rotation", ref rotation, 0.5f)) { frame.Rotation = rotation; changed = true; }
                if (ImGui.DragFloat3("Scale", ref scale, 0.05f)) { frame.Scale = scale; changed = true; }
                if (ImGui.SmallButton("Remove Keyframe"))
                    removeIndex = index;
                ImGui.Separator();
                ImGui.PopID();
            }

            if (removeIndex >= 0)
            {
                clip.Keyframes.RemoveAt(removeIndex);
                changed = true;
            }

            ImGui.DragFloat("New Key Time", ref _newAnimationKeyTime, 0.05f, 0.0f, clip.Duration);
            if (ImGui.Button("Add Keyframe From Current Transform"))
            {
                clip.Keyframes.Add(new AnimationKeyframe
                {
                    Time = Math.Clamp(_newAnimationKeyTime, 0.0f, clip.Duration),
                    Position = target.Transform.Position,
                    Rotation = target.Transform.Rotation,
                    Scale = target.Transform.Scale
                });
                clip.Keyframes = clip.Keyframes.OrderBy(frame => frame.Time).ToList();
                changed = true;
            }

            if (changed)
                clip.Save(path);
        }
        catch (Exception exception)
        {
            ImGui.TextDisabled($"Could not edit clip: {exception.GetBaseException().Message}");
        }
    }

    private void OpenAnimationEditor(string path)
    {
        RestoreAnimationPreviewTransform();
        _animationEditorPath = path;
        _showAnimationWindow = true;
        _animationPreviewPlaying = false;
        _animationPreviewTime = 0.0f;
        _animationSelectedKeyframe = -1;
        if (_selectedObject != null)
            SetAnimationPreviewObject(_selectedObject);
    }

    private void SetAnimationPreviewObject(GameObject gameObject)
    {
        RestoreAnimationPreviewTransform();
        _animationPreviewObject = gameObject;
        _animationPreviewOriginalPosition = gameObject.Transform.Position;
        _animationPreviewOriginalRotation = gameObject.Transform.Rotation;
        _animationPreviewOriginalScale = gameObject.Transform.Scale;
        _hasAnimationPreviewOriginal = true;
    }

    private void RestoreAnimationPreviewTransform()
    {
        if (_hasAnimationPreviewOriginal && _animationPreviewObject != null &&
            _animationPreviewObject.Scene == _scene)
        {
            _animationPreviewObject.Transform.Position = _animationPreviewOriginalPosition;
            _animationPreviewObject.Transform.Rotation = _animationPreviewOriginalRotation;
            _animationPreviewObject.Transform.Scale = _animationPreviewOriginalScale;
        }
        _hasAnimationPreviewOriginal = false;
    }

    private void DrawAnimationWindow()
    {
        if (!_showAnimationWindow)
            return;

        ImGui.SetNextWindowSize(new Vector2(720.0f, 470.0f), ImGuiCond.FirstUseEver);
        bool open = _showAnimationWindow;
        if (!ImGui.Begin("Animation", ref open))
        {
            ImGui.End();
            if (!open)
            {
                RestoreAnimationPreviewTransform();
                _showAnimationWindow = false;
            }
            return;
        }

        if (_animationEditorPath == null || !File.Exists(_animationEditorPath))
        {
            ImGui.TextDisabled("Double-click a .r2anim asset in the Project browser.");
            if (_selectedAssetPath != null &&
                string.Equals(Path.GetExtension(_selectedAssetPath), ".r2anim", StringComparison.OrdinalIgnoreCase) &&
                ImGui.Button("Open Selected Animation"))
                OpenAnimationEditor(_selectedAssetPath);
            ImGui.End();
            _showAnimationWindow = open;
            return;
        }

        try
        {
            AnimationClip clip = AnimationClip.Load(_animationEditorPath);
            bool changed = false;
            bool applyPreview = _animationPreviewPlaying;

            ImGui.TextUnformatted(Path.GetFileName(_animationEditorPath));
            ImGui.SameLine();
            ImGui.TextDisabled(_animationPreviewObject == null
                ? "No preview object"
                : $"Preview: {_animationPreviewObject.Name}");
            if (ImGui.Button("Use Selected Object") && _selectedObject != null)
                SetAnimationPreviewObject(_selectedObject);

            float editedDuration = clip.Duration;
            if (ImGui.DragFloat("Duration##AnimationWindow", ref editedDuration, 0.05f, 0.01f, 3600.0f))
            {
                clip.Duration = Math.Max(0.01f, editedDuration);
                _animationPreviewTime = Math.Min(_animationPreviewTime, clip.Duration);
                changed = true;
            }

            if (ImGui.Button(_animationPreviewPlaying ? "Pause" : "Play"))
            {
                _animationPreviewPlaying = !_animationPreviewPlaying;
                applyPreview = _animationPreviewPlaying;
            }
            ImGui.SameLine();
            if (ImGui.Button("Stop"))
            {
                _animationPreviewPlaying = false;
                _animationPreviewTime = 0.0f;
                applyPreview = true;
            }

            if (_animationPreviewPlaying)
            {
                _animationPreviewTime += Math.Max(0.0f, ImGui.GetIO().DeltaTime);
                if (_animationPreviewTime > clip.Duration)
                    _animationPreviewTime = clip.Duration <= 0.0f ? 0.0f : _animationPreviewTime % clip.Duration;
            }

            float duration = Math.Max(0.01f, clip.Duration);
            if (ImGui.SliderFloat("Playhead", ref _animationPreviewTime, 0.0f, duration, "%.2f s"))
            {
                _animationPreviewPlaying = false;
                applyPreview = true;
            }

            Vector2 timelineSize = new(Math.Max(100.0f, ImGui.GetContentRegionAvail().X), 72.0f);
            ImGui.InvisibleButton("##AnimationTimeline", timelineSize);
            Vector2 timelineMin = ImGui.GetItemRectMin();
            Vector2 timelineMax = ImGui.GetItemRectMax();
            ImDrawListPtr drawList = ImGui.GetWindowDrawList();
            uint background = ImGui.ColorConvertFloat4ToU32(new Vector4(0.025f, 0.055f, 0.11f, 1.0f));
            uint lineColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.18f, 0.48f, 0.88f, 1.0f));
            uint selectedColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 0.88f, 1.0f, 1.0f));
            drawList.AddRectFilled(timelineMin, timelineMax, background, 3.0f);
            drawList.AddRect(timelineMin, timelineMax, lineColor, 3.0f);

            for (int index = 0; index < clip.Keyframes.Count; index++)
            {
                float x = timelineMin.X + Math.Clamp(clip.Keyframes[index].Time / duration, 0.0f, 1.0f) * timelineSize.X;
                Vector2 marker = new(x, (timelineMin.Y + timelineMax.Y) * 0.5f);
                drawList.AddCircleFilled(marker, index == _animationSelectedKeyframe ? 7.0f : 5.0f,
                    index == _animationSelectedKeyframe ? selectedColor : lineColor, 12);
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left) &&
                    Vector2.Distance(ImGui.GetMousePos(), marker) <= 10.0f)
                    _animationSelectedKeyframe = index;
            }

            float playheadX = timelineMin.X + Math.Clamp(_animationPreviewTime / duration, 0.0f, 1.0f) * timelineSize.X;
            drawList.AddLine(new Vector2(playheadX, timelineMin.Y), new Vector2(playheadX, timelineMax.Y), selectedColor, 2.0f);
            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                _animationPreviewTime = Math.Clamp(
                    (ImGui.GetMousePos().X - timelineMin.X) / timelineSize.X * duration,
                    0.0f,
                    duration);
                _animationPreviewPlaying = false;
                applyPreview = true;
            }

            if (applyPreview && _animationPreviewObject != null)
            {
                clip.Sample(_animationPreviewTime, out Vector3 position, out Vector3 rotation, out Vector3 scale);
                _animationPreviewObject.Transform.Position = position;
                _animationPreviewObject.Transform.Rotation = rotation;
                _animationPreviewObject.Transform.Scale = scale;
            }

            if (ImGui.Button("Add Keyframe At Playhead") && _animationPreviewObject != null)
            {
                clip.Keyframes.Add(new AnimationKeyframe
                {
                    Time = _animationPreviewTime,
                    Position = _animationPreviewObject.Transform.Position,
                    Rotation = _animationPreviewObject.Transform.Rotation,
                    Scale = _animationPreviewObject.Transform.Scale
                });
                clip.Keyframes = clip.Keyframes.OrderBy(frame => frame.Time).ToList();
                _animationSelectedKeyframe = clip.Keyframes.FindIndex(frame => MathF.Abs(frame.Time - _animationPreviewTime) < 0.0001f);
                changed = true;
            }

            if (_animationSelectedKeyframe >= 0 && _animationSelectedKeyframe < clip.Keyframes.Count)
            {
                AnimationKeyframe frame = clip.Keyframes[_animationSelectedKeyframe];
                ImGui.SameLine();
                if (ImGui.Button("Copy"))
                    _copiedAnimationKeyframe = CloneAnimationKeyframe(frame);
                ImGui.SameLine();
                if (ImGui.Button("Delete"))
                {
                    clip.Keyframes.RemoveAt(_animationSelectedKeyframe);
                    _animationSelectedKeyframe = -1;
                    changed = true;
                }

                if (_animationSelectedKeyframe >= 0)
                {
                    float keyTime = frame.Time;
                    Vector3 keyPosition = frame.Position;
                    Vector3 keyRotation = frame.Rotation;
                    Vector3 keyScale = frame.Scale;
                    if (ImGui.DragFloat("Key Time", ref keyTime, 0.01f, 0.0f, duration)) { frame.Time = keyTime; changed = true; }
                    if (ImGui.DragFloat3("Key Position", ref keyPosition, 0.05f)) { frame.Position = keyPosition; changed = true; }
                    if (ImGui.DragFloat3("Key Rotation", ref keyRotation, 0.5f)) { frame.Rotation = keyRotation; changed = true; }
                    if (ImGui.DragFloat3("Key Scale", ref keyScale, 0.05f)) { frame.Scale = keyScale; changed = true; }
                }
            }

            if (_copiedAnimationKeyframe != null)
            {
                ImGui.SameLine();
                if (ImGui.Button("Paste At Playhead"))
                {
                    AnimationKeyframe pasted = CloneAnimationKeyframe(_copiedAnimationKeyframe);
                    pasted.Time = _animationPreviewTime;
                    clip.Keyframes.Add(pasted);
                    clip.Keyframes = clip.Keyframes.OrderBy(frame => frame.Time).ToList();
                    changed = true;
                }
            }

            if (changed)
                clip.Save(_animationEditorPath);
        }
        catch (Exception exception)
        {
            ImGui.TextDisabled($"Animation editor error: {exception.GetBaseException().Message}");
        }

        ImGui.End();
        _showAnimationWindow = open;
        if (!open)
            RestoreAnimationPreviewTransform();
    }

    private static AnimationKeyframe CloneAnimationKeyframe(AnimationKeyframe source) => new()
    {
        Time = source.Time,
        Position = source.Position,
        Rotation = source.Rotation,
        Scale = source.Scale
    };

    // =========================================================
    // Unsaved Changes
    // =========================================================

    public bool ConfirmWindowClose()
    {
        if (!IsSceneDirty) return true;
        RequestSceneAction(PendingSceneAction.CloseEditor);
        return false;
    }

    private void RequestSceneAction(
        PendingSceneAction action)
    {
        if (!IsSceneDirty)
        {
            ExecuteSceneAction(
                action);

            return;
        }

        _pendingSceneAction =
            action;

        _openUnsavedChangesPopup =
            true;
    }

    private void RequestOpenScene(string path)
    {
        _pendingOpenScenePath = path;
        RequestSceneAction(PendingSceneAction.OpenScene);
    }

    private void DrawUnsavedChangesPopup()
    {
        if (_openUnsavedChangesPopup)
        {
            ImGui.OpenPopup(
                "Unsaved Changes");

            _openUnsavedChangesPopup =
                false;
        }

        bool open =
            true;

        if (!ImGui.BeginPopupModal(
                "Unsaved Changes",
                ref open,
                ImGuiWindowFlags.AlwaysAutoResize))
        {
            return;
        }

        ImGui.Text(
            "The current scene has unsaved changes.");

        ImGui.Text(_pendingSceneAction == PendingSceneAction.CloseEditor
            ? "Save your scene before closing the editor?"
            : "Do you want to save before continuing?");
        if (_statusMessage.StartsWith("Save failed:", StringComparison.Ordinal))
            ImGui.TextWrapped(_statusMessage);

        ImGui.Separator();

        if (ImGui.Button(
                "Save",
                new Vector2(
                    100.0f,
                    0.0f)))
        {
            if (SaveScene())
            {
                PendingSceneAction action =
                    _pendingSceneAction;

                _pendingSceneAction =
                    PendingSceneAction.None;

                ImGui.CloseCurrentPopup();

                ExecuteSceneAction(
                    action);
            }
        }

        ImGui.SameLine();

        if (ImGui.Button(
                "Don't Save",
                new Vector2(
                    100.0f,
                    0.0f)))
        {
            PendingSceneAction action =
                _pendingSceneAction;

            _pendingSceneAction =
                PendingSceneAction.None;

            ImGui.CloseCurrentPopup();

            ExecuteSceneAction(
                action);
        }

        ImGui.SameLine();

        if (ImGui.Button(
                "Cancel",
                new Vector2(
                    100.0f,
                    0.0f)))
        {
            _pendingSceneAction =
                PendingSceneAction.None;
            _pendingOpenScenePath = null;

            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
        if (!open) _pendingSceneAction = PendingSceneAction.None;
    }

    private void ExecuteSceneAction(
        PendingSceneAction action)
    {
        switch (action)
        {
            case PendingSceneAction.CloseEditor:
                // The main loop owns disposal, after this ImGui frame has finished.
                _window.IsClosing = true;
                break;
            case PendingSceneAction.NewScene:

                NewSceneImmediately();

                break;

            case PendingSceneAction.OpenScene:

                OpenSceneImmediately(_pendingOpenScenePath);
                _pendingOpenScenePath = null;

                break;
        }
    }

    // =========================================================
    // Scene Files
    // =========================================================

    private void NewSceneImmediately()
    {
        ExitPlayMode();

        _scene =
            CreateDefaultScene();

        _currentScenePath =
            null;

        _undoRedo.Clear();

        _savedSceneSnapshot =
            SceneSerializer.Serialize(
                _scene);

        _statusMessage =
            "Created new scene.";
    }

    private bool SaveScene()
    {
        if (_currentScenePath ==
            null)
        {
            return SaveSceneAs();
        }

        try
        {
            SceneSerializer.Save(
                _scene,
                _currentScenePath);

            _savedSceneSnapshot =
                SceneSerializer.Serialize(
                    _scene);

            _statusMessage =
                $"Saved {Path.GetFileName(_currentScenePath)}";

            return true;
        }
        catch (
            Exception exception)
        {
            _statusMessage =
                $"Save failed: {exception.Message}";

            return false;
        }
    }

    private bool SaveSceneAs()
    {
        string suggestedName =
            GetSuggestedSceneFileName();

        string? path =
            WindowsFileDialog.SaveScene(
                suggestedName);

        if (path ==
            null)
        {
            return false;
        }

        try
        {
            if (!path.EndsWith(
                    ".r2scene",
                    StringComparison.OrdinalIgnoreCase))
            {
                path +=
                    ".r2scene";
            }

            _currentScenePath =
                path;

            _scene.Name =
                Path.GetFileNameWithoutExtension(
                    path);

            SceneSerializer.Save(
                _scene,
                path);

            _savedSceneSnapshot =
                SceneSerializer.Serialize(
                    _scene);

            _statusMessage =
                $"Saved {Path.GetFileName(path)}";

            return true;
        }
        catch (
            Exception exception)
        {
            _statusMessage =
                $"Save failed: {exception.Message}";

            return false;
        }
    }

    private void OpenSceneImmediately(string? requestedPath = null)
    {
        string? path = requestedPath ?? WindowsFileDialog.OpenScene();

        if (path ==
            null)
        {
            return;
        }

        try
        {
            ExitPlayMode();

            _scene =
                SceneSerializer.Load(
                    path);

            _scene.Name =
                Path.GetFileNameWithoutExtension(
                    path);

            _currentScenePath =
                path;

            _selectedObject =
                _scene.GameObjects.Count >
                0
                    ? _scene.GameObjects[0]
                    : null;

            _undoRedo.Clear();

            _savedSceneSnapshot =
                SceneSerializer.Serialize(
                    _scene);

            List<string> referenceMessages = AssetDatabase.ValidateSceneReferences(_scene);
            foreach (string referenceMessage in referenceMessages)
                _scriptMessages.Add(referenceMessage);

            _statusMessage = referenceMessages.Count == 0
                ? $"Opened {Path.GetFileName(path)}"
                : $"Opened {Path.GetFileName(path)} with {referenceMessages.Count} missing or invalid reference(s). See Console.";
        }
        catch (
            Exception exception)
        {
            _statusMessage =
                $"Open failed: {exception.Message}";
        }
    }

    private string GetSuggestedSceneFileName()
    {
        if (_currentScenePath !=
            null)
        {
            return Path.GetFileName(
                _currentScenePath);
        }

        if (!string.IsNullOrWhiteSpace(
                _scene.Name) &&
            _scene.Name !=
            "Untitled Scene")
        {
            return
                $"{_scene.Name}.r2scene";
        }

        return
            "Untitled.r2scene";
    }

    // =========================================================
    // Model Import
    // =========================================================

    private void ImportCharacterSourceToScene(string sourcePath)
    {
        try
        {
            string sourceExtension = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (sourceExtension is not (".fbx" or ".glb" or ".gltf"))
                throw new InvalidDataException("Choose an FBX, GLB, or glTF character file.");

            string characterPath = AssetDatabase.ImportModel(sourcePath);
            string absolutePath = AssetDatabase.ToAbsolutePath(characterPath);
            MeshAssetCache.Clear();
            CreateModelObjectFromAsset(absolutePath);
            _projectBrowserPath = AssetDatabase.ModelsDirectory;
            _selectedAssetPath = absolutePath;
            _statusMessage = DescribeImportedModel(characterPath) + " Added it to the scene.";
        }
        catch (Exception exception)
        {
            _statusMessage = $"Character import failed: {exception.Message}";
        }
    }

    private void CreateModelObjectFromAsset(
        string modelPath)
    {
        try
        {
            string assetPath =
                AssetDatabase.ToProjectRelativePath(
                    modelPath);

            // Force-load now so malformed OBJ files fail here
            // instead of later during rendering.
            Mesh mesh =
                MeshAssetCache.Get(
                    assetPath);

            SkeletalAsset? skeletalAsset =
                string.Equals(Path.GetExtension(assetPath), ".r2skel", StringComparison.OrdinalIgnoreCase)
                    ? SkeletalAsset.Load(AssetDatabase.ToAbsolutePath(assetPath))
                    : null;

            RecordImmediateEdit(
                () =>
                {
                    GameObject modelObject = _scene.CreateGameObject(mesh.Name);

                    MeshRenderer? renderer = null;
                    if (skeletalAsset != null || mesh.Submeshes.Count == 1)
                    {
                        renderer = modelObject.AddComponent<MeshRenderer>();
                        renderer.MeshPath = assetPath;
                    }
                    else
                    {
                        Dictionary<string, int> objectNameCounts = mesh.Submeshes
                            .GroupBy(section => section.ObjectName ?? section.MaterialSlotName,
                                StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(group => group.Key, group => group.Count(),
                                StringComparer.OrdinalIgnoreCase);
                        for (int sectionIndex = 0; sectionIndex < mesh.Submeshes.Count; sectionIndex++)
                        {
                            MeshSubmesh section = mesh.Submeshes[sectionIndex];
                            string objectName = section.ObjectName ?? section.MaterialSlotName;
                            string childName = objectNameCounts[objectName] > 1
                                ? $"{objectName} - {section.MaterialSlotName}"
                                : objectName;
                            GameObject child = _scene.CreateGameObject(childName);
                            child.SetParent(modelObject, keepWorldPosition: false);
                            MeshRenderer childRenderer = child.AddComponent<MeshRenderer>();
                            childRenderer.MeshPath = assetPath;
                            childRenderer.SubmeshIndex = sectionIndex;
                        }
                    }

                    if (skeletalAsset != null)
                    {
                        CreateBoneHierarchy(modelObject, skeletalAsset);
                        if (skeletalAsset.Animations.Count > 0)
                        {
                            Animator animator = modelObject.AddComponent<Animator>();
                            animator.SkeletalTake = skeletalAsset.Animations[0].Name;
                        }
                    }

                    _selectedObject =
                        modelObject;
                });

            _statusMessage =
                skeletalAsset == null
                    ? $"Created model object: {mesh.Name}"
                    : $"Created character object: {mesh.Name} with {skeletalAsset.Bones.Count} bone nodes.";
        }
        catch (
            Exception exception)
        {
            _statusMessage =
                $"Could not create model object: {exception.Message}";
        }
    }

    private void CreateBoneHierarchy(GameObject characterRoot, SkeletalAsset asset)
    {
        GameObject[] boneObjects = new GameObject[asset.Bones.Count];
        for (int index = 0; index < asset.Bones.Count; index++)
        {
            SkeletalBone bone = asset.Bones[index];
            GameObject boneObject = _scene.CreateGameObject(bone.Name);
            boneObjects[index] = boneObject;
            SkeletonBone marker = boneObject.AddComponent<SkeletonBone>();
            marker.BoneIndex = index;
            marker.BoneName = bone.Name;

            GameObject parent = bone.ParentIndex >= 0 && bone.ParentIndex < index
                ? boneObjects[bone.ParentIndex]
                : characterRoot;
            boneObject.SetParent(parent, keepWorldPosition: false);

            if (Matrix4x4.Decompose(
                    bone.LocalBindTransform,
                    out Vector3 scale,
                    out Quaternion rotation,
                    out Vector3 translation))
            {
                boneObject.Transform.Position = translation;
                boneObject.Transform.Rotation = QuaternionToEulerDegrees(rotation);
                boneObject.Transform.Scale = scale;
            }
        }
    }

    private static Vector3 QuaternionToEulerDegrees(Quaternion rotation)
    {
        rotation = Quaternion.Normalize(rotation);

        float sinPitch = 2.0f * (rotation.W * rotation.X - rotation.Z * rotation.Y);
        float pitch = MathF.Abs(sinPitch) >= 1.0f
            ? MathF.CopySign(MathF.PI * 0.5f, sinPitch)
            : MathF.Asin(sinPitch);
        float yaw = MathF.Atan2(
            2.0f * (rotation.W * rotation.Y + rotation.X * rotation.Z),
            1.0f - 2.0f * (rotation.X * rotation.X + rotation.Y * rotation.Y));
        float roll = MathF.Atan2(
            2.0f * (rotation.W * rotation.Z + rotation.X * rotation.Y),
            1.0f - 2.0f * (rotation.X * rotation.X + rotation.Z * rotation.Z));

        const float radiansToDegrees = 180.0f / MathF.PI;
        return new Vector3(pitch, yaw, roll) * radiansToDegrees;
    }

    private void CreatePrefabAsset(GameObject source)
    {
        try
        {
            AssetDatabase.Initialize();

            string safeName = source.Name;
            foreach (char invalid in Path.GetInvalidFileNameChars())
                safeName = safeName.Replace(invalid, '_');

            if (string.IsNullOrWhiteSpace(safeName))
                safeName = "New Prefab";

            Directory.CreateDirectory(AssetDatabase.PrefabsDirectory);
            string path = Path.Combine(AssetDatabase.PrefabsDirectory, safeName + ".r2prefab");
            int suffix = 1;
            while (File.Exists(path))
                path = Path.Combine(AssetDatabase.PrefabsDirectory, $"{safeName} ({suffix++}).r2prefab");

            File.WriteAllText(path, SerializePrefabState(source));
            source.PrefabPath = AssetDatabase.ToProjectRelativePath(path);
            source.PrefabBaseline = SerializePrefabState(source);
            _projectBrowserPath = AssetDatabase.PrefabsDirectory;
            _selectedAssetPath = path;
            _statusMessage = $"Created prefab: {Path.GetFileName(path)}";
        }
        catch (Exception exception)
        {
            _statusMessage = $"Could not create prefab: {exception.Message}";
        }
    }

    private void InstantiatePrefab(string prefabPath)
    {
        try
        {
            List<GameObject> prefabObjects = LoadPrefabObjects(prefabPath);
            GameObject prefabObject = prefabObjects.First(gameObject => gameObject.Parent == null);

            RecordImmediateEdit(() =>
            {
                GameObject nameProbe = _scene.CreateGameObject(prefabObject.Name);
                string uniqueName = nameProbe.Name;
                _scene.DeleteGameObject(nameProbe);

                prefabObject.Name = uniqueName;
                prefabObject.PrefabPath = AssetDatabase.ToProjectRelativePath(prefabPath);
                foreach (GameObject item in prefabObjects)
                    _scene.AddGameObject(item);
                prefabObject.PrefabBaseline = SerializePrefabState(prefabObject);
                _selectedObject = prefabObject;
            });

            _statusMessage = $"Instantiated prefab: {Path.GetFileName(prefabPath)}";
        }
        catch (Exception exception)
        {
            _statusMessage = $"Could not instantiate prefab: {exception.Message}";
        }
    }

    private static List<GameObject> LoadPrefabObjects(string prefabPath)
    {
        Scene.Scene prefabScene = SceneSerializer.Load(prefabPath);
        List<GameObject> objects = prefabScene.GameObjects.ToList();
        if (objects.Count == 0)
            throw new InvalidDataException("The prefab contains no GameObject.");

        prefabScene.Clear();
        foreach (GameObject item in objects)
        {
            item.PrefabPath = null;
            item.PrefabBaseline = null;
        }

        return objects;
    }

    private static string SerializePrefabState(GameObject source)
    {
        Scene.Scene owner = source.Scene
            ?? throw new InvalidOperationException("The prefab object must belong to a scene.");
        int sourceIndex = owner.GameObjects.IndexOf(source);
        Scene.Scene clone = SceneSerializer.Deserialize(SceneSerializer.Serialize(owner));
        GameObject prefabObject = clone.GameObjects[sourceIndex];

        prefabObject.SetParent(null);
        HashSet<GameObject> included = new() { prefabObject };
        void IncludeChildren(GameObject parent)
        {
            foreach (GameObject child in parent.Children.ToArray())
            {
                included.Add(child);
                IncludeChildren(child);
            }
        }
        IncludeChildren(prefabObject);

        foreach (GameObject other in clone.GameObjects.ToArray())
        {
            if (!included.Contains(other))
                clone.DeleteGameObject(other);
        }

        prefabObject.PrefabPath = null;
        prefabObject.PrefabBaseline = null;
        clone.Name = "Prefab";
        return SceneSerializer.Serialize(clone);
    }

    private bool IsPrefabOverridden(GameObject gameObject) =>
        GetPrefabOverridePaths(gameObject).Count > 0;

    private static GameObject? FindPrefabRoot(GameObject gameObject)
    {
        for (GameObject? candidate = gameObject; candidate != null; candidate = candidate.Parent)
            if (!string.IsNullOrWhiteSpace(candidate.PrefabPath)) return candidate;
        return null;
    }

    private static int GetPrefabObjectIndex(GameObject root, GameObject target)
    {
        if (root.Scene == null) return -1;
        HashSet<GameObject> subtree = new();
        void Include(GameObject item)
        {
            subtree.Add(item);
            foreach (GameObject child in item.Children) Include(child);
        }
        Include(root);
        return root.Scene.GameObjects.Where(subtree.Contains).ToList().IndexOf(target);
    }

    private static HashSet<string> GetPrefabOverridePaths(GameObject root)
    {
        HashSet<string> differences = new(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(root.PrefabBaseline)) return differences;
        try
        {
            JsonNode? current = JsonNode.Parse(SerializePrefabState(root));
            JsonNode? baseline = JsonNode.Parse(root.PrefabBaseline);
            CollectJsonDifferences(current, baseline, "", differences);
        }
        catch
        {
            differences.Add("Prefab data");
        }
        return differences;
    }

    private static void CollectJsonDifferences(JsonNode? current, JsonNode? baseline, string path, HashSet<string> differences)
    {
        if (JsonNode.DeepEquals(current, baseline)) return;
        if (current is JsonObject currentObject && baseline is JsonObject baselineObject)
        {
            foreach (string key in currentObject.Select(item => item.Key).Union(baselineObject.Select(item => item.Key)))
                CollectJsonDifferences(currentObject[key], baselineObject[key], string.IsNullOrEmpty(path) ? key : $"{path}.{key}", differences);
            return;
        }
        if (current is JsonArray currentArray && baseline is JsonArray baselineArray)
        {
            int count = Math.Max(currentArray.Count, baselineArray.Count);
            for (int index = 0; index < count; index++)
                CollectJsonDifferences(
                    index < currentArray.Count ? currentArray[index] : null,
                    index < baselineArray.Count ? baselineArray[index] : null,
                    $"{path}[{index}]",
                    differences);
            return;
        }
        differences.Add(path);
    }

    private string PrefabSectionLabel(string label, params string[] properties)
    {
        if (_currentPrefabObjectIndex < 0) return label;
        string objectPrefix = $"GameObjects[{_currentPrefabObjectIndex}].";
        bool overridden = _currentPrefabOverrides.Any(path =>
            properties.Any(property => path.StartsWith(objectPrefix + property, StringComparison.Ordinal)));
        return overridden ? $"{label}  *" : label;
    }

    private string PrefabFieldLabel(string label, string property)
    {
        if (_currentPrefabObjectIndex < 0) return $"{label}##{label}";
        string prefix = $"GameObjects[{_currentPrefabObjectIndex}].{property}";
        bool overridden = _currentPrefabOverrides.Any(path => path.StartsWith(prefix, StringComparison.Ordinal));
        return overridden ? $"{label} *##{label}" : $"{label}##{label}";
    }

    private static string DescribePrefabOverride(GameObject root, string path)
    {
        const string objectPrefix = "GameObjects[";
        if (!path.StartsWith(objectPrefix, StringComparison.Ordinal)) return path;
        int closingBracket = path.IndexOf(']');
        if (closingBracket < objectPrefix.Length || !int.TryParse(path[objectPrefix.Length..closingBracket], out int index))
            return path;
        List<GameObject> objects = new();
        if (root.Scene != null)
        {
            HashSet<GameObject> subtree = new();
            void Include(GameObject item)
            {
                subtree.Add(item);
                foreach (GameObject child in item.Children) Include(child);
            }
            Include(root);
            objects = root.Scene.GameObjects.Where(subtree.Contains).ToList();
        }
        string objectName = index >= 0 && index < objects.Count ? objects[index].Name : $"Object {index}";
        string property = path[(closingBracket + 1)..].TrimStart('.');
        string[] parts = property.Split('.');
        if (parts.Length > 1 && parts[^1] is "X" or "Y" or "Z" or "W")
            property = string.Join(" / ", parts[..^1]);
        else
            property = string.Join(" / ", parts);
        return $"{objectName} — {property}";
    }

    private void RevertPrefab(GameObject instance)
    {
        string path = AssetDatabase.ToAbsolutePath(instance.PrefabPath!);
        List<GameObject> replacements = LoadPrefabObjects(path);
        GameObject replacement = replacements.First(gameObject => gameObject.Parent == null);
        replacement.Name = instance.Name;
        replacement.PrefabPath = instance.PrefabPath;

        RecordImmediateEdit(() =>
        {
            _scene.DeleteGameObject(instance);
            foreach (GameObject item in replacements)
                _scene.AddGameObject(item);
            replacement.PrefabBaseline = SerializePrefabState(replacement);
            _selectedObject = replacement;
        });

        _statusMessage = $"Reverted {instance.Name} to its prefab.";
    }

    private void ApplyPrefab(GameObject instance)
    {
        string relativePath = instance.PrefabPath!;
        string path = AssetDatabase.ToAbsolutePath(relativePath);
        List<GameObject> cleanInstances = _scene.GameObjects
            .Where(other => !ReferenceEquals(other, instance) &&
                            string.Equals(other.PrefabPath, relativePath, StringComparison.OrdinalIgnoreCase) &&
                            !IsPrefabOverridden(other))
            .ToList();

        File.WriteAllText(path, SerializePrefabState(instance));
        instance.PrefabBaseline = SerializePrefabState(instance);

        foreach (GameObject oldInstance in cleanInstances)
        {
            List<GameObject> replacements = LoadPrefabObjects(path);
            GameObject replacement = replacements.First(gameObject => gameObject.Parent == null);
            replacement.Name = oldInstance.Name;
            replacement.PrefabPath = relativePath;
            _scene.DeleteGameObject(oldInstance);
            foreach (GameObject item in replacements)
                _scene.AddGameObject(item);
            replacement.PrefabBaseline = SerializePrefabState(replacement);
        }

        _statusMessage = $"Applied changes to {Path.GetFileName(path)}.";
    }

    // =========================================================
    // Hierarchy
    // =========================================================

    private Action? DrawObjectCreationMenu(GameObject? parent = null)
    {
        Action? action = null;
        void Item(string label, string name, Action<GameObject>? configure = null)
        {
            if (ImGui.MenuItem(label))
                action = () => RecordImmediateEdit(() =>
                {
                    GameObject created = _scene.CreateGameObject(name);
                    configure?.Invoke(created);
                    if (parent != null)
                    {
                        created.SetParent(parent, keepWorldPosition: false);
                        _expandedHierarchyObjects.Add(parent);
                    }
                    _hierarchySearch = string.Empty;
                    SelectObject(created, false);
                });
        }

        ImGui.BeginDisabled(_runtimeScene != null);
        Item("Create Empty", "GameObject");
        if (ImGui.BeginMenu("3D Object"))
        {
            Item("Cube", "Cube", o => o.AddComponent<MeshRenderer>().Mesh = PrimitiveMesh.Cube);
            Item("Sphere", "Sphere", o => o.AddComponent<MeshRenderer>().Mesh = PrimitiveMesh.Sphere);
            Item("Plane", "Plane", o => o.AddComponent<MeshRenderer>().Mesh = PrimitiveMesh.Plane);
            ImGui.EndMenu();
        }
        Item("Camera", "Camera", o => o.AddComponent<Camera>());
        if (ImGui.BeginMenu("Light"))
        {
            Item("Directional Light", "Directional Light", o => o.AddComponent<Light>().Type = LightType.Directional);
            Item("Point Light", "Point Light", o =>
            {
                var light = o.AddComponent<Light>(); light.Type = LightType.Point; light.Range = 5.0f;
            });
            Item("Spot Light", "Spot Light", o =>
            {
                var light = o.AddComponent<Light>(); light.Type = LightType.Spot;
                light.Range = 8.0f; light.SpotAngle = 45.0f;
            });
            ImGui.EndMenu();
        }
        if (ImGui.BeginMenu("UI"))
        {
            Item("Canvas", "Canvas", o => o.AddComponent<Canvas>());
            ImGui.EndMenu();
        }
        ImGui.Separator();
        if (ImGui.BeginMenu("R2Engine"))
        {
            Item("Sliding Doorway", "Sliding Doorway", SlidingDoor.BuildDoorway);
            ImGui.EndMenu();
        }
        ImGui.EndDisabled();
        return action;
    }

    private void DrawHierarchy()
    {
        ImGui.Begin(
            "Hierarchy");

        string displayName =
            IsSceneDirty
                ? $"{_scene.Name} *"
                : _scene.Name;

        GameObject? objectToDelete =
            null;
        Action? hierarchyAction = null;

        if (_hierarchyDragActive)
            _hierarchyDropTarget = null;

        if (ImGui.Button("Create", new Vector2(64.0f, 0.0f)))
            ImGui.OpenPopup("##hierarchyCreateButton");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1.0f);
        if (_focusHierarchySearch)
        {
            ImGui.SetKeyboardFocusHere();
            _focusHierarchySearch = false;
        }
        ImGui.InputTextWithHint("##hierarchySearch", "Search", ref _hierarchySearch, 128);
        if (ImGui.BeginPopup("##hierarchyCreateButton"))
        {
            hierarchyAction = DrawObjectCreationMenu() ?? hierarchyAction;
            ImGui.EndPopup();
        }

        ImGui.Separator();

        ImGuiTableFlags hierarchyTableFlags = ImGuiTableFlags.SizingStretchProp;

        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(2.0f, 1.0f));
        if (ImGui.BeginTable("##hierarchyColumns", 2, hierarchyTableFlags))
        {
            ImGui.TableSetupColumn("##Name", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("##Controls", ImGuiTableColumnFlags.WidthFixed, 42.0f);

            bool searchActive = !string.IsNullOrWhiteSpace(_hierarchySearch);
            bool sceneExpanded = searchActive || _hierarchySceneExpanded;
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            if (DrawHierarchyDisclosure("##sceneExpand", sceneExpanded, _scene.GameObjects.Count > 0) && !searchActive)
                _hierarchySceneExpanded = !_hierarchySceneExpanded;
            ImGui.SameLine(0.0f, 3.0f);
            DrawHierarchyObjectIcon(null, isScene: true);
            ImGui.SameLine(0.0f, 4.0f);
            ImGui.TextUnformatted(displayName);

            ImGui.TableSetColumnIndex(1);
            bool allVisible = _scene.GameObjects.All(gameObject => gameObject.IsVisibleInEditor);
            if (DrawHierarchyIconButton("##allVisible", HierarchyIcon.Eye, allVisible))
            {
                RecordImmediateEdit(() =>
                {
                    bool newVisibility = !allVisible;
                    foreach (GameObject gameObject in _scene.GameObjects)
                        gameObject.IsVisibleInEditor = newVisibility;
                });
            }
            ImGui.SameLine(0.0f, 1.0f);
            bool allLocked = _scene.GameObjects.Count > 0 &&
                             _scene.GameObjects.All(gameObject => gameObject.IsLockedInEditor);
            if (DrawHierarchyIconButton("##allLocked", HierarchyIcon.Lock, allLocked))
            {
                RecordImmediateEdit(() =>
                {
                    bool newLockState = !allLocked;
                    foreach (GameObject gameObject in _scene.GameObjects)
                        gameObject.IsLockedInEditor = newLockState;
                });
            }

            if (sceneExpanded)
            {
                foreach (GameObject root in _scene.GameObjects
                             .Where(gameObject => gameObject.Parent == null && HierarchySubtreeMatchesSearch(gameObject))
                             .ToArray())
                    DrawHierarchyRow(root, 0, ref objectToDelete, ref hierarchyAction);
            }

            ImGui.EndTable();
        }
        ImGui.PopStyleVar();

        if (_hierarchyDragActive && ImGui.IsMouseReleased(ImGuiMouseButton.Left) &&
            _draggedHierarchyObject != null &&
            ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem))
        {
            GameObject dragged = _draggedHierarchyObject;
            GameObject? target = _hierarchyDropTarget;
            HierarchyDropMode mode = _hierarchyDropMode;
            hierarchyAction = () => RecordImmediateEdit(
                () => MoveHierarchyObject(dragged, target, mode));
        }

        if (ImGui.IsWindowHovered() && !ImGui.IsAnyItemHovered() &&
            ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            ImGui.OpenPopup("##hierarchyCreate");

        if (ImGui.BeginPopup("##hierarchyCreate"))
        {
            hierarchyAction = DrawObjectCreationMenu() ?? hierarchyAction;
            ImGui.EndPopup();
        }

        hierarchyAction?.Invoke();

        if (ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem) &&
            ImGui.IsMouseReleased(ImGuiMouseButton.Left) &&
            _draggedAssetPath != null)
        {
            string extension = Path.GetExtension(_draggedAssetPath).ToLowerInvariant();
            if (string.Equals(extension, ".r2prefab", StringComparison.OrdinalIgnoreCase))
                InstantiatePrefab(_draggedAssetPath);
            else if (string.Equals(extension, ".obj", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(extension, ".r2skel", StringComparison.OrdinalIgnoreCase))
                CreateModelObjectFromAsset(_draggedAssetPath);
            else if (extension is ".fbx" or ".glb" or ".gltf")
            {
                try
                {
                    FbxImporterSettings importer = AssetDatabase.LoadFbxImporterSettings(_draggedAssetPath);
                    string generated = importer.GeneratedModelPath;
                    if (string.IsNullOrWhiteSpace(generated) || !File.Exists(AssetDatabase.ToAbsolutePath(generated)))
                        generated = AssetDatabase.ApplyFbxImporter(_draggedAssetPath);
                    if (!string.IsNullOrWhiteSpace(generated))
                        CreateModelObjectFromAsset(AssetDatabase.ToAbsolutePath(generated));
                }
                catch (Exception exception)
                {
                    _statusMessage = $"FBX import failed: {exception.GetBaseException().Message}";
                    _scriptMessages.Add(_statusMessage);
                }
            }
            _draggedAssetPath = null;
        }

        if (objectToDelete !=
            null)
        {
            RecordImmediateEdit(
                () =>
                {
                    _scene.DeleteGameObject(
                        objectToDelete);

                    if (_selectedObject == null || !_scene.GameObjects.Contains(_selectedObject))
                    {
                        _selectedObject =
                            _scene.GameObjects.Count >
                            0
                                ? _scene.GameObjects[0]
                                : null;
                    }
                });
        }

        ImGui.End();
    }

    private void DrawHierarchyRow(
        GameObject gameObject,
        int depth,
        ref GameObject? objectToDelete,
        ref Action? hierarchyAction)
    {
        ImGui.PushID(gameObject.GetHashCode());
        ImGui.TableNextRow();

        ImGui.TableSetColumnIndex(0);
        ImGui.Indent((depth + 1) * 14.0f);

        GameObject[] children = gameObject.Children
            .Where(HierarchySubtreeMatchesSearch)
            .ToArray();
        bool hasChildren = children.Length > 0;
        bool searchActive = !string.IsNullOrWhiteSpace(_hierarchySearch);
        bool isExpanded = searchActive || _expandedHierarchyObjects.Contains(gameObject);

        if (DrawHierarchyDisclosure("##expand", isExpanded, hasChildren))
        {
            if (!searchActive && hasChildren)
            {
                if (isExpanded)
                    _expandedHierarchyObjects.Remove(gameObject);
                else
                    _expandedHierarchyObjects.Add(gameObject);
                isExpanded = !isExpanded;
            }
        }
        ImGui.SameLine(0.0f, 2.0f);
        DrawHierarchyObjectIcon(gameObject);
        ImGui.SameLine(0.0f, 4.0f);
        bool renamingObject = ReferenceEquals(_inlineRenameHierarchyObject, gameObject);
        if (renamingObject)
        {
            if (_focusInlineHierarchyRename)
            {
                ImGui.SetKeyboardFocusHere();
                _focusInlineHierarchyRename = false;
            }
            ImGui.SetNextItemWidth(-1.0f);
            bool submitRename = ImGui.InputText("##HierarchyRename", ref _inlineRenameHierarchyBuffer, 128,
                ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
            if (ImGui.IsKeyPressed(ImGuiKey.Escape))
                _inlineRenameHierarchyObject = null;
            else if (submitRename || ImGui.IsItemDeactivatedAfterEdit())
            {
                string renamed = _inlineRenameHierarchyBuffer.Trim();
                _inlineRenameHierarchyObject = null;
                if (!string.IsNullOrWhiteSpace(renamed))
                    RecordImmediateEdit(() => gameObject.Name = renamed);
            }
            else if (ImGui.IsItemDeactivated() && !ImGui.IsItemActive())
                _inlineRenameHierarchyObject = null;
        }
        else
        {
            if (!gameObject.IsActiveInHierarchy)
                ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
            bool selected = ImGui.Selectable(
                gameObject.Name,
                _selectedObjects.Contains(gameObject),
                ImGuiSelectableFlags.AllowDoubleClick,
                new Vector2(ImGui.GetContentRegionAvail().X, 0.0f));
            if (!gameObject.IsActiveInHierarchy)
                ImGui.PopStyleColor();

            if (selected && !_hierarchyDragActive)
            {
                bool alreadySelected = _selectedObjects.Contains(gameObject);
                bool additive = ImGui.IsKeyDown(ImGuiKey.LeftCtrl) || ImGui.IsKeyDown(ImGuiKey.RightCtrl);
                bool range = ImGui.IsKeyDown(ImGuiKey.LeftShift) || ImGui.IsKeyDown(ImGuiKey.RightShift);
                SelectHierarchyObject(gameObject, additive, range);
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    _editorCamera.FocusObject(gameObject);
                else if (alreadySelected && !additive && !range)
                    BeginInlineHierarchyRename(gameObject);
            }
        }

        if (!renamingObject && ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            _draggedHierarchyObject = gameObject;
            _hierarchyDropTarget = null;
        }

        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 2.0f))
            _hierarchyDragActive = true;

        bool openObjectContext = ImGui.IsItemClicked(ImGuiMouseButton.Right);

        Vector2 rowMin = ImGui.GetItemRectMin();
        Vector2 rowMax = ImGui.GetItemRectMax();

        Vector2 hierarchyMouse = ImGui.GetMousePos();
        bool mouseInsideRow = hierarchyMouse.X >= rowMin.X && hierarchyMouse.X <= rowMax.X &&
                              hierarchyMouse.Y >= rowMin.Y && hierarchyMouse.Y <= rowMax.Y;

        if (_hierarchyDragActive && mouseInsideRow &&
            !ReferenceEquals(_draggedHierarchyObject, gameObject))
        {
            _hierarchyDropTarget = gameObject;
            Vector2 itemMin = rowMin;
            Vector2 itemMax = rowMax;
            float rowFraction = (ImGui.GetMousePos().Y - itemMin.Y) /
                                MathF.Max(1.0f, itemMax.Y - itemMin.Y);
            _hierarchyDropMode = rowFraction < 0.30f
                ? HierarchyDropMode.Before
                : rowFraction > 0.70f
                    ? HierarchyDropMode.After
                    : HierarchyDropMode.Parent;

            ImDrawListPtr drawList = ImGui.GetWindowDrawList();
            uint targetColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.25f, 0.65f, 1.0f, 1.0f));
            uint targetFill = ImGui.ColorConvertFloat4ToU32(new Vector4(0.25f, 0.65f, 1.0f, 0.25f));
            float lineStart = ImGui.GetWindowPos().X + 7.0f;
            if (_hierarchyDropMode == HierarchyDropMode.Before)
                drawList.AddLine(new Vector2(lineStart, itemMin.Y), new Vector2(itemMax.X, itemMin.Y), targetColor, 3.0f);
            else if (_hierarchyDropMode == HierarchyDropMode.After)
                drawList.AddLine(new Vector2(lineStart, itemMax.Y), itemMax, targetColor, 3.0f);
            else
            {
                drawList.AddRectFilled(itemMin, itemMax, targetFill, 2.0f);
                drawList.AddRect(itemMin, itemMax, targetColor, 2.0f, ImDrawFlags.None, 2.0f);
            }
        }

        if (openObjectContext)
            ImGui.OpenPopup("##objectContext");

        if (ImGui.BeginPopup("##objectContext"))
        {
            if (ImGui.BeginMenu("Create Child"))
            {
                hierarchyAction = DrawObjectCreationMenu(gameObject) ?? hierarchyAction;
                ImGui.EndMenu();
            }
            ImGui.Separator();

            if (ImGui.MenuItem("Copy", "Ctrl+C"))
                CopyHierarchyObject(gameObject);

            if (ImGui.MenuItem("Paste", "Ctrl+V", false, _copiedHierarchyScene != null))
                hierarchyAction = PasteHierarchyObject;

            if (ImGui.MenuItem("Duplicate", "Ctrl+D"))
                hierarchyAction = () => DuplicateHierarchyObject(gameObject);

            if (ImGui.MenuItem("Rename", "F2"))
                hierarchyAction = () => BeginInlineHierarchyRename(gameObject);

            ImGui.Separator();

            if (ImGui.MenuItem("Create Prefab Asset"))
                CreatePrefabAsset(gameObject);

            if (gameObject.Parent != null && ImGui.MenuItem("Unparent"))
                hierarchyAction = () => RecordImmediateEdit(() => gameObject.SetParent(null));

            ImGui.Separator();

            if (ImGui.MenuItem("Delete", "Delete"))
                objectToDelete = gameObject;

            ImGui.EndPopup();
        }

        ImGui.Unindent((depth + 1) * 14.0f);

        ImGui.TableSetColumnIndex(1);
        if (DrawHierarchyIconButton("##visible", HierarchyIcon.Eye, gameObject.IsVisibleInEditor))
            hierarchyAction = () => RecordImmediateEdit(() => gameObject.IsVisibleInEditor = !gameObject.IsVisibleInEditor);
        ImGui.SameLine(0.0f, 1.0f);
        if (DrawHierarchyIconButton("##locked", HierarchyIcon.Lock, gameObject.IsLockedInEditor))
            hierarchyAction = () => RecordImmediateEdit(() => gameObject.IsLockedInEditor = !gameObject.IsLockedInEditor);

        if (isExpanded)
        {
            foreach (GameObject child in children)
                DrawHierarchyRow(child, depth + 1, ref objectToDelete, ref hierarchyAction);
        }

        ImGui.PopID();

    }

    private enum HierarchyIcon
    {
        Eye,
        Lock
    }

    private static bool DrawHierarchyDisclosure(string id, bool expanded, bool hasChildren)
    {
        Vector2 size = new(12.0f, ImGui.GetFrameHeight());
        if (!hasChildren)
        {
            ImGui.Dummy(size);
            return false;
        }

        bool clicked = ImGui.InvisibleButton(id, size);
        Vector2 center = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) * 0.5f;
        uint color = ImGui.GetColorU32(ImGuiCol.Text);
        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        if (expanded)
            draw.AddTriangleFilled(center + new Vector2(-4.0f, -2.5f), center + new Vector2(4.0f, -2.5f),
                center + new Vector2(0.0f, 3.5f), color);
        else
            draw.AddTriangleFilled(center + new Vector2(-2.5f, -4.0f), center + new Vector2(-2.5f, 4.0f),
                center + new Vector2(3.5f, 0.0f), color);
        return clicked;
    }

    private static void DrawHierarchyObjectIcon(GameObject? gameObject, bool isScene = false)
    {
        Vector2 size = new(14.0f, ImGui.GetFrameHeight());
        Vector2 minimum = ImGui.GetCursorScreenPos();
        ImGui.Dummy(size);
        Vector2 center = minimum + size * 0.5f;
        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        uint dark = ImGui.ColorConvertFloat4ToU32(new Vector4(0.28f, 0.28f, 0.28f, 1.0f));
        uint accent = ImGui.ColorConvertFloat4ToU32(new Vector4(0.28f, 0.48f, 0.70f, 1.0f));

        if (isScene)
        {
            draw.AddQuad(center + new Vector2(0, -6), center + new Vector2(6, -2),
                center, center + new Vector2(-6, -2), accent, 1.4f);
            draw.AddLine(center + new Vector2(-6, -2), center + new Vector2(-6, 4), dark, 1.4f);
            draw.AddLine(center + new Vector2(6, -2), center + new Vector2(6, 4), dark, 1.4f);
            draw.AddLine(center + new Vector2(-6, 4), center, dark, 1.4f);
            draw.AddLine(center + new Vector2(6, 4), center, dark, 1.4f);
            return;
        }

        if (gameObject?.GetComponent<Camera>() != null)
        {
            draw.AddRect(center + new Vector2(-6, -4), center + new Vector2(2, 4), dark, 1.0f, ImDrawFlags.None, 1.4f);
            draw.AddTriangle(center + new Vector2(2, -3), center + new Vector2(7, -5),
                center + new Vector2(7, 5), accent, 1.4f);
        }
        else if (gameObject?.GetComponent<Light>() != null)
        {
            draw.AddCircle(center, 3.0f, accent, 12, 1.5f);
            for (int i = 0; i < 8; i++)
            {
                float angle = i * MathF.Tau / 8.0f;
                draw.AddLine(center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 4.5f,
                    center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 6.5f, dark, 1.2f);
            }
        }
        else if (gameObject?.GetComponent<Canvas>() != null)
        {
            draw.AddRect(center + new Vector2(-6, -5), center + new Vector2(6, 5), accent, 1.0f, ImDrawFlags.None, 1.4f);
            draw.AddLine(center + new Vector2(-3, -2), center + new Vector2(3, -2), dark, 1.2f);
            draw.AddLine(center + new Vector2(-3, 1), center + new Vector2(2, 1), dark, 1.2f);
        }
        else if (gameObject?.GetComponent<MeshRenderer>() != null)
        {
            draw.AddRect(center + new Vector2(-5, -5), center + new Vector2(5, 5), dark, 1.0f, ImDrawFlags.None, 1.3f);
            draw.AddLine(center + new Vector2(-5, -5), center + new Vector2(1, -1), accent, 1.2f);
            draw.AddLine(center + new Vector2(5, -5), center + new Vector2(1, -1), accent, 1.2f);
            draw.AddLine(center + new Vector2(1, -1), center + new Vector2(1, 5), accent, 1.2f);
        }
        else
        {
            draw.AddQuad(center + new Vector2(0, -5), center + new Vector2(5, 0),
                center + new Vector2(0, 5), center + new Vector2(-5, 0), dark, 1.3f);
        }
    }

    private static bool DrawHierarchyIconButton(string id, HierarchyIcon icon, bool enabled)
    {
        Vector2 size = new(18.0f, ImGui.GetFrameHeight());
        bool clicked = ImGui.InvisibleButton(id, size);
        Vector2 minimum = ImGui.GetItemRectMin();
        Vector2 maximum = ImGui.GetItemRectMax();
        bool hovered = ImGui.IsItemHovered();
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();

        if (hovered)
        {
            uint hoverColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.68f, 0.83f, 0.65f));
            drawList.AddRectFilled(minimum, maximum, hoverColor, 1.0f);
        }

        Vector2 center = (minimum + maximum) * 0.5f;
        uint color = ImGui.ColorConvertFloat4ToU32(enabled
            ? new Vector4(0.23f, 0.34f, 0.46f, 1.0f)
            : new Vector4(0.48f, 0.48f, 0.48f, 0.55f));

        if (icon == HierarchyIcon.Eye)
        {
            Vector2 left = center + new Vector2(-7.0f, 0.0f);
            Vector2 right = center + new Vector2(7.0f, 0.0f);
            Vector2 top = center + new Vector2(0.0f, -4.5f);
            Vector2 bottom = center + new Vector2(0.0f, 4.5f);
            drawList.AddLine(left, top, color, 1.5f);
            drawList.AddLine(top, right, color, 1.5f);
            drawList.AddLine(right, bottom, color, 1.5f);
            drawList.AddLine(bottom, left, color, 1.5f);
            drawList.AddCircleFilled(center, 2.2f, color, 12);

            if (!enabled)
                drawList.AddLine(center + new Vector2(-7.0f, -6.0f), center + new Vector2(7.0f, 6.0f), color, 1.8f);
        }
        else
        {
            Vector2 bodyMin = center + new Vector2(-5.5f, -0.5f);
            Vector2 bodyMax = center + new Vector2(5.5f, 6.0f);
            drawList.AddRect(bodyMin, bodyMax, color, 1.5f, ImDrawFlags.None, 1.5f);
            drawList.AddLine(center + new Vector2(-3.8f, -0.5f), center + new Vector2(-3.8f, -4.0f), color, 1.5f);
            drawList.AddLine(center + new Vector2(-3.8f, -4.0f), center + new Vector2(3.8f, -4.0f), color, 1.5f);
            drawList.AddLine(center + new Vector2(3.8f, -4.0f), center + new Vector2(3.8f, -0.5f), color, 1.5f);

            if (!enabled)
                drawList.AddLine(center + new Vector2(3.8f, -4.0f), center + new Vector2(6.0f, -1.5f), color, 1.5f);
        }

        return clicked;
    }

    private void MoveHierarchyObject(
        GameObject dragged,
        GameObject? target,
        HierarchyDropMode mode)
    {
        if (target != null && GetHierarchySubtree(dragged).Contains(target))
            return;

        GameObject? newParent = target == null
            ? null
            : mode == HierarchyDropMode.Parent
                ? target
                : target.Parent;

        if (!dragged.SetParent(newParent))
            return;

        List<GameObject> moving = GetHierarchySubtree(dragged);
        foreach (GameObject item in moving)
            _scene.GameObjects.Remove(item);

        int insertionIndex;
        if (target == null)
        {
            insertionIndex = _scene.GameObjects.Count;
        }
        else if (mode == HierarchyDropMode.Before)
        {
            insertionIndex = _scene.GameObjects.IndexOf(target);
        }
        else
        {
            HashSet<GameObject> targetTree = GetHierarchySubtree(target).ToHashSet();
            insertionIndex = _scene.GameObjects
                .Select((item, index) => (item, index))
                .Where(pair => targetTree.Contains(pair.item))
                .Select(pair => pair.index + 1)
                .DefaultIfEmpty(_scene.GameObjects.IndexOf(target) + 1)
                .Max();
        }

        insertionIndex = Math.Clamp(insertionIndex, 0, _scene.GameObjects.Count);
        _scene.GameObjects.InsertRange(insertionIndex, moving);
    }

    private static List<GameObject> GetHierarchySubtree(GameObject root)
    {
        List<GameObject> result = new() { root };
        foreach (GameObject child in root.Children.ToArray())
            result.AddRange(GetHierarchySubtree(child));
        return result;
    }

    private void SelectObject(GameObject? gameObject, bool additive)
    {
        _inspectAnimatorController = false;
        _inspectedAssetPath = null;
        _selectionArea = SelectionArea.Scene;

        if (!additive)
            _selectedObjects.Clear();

        if (gameObject == null)
        {
            _selectedObject = _selectedObjects.LastOrDefault();
            return;
        }

        if (additive && _selectedObjects.Contains(gameObject))
        {
            _selectedObjects.Remove(gameObject);
            _selectedObject = _selectedObjects.LastOrDefault();
            return;
        }

        _selectedObjects.Add(gameObject);
        _selectedObject = gameObject;
    }

    private void SelectHierarchyObject(GameObject gameObject, bool additive, bool range)
    {
        _selectionArea = SelectionArea.Scene;

        if (range && _hierarchySelectionAnchor != null)
        {
            List<GameObject> visibleObjects = GetVisibleHierarchyObjects();
            int anchorIndex = visibleObjects.IndexOf(_hierarchySelectionAnchor);
            int targetIndex = visibleObjects.IndexOf(gameObject);
            if (anchorIndex >= 0 && targetIndex >= 0)
            {
                if (!additive)
                    _selectedObjects.Clear();

                int first = Math.Min(anchorIndex, targetIndex);
                int last = Math.Max(anchorIndex, targetIndex);
                for (int index = first; index <= last; index++)
                    _selectedObjects.Add(visibleObjects[index]);

                _selectedObject = gameObject;
                return;
            }
        }

        SelectObject(gameObject, additive);
        _hierarchySelectionAnchor = gameObject;
    }

    private List<GameObject> GetVisibleHierarchyObjects()
    {
        List<GameObject> visibleObjects = new();
        foreach (GameObject root in _scene.GameObjects.Where(gameObject =>
                     gameObject.Parent == null && HierarchySubtreeMatchesSearch(gameObject)))
            AddVisibleHierarchyObject(root, visibleObjects);
        return visibleObjects;
    }

    private void AddVisibleHierarchyObject(GameObject gameObject, List<GameObject> visibleObjects)
    {
        visibleObjects.Add(gameObject);
        if (string.IsNullOrWhiteSpace(_hierarchySearch) && !_expandedHierarchyObjects.Contains(gameObject))
            return;

        foreach (GameObject child in gameObject.Children.Where(HierarchySubtreeMatchesSearch))
            AddVisibleHierarchyObject(child, visibleObjects);
    }

    private bool HierarchySubtreeMatchesSearch(GameObject gameObject)
    {
        if (HierarchyObjectMatchesSearch(gameObject))
            return true;

        return gameObject.Children.Any(HierarchySubtreeMatchesSearch);
    }

    private bool HierarchyObjectMatchesSearch(GameObject gameObject)
    {
        string query = _hierarchySearch.Trim();
        if (query.Length == 0)
            return true;

        if (gameObject.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;

        return gameObject.Components.Any(component =>
            component.GetType().Name.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void CopyHierarchyObject(GameObject source)
    {
        Scene.Scene owner = source.Scene
            ?? throw new InvalidOperationException("The copied object must belong to a scene.");
        _copiedHierarchyRootIndex = owner.GameObjects.IndexOf(source);
        _copiedHierarchyScene = SceneSerializer.Serialize(owner);
        _copiedHierarchyParentName = source.Parent?.Name;
        _statusMessage = $"Copied {source.Name} and its children.";
    }

    private void DuplicateHierarchyObject(GameObject source)
    {
        CopyHierarchyObject(source);
        PasteHierarchyObject(source.Parent, $"{source.Name} Copy");
    }

    private void PasteHierarchyObject()
    {
        GameObject? parent = _copiedHierarchyParentName == null
            ? null
            : _scene.FindGameObject(_copiedHierarchyParentName);
        PasteHierarchyObject(parent, null);
    }

    private void PasteHierarchyObject(GameObject? parent, string? rootName)
    {
        if (_copiedHierarchyScene == null || _copiedHierarchyRootIndex < 0)
            return;

        RecordImmediateEdit(() =>
        {
            Scene.Scene copiedScene = SceneSerializer.Deserialize(_copiedHierarchyScene);
            if (_copiedHierarchyRootIndex >= copiedScene.GameObjects.Count)
                return;

            GameObject copiedRoot = copiedScene.GameObjects[_copiedHierarchyRootIndex];
            List<GameObject> copiedObjects = GetHierarchySubtree(copiedRoot);
            copiedRoot.SetParent(null, keepWorldPosition: false);

            foreach (GameObject copiedObject in copiedObjects)
            {
                string requestedName = ReferenceEquals(copiedObject, copiedRoot) && rootName != null
                    ? rootName
                    : copiedObject.Name;
                copiedObject.Name = _scene.GetUniqueName(requestedName);
                _scene.AddGameObject(copiedObject);
            }

            copiedRoot.SetParent(parent, keepWorldPosition: false);
            _expandedHierarchyObjects.Remove(copiedRoot);
            _selectedObject = copiedRoot;
            _statusMessage = $"Pasted {copiedRoot.Name} and its children.";
        });
    }

    // =========================================================
    // Inspector
    // =========================================================

    private void DrawSelectedAssetInspector(string assetPath)
    {
        if (ImGui.Button("Back To Object Inspector")) { _inspectedAssetPath = null; return; }
        if (assetPath !=
            null)
        {
            ImGui.Separator();

            string selectedRelativePath =
                AssetDatabase
                    .ToProjectRelativePath(
                        assetPath);

            ImGui.Text(
                "Selected Asset");

            ImGui.TextWrapped(
                selectedRelativePath);

            ImGui.TextDisabled(
                AssetDatabase.GetAssetTypeLabel(
                    assetPath));

            if (File.Exists(
                    assetPath))
            {
                FileInfo fileInfo =
                    new FileInfo(
                        assetPath);

                ImGui.TextDisabled(
                    $"{fileInfo.Length:N0} bytes");

                string selectedExtension = Path.GetExtension(assetPath).ToLowerInvariant();
                if (selectedExtension is ".fbx" or ".glb" or ".gltf")
                    DrawFbxImporterInspector(assetPath);
                if (string.Equals(selectedExtension, ".obj", StringComparison.OrdinalIgnoreCase))
                {
                    if (ImGui.Button(
                            "Create Model Object"))
                    {
                        CreateModelObjectFromAsset(
                            assetPath);
                    }
                }

                if (string.Equals(selectedExtension, ".r2sanim", StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(assetPath))
                {
                    HumanoidAnimationAsset animationAsset = HumanoidAnimationAsset.Load(assetPath);
                    ImGui.SeparatorText("Animation Settings");
                    foreach (SkeletalAnimationClip clip in animationAsset.Clips)
                    {
                        ImGui.PushID($"RootMotion_{clip.Name}");
                        bool applyRootMotion = clip.ApplyRootMotion;
                        string label = animationAsset.Clips.Count == 1
                            ? "Apply Root Motion"
                            : $"Apply Root Motion — {clip.Name}";
                        if (ImGui.Checkbox(label, ref applyRootMotion))
                        {
                            clip.ApplyRootMotion = applyRootMotion;
                            animationAsset.Save(assetPath);
                            _statusMessage = $"Updated root motion for {Path.GetFileName(assetPath)}.";
                        }
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Keep imported hips/root movement. Usually enabled for sitting and standing transitions, and disabled for controller-driven walking and running.");
                        ImGui.PopID();
                    }
                    if (ImGui.Button("Edit Bone Mapping..."))
                    {
                        _humanoidBoneMappingPath = assetPath;
                        _showHumanoidBoneMappingWindow = true;
                    }
                }

                if (selectedExtension is ".r2skel" or ".r2sanim" or ".obj" or ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga" or ".wav")
                {
                    ImGui.SeparatorText("Import Settings");
                    ImGui.PushID("InlineImportSettings");
                    DrawImportSettingsInspector(assetPath);
                    ImGui.PopID();
                }

                if (string.Equals(selectedExtension, ".r2skel", StringComparison.OrdinalIgnoreCase) &&
                    ImGui.Button("Configure Skeleton..."))
                {
                    _skeletonConfigurationPath = assetPath;
                    _showSkeletonConfigurationWindow = true;
                }

                if (string.Equals(
                        Path.GetExtension(
                            assetPath),
                        ".r2mat",
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (_selectedObject !=
                        null)
                    {
                        MeshRenderer? selectedRenderer =
                            _selectedObject
                                .GetComponent<MeshRenderer>();

                        if (selectedRenderer !=
                            null &&
                            ImGui.Button(
                                "Assign Material to Selected"))
                        {
                            string materialPath =
                                AssetDatabase.ToProjectRelativePath(
                                    assetPath);

                            try
                            {
                                MaterialAssetCache.Get(
                                    materialPath);

                                RecordImmediateEdit(
                                    () =>
                                    {
                                        selectedRenderer.MaterialPath =
                                            materialPath;
                                    });

                                _statusMessage =
                                    $"Assigned material: {materialPath}";
                            }
                            catch (
                                Exception exception)
                            {
                                _statusMessage =
                                    $"Material assignment failed: {exception.Message}";
                            }
                        }
                    }
                }

                if (string.Equals(
                        Path.GetExtension(assetPath),
                        ".r2prefab",
                        StringComparison.OrdinalIgnoreCase) &&
                    ImGui.Button("Instantiate Prefab"))
                {
                    InstantiatePrefab(assetPath);
                }
            }
        }


    }

    private void DrawFbxImporterInspector(string sourcePath)
    {
        FbxImporterSettings settings = AssetDatabase.LoadFbxImporterSettings(sourcePath);
        bool changed = false;
        ImGui.SeparatorText("Model Importer");
        if (ImGui.BeginTabBar("##fbxImporterTabs"))
        {
            if (ImGui.BeginTabItem("Model"))
            {
                bool importModel = settings.ImportModel;
                if (ImGui.Checkbox("Import Model", ref importModel)) { settings.ImportModel = importModel; changed = true; }
                float scale = settings.Scale;
                if (ImGui.DragFloat("Scale Factor", ref scale, 0.01f, 0.0001f, 1000f))
                { settings.Scale = Math.Max(0.0001f, scale); changed = true; }
                Vector3 rotation = settings.Rotation;
                if (ImGui.DragFloat3("Bake Rotation", ref rotation, 0.5f, -360f, 360f))
                { settings.Rotation = rotation; changed = true; }
                bool orient = settings.AutoCorrectOrientation;
                if (ImGui.Checkbox("Auto-correct Orientation", ref orient))
                { settings.AutoCorrectOrientation = orient; changed = true; }
                ImGui.TextDisabled("Triangulation, smooth normals, UV conversion and four-weight limiting are applied automatically.");
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Rig"))
            {
                int limit = settings.SkeletonLimit;
                if (ImGui.DragInt("Skeleton Safety Limit", ref limit, 1f, 1, 1024))
                { settings.SkeletonLimit = Math.Max(1, limit); changed = true; }
                if (!string.IsNullOrWhiteSpace(settings.GeneratedModelPath) &&
                    File.Exists(AssetDatabase.ToAbsolutePath(settings.GeneratedModelPath)))
                {
                    SkeletalAsset generated = SkeletalAsset.Load(AssetDatabase.ToAbsolutePath(settings.GeneratedModelPath));
                    ImGui.Text($"Bones: {generated.Bones.Count} ({generated.DeformBoneCount} deforming)");
                    if (ImGui.Button("Configure Bone Mapping"))
                    {
                        _skeletonConfigurationPath = AssetDatabase.ToAbsolutePath(settings.GeneratedModelPath);
                        _showSkeletonConfigurationWindow = true;
                    }
                }
                else ImGui.TextDisabled("Apply the importer to generate the rig.");
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Animation"))
            {
                bool importAnimations = settings.ImportAnimations;
                if (ImGui.Checkbox("Import Animation", ref importAnimations))
                { settings.ImportAnimations = importAnimations; changed = true; }
                float speed = settings.AnimationSpeedScale;
                if (ImGui.DragFloat("Speed Scale", ref speed, 0.01f, 0.01f, 10f))
                { settings.AnimationSpeedScale = Math.Max(0.01f, speed); changed = true; }
                if (!string.IsNullOrWhiteSpace(settings.GeneratedAnimationPath) &&
                    File.Exists(AssetDatabase.ToAbsolutePath(settings.GeneratedAnimationPath)))
                {
                    HumanoidAnimationAsset animation = HumanoidAnimationAsset.Load(
                        AssetDatabase.ToAbsolutePath(settings.GeneratedAnimationPath));
                    ImGui.Text($"Imported Takes: {animation.Clips.Count}");
                    foreach (SkeletalAnimationClip clip in animation.Clips)
                        ImGui.BulletText($"{clip.Name}  {clip.Duration:0.00}s");
                }
                else ImGui.TextDisabled("No embedded animation takes have been generated.");
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Materials"))
            {
                ImGui.TextWrapped("The FBX mesh and UVs are imported here. Assign engine materials on the generated model or prefab so reimporting the FBX does not overwrite your material work.");
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
        if (changed) AssetDatabase.SaveFbxImporterSettings(sourcePath, settings);
        ImGui.Separator();
        bool sourceChanged = settings.SourceLastWriteUtcTicks != 0 &&
            File.GetLastWriteTimeUtc(sourcePath).Ticks != settings.SourceLastWriteUtcTicks;
        if (sourceChanged) ImGui.TextColored(new Vector4(1f, 0.75f, 0.25f, 1f), "Source file changed; Apply will reimport it.");
        if (ImGui.Button("Apply / Reimport"))
        {
            try
            {
                AssetDatabase.SaveFbxImporterSettings(sourcePath, settings);
                string generated = AssetDatabase.ApplyFbxImporter(sourcePath);
                ClearAssetCaches();
                _statusMessage = $"Reimported {Path.GetFileName(sourcePath)} -> {generated}";
            }
            catch (Exception exception)
            {
                _statusMessage = $"FBX import failed: {exception.GetBaseException().Message}";
                _scriptMessages.Add(_statusMessage);
            }
        }
        if (!string.IsNullOrWhiteSpace(settings.GeneratedModelPath))
        {
            ImGui.SameLine();
            if (ImGui.Button("Create In Scene"))
            {
                string generated = AssetDatabase.ToAbsolutePath(settings.GeneratedModelPath);
                if (File.Exists(generated)) CreateModelObjectFromAsset(generated);
            }
            ImGui.TextDisabled($"Model: {settings.GeneratedModelPath}");
        }
        if (!string.IsNullOrWhiteSpace(settings.GeneratedAnimationPath))
            ImGui.TextDisabled($"Animations: {settings.GeneratedAnimationPath}");
    }

    private void SelectInspectorAsset(string projectPath)
    {
        string absolutePath = AssetDatabase.ToAbsolutePath(projectPath);
        if (!File.Exists(absolutePath))
        {
            _statusMessage = $"Asset could not be found: {projectPath}";
            return;
        }

        _selectedAssetPath = absolutePath;
        _inspectedAssetPath = absolutePath;
        _selectionArea = SelectionArea.Assets;
        _projectBrowserPath = Path.GetDirectoryName(absolutePath) ?? AssetDatabase.AssetsRoot;
    }

    private void DrawInspector()
    {
        ImGui.Begin(
            "Inspector");

        GameObject? sceneSelection = _selectedObject;
        if (_inspectorLocked && (_lockedInspectorObject == null || !_scene.GameObjects.Contains(_lockedInspectorObject)))
        {
            _inspectorLocked = false;
            _lockedInspectorObject = null;
        }
        if (_inspectorLocked && _lockedInspectorObject != null)
            _selectedObject = _lockedInspectorObject;

        float lockButtonWidth = 58.0f;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() +
            Math.Max(0.0f, ImGui.GetContentRegionAvail().X - lockButtonWidth));
        if (ImGui.Button(_inspectorLocked ? "Locked" : "Lock", new Vector2(lockButtonWidth, 0.0f)))
        {
            _inspectorLocked = !_inspectorLocked;
            _lockedInspectorObject = _inspectorLocked ? _selectedObject : null;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(_inspectorLocked
                ? "Unlock the Inspector so it follows selection again."
                : "Keep inspecting this object while selecting other things.");
        ImGui.Separator();
        if (!string.IsNullOrWhiteSpace(_statusMessage) &&
            System.Diagnostics.Stopwatch.GetElapsedTime(_statusMessageStarted).TotalSeconds < 5)
        {
            ImGui.Separator();

            ImGui.TextWrapped(
                _statusMessage);
        }


        if (!_inspectorLocked && _inspectAnimatorController && _showAnimatorControllerWindow &&
            _animatorControllerPath != null && File.Exists(_animatorControllerPath))
        {
            DrawAnimatorSelectionInspector();
            _selectedObject = sceneSelection;
            ImGui.End();
            return;
        }

        if (!_inspectorLocked && _inspectedAssetPath != null)
        {
            DrawSelectedAssetInspector(_inspectedAssetPath);
            _selectedObject = sceneSelection;
            ImGui.End();
            return;
        }

        if (_selectedObject ==
            null)
        {
            ImGui.TextDisabled(
                "No object selected.");

            _selectedObject = sceneSelection;
            ImGui.End();

            return;
        }

        // -----------------------------------------------------
        // Name
        // -----------------------------------------------------

        string name =
            _selectedObject.Name;

        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(5.0f, 3.0f));
        bool objectActive = _selectedObject.IsActive;
        if (ImGui.Checkbox("##objectActive", ref objectActive))
        {
            GameObject target = _selectedObject;
            RecordImmediateEdit(() => target.IsActive = objectActive);
        }

        ImGui.SameLine();
        float staticWidth = 58.0f;
        ImGui.SetNextItemWidth(Math.Max(40.0f,
            ImGui.GetContentRegionAvail().X - staticWidth - ImGui.GetStyle().ItemSpacing.X));

        ImGui.InputText(
            "##objectName",
            ref name,
            128);

        BeginInspectorEditIfNeeded();

        if (!string.IsNullOrWhiteSpace(
                name))
        {
            _selectedObject.Name =
                name;
        }

        FinishInspectorEditIfNeeded();

        ImGui.SameLine();
        bool objectStatic = _selectedObject.IsStatic;
        if (ImGui.Checkbox("Static", ref objectStatic))
        {
            GameObject target = _selectedObject;
            RecordImmediateEdit(() => SetStaticRecursively(target, objectStatic));
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Static (Bake Lighting)\nChanging a parent applies Static to all descendants.\nChildren can be changed individually afterward.");

        int objectLayer = Math.Clamp(_selectedObject.Layer, 0, _projectSettings.CollisionLayers.Count - 1);
        string currentLayerName = _projectSettings.CollisionLayers[objectLayer];
        if (ImGui.BeginTable("##ObjectClassification", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("##ObjectLabel", ImGuiTableColumnFlags.WidthFixed, 48.0f);
            ImGui.TableSetupColumn("##ObjectValue", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Layer");
            ImGui.TableSetColumnIndex(1);
            ImGui.SetNextItemWidth(-1.0f);
            if (ImGui.BeginCombo("##GameObjectLayer", $"{objectLayer}: {currentLayerName}"))
            {
                for (int layer = 0; layer < _projectSettings.CollisionLayers.Count; layer++)
                {
                    ImGui.PushID($"GameObjectLayerChoice{layer}");
                    bool selected = layer == objectLayer;
                    if (ImGui.Selectable($"{layer}: {_projectSettings.CollisionLayers[layer]}##Choice", selected))
                    {
                        GameObject target = _selectedObject;
                        int selectedLayer = layer;
                        RecordImmediateEdit(() => SetLayerRecursively(target, selectedLayer));
                    }
                    if (selected) ImGui.SetItemDefaultFocus();
                    ImGui.PopID();
                }
                ImGui.EndCombo();
            }
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();

        ImGui.Separator();

        GameObject? prefabRoot = FindPrefabRoot(_selectedObject);
        _currentPrefabOverrides = prefabRoot == null ? new HashSet<string>(StringComparer.Ordinal) : GetPrefabOverridePaths(prefabRoot);
        _currentPrefabObjectIndex = prefabRoot == null ? -1 : GetPrefabObjectIndex(prefabRoot, _selectedObject);
        if (prefabRoot != null)
        {
            bool overridden = _currentPrefabOverrides.Count > 0;
            ImGui.TextWrapped($"Prefab: {prefabRoot.PrefabPath}");
            ImGui.TextDisabled(overridden ? $"{_currentPrefabOverrides.Count} overridden field(s)." : "Instance matches prefab.");
            if (overridden && ImGui.TreeNode("Show Overrides"))
            {
                foreach (string overridePath in _currentPrefabOverrides.Take(12))
                    ImGui.BulletText(DescribePrefabOverride(prefabRoot, overridePath));
                if (_currentPrefabOverrides.Count > 12)
                    ImGui.TextDisabled($"...and {_currentPrefabOverrides.Count - 12} more");
                ImGui.TreePop();
            }

            if (!overridden) ImGui.BeginDisabled();
            if (ImGui.Button("Apply"))
                ApplyPrefab(prefabRoot);
            ImGui.SameLine();
            if (ImGui.Button("Revert"))
                RevertPrefab(prefabRoot);
            if (!overridden) ImGui.EndDisabled();

            ImGui.SameLine();
            if (ImGui.Button("Unpack Prefab"))
            {
                GameObject target = prefabRoot;
                RecordImmediateEdit(() =>
                {
                    target.PrefabPath = null;
                    target.PrefabBaseline = null;
                });
                _statusMessage = $"Unpacked {target.Name}.";
            }

            ImGui.Separator();
        }

        // -----------------------------------------------------
        // Transform
        // -----------------------------------------------------

        bool transformOpen = InspectorComponentHeader(
                PrefabSectionLabel("Transform", "Position", "Rotation", "Scale"),
                ImGuiTreeNodeFlags.DefaultOpen);
        if (ImGui.BeginPopupContextItem("##TransformOptions"))
        {
            GameObject transformTarget = _selectedObject;
            if (ImGui.MenuItem("Reset Position"))
                RecordImmediateEdit(() => transformTarget.Transform.Position = Vector3.Zero);
            if (ImGui.MenuItem("Reset Rotation"))
                RecordImmediateEdit(() => transformTarget.Transform.Rotation = Vector3.Zero);
            if (ImGui.MenuItem("Reset Scale"))
                RecordImmediateEdit(() => transformTarget.Transform.Scale = Vector3.One);
            if (ImGui.MenuItem("Reset"))
                RecordImmediateEdit(() =>
                {
                    transformTarget.Transform.Position = Vector3.Zero;
                    transformTarget.Transform.Rotation = Vector3.Zero;
                    transformTarget.Transform.Scale = Vector3.One;
                });
            ImGui.Separator();
            if (ImGui.MenuItem("Copy Component"))
                _copiedTransform = (transformTarget.Transform.Position,
                    transformTarget.Transform.Rotation, transformTarget.Transform.Scale);
            if (!_copiedTransform.HasValue) ImGui.BeginDisabled();
            if (ImGui.MenuItem("Paste Component Values") && _copiedTransform is { } copied)
                RecordImmediateEdit(() =>
                {
                    transformTarget.Transform.Position = copied.Position;
                    transformTarget.Transform.Rotation = copied.Rotation;
                    transformTarget.Transform.Scale = copied.Scale;
                });
            if (!_copiedTransform.HasValue) ImGui.EndDisabled();
            ImGui.EndPopup();
        }
        if (transformOpen)
        {
            if (ImGui.BeginTable("##TransformFields", 2, ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("##TransformLabel", ImGuiTableColumnFlags.WidthFixed, 58.0f);
                ImGui.TableSetupColumn("##TransformValue", ImGuiTableColumnFlags.WidthStretch);

                ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted(PrefabFieldLabel("Position", "Position").Split("##")[0]);
                ImGui.TableSetColumnIndex(1); ImGui.SetNextItemWidth(-1.0f);
                ImGui.DragFloat3("##Position", ref _selectedObject.Transform.Position, 0.1f);

                BeginInspectorEditIfNeeded();
                FinishInspectorEditIfNeeded();

                ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted(PrefabFieldLabel("Rotation", "Rotation").Split("##")[0]);
                ImGui.TableSetColumnIndex(1); ImGui.SetNextItemWidth(-1.0f);
                ImGui.DragFloat3("##Rotation", ref _selectedObject.Transform.Rotation, 1.0f);

                BeginInspectorEditIfNeeded();
                FinishInspectorEditIfNeeded();

                ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted(PrefabFieldLabel("Scale", "Scale").Split("##")[0]);
                ImGui.TableSetColumnIndex(1); ImGui.SetNextItemWidth(-1.0f);
                ImGui.DragFloat3("##Scale", ref _selectedObject.Transform.Scale, 0.1f);

                BeginInspectorEditIfNeeded();
                FinishInspectorEditIfNeeded();
                ImGui.EndTable();
            }
        }

        // -----------------------------------------------------
        // Mesh Renderer
        // -----------------------------------------------------

        MeshRenderer? meshRenderer =
            _selectedObject
                .GetComponent<MeshRenderer>();

        if (meshRenderer !=
            null)
        {
            bool meshRendererOpen =
                InspectorComponentHeader(
                    PrefabSectionLabel("Mesh Renderer", "MeshRenderer"),
                    ImGuiTreeNodeFlags.DefaultOpen |
                    ImGuiTreeNodeFlags.AllowOverlap);

            GameObject meshRendererTarget = _selectedObject;

            DrawComponentOptions(
                "MeshRenderer",
                () => RecordImmediateEdit(
                    () =>
                    {
                        meshRendererTarget.RemoveComponent<MeshRenderer>();
                        meshRendererTarget.AddComponent<MeshRenderer>();
                    }),
                () => RecordImmediateEdit(
                    () => meshRendererTarget.RemoveComponent<MeshRenderer>()));

            if (meshRendererOpen)
            {
                if (!string.IsNullOrWhiteSpace(
                        meshRenderer.MeshPath))
                {
                    if (ImGui.Button($"Mesh: {Path.GetFileName(meshRenderer.MeshPath)}##MeshAsset",
                            new Vector2(ImGui.GetContentRegionAvail().X, 0.0f)))
                        SelectInspectorAsset(meshRenderer.MeshPath);
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip(meshRenderer.MeshPath);
                }
                else
                {
                    ImGui.Text(
                        $"Mesh: {meshRenderer.Mesh}");
                }

                string? droppedMesh =
                    AcceptAssetDrop(".obj");

                if (droppedMesh != null)
                {
                    try
                    {
                        string meshPath =
                            AssetDatabase.ToProjectRelativePath(droppedMesh);

                        MeshAssetCache.Get(meshPath);
                        RecordImmediateEdit(
                            () => meshRenderer.MeshPath = meshPath);

                        _statusMessage = $"Assigned mesh: {meshPath}";
                    }
                    catch (Exception exception)
                    {
                        _statusMessage = $"Mesh assignment failed: {exception.Message}";
                    }
                }

                Mesh meshData =
                    meshRenderer.MeshData;

                ImGui.TextDisabled(
                    $"Vertices: {meshData.VertexCount}");

                ImGui.TextDisabled(
                    $"Triangles: {meshData.TriangleCount}");

                ImGui.Separator();

                // ---------------------------------------------
                // Material Asset
                // ---------------------------------------------

                _selectedMeshMaterialSlot = Math.Clamp(
                    _selectedMeshMaterialSlot, 0, meshRenderer.MaterialSlotCount - 1);
                string[] materialSlotLabels = Enumerable.Range(0, meshRenderer.MaterialSlotCount)
                    .Select(meshRenderer.GetSubmesh)
                    .Select((submesh, slot) => $"{slot}: {submesh.MaterialSlotName}")
                    .ToArray();
                ImGui.Combo("Material Slot", ref _selectedMeshMaterialSlot,
                    materialSlotLabels, materialSlotLabels.Length);

                int selectedSlot = _selectedMeshMaterialSlot;
                MeshSubmesh selectedSubmesh = meshRenderer.GetSubmesh(selectedSlot);
                string? selectedMaterialPath = meshRenderer.GetMaterialPath(selectedSlot);
                bool hasSelectedMaterial = !string.IsNullOrWhiteSpace(selectedMaterialPath);

                ImGui.TextDisabled($"Editing: {selectedSubmesh.MaterialSlotName}");

                string materialFieldLabel =
                    hasSelectedMaterial
                        ? Path.GetFileName(selectedMaterialPath!)
                        : "None - drop a .r2mat material here";

                float clearMaterialWidth =
                    ImGui.GetFrameHeight();

                float materialFieldWidth =
                    Math.Max(
                        1.0f,
                        ImGui.GetContentRegionAvail().X -
                        clearMaterialWidth -
                        ImGui.GetStyle().ItemSpacing.X);

                bool inspectMaterial = ImGui.Button(
                    $"{materialFieldLabel}##MaterialAssetField{selectedSlot}",
                    new Vector2(materialFieldWidth, 0.0f));
                if (inspectMaterial && hasSelectedMaterial)
                    SelectInspectorAsset(selectedMaterialPath!);

                string? droppedMaterial =
                    AcceptAssetDrop(".r2mat");

                if (droppedMaterial != null)
                {
                    try
                    {
                        string materialPath =
                            AssetDatabase.ToProjectRelativePath(droppedMaterial);

                        MaterialAssetCache.Get(materialPath);
                        RecordImmediateEdit(
                            () => meshRenderer.SetMaterialPath(selectedSlot, materialPath));

                        _statusMessage = $"Assigned {materialPath} to '{selectedSubmesh.MaterialSlotName}'.";
                    }
                    catch (Exception exception)
                    {
                        _statusMessage = $"Material assignment failed: {exception.Message}";
                    }
                }

                ImGui.SameLine();

                if (!hasSelectedMaterial)
                    ImGui.BeginDisabled();

                if (ImGui.Button(
                        $"X##ClearMaterialAsset{selectedSlot}",
                        new Vector2(clearMaterialWidth, 0.0f)))
                {
                    RecordImmediateEdit(
                        () => meshRenderer.SetMaterialPath(selectedSlot, null));

                    _statusMessage = $"Removed material from '{selectedSubmesh.MaterialSlotName}'.";
                }

                if (!hasSelectedMaterial)
                    ImGui.EndDisabled();

                ImGui.Separator();

                int activeMaterialSlot = _selectedMeshMaterialSlot;
                string? activeMaterialPath = meshRenderer.GetMaterialPath(activeMaterialSlot);
                bool activeUsesMaterialAsset = !string.IsNullOrWhiteSpace(activeMaterialPath);
                bool materialEditingEnabled = activeUsesMaterialAsset;

                if (!materialEditingEnabled)
                    ImGui.BeginDisabled();

                Material activeMaterial =
                    meshRenderer.GetMaterial(activeMaterialSlot);

                ImGui.Text($"Editing Slot {activeMaterialSlot}: {meshRenderer.GetSubmesh(activeMaterialSlot).MaterialSlotName}");
                if (!activeUsesMaterialAsset && activeMaterialSlot > 0)
                    ImGui.TextDisabled("Assign a material asset to edit this slot.");

                // ---------------------------------------------
                // Portable Built-In Material Path
                // ---------------------------------------------

                int lightingMode = (int)activeMaterial.LightingMode;
                string[] lightingModes = { "Vertex", "Unlit", "Object Smooth" };
                if (ImGui.Combo("Lighting", ref lightingMode, lightingModes, lightingModes.Length))
                {
                    activeMaterial.LightingMode = (MaterialLightingMode)lightingMode;
                    MaterialAssetCache.Save(activeMaterialPath!);
                }

                int surfaceMode = (int)activeMaterial.SurfaceMode;
                string[] surfaceModes = Enum.GetNames<MaterialSurfaceMode>();
                if (ImGui.Combo("Surface", ref surfaceMode, surfaceModes, surfaceModes.Length))
                {
                    activeMaterial.SurfaceMode = (MaterialSurfaceMode)surfaceMode;
                    MaterialAssetCache.Save(activeMaterialPath!);
                }

                if (activeMaterial.SurfaceMode == MaterialSurfaceMode.Cutout)
                {
                    float alphaCutoff = activeMaterial.AlphaCutoff;
                    if (ImGui.SliderFloat("Alpha Cutoff", ref alphaCutoff, 0.0f, 1.0f))
                        activeMaterial.AlphaCutoff = alphaCutoff;

                    if (ImGui.IsItemDeactivatedAfterEdit())
                        MaterialAssetCache.Save(activeMaterialPath!);
                }

                bool doubleSided = activeMaterial.DoubleSided;
                if (ImGui.Checkbox("Double Sided", ref doubleSided))
                {
                    activeMaterial.DoubleSided = doubleSided;
                    MaterialAssetCache.Save(activeMaterialPath!);
                }

                bool receiveFog = activeMaterial.ReceiveFog;
                if (ImGui.Checkbox("Receive Fog", ref receiveFog))
                {
                    activeMaterial.ReceiveFog = receiveFog;
                    MaterialAssetCache.Save(activeMaterialPath!);
                }

                ImGui.TextDisabled("Built-in PS2-portable material path");

                // ---------------------------------------------
                // Base Color
                // ---------------------------------------------

                Vector4 baseColor =
                    activeMaterial.BaseColor;

                bool baseColorChanged =
                    ImGui.ColorEdit4(
                        "Base Color",
                        ref baseColor);

                if (activeUsesMaterialAsset)
                {
                    if (baseColorChanged)
                    {
                        activeMaterial.BaseColor =
                            baseColor;
                    }

                    if (ImGui.IsItemDeactivatedAfterEdit())
                    {
                        MaterialAssetCache.Save(
                            activeMaterialPath!);

                        _statusMessage =
                            $"Saved {activeMaterialPath}";
                    }
                }
                else
                {
                    if (baseColorChanged)
                    {
                        activeMaterial.BaseColor =
                            baseColor;
                    }

                    BeginInspectorEditIfNeeded();
                    FinishInspectorEditIfNeeded();
                }

                // ---------------------------------------------
                // Texture
                // ---------------------------------------------

                ImGui.Separator();

                string texturePath =
                    activeMaterial.TexturePath ??
                    "";

                ImGui.InputText(
                    "Texture",
                    ref texturePath,
                    512);

                string? droppedTexture =
                    materialEditingEnabled
                        ? AcceptAssetDrop(
                            ".png",
                            ".jpg",
                            ".jpeg",
                            ".bmp",
                            ".tga")
                        : null;

                if (droppedTexture != null)
                {
                    string droppedTexturePath =
                        AssetDatabase.ToProjectRelativePath(droppedTexture);

                    if (activeUsesMaterialAsset)
                    {
                        activeMaterial.TexturePath = droppedTexturePath;
                        MaterialAssetCache.Save(activeMaterialPath!);
                    }
                    else
                    {
                        RecordImmediateEdit(
                            () => activeMaterial.TexturePath = droppedTexturePath);
                    }

                    texturePath = droppedTexturePath;
                    _statusMessage = $"Assigned texture: {droppedTexturePath}";
                }

                if (activeUsesMaterialAsset)
                {
                    string? normalizedTexturePath =
                        string.IsNullOrWhiteSpace(
                            texturePath)
                            ? null
                            : texturePath;

                    if (normalizedTexturePath !=
                        activeMaterial.TexturePath)
                    {
                        activeMaterial.TexturePath =
                            normalizedTexturePath;

                        if (ImGui.IsItemDeactivatedAfterEdit())
                        {
                            MaterialAssetCache.Save(
                                activeMaterialPath!);

                            _statusMessage =
                                $"Saved {activeMaterialPath}";
                        }
                    }
                }
                else
                {
                    BeginInspectorEditIfNeeded();

                    activeMaterial.TexturePath =
                        string.IsNullOrWhiteSpace(
                            texturePath)
                            ? null
                            : texturePath;

                    FinishInspectorEditIfNeeded();
                }

                if (ImGui.Button(
                        "Browse / Import Texture..."))
                {
                    string? sourcePath =
                        WindowsFileDialog
                            .OpenTexture();

                    if (sourcePath !=
                        null)
                    {
                        try
                        {
                            string importedPath =
                                AssetDatabase
                                    .ImportTexture(
                                        sourcePath);

                            if (activeUsesMaterialAsset)
                            {
                                activeMaterial.TexturePath =
                                    importedPath;

                                MaterialAssetCache.Save(
                                    activeMaterialPath!);
                            }
                            else
                            {
                                RecordImmediateEdit(
                                    () =>
                                    {
                                        activeMaterial.TexturePath =
                                            importedPath;
                                    });
                            }

                            _statusMessage =
                                $"Imported texture: {importedPath}";
                        }
                        catch (
                            Exception exception)
                        {
                            _statusMessage =
                                $"Texture import failed: {exception.Message}";
                        }
                    }
                }

                if (activeMaterial.TexturePath !=
                    null)
                {
                    ImGui.SameLine();

                    if (ImGui.Button(
                            "Clear Texture"))
                    {
                        if (activeUsesMaterialAsset)
                        {
                            activeMaterial.TexturePath =
                                null;

                            MaterialAssetCache.Save(
                                activeMaterialPath!);
                        }
                        else
                        {
                            RecordImmediateEdit(
                                () =>
                                {
                                    activeMaterial.TexturePath =
                                        null;
                                });
                        }
                    }
                }

                string? assignedTexture =
                    activeMaterial.TexturePath;

                if (assignedTexture ==
                    null)
                {
                    ImGui.TextDisabled(
                        "No texture assigned.");
                }
                else
                {
                    string absolutePath =
                        AssetDatabase
                            .ToAbsolutePath(
                                assignedTexture);

                    if (File.Exists(
                            absolutePath))
                    {
                        if (ImGui.Button($"Select {Path.GetFileName(assignedTexture)}##SelectMaterialTexture",
                                new Vector2(ImGui.GetContentRegionAvail().X, 0.0f)))
                            SelectInspectorAsset(assignedTexture);
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip(assignedTexture);
                    }
                    else
                    {
                        ImGui.Text(
                            "Texture file not found.");

                        ImGui.TextWrapped(
                        assignedTexture);
                    }
                }

                ImGui.Separator();
                Vector2 textureTiling = activeMaterial.TextureTiling;
                bool tilingChanged = ImGui.InputFloat2("Tiling", ref textureTiling);
                if (tilingChanged && float.IsFinite(textureTiling.X) && float.IsFinite(textureTiling.Y))
                    activeMaterial.TextureTiling = textureTiling;
                bool tilingFinished = ImGui.IsItemDeactivatedAfterEdit();

                Vector2 textureOffset = activeMaterial.TextureOffset;
                bool offsetChanged = ImGui.InputFloat2("Offset", ref textureOffset);
                if (offsetChanged && float.IsFinite(textureOffset.X) && float.IsFinite(textureOffset.Y))
                    activeMaterial.TextureOffset = textureOffset;
                bool offsetFinished = ImGui.IsItemDeactivatedAfterEdit();

                if (activeUsesMaterialAsset)
                {
                    if (tilingFinished || offsetFinished)
                    {
                        MaterialAssetCache.Save(activeMaterialPath!);
                        _statusMessage = $"Saved {activeMaterialPath}";
                    }
                }
                else
                {
                    BeginInspectorEditIfNeeded();
                    FinishInspectorEditIfNeeded();
                }

                if (!materialEditingEnabled)
                    ImGui.EndDisabled();

            }
        }

        // -----------------------------------------------------
        // Camera
        // -----------------------------------------------------

        Camera? camera =
            _selectedObject
                .GetComponent<Camera>();

        if (camera !=
            null)
        {
            bool cameraOpen =
                InspectorComponentHeader(
                    PrefabSectionLabel("Camera", "Camera"),
                    ImGuiTreeNodeFlags.DefaultOpen |
                    ImGuiTreeNodeFlags.AllowOverlap);

            GameObject cameraTarget = _selectedObject;

            DrawComponentOptions(
                "Camera",
                () => RecordImmediateEdit(
                    () =>
                    {
                        cameraTarget.RemoveComponent<Camera>();
                        cameraTarget.AddComponent<Camera>();
                    }),
                () => RecordImmediateEdit(
                    () => cameraTarget.RemoveComponent<Camera>()));

            if (cameraOpen)
            {
                bool isPrimary =
                    camera.IsPrimary;

                if (ImGui.Checkbox(
                        "Main Camera",
                        ref isPrimary))
                {
                    GameObject target =
                        _selectedObject;

                    RecordImmediateEdit(
                        () =>
                        {
                            if (isPrimary)
                            {
                                foreach (GameObject gameObject in _scene.GameObjects)
                                {
                                    Camera? otherCamera =
                                        gameObject.GetComponent<Camera>();

                                    if (otherCamera != null)
                                        otherCamera.IsPrimary = false;
                                }
                            }

                            Camera? targetCamera =
                                target.GetComponent<Camera>();

                            if (targetCamera != null)
                                targetCamera.IsPrimary = isPrimary;
                        });
                }

                ImGui.DragFloat(
                    "Field of View",
                    ref camera.FieldOfView,
                    1.0f,
                    1.0f,
                    179.0f);

                BeginInspectorEditIfNeeded();
                FinishInspectorEditIfNeeded();

                if (ImGui.DragFloat(
                    "Near Clip",
                    ref camera.NearClip,
                    0.01f,
                    0.01f,
                    100.0f))
                {
                    camera.NearClip = Math.Clamp(camera.NearClip, 0.01f, 100.0f);
                    if (camera.FarClip <= camera.NearClip)
                        camera.FarClip = camera.NearClip + 0.01f;
                }

                BeginInspectorEditIfNeeded();
                FinishInspectorEditIfNeeded();

                ImGui.DragFloat(
                    "Far Clip",
                    ref camera.FarClip,
                    1.0f,
                    1.0f,
                    10000.0f);

                BeginInspectorEditIfNeeded();
                FinishInspectorEditIfNeeded();

                ImGui.TextUnformatted("Audio Listener Override");
                string listenerLabel = camera.AudioListenerOverride?.Name ?? "None (use camera)";
                float listenerClearWidth = ImGui.GetFrameHeight();
                float listenerBoxWidth = camera.AudioListenerOverride == null
                    ? ImGui.GetContentRegionAvail().X
                    : Math.Max(1.0f, ImGui.GetContentRegionAvail().X - listenerClearWidth - ImGui.GetStyle().ItemSpacing.X);
                ImGui.Button($"{listenerLabel}##AudioListenerOverride", new Vector2(listenerBoxWidth, 0.0f));
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Drag an object from the Hierarchy here to override the spatial-audio listener.");
                if (_hierarchyDragActive && _draggedHierarchyObject is { } droppedListener &&
                    ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem) &&
                    ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                {
                    GameObject target = cameraTarget;
                    RecordImmediateEdit(() => target.GetComponent<Camera>()!.AudioListenerOverride = droppedListener);
                }
                if (camera.AudioListenerOverride != null)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton("X##ClearAudioListenerOverride"))
                    {
                        GameObject target = cameraTarget;
                        RecordImmediateEdit(() => target.GetComponent<Camera>()!.AudioListenerOverride = null);
                    }
                }

            }
        }

        // -----------------------------------------------------
        // Light
        // -----------------------------------------------------

        Light? light =
            _selectedObject
                .GetComponent<Light>();

        if (light !=
            null)
        {
            bool lightOpen =
                InspectorComponentHeader(
                    PrefabSectionLabel("Light", "Light"),
                    ImGuiTreeNodeFlags.DefaultOpen |
                    ImGuiTreeNodeFlags.AllowOverlap);

            GameObject lightTarget = _selectedObject;

            DrawComponentOptions(
                "Light",
                () => RecordImmediateEdit(
                    () =>
                    {
                        lightTarget.RemoveComponent<Light>();
                        lightTarget.AddComponent<Light>();
                    }),
                () => RecordImmediateEdit(
                    () => lightTarget.RemoveComponent<Light>()));

            if (lightOpen)
            {
                int lightMode = (int)light.Mode;
                string[] lightModes = { "Realtime", "Mixed", "Baked" };
                if (ImGui.Combo("Mode", ref lightMode, lightModes, lightModes.Length))
                {
                    LightBakeMode value = (LightBakeMode)lightMode;
                    RecordImmediateEdit(() => light.Mode = value);
                }
                ImGui.TextDisabled(light.Mode switch
                {
                    LightBakeMode.Realtime => "Realtime only; filtered by the layer mask.",
                    LightBakeMode.Mixed => "Bakes Static objects and lights masked layers in realtime.",
                    _ => "Bakes Static objects only; no realtime cost."
                });
                if (light.RealtimeEnabled && ImGui.TreeNode("Realtime Layer Mask"))
                {
                    for (int layer = 0; layer < _projectSettings.CollisionLayers.Count; layer++)
                    {
                        uint bit = 1u << layer;
                        bool enabled = (light.RealtimeLayerMask & bit) != 0;
                        if (ImGui.Checkbox($"{layer}: {_projectSettings.CollisionLayers[layer]}", ref enabled))
                        {
                            uint mask = light.RealtimeLayerMask;
                            uint updatedMask = enabled ? mask | bit : mask & ~bit;
                            RecordImmediateEdit(() => light.RealtimeLayerMask = updatedMask);
                        }
                    }
                    ImGui.TreePop();
                }

                int lightTypeIndex =
                    (int)light.Type;

                string[] lightTypeNames =
                {
                    "Directional",
                    "Point",
                    "Spot"
                };

                if (ImGui.Combo(
                        "Type",
                        ref lightTypeIndex,
                        lightTypeNames,
                        lightTypeNames.Length))
                {
                    LightType newType =
                        (LightType)lightTypeIndex;

                    RecordImmediateEdit(
                        () =>
                        {
                            light.Type =
                                newType;
                        });
                }

                Vector3 lightColor =
                    light.Color;

                bool lightColorChanged =
                    ImGui.ColorEdit3(
                        "Color",
                        ref lightColor);

                if (lightColorChanged)
                {
                    light.Color =
                        lightColor;
                }

                BeginInspectorEditIfNeeded();
                FinishInspectorEditIfNeeded();

                ImGui.DragFloat(
                    "Intensity",
                    ref light.Intensity,
                    0.05f,
                    0.0f,
                    8.0f);

                BeginInspectorEditIfNeeded();
                FinishInspectorEditIfNeeded();

                if (light.Type ==
                        LightType.Point ||
                    light.Type ==
                        LightType.Spot)
                {
                    ImGui.DragFloat(
                        "Range",
                        ref light.Range,
                        0.1f,
                        0.1f,
                        1000.0f);

                    BeginInspectorEditIfNeeded();
                    FinishInspectorEditIfNeeded();
                }

                if (light.Type ==
                    LightType.Spot)
                {
                    ImGui.DragFloat(
                        "Spot Angle",
                        ref light.SpotAngle,
                        1.0f,
                        1.0f,
                        179.0f);

                    BeginInspectorEditIfNeeded();
                    FinishInspectorEditIfNeeded();

                    ImGui.TextDisabled(
                        "Spot lights use Transform Position + Rotation.");
                }
                else if (light.Type ==
                    LightType.Point)
                {
                    ImGui.TextDisabled(
                        "Point lights use Transform Position.");
                }
                else
                {
                    ImGui.TextDisabled(
                        "Directional lights use Transform Rotation.");
                }

                ImGui.TextDisabled(
                    "Up to 3 Point/Spot lights affect a mesh.");

            }
        }

        // -----------------------------------------------------
        // Rotator
        // -----------------------------------------------------

        Rotator? rotator =
            _selectedObject
                .GetComponent<Rotator>();

        if (rotator != null)
        {
            bool rotatorOpen =
                InspectorComponentHeader(
                    PrefabSectionLabel("Rotator", "Rotator"),
                    ImGuiTreeNodeFlags.DefaultOpen |
                    ImGuiTreeNodeFlags.AllowOverlap);

            GameObject rotatorTarget = _selectedObject;

            DrawComponentOptions(
                "Rotator",
                () => RecordImmediateEdit(
                    () =>
                    {
                        rotatorTarget.RemoveComponent<Rotator>();
                        rotatorTarget.AddComponent<Rotator>();
                    }),
                () => RecordImmediateEdit(
                    () => rotatorTarget.RemoveComponent<Rotator>()));

            if (rotatorOpen)
            {
                ImGui.DragFloat3(
                    "Axis",
                    ref rotator.Axis,
                    0.05f,
                    -1.0f,
                    1.0f);

                BeginInspectorEditIfNeeded();
                FinishInspectorEditIfNeeded();

                ImGui.DragFloat(
                    "Degrees / Second",
                    ref rotator.DegreesPerSecond,
                    1.0f,
                    -720.0f,
                    720.0f);

                BeginInspectorEditIfNeeded();
                FinishInspectorEditIfNeeded();

                ImGui.TextDisabled(
                    "Runs only in Play Mode.");

            }
        }

        PlayerController? playerController =
            _selectedObject
                .GetComponent<PlayerController>();

        if (playerController != null)
        {
            bool playerControllerOpen =
                InspectorComponentHeader(
                    PrefabSectionLabel("Player Controller", "PlayerController"),
                    ImGuiTreeNodeFlags.DefaultOpen |
                    ImGuiTreeNodeFlags.AllowOverlap);

            GameObject playerControllerTarget = _selectedObject;

            DrawComponentOptions(
                "PlayerController",
                () => RecordImmediateEdit(
                    () =>
                    {
                        playerControllerTarget.RemoveComponent<PlayerController>();
                        playerControllerTarget.AddComponent<PlayerController>();
                    }),
                () => RecordImmediateEdit(
                    () => playerControllerTarget.RemoveComponent<PlayerController>()));

            if (playerControllerOpen)
            {
                ImGui.DragFloat(
                    "Move Speed",
                    ref playerController.MoveSpeed,
                    0.1f,
                    0.0f,
                    100.0f);

                BeginInspectorEditIfNeeded();
                FinishInspectorEditIfNeeded();

                ImGui.DragFloat(
                    "Camera Orbit Speed",
                    ref playerController.TurnSpeed,
                    1.0f,
                    0.0f,
                    720.0f);

                BeginInspectorEditIfNeeded();
                FinishInspectorEditIfNeeded();

                ImGui.DragFloat("Camera Pitch Speed", ref playerController.CameraPitchSpeed, 1f, 0f, 720f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.DragFloat("Camera Min Pitch", ref playerController.CameraMinPitch, 1f, -85f, 85f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.DragFloat("Camera Max Pitch", ref playerController.CameraMaxPitch, 1f, -85f, 85f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.Checkbox("Invert Camera Pitch", ref playerController.InvertCameraPitch);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.Checkbox("Camera Collision", ref playerController.CameraCollision);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                int cameraLayerCount = _projectSettings.CollisionLayers.Count;
                int cameraMaskCount = Enumerable.Range(0, cameraLayerCount)
                    .Count(index => (playerController.CameraCollisionMask & (1u << index)) != 0);
                string cameraMaskLabel = cameraMaskCount == cameraLayerCount ? "Everything" :
                    cameraMaskCount == 0 ? "Nothing" : $"{cameraMaskCount} layer(s)";
                if (ImGui.BeginCombo("Camera Collision Mask", cameraMaskLabel))
                {
                    if (ImGui.Selectable("Everything")) playerController.CameraCollisionMask = uint.MaxValue;
                    if (ImGui.Selectable("Nothing")) playerController.CameraCollisionMask = 0u;
                    ImGui.Separator();
                    for (int index = 0; index < cameraLayerCount; index++)
                    {
                        bool enabled = (playerController.CameraCollisionMask & (1u << index)) != 0;
                        if (ImGui.Checkbox($"{_projectSettings.CollisionLayers[index]}##CameraMask{index}", ref enabled))
                        {
                            if (enabled) playerController.CameraCollisionMask |= 1u << index;
                            else playerController.CameraCollisionMask &= ~(1u << index);
                        }
                    }
                    ImGui.EndCombo();
                }
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.DragFloat("Camera Collision Radius", ref playerController.CameraCollisionRadius, 0.01f, 0.01f, 2f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.DragFloat("Camera Clearance", ref playerController.CameraCollisionClearance, 0.01f, 0f, 1f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.DragFloat("Camera Return Speed", ref playerController.CameraReturnSpeed, 0.1f, 0f, 50f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.DragFloat("Camera Target Height", ref playerController.CameraTargetHeight, 0.05f, 0f, 10f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.DragFloat("Acceleration", ref playerController.Acceleration, 0.5f, 0.0f, 200.0f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.DragFloat("Deceleration", ref playerController.Deceleration, 0.5f, 0.0f, 200.0f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.DragFloat("Air Control", ref playerController.AirControl, 0.01f, 0.0f, 1.0f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.DragFloat("Max Slope Angle", ref playerController.MaxSlopeAngle, 0.5f, 0.0f, 89.9f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.DragFloat("Step Height", ref playerController.StepHeight, 0.01f, 0.0f, 2.0f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.DragFloat("Ground Snap", ref playerController.GroundSnapDistance, 0.01f, 0.0f, 2.0f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.Checkbox("Enable Kill Floor", ref playerController.EnableKillFloor);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                if (!playerController.EnableKillFloor) ImGui.BeginDisabled();
                ImGui.DragFloat("Kill Floor Height", ref playerController.KillFloorHeight, 1.0f, -100000.0f, 100000.0f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                if (!playerController.EnableKillFloor) ImGui.EndDisabled();

                ImGui.Checkbox(
                    "Invert A / D",
                    ref playerController.InvertStrafe);

                BeginInspectorEditIfNeeded();
                FinishInspectorEditIfNeeded();

                ImGui.Checkbox(
                    "Invert Camera Orbit",
                    ref playerController.InvertTurn);

                BeginInspectorEditIfNeeded();
                FinishInspectorEditIfNeeded();

                ImGui.Checkbox("Enable Sprinting", ref playerController.EnableSprinting);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                if (!playerController.EnableSprinting) ImGui.BeginDisabled();
                ImGui.DragFloat("Sprint Multiplier", ref playerController.SprintMultiplier, 0.05f, 1.0f, 10.0f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                if (!playerController.EnableSprinting) ImGui.EndDisabled();

                ImGui.Checkbox("Enable Jumping", ref playerController.EnableJumping);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                if (!playerController.EnableJumping) ImGui.BeginDisabled();
                ImGui.DragFloat("Jump Strength", ref playerController.JumpStrength, 0.1f, 0.0f, 100.0f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.DragFloat("Coyote Time", ref playerController.CoyoteTime, 0.01f, 0.0f, 1.0f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.DragFloat("Jump Buffer", ref playerController.JumpBufferTime, 0.01f, 0.0f, 1.0f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.Checkbox("Launch From Animation Event", ref playerController.LaunchJumpFromAnimationEvent);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                if (!playerController.LaunchJumpFromAnimationEvent) ImGui.BeginDisabled();
                ImGui.DragFloat("Event Timeout", ref playerController.JumpEventTimeout, 0.01f, 0.0f, 2.0f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                if (!playerController.LaunchJumpFromAnimationEvent) ImGui.EndDisabled();
                if (!playerController.EnableJumping) ImGui.EndDisabled();

                ImGui.Separator();
                ImGui.TextDisabled("Input Actions");
                ImGui.InputText("Move Action", ref playerController.MoveAction, 64);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.InputText("Turn Action", ref playerController.TurnAction, 64);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.InputText("Sprint Action", ref playerController.SprintAction, 64);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.InputText("Jump Action", ref playerController.JumpAction, 64);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();

                ImGui.TextDisabled("Animator Integration");
                ImGui.InputText("Jump Trigger", ref playerController.JumpAnimationTrigger, 64);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.InputText("Launch Event", ref playerController.JumpLaunchEvent, 64);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();

                ImGui.TextDisabled("Animator Parameters");
                ImGui.InputText("Speed Parameter", ref playerController.SpeedParameter, 64);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.InputText("Moving Parameter", ref playerController.MovingParameter, 64);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.InputText("Backward Parameter", ref playerController.MovingBackwardParameter, 64);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.InputText("Sprinting Parameter", ref playerController.SprintingParameter, 64);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.InputText("Grounded Parameter", ref playerController.GroundedParameter, 64);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.InputText("Vertical Speed Parameter", ref playerController.VerticalSpeedParameter, 64);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.InputText("Idle Time Parameter", ref playerController.IdleTimeParameter, 64);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();

                ImGui.TextDisabled(
                    "Bindings are configured in Project Settings > Input Actions.");

            }
        }

        Rigidbody? rigidbody = _selectedObject.GetComponent<Rigidbody>();
        if (rigidbody != null)
        {
            bool open = InspectorComponentHeader(
                PrefabSectionLabel("Rigidbody", "Rigidbody"),
                ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.AllowOverlap);

            GameObject target = _selectedObject;
            DrawComponentOptions(
                "Rigidbody",
                () => RecordImmediateEdit(() =>
                {
                    target.RemoveComponent<Rigidbody>();
                    target.AddComponent<Rigidbody>();
                }),
                () => RecordImmediateEdit(() => target.RemoveComponent<Rigidbody>()));

            if (open)
            {
                string[] bodyTypeNames = { "Static", "Kinematic", "Dynamic" };
                int bodyTypeIndex = (int)rigidbody.BodyType;
                if (ImGui.Combo("Body Type", ref bodyTypeIndex, bodyTypeNames, bodyTypeNames.Length))
                {
                    BeginInspectorEditIfNeeded();
                    rigidbody.BodyType = (RigidbodyBodyType)bodyTypeIndex;
                    FinishInspectorEditIfNeeded();
                }

                ImGui.DragFloat3("Velocity", ref rigidbody.Velocity, 0.1f, -1000.0f, 1000.0f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();

                bool usesGravity = rigidbody.BodyType == RigidbodyBodyType.Dynamic;
                if (!usesGravity) ImGui.BeginDisabled();
                ImGui.DragFloat("Gravity Scale", ref rigidbody.GravityScale, 0.05f, -10.0f, 10.0f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                if (!usesGravity) ImGui.EndDisabled();

                ImGui.DragFloat("Linear Drag", ref rigidbody.LinearDrag, 0.05f, 0.0f, 100.0f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();

                bool dynamicBody = rigidbody.BodyType == RigidbodyBodyType.Dynamic;
                if (!dynamicBody) ImGui.BeginDisabled();
                ImGui.DragFloat("Mass", ref rigidbody.Mass, 0.05f, 0.01f, 10000.0f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.DragFloat("Restitution", ref rigidbody.Restitution, 0.01f, 0.0f, 1.0f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.DragFloat("Friction", ref rigidbody.Friction, 0.01f, 0.0f, 1.0f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                if (!dynamicBody) ImGui.EndDisabled();
                ImGui.TextDisabled("Restitution controls bounce; friction reduces sliding.");

                ImGui.TextDisabled("Simulates only in Play Mode.");
            }
        }

        BoxCollider? boxCollider = _selectedObject.GetComponent<BoxCollider>();
        if (boxCollider != null)
        {
            bool isPlaneCollider = boxCollider is PlaneCollider;
            bool open = InspectorComponentHeader(
                PrefabSectionLabel(isPlaneCollider ? "Plane Collider" : "Box Collider",
                    isPlaneCollider ? "PlaneCollider" : "BoxCollider"),
                ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.AllowOverlap);

            GameObject target = _selectedObject;
            DrawComponentOptions(
                isPlaneCollider ? "PlaneCollider" : "BoxCollider",
                () => RecordImmediateEdit(() =>
                {
                    target.RemoveComponent<BoxCollider>();
                    if (isPlaneCollider) AddFittedPlaneCollider(target);
                    else AddFittedBoxCollider(target);
                }),
                () => RecordImmediateEdit(() => target.RemoveComponent<BoxCollider>()));

            if (open)
            {
                ImGui.DragFloat3("Center", ref boxCollider.Center, 0.05f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.DragFloat3("Size", ref boxCollider.Size, 0.05f, 0.01f, 10000.0f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.Checkbox("Is Trigger", ref boxCollider.IsTrigger);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                DrawColliderFiltering(boxCollider);
            }
        }

        SphereCollider? sphereCollider = _selectedObject.GetComponent<SphereCollider>();
        if (sphereCollider != null)
        {
            bool open = InspectorComponentHeader(
                PrefabSectionLabel("Sphere Collider", "SphereCollider"),
                ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.AllowOverlap);

            GameObject target = _selectedObject;
            DrawComponentOptions(
                "SphereCollider",
                () => RecordImmediateEdit(() =>
                {
                    target.RemoveComponent<SphereCollider>();
                    target.AddComponent<SphereCollider>();
                }),
                () => RecordImmediateEdit(() => target.RemoveComponent<SphereCollider>()));

            if (open)
            {
                ImGui.DragFloat3("Center", ref sphereCollider.Center, 0.05f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.DragFloat("Radius", ref sphereCollider.Radius, 0.05f, 0.01f, 10000.0f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.Checkbox("Is Trigger", ref sphereCollider.IsTrigger);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                DrawColliderFiltering(sphereCollider);
            }
        }

        CapsuleCollider? capsuleCollider = _selectedObject.GetComponent<CapsuleCollider>();
        if (capsuleCollider != null)
        {
            bool open = InspectorComponentHeader(
                PrefabSectionLabel("Capsule Collider", "CapsuleCollider"),
                ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.AllowOverlap);
            GameObject target = _selectedObject;
            DrawComponentOptions(
                "CapsuleCollider",
                () => RecordImmediateEdit(() => { target.RemoveComponent<CapsuleCollider>(); target.AddComponent<CapsuleCollider>(); }),
                () => RecordImmediateEdit(() => target.RemoveComponent<CapsuleCollider>()));
            if (open)
            {
                ImGui.DragFloat3("Center", ref capsuleCollider.Center, 0.05f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.DragFloat("Radius", ref capsuleCollider.Radius, 0.05f, 0.01f, 10000.0f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.DragFloat("Height", ref capsuleCollider.Height, 0.05f, 0.02f, 10000.0f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.Checkbox("Is Trigger", ref capsuleCollider.IsTrigger);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                DrawColliderFiltering(capsuleCollider);
            }
        }

        MeshCollider? meshCollider = _selectedObject.GetComponent<MeshCollider>();
        if (meshCollider != null)
        {
            bool open = InspectorComponentHeader(
                PrefabSectionLabel("Mesh Collider", "MeshCollider"),
                ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.AllowOverlap);
            GameObject target = _selectedObject;
            DrawComponentOptions(
                "MeshCollider",
                () => RecordImmediateEdit(() => { target.RemoveComponent<MeshCollider>(); target.AddComponent<MeshCollider>(); }),
                () => RecordImmediateEdit(() => target.RemoveComponent<MeshCollider>()));
            if (open)
            {
                ImGui.DragFloat3("Center", ref meshCollider.Center, 0.05f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.Checkbox("Is Trigger", ref meshCollider.IsTrigger);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                DrawColliderFiltering(meshCollider);
                ImGui.TextDisabled("Uses the Mesh Renderer triangles. Intended for static level geometry.");
                Rigidbody? meshBody = _selectedObject.GetComponent<Rigidbody>();
                if (meshBody?.BodyType == RigidbodyBodyType.Dynamic)
                    ImGui.TextColored(new Vector4(1.0f, 0.4f, 0.25f, 1.0f), "Warning: Mesh Colliders should not use Dynamic rigidbodies.");
            }
        }

        void DrawColliderFiltering(Collider collider)
        {
            List<string> layers = _projectSettings.CollisionLayers;
            int layer = Math.Clamp(collider.Layer, 0, Math.Max(0, layers.Count - 1));
            string[] layerNames = layers.ToArray();
            if (layerNames.Length > 0 && ImGui.Combo("Layer", ref layer, layerNames, layerNames.Length))
            {
                BeginInspectorEditIfNeeded();
                collider.Layer = layer;
                FinishInspectorEditIfNeeded();
            }

            int selectedCount = Enumerable.Range(0, layers.Count)
                .Count(index => (collider.CollisionMask & (1u << index)) != 0);
            string maskLabel = selectedCount == layers.Count ? "Everything" : selectedCount == 0 ? "Nothing" : $"{selectedCount} layer(s)";
            if (ImGui.BeginCombo("Collision Mask", maskLabel))
            {
                if (ImGui.Selectable("Everything"))
                {
                    BeginInspectorEditIfNeeded();
                    collider.CollisionMask = uint.MaxValue;
                    FinishInspectorEditIfNeeded();
                }
                if (ImGui.Selectable("Nothing"))
                {
                    BeginInspectorEditIfNeeded();
                    collider.CollisionMask = 0;
                    FinishInspectorEditIfNeeded();
                }
                ImGui.Separator();
                for (int index = 0; index < layers.Count; index++)
                {
                    bool enabled = (collider.CollisionMask & (1u << index)) != 0;
                    if (ImGui.Checkbox($"{layers[index]}##Mask{index}", ref enabled))
                    {
                        BeginInspectorEditIfNeeded();
                        if (enabled) collider.CollisionMask |= 1u << index;
                        else collider.CollisionMask &= ~(1u << index);
                        FinishInspectorEditIfNeeded();
                    }
                }
                ImGui.EndCombo();
            }
        }

        AudioSource? audioSource = _selectedObject.GetComponent<AudioSource>();
        if (audioSource != null)
        {
            bool open = InspectorComponentHeader(
                PrefabSectionLabel("Audio Source", "AudioSource"),
                ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.AllowOverlap);
            GameObject target = _selectedObject;
            DrawComponentOptions(
                "AudioSource",
                () => RecordImmediateEdit(() =>
                {
                    target.RemoveComponent<AudioSource>();
                    target.AddComponent<AudioSource>();
                }),
                () => RecordImmediateEdit(() => target.RemoveComponent<AudioSource>()));

            if (open)
            {
                if (ImGui.Button("Browse / Import WAV..."))
                {
                    string? selectedAudio = WindowsFileDialog.OpenAudio();
                    if (selectedAudio != null)
                    {
                        string imported = AssetDatabase.ImportAudio(selectedAudio);
                        RecordImmediateEdit(() => audioSource.ClipPath = imported);
                        _projectBrowserPath = AssetDatabase.AudioDirectory;
                        _statusMessage = $"Imported audio: {Path.GetFileName(imported)}";
                    }
                }

                string clipLabel = string.IsNullOrWhiteSpace(audioSource.ClipPath)
                    ? "None - drop a .wav audio clip here"
                    : Path.GetFileName(audioSource.ClipPath);
                float clearWidth = ImGui.GetFrameHeight();
                bool inspectAudio = ImGui.Button($"{clipLabel}##AudioClip", new Vector2(
                    Math.Max(1.0f, ImGui.GetContentRegionAvail().X - clearWidth - ImGui.GetStyle().ItemSpacing.X), 0.0f));
                if (inspectAudio && !string.IsNullOrWhiteSpace(audioSource.ClipPath))
                    SelectInspectorAsset(audioSource.ClipPath);
                string? droppedAudio = AcceptAssetDrop(".wav");
                if (droppedAudio != null)
                {
                    string relative = AssetDatabase.ToProjectRelativePath(droppedAudio);
                    RecordImmediateEdit(() => audioSource.ClipPath = relative);
                }
                ImGui.SameLine();
                bool hasAudioClip = !string.IsNullOrWhiteSpace(audioSource.ClipPath);
                if (!hasAudioClip) ImGui.BeginDisabled();
                if (ImGui.Button("X##ClearAudio", new Vector2(clearWidth, 0.0f)))
                    RecordImmediateEdit(() => audioSource.ClipPath = "");
                if (!hasAudioClip) ImGui.EndDisabled();

                ImGui.Checkbox("Play On Start", ref audioSource.PlayOnStart);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.Checkbox("Loop", ref audioSource.Loop);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.Checkbox("3D Spatial", ref audioSource.Spatial);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.DragFloat("Volume", ref audioSource.Volume, 0.01f, 0.0f, 1.0f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.DragFloat("Pitch", ref audioSource.Pitch, 0.01f, 0.1f, 3.0f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();

                if (!audioSource.Spatial) ImGui.BeginDisabled();
                ImGui.DragFloat("Min Distance", ref audioSource.MinDistance, 0.1f, 0.01f, 1000.0f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.DragFloat("Max Distance", ref audioSource.MaxDistance, 0.1f, 0.01f, 10000.0f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                if (!audioSource.Spatial) ImGui.EndDisabled();
            }
        }

        SavePoint? savePoint = _selectedObject.GetComponent<SavePoint>();
        if (_selectedObject.GetComponent<SlidingDoor>() is { } door)
        {
            bool open=InspectorComponentHeader("Sliding Door",ImGuiTreeNodeFlags.DefaultOpen|ImGuiTreeNodeFlags.AllowOverlap);
            var owner=_selectedObject;
            DrawComponentOptions("Sliding Door",()=>RecordImmediateEdit(()=>{owner.RemoveComponent<SlidingDoor>();owner.AddComponent<SlidingDoor>();}),()=>RecordImmediateEdit(()=>owner.RemoveComponent<SlidingDoor>()));
            if(open){float lift=door.Lift,speed=door.Speed,range=door.Range;
                if(ImGui.DragFloat("Lift (world units)",ref lift,.05f,.1f,100))RecordImmediateEdit(()=>door.Lift=lift);
                if(ImGui.DragFloat("Opening Speed",ref speed,.05f,.1f,100))RecordImmediateEdit(()=>door.Speed=speed);
                if(ImGui.DragFloat("Interaction Range",ref range,.05f,.1f,100))RecordImmediateEdit(()=>door.Range=range);
                ImGui.TextWrapped("Interact / Cross opens once. Stays open until scene reload. Solid collider moves with the panel.");}
        }
        if (_selectedObject.GetComponent<Interactable>() is { } interaction)
        {
            bool interactionOpen=InspectorComponentHeader("Interactable", ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.AllowOverlap);
            var owner=_selectedObject;
            DrawComponentOptions("Interactable", () => RecordImmediateEdit(() => { owner.RemoveComponent<Interactable>(); owner.AddComponent<Interactable>(); }),
                () => RecordImmediateEdit(() => owner.RemoveComponent<Interactable>()));
            if(interactionOpen)
            {
                string prompt=interaction.Prompt, action=interaction.InputAction, canvasName=interaction.TargetCanvas;
                if(ImGui.InputText("Prompt",ref prompt,48)) RecordImmediateEdit(() => interaction.Prompt=prompt);
                string promptFont=interaction.PromptFontPath;
                float promptFontSize=interaction.PromptFontSize;
                if(ImGui.InputTextWithHint("Prompt Font","Drop an imported .r2font here",ref promptFont,512))
                    RecordImmediateEdit(() => interaction.PromptFontPath=promptFont);
                string? droppedPromptFont=AcceptAssetDrop(".r2font");
                if(droppedPromptFont!=null)
                {
                    string value=AssetDatabase.ToProjectRelativePath(droppedPromptFont);
                    RecordImmediateEdit(() => interaction.PromptFontPath=value);
                    _draggedAssetPath=null;
                }
                if(ImGui.DragFloat("Prompt Font Size",ref promptFontSize,.5f,4,256))
                    RecordImmediateEdit(() => interaction.PromptFontSize=promptFontSize);
                if(ImGui.InputText("Input Action",ref action,64)) RecordImmediateEdit(() => interaction.InputAction=action);
                if(ImGui.InputTextWithHint("Open Canvas","Drop dialogue Canvas here",ref canvasName,128)) RecordImmediateEdit(() => interaction.TargetCanvas=canvasName);
                if(_hierarchyDragActive && _draggedHierarchyObject is {} dragged && dragged.GetComponent<Canvas>()!=null &&
                    ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem) && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                    RecordImmediateEdit(() => interaction.TargetCanvas=dragged.Name);
                string pages=interaction.DialoguePages;
                ImGui.Text("Dialogue Pages");
                ImGui.SetNextItemWidth(-1);
                if(ImGui.InputTextMultiline("##DialoguePages",ref pages,4096,new Vector2(-1,100)))
                    RecordImmediateEdit(() => interaction.DialoguePages=pages);
                ImGui.TextDisabled("One line per page. E / Cross advances; the last page closes.");
                ImGui.TextDisabled("Leave empty to show the Canvas text as one page.");
                if(owner.GetComponent<BoxCollider>()?.IsTrigger != true)
                    ImGui.TextDisabled("Requires a Box Collider with Is Trigger enabled.");
            }
        }
        if (_selectedObject.GetComponent<AnimatorTriggerZone>() is { } animatorZone)
        {
            bool zoneOpen = InspectorComponentHeader("Animator Trigger Zone", ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.AllowOverlap);
            var targetObject = _selectedObject;
            DrawComponentOptions("AnimatorTriggerZone",
                () => RecordImmediateEdit(() => { targetObject.RemoveComponent<AnimatorTriggerZone>(); targetObject.AddComponent<AnimatorTriggerZone>(); }),
                () => RecordImmediateEdit(() => targetObject.RemoveComponent<AnimatorTriggerZone>()));
            if (zoneOpen)
            {
                string targetName = animatorZone.TargetAnimator;
                if (ImGui.InputTextWithHint("Target Animator", "Drop NPC from Hierarchy", ref targetName, 128))
                    RecordImmediateEdit(() => animatorZone.TargetAnimator = targetName);
                if (_hierarchyDragActive && _draggedHierarchyObject is { } dragged &&
                    ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem))
                {
                    bool valid = dragged.Scene == _scene && dragged.GetComponent<Animator>() != null;
                    ImGui.SetTooltip(valid ? "Assign " + dragged.Name : "Drop an object with an Animator.");
                    if (valid && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                        RecordImmediateEdit(() => animatorZone.TargetAnimator = dragged.Name);
                }
                string triggerName = animatorZone.TriggerName;
                if (ImGui.InputText("Trigger Name", ref triggerName, 64))
                    RecordImmediateEdit(() => animatorZone.TriggerName = triggerName);
                ImGui.TextDisabled("Player entry fires once; leave and return to fire again.");
                if (targetObject.GetComponent<BoxCollider>()?.IsTrigger != true)
                    ImGui.TextDisabled("Requires a Box Collider with Is Trigger enabled. No renderer needed.");
            }
        }
        if (savePoint != null)
        {
            bool open = InspectorComponentHeader(
                PrefabSectionLabel("Save Point", "SavePoint"),
                ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.AllowOverlap);
            GameObject target = _selectedObject;
            DrawComponentOptions(
                "SavePoint",
                () => RecordImmediateEdit(() =>
                {
                    target.RemoveComponent<SavePoint>();
                    target.AddComponent<SavePoint>();
                }),
                () => RecordImmediateEdit(() => target.RemoveComponent<SavePoint>()));
            if (open)
            {
                string slotName = savePoint.SlotName;
                if (ImGui.InputText("Slot Name", ref slotName, 17))
                    RecordImmediateEdit(() => savePoint.SlotName = slotName);
                bool saveOnEnter = savePoint.SaveOnTriggerEnter;
                if (ImGui.Checkbox("Save On Trigger Enter", ref saveOnEnter))
                    RecordImmediateEdit(() => savePoint.SaveOnTriggerEnter = saveOnEnter);
                BoxCollider? trigger = target.GetComponent<BoxCollider>();
                if (trigger?.IsTrigger != true)
                    ImGui.TextDisabled("Requires a Box Collider with Is Trigger enabled.");
            }
        }

        Canvas? canvasComponent = _selectedObject.GetComponent<Canvas>();
        if (!IsPlaying && Canvas.FindOwner(_selectedObject) is { } previewCanvas)
        {
            _canvasPreview.Resolve(_scene, _selectedObject);
            ImGui.SeparatorText("Canvas Preview (Editor Only)");
            ImGui.Checkbox("Show Canvas In Scene", ref _canvasPreview.ShowInScene);
            if (ImGui.Checkbox("Show Only Focused Canvas", ref _canvasPreview.Isolate))
            {
                try { _canvasPreview.SavePreferences(Path.Combine(AssetDatabase.ProjectRoot, "CanvasPreview.editor.json")); }
                catch (Exception error) { _statusMessage = $"Could not save canvas preview preference: {error.Message}"; }
            }
            ImGui.TextDisabled($"Editing: {previewCanvas.GameObject?.Name}");
            ImGui.TextWrapped("Select a canvas or its child in the Hierarchy to switch. Saved visibility and Play Mode are unchanged.");
        }
        if (canvasComponent != null && InspectorComponentHeader("Canvas", ImGuiTreeNodeFlags.DefaultOpen))
        {
            GameObject canvasOwner = _selectedObject;
            if (ImGui.Button("Add UI Element")) ImGui.OpenPopup("CanvasAddUiElement");
            if (ImGui.BeginPopup("CanvasAddUiElement"))
            {
                void AddElement<T>(string name) where T : Component, new()
                {
                    if (!ImGui.MenuItem(name)) return;
                    RecordImmediateEdit(() =>
                    {
                        GameObject element = _scene.CreateGameObject(name);
                        element.AddComponent<T>();
                        element.SetParent(canvasOwner, false);
                        _selectedObject = element;
                    });
                }
                AddElement<UIPanel>("Panel");
                AddElement<UIButton>("Button");
                AddElement<UIText>("Text");
                AddElement<UIImage>("Image");
                ImGui.EndPopup();
            }
            Vector2 resolution = canvasComponent.ReferenceResolution;
            if (ImGui.DragFloat2("Reference Resolution", ref resolution, 1, 1, 8192))
                RecordImmediateEdit(() => canvasComponent.ReferenceResolution = resolution);
            bool startsVisible = canvasComponent.StartsVisible;
            bool pause = canvasComponent.PauseGameplayWhenVisible;
            if (ImGui.Checkbox("Starts Visible", ref startsVisible)) RecordImmediateEdit(() => canvasComponent.StartsVisible = startsVisible);
            if (ImGui.Checkbox("Pause Gameplay When Visible", ref pause)) RecordImmediateEdit(() => canvasComponent.PauseGameplayWhenVisible = pause);
            bool toggleInput = canvasComponent.ToggleWithMenuInput;
            if (ImGui.Checkbox("Toggle With Start / Escape", ref toggleInput)) RecordImmediateEdit(() => canvasComponent.ToggleWithMenuInput = toggleInput);
            bool cancelInput = canvasComponent.CloseWithCancelInput;
            if (ImGui.Checkbox("Close With Cancel / Circle", ref cancelInput)) RecordImmediateEdit(() => canvasComponent.CloseWithCancelInput = cancelInput);
            ImGui.SeparatorText("UI Sounds (variation sheets supported)");
            void DrawUiSound(string label, string current, Action<string> assign)
            {
                string value = current;
                if (ImGui.InputTextWithHint(label, "Drop a .wav here", ref value, 512))
                    RecordImmediateEdit(() => assign(value));
                string? dropped = AcceptAssetDrop(".wav");
                if (dropped != null)
                {
                    string relative = AssetDatabase.ToProjectRelativePath(dropped);
                    RecordImmediateEdit(() => assign(relative));
                    _draggedAssetPath = null;
                }
            }
            DrawUiSound("Navigate / Scroll", canvasComponent.NavigateSound, value => canvasComponent.NavigateSound = value);
            DrawUiSound("Submit / Select", canvasComponent.SubmitSound, value => canvasComponent.SubmitSound = value);
            DrawUiSound("Cancel / Back", canvasComponent.CancelSound, value => canvasComponent.CancelSound = value);
            DrawUiSound("Error", canvasComponent.ErrorSound, value => canvasComponent.ErrorSound = value);
            DrawUiSound("Open / Appear", canvasComponent.OpenSound, value => canvasComponent.OpenSound = value);
            bool playOpenAtStart = canvasComponent.PlayOpenSoundOnSceneStart;
            if (ImGui.Checkbox("Play Open Sound On Scene Start", ref playOpenAtStart))
                RecordImmediateEdit(() => canvasComponent.PlayOpenSoundOnSceneStart = playOpenAtStart);
            ImGui.TextWrapped("Each WAV is split at silent gaps during playback; one variation is chosen randomly without immediately repeating.");
            ImGui.TextDisabled("Disable both for a main menu or permanent HUD.");
        }
        UIPanel? panelComponent = _selectedObject.GetComponent<UIPanel>();
        if (panelComponent != null && InspectorComponentHeader("UI Panel", ImGuiTreeNodeFlags.DefaultOpen))
        {
            int sortOrder = panelComponent.SortOrder;
            Vector2 anchor = panelComponent.Anchor, offset = panelComponent.Offset, size = panelComponent.Size;
            Vector4 color = panelComponent.Color;
            if (ImGui.DragFloat2("Anchor", ref anchor, .01f, 0, 1)) RecordImmediateEdit(() => panelComponent.Anchor = anchor);
            if (ImGui.DragFloat2("Offset", ref offset, 1)) RecordImmediateEdit(() => panelComponent.Offset = offset);
            if (ImGui.DragFloat2("Size", ref size, 1, 1, 8192)) RecordImmediateEdit(() => panelComponent.Size = size);
            if (ImGui.ColorEdit4("Color", ref color)) RecordImmediateEdit(() => panelComponent.Color = color);
            if (ImGui.DragInt("Sort Order", ref sortOrder)) RecordImmediateEdit(() => panelComponent.SortOrder = sortOrder);
        }
        UIButton? buttonComponent = _selectedObject.GetComponent<UIButton>();
        if (buttonComponent != null && InspectorComponentHeader("UI Button", ImGuiTreeNodeFlags.DefaultOpen))
        {
            int sortOrder = buttonComponent.SortOrder;
            string text = buttonComponent.Text;
            Vector2 anchor = buttonComponent.Anchor, offset = buttonComponent.Offset, size = buttonComponent.Size;
            Vector4 normal = buttonComponent.NormalColor, hover = buttonComponent.HoverColor, pressed = buttonComponent.PressedColor;
            bool interactable = buttonComponent.Interactable;
            if (ImGui.InputText("Text", ref text, 128)) RecordImmediateEdit(() => buttonComponent.Text = text);
            string buttonFont = buttonComponent.FontPath;
            float buttonFontSize = buttonComponent.FontSize;
            if (ImGui.InputTextWithHint("Font", "Drop an imported .r2font here", ref buttonFont, 512))
                RecordImmediateEdit(() => buttonComponent.FontPath = buttonFont);
            string? droppedButtonFont = AcceptAssetDrop(".r2font");
            if (droppedButtonFont != null)
            {
                string value = AssetDatabase.ToProjectRelativePath(droppedButtonFont);
                RecordImmediateEdit(() => buttonComponent.FontPath = value);
                _draggedAssetPath = null;
            }
            if (ImGui.DragFloat("Font Size", ref buttonFontSize, .5f, 4, 256))
                RecordImmediateEdit(() => buttonComponent.FontSize = buttonFontSize);
            if (ImGui.DragFloat2("Anchor", ref anchor, .01f, 0, 1)) RecordImmediateEdit(() => buttonComponent.Anchor = anchor);
            if (ImGui.DragFloat2("Offset", ref offset, 1)) RecordImmediateEdit(() => buttonComponent.Offset = offset);
            if (ImGui.DragFloat2("Size", ref size, 1, 1, 8192)) RecordImmediateEdit(() => buttonComponent.Size = size);
            if (ImGui.ColorEdit4("Normal", ref normal)) RecordImmediateEdit(() => buttonComponent.NormalColor = normal);
            if (ImGui.ColorEdit4("Hover", ref hover)) RecordImmediateEdit(() => buttonComponent.HoverColor = hover);
            if (ImGui.ColorEdit4("Pressed", ref pressed)) RecordImmediateEdit(() => buttonComponent.PressedColor = pressed);
            if (ImGui.Checkbox("Interactable", ref interactable)) RecordImmediateEdit(() => buttonComponent.Interactable = interactable);
            string normalSprite = buttonComponent.NormalSprite, hoverSprite = buttonComponent.HoverSprite, pressedSprite = buttonComponent.PressedSprite;
            Vector4 buttonBorders = buttonComponent.SpriteBorders;
            if (ImGui.DragFloat4("9-Slice Borders (L T R B)##button", ref buttonBorders, 1, 0, 8192)) RecordImmediateEdit(() => buttonComponent.SpriteBorders = Vector4.Max(buttonBorders, Vector4.Zero));
            ImGui.TextDisabled("Original image pixels; zero = stretch. Shared by button states.");
            if (ImGui.InputText("Normal Sprite", ref normalSprite, 512)) RecordImmediateEdit(() => buttonComponent.NormalSprite = normalSprite);
            string? droppedNormalSprite = AcceptTextureAssetDrop();
            if (droppedNormalSprite != null) { string value = AssetDatabase.ToProjectRelativePath(droppedNormalSprite); RecordImmediateEdit(() => buttonComponent.NormalSprite = value); _draggedAssetPath = null; }
            if (ImGui.InputText("Hover Sprite", ref hoverSprite, 512)) RecordImmediateEdit(() => buttonComponent.HoverSprite = hoverSprite);
            string? droppedHoverSprite = AcceptTextureAssetDrop();
            if (droppedHoverSprite != null) { string value = AssetDatabase.ToProjectRelativePath(droppedHoverSprite); RecordImmediateEdit(() => buttonComponent.HoverSprite = value); _draggedAssetPath = null; }
            if (ImGui.InputText("Pressed Sprite", ref pressedSprite, 512)) RecordImmediateEdit(() => buttonComponent.PressedSprite = pressedSprite);
            string? droppedPressedSprite = AcceptTextureAssetDrop();
            if (droppedPressedSprite != null) { string value = AssetDatabase.ToProjectRelativePath(droppedPressedSprite); RecordImmediateEdit(() => buttonComponent.PressedSprite = value); _draggedAssetPath = null; }
            if (ImGui.DragInt("Sort Order", ref sortOrder)) RecordImmediateEdit(() => buttonComponent.SortOrder = sortOrder);
            UIButtonAction action = buttonComponent.Action;
            if (ImGui.BeginCombo("On Click", action.ToString()))
            {
                foreach (UIButtonAction candidate in Enum.GetValues<UIButtonAction>())
                    if (ImGui.Selectable(candidate.ToString(), candidate == action))
                        RecordImmediateEdit(() => buttonComponent.Action = candidate);
                ImGui.EndCombo();
            }
            if (buttonComponent.Action is UIButtonAction.SaveCheckpoint or UIButtonAction.LoadCheckpoint)
            {
                string slot = buttonComponent.SaveSlot;
                if (ImGui.InputText("Save Slot", ref slot, 17)) RecordImmediateEdit(() => buttonComponent.SaveSlot = slot);
            }
            if (buttonComponent.Action == UIButtonAction.GoToScene)
            {
                string targetScene = buttonComponent.TargetScene;
                if (ImGui.InputTextWithHint("Target Scene", "Scene name or drop a scene", ref targetScene, 128))
                    RecordImmediateEdit(() => buttonComponent.TargetScene = targetScene);
                string? droppedScene = AcceptAssetDrop(".r2scene");
                if (droppedScene != null)
                {
                    string droppedSceneName = Path.GetFileNameWithoutExtension(droppedScene);
                    RecordImmediateEdit(() => buttonComponent.TargetScene = droppedSceneName);
                    _draggedAssetPath = null;
                }
                ImGui.TextDisabled("Include the target scene in Build Settings. Uses the loading screen if configured.");
            }
            if (buttonComponent.Action is UIButtonAction.OpenCanvas or UIButtonAction.CloseCanvas or UIButtonAction.ToggleCanvas or UIButtonAction.OpenSubmenu)
            {
                string targetCanvas = buttonComponent.TargetCanvas;
                if (ImGui.InputTextWithHint("Target Canvas", "Drop a Canvas from Hierarchy", ref targetCanvas, 128)) RecordImmediateEdit(() => buttonComponent.TargetCanvas = targetCanvas);
                if (_hierarchyDragActive && _draggedHierarchyObject is { } draggedCanvas &&
                    ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem))
                {
                    bool valid = draggedCanvas.Scene == _scene && draggedCanvas.GetComponent<Canvas>() != null &&
                        (buttonComponent.Action != UIButtonAction.OpenSubmenu || Canvas.FindOwner(buttonComponent.GameObject!) != draggedCanvas.GetComponent<Canvas>());
                    uint color = ImGui.ColorConvertFloat4ToU32(valid ? new Vector4(.2f, .85f, .5f, 1) : new Vector4(1, .3f, .3f, 1));
                    ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), color, 3, ImDrawFlags.None, 2);
                    ImGui.SetTooltip(valid ? $"Assign Canvas: {draggedCanvas.Name}" : "Drop a Canvas object (a submenu must target a different Canvas).");
                    if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                    {
                        if (valid)
                        {
                            string droppedCanvasName = draggedCanvas.Name;
                            RecordImmediateEdit(() => buttonComponent.TargetCanvas = droppedCanvasName);
                        }
                        else _statusMessage = "Target Canvas requires a Canvas object; OpenSubmenu cannot target its own Canvas.";
                    }
                }
                ImGui.TextDisabled("Drag a Canvas from the Hierarchy onto this field.");
            }
            ImGui.TextDisabled("Scripts can call ConsumeClick() during Play Mode.");
        }
        UIText? textComponent = _selectedObject.GetComponent<UIText>();
        if (textComponent != null && InspectorComponentHeader("UI Text", ImGuiTreeNodeFlags.DefaultOpen))
        {
            int sortOrder = textComponent.SortOrder;
            string text = textComponent.Text;
            Vector2 anchor = textComponent.Anchor, offset = textComponent.Offset, size = textComponent.Size;
            Vector4 color = textComponent.Color;
            float fontSize = textComponent.FontSize;
            if (ImGui.InputText("Text", ref text, 256)) RecordImmediateEdit(() => textComponent.Text = text);
            string fontPath=textComponent.FontPath;
            if(ImGui.InputTextWithHint("Font","Drop an imported .r2font here",ref fontPath,512)) RecordImmediateEdit(() => textComponent.FontPath=fontPath);
            string? droppedFont=AcceptAssetDrop(".r2font");
            if(droppedFont!=null) { string value=AssetDatabase.ToProjectRelativePath(droppedFont); RecordImmediateEdit(() => textComponent.FontPath=value); _draggedAssetPath=null; }
            bool feedback = textComponent.Ps2SaveLoadFeedback;
            if (ImGui.Checkbox("PS2 Save/Load Feedback", ref feedback)) RecordImmediateEdit(() => textComponent.Ps2SaveLoadFeedback = feedback);
            if (feedback)
            {
                string saved = textComponent.SavedMessage, saveFailed = textComponent.SaveFailedMessage;
                string loaded = textComponent.LoadedMessage, loadFailed = textComponent.LoadFailedMessage;
                if (ImGui.InputText("Saved Message", ref saved, 64)) RecordImmediateEdit(() => textComponent.SavedMessage = saved);
                if (ImGui.InputText("Save Failed Message", ref saveFailed, 64)) RecordImmediateEdit(() => textComponent.SaveFailedMessage = saveFailed);
                if (ImGui.InputText("Loaded Message", ref loaded, 64)) RecordImmediateEdit(() => textComponent.LoadedMessage = loaded);
                if (ImGui.InputText("Load Failed Message", ref loadFailed, 64)) RecordImmediateEdit(() => textComponent.LoadFailedMessage = loadFailed);
                ImGui.TextWrapped("PS2: shows the latest checkpoint result, including autosaves. Text is the initial message; leave it empty to start hidden. Editor/Windows preview shows Text.");
            }
            if (ImGui.DragFloat2("Anchor", ref anchor, .01f, 0, 1)) RecordImmediateEdit(() => textComponent.Anchor = anchor);
            if (ImGui.DragFloat2("Offset", ref offset, 1)) RecordImmediateEdit(() => textComponent.Offset = offset);
            if (ImGui.DragFloat2("Size", ref size, 1, 1, 8192)) RecordImmediateEdit(() => textComponent.Size = size);
            if (ImGui.DragFloat("Font Size", ref fontSize, .5f, 4, 256)) RecordImmediateEdit(() => textComponent.FontSize = fontSize);
            if (ImGui.ColorEdit4("Color", ref color)) RecordImmediateEdit(() => textComponent.Color = color);
            if (ImGui.DragInt("Sort Order", ref sortOrder)) RecordImmediateEdit(() => textComponent.SortOrder = sortOrder);
        }
        UIImage? imageComponent = _selectedObject.GetComponent<UIImage>();
        if (imageComponent != null && InspectorComponentHeader("UI Image", ImGuiTreeNodeFlags.DefaultOpen))
        {
            Vector4 imageBorders = imageComponent.SpriteBorders;
            if (ImGui.DragFloat4("9-Slice Borders (L T R B)##image", ref imageBorders, 1, 0, 8192)) RecordImmediateEdit(() => imageComponent.SpriteBorders = Vector4.Max(imageBorders, Vector4.Zero));
            ImGui.TextDisabled("Original image pixels; zero = stretch.");
            string path = imageComponent.TexturePath; Vector2 anchor = imageComponent.Anchor, offset = imageComponent.Offset, size = imageComponent.Size; Vector4 tint = imageComponent.Tint; bool preserve = imageComponent.PreserveAspect; int sort = imageComponent.SortOrder;
            if (ImGui.InputText("Texture", ref path, 512)) RecordImmediateEdit(() => imageComponent.TexturePath = path);
            string? droppedImageTexture = AcceptTextureAssetDrop();
            if (droppedImageTexture != null) { string value = AssetDatabase.ToProjectRelativePath(droppedImageTexture); RecordImmediateEdit(() => imageComponent.TexturePath = value); _draggedAssetPath = null; }
            if (!string.IsNullOrWhiteSpace(imageComponent.TexturePath) &&
                ImGui.Button($"Select {Path.GetFileName(imageComponent.TexturePath)}##SelectUiImageTexture",
                    new Vector2(ImGui.GetContentRegionAvail().X, 0.0f)))
                SelectInspectorAsset(imageComponent.TexturePath);
            Vector4 sourceRect = imageComponent.SourceRect;
            if (ImGui.DragFloat4("Sprite Region (X Y W H)", ref sourceRect, 1, 0, 65536, "%.0f"))
                RecordImmediateEdit(() => imageComponent.SourceRect = Vector4.Max(Vector4.Zero, Vector4.Round(sourceRect)));
            ImGui.TextWrapped("Original image pixels from the top-left. Width or height 0 uses the whole image.");
            if (ImGui.Button("Use Whole Image")) RecordImmediateEdit(() => imageComponent.SourceRect = Vector4.Zero);
            if (TryGetUiThumbnail(imageComponent.TexturePath, out uint atlasTexture, out int atlasWidth, out int atlasHeight))
            {
                Vector4 cropUv = UiNineSlice.UvRegion(imageComponent.TexturePath, imageComponent.SourceRect);
                Vector2 cropPixels = new(atlasWidth * (cropUv.Z - cropUv.X), atlasHeight * (cropUv.W - cropUv.Y));
                Vector2 cropPreview = cropPixels * MathF.Min(4f, 120f / MathF.Max(cropPixels.X, cropPixels.Y));
                ImGui.Image((nint)atlasTexture, cropPreview, new(cropUv.X, cropUv.Y), new(cropUv.Z, cropUv.W));
                ImGui.TextDisabled($"Sheet: {atlasWidth} x {atlasHeight} | Region: {cropPixels.X:0} x {cropPixels.Y:0}");
                if (ImGui.Button("Set Size To Sprite Region")) RecordImmediateEdit(() => imageComponent.Size = cropPixels);
            }
            if (ImGui.DragFloat2("Anchor", ref anchor, .01f, 0, 1)) RecordImmediateEdit(() => imageComponent.Anchor = anchor);
            if (ImGui.DragFloat2("Offset", ref offset, 1)) RecordImmediateEdit(() => imageComponent.Offset = offset);
            if (ImGui.DragFloat2("Size", ref size, 1, 1, 8192)) RecordImmediateEdit(() => imageComponent.Size = size);
            if (ImGui.ColorEdit4("Tint", ref tint)) RecordImmediateEdit(() => imageComponent.Tint = tint);
            if (ImGui.Checkbox("Preserve Aspect", ref preserve)) RecordImmediateEdit(() => imageComponent.PreserveAspect = preserve);
            if (ImGui.DragInt("Sort Order", ref sort)) RecordImmediateEdit(() => imageComponent.SortOrder = sort);
        }

        PersistentObject? persistentObject = _selectedObject.GetComponent<PersistentObject>();
        if (persistentObject != null)
        {
            bool open = InspectorComponentHeader(
                PrefabSectionLabel("Persistent Object", "PersistentObject"),
                ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.AllowOverlap);
            GameObject target = _selectedObject;
            DrawComponentOptions(
                "PersistentObject",
                () => RecordImmediateEdit(() =>
                {
                    target.RemoveComponent<PersistentObject>();
                    target.AddComponent<PersistentObject>();
                }),
                () => RecordImmediateEdit(() => target.RemoveComponent<PersistentObject>()));
            if (open)
            {
                string saveId = persistentObject.SaveId;
                if (ImGui.InputText("Save ID", ref saveId, 32))
                    RecordImmediateEdit(() => persistentObject.SaveId = saveId);
                bool saveTransform = persistentObject.SaveTransform;
                if (ImGui.Checkbox("Save Transform", ref saveTransform))
                    RecordImmediateEdit(() => persistentObject.SaveTransform = saveTransform);
                bool saveActiveState = persistentObject.SaveActiveState;
                if (ImGui.Checkbox("Save Active State", ref saveActiveState))
                    RecordImmediateEdit(() => persistentObject.SaveActiveState = saveActiveState);
                bool saveInteger = persistentObject.SaveInteger;
                if (ImGui.Checkbox("Save Integer", ref saveInteger))
                    RecordImmediateEdit(() => persistentObject.SaveInteger = saveInteger);
                if (saveInteger)
                {
                    int integerValue = persistentObject.IntegerValue;
                    if (ImGui.InputInt("Integer Value", ref integerValue))
                        RecordImmediateEdit(() => persistentObject.IntegerValue = integerValue);
                }
                bool saveBoolean = persistentObject.SaveBoolean;
                if (ImGui.Checkbox("Save Boolean", ref saveBoolean))
                    RecordImmediateEdit(() => persistentObject.SaveBoolean = saveBoolean);
                if (saveBoolean)
                {
                    bool booleanValue = persistentObject.BooleanValue;
                    if (ImGui.Checkbox("Boolean Value", ref booleanValue))
                        RecordImmediateEdit(() => persistentObject.BooleanValue = booleanValue);
                }
                ImGui.TextDisabled("Save IDs must be unique within the scene.");
            }
        }

        Animator? animator = _selectedObject.GetComponent<Animator>();
        if (animator != null)
        {
            bool open = InspectorComponentHeader(
                PrefabSectionLabel("Animator", "Animator"),
                ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.AllowOverlap);
            GameObject target = _selectedObject;
            DrawComponentOptions(
                "Animator",
                () => RecordImmediateEdit(() =>
                {
                    target.RemoveComponent<Animator>();
                    target.AddComponent<Animator>();
                }),
                () => RecordImmediateEdit(() => target.RemoveComponent<Animator>()));

            if (open)
            {
                string meshPath = target.GetComponent<MeshRenderer>()?.MeshPath ?? "";
                bool isSkeletal = string.Equals(Path.GetExtension(meshPath), ".r2skel", StringComparison.OrdinalIgnoreCase);
                SkeletalAsset? skeletalAsset = isSkeletal
                    ? MeshAssetCache.GetSkeletalAsset(meshPath)
                    : null;
                bool hasClip = !string.IsNullOrWhiteSpace(animator.ClipPath);

                if (skeletalAsset != null)
                {
                    string controllerLabel = string.IsNullOrWhiteSpace(animator.ControllerPath)
                        ? "None - drop a .r2controller here"
                        : Path.GetFileName(animator.ControllerPath);
                    bool inspectController = ImGui.Button($"{controllerLabel}##AnimatorController", new Vector2(ImGui.GetContentRegionAvail().X, 0));
                    if (inspectController && !string.IsNullOrWhiteSpace(animator.ControllerPath))
                        SelectInspectorAsset(animator.ControllerPath);
                    string? droppedController = AcceptAssetDrop(".r2controller");
                    if (droppedController != null)
                        RecordImmediateEdit(() => animator.ControllerPath = AssetDatabase.ToProjectRelativePath(droppedController));

                    if (skeletalAsset.Animations.Count == 0)
                    {
                        ImGui.TextDisabled("No embedded animation takes.");
                        ImGui.TextWrapped("Re-export the FBX with its Blender Action/NLA animation enabled.");
                    }
                    else
                    {
                        string takeLabel = string.IsNullOrWhiteSpace(animator.SkeletalTake)
                            ? skeletalAsset.Animations[0].Name
                            : animator.SkeletalTake;
                        if (ImGui.BeginCombo("Animation Take", takeLabel))
                        {
                            foreach (SkeletalAnimationClip take in skeletalAsset.Animations)
                            {
                                bool selected = string.Equals(take.Name, animator.SkeletalTake, StringComparison.Ordinal);
                                if (ImGui.Selectable($"{take.Name} ({take.Duration:0.00}s)", selected))
                                    RecordImmediateEdit(() => animator.SkeletalTake = take.Name);
                                if (selected) ImGui.SetItemDefaultFocus();
                            }
                            ImGui.EndCombo();
                        }
                    }
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(animator.ClipPath) && ImGui.Button("Create Animation Clip"))
                        CreateAnimationClip(target, animator);

                    string clipLabel = hasClip ? Path.GetFileName(animator.ClipPath) : "None - drop a .r2anim clip here";
                    float clearWidth = ImGui.GetFrameHeight();
                    bool inspectClip = ImGui.Button($"{clipLabel}##AnimationClip", new Vector2(
                        Math.Max(1.0f, ImGui.GetContentRegionAvail().X - clearWidth - ImGui.GetStyle().ItemSpacing.X), 0.0f));
                    if (inspectClip && hasClip)
                        SelectInspectorAsset(animator.ClipPath);
                    string? droppedClip = AcceptAssetDrop(".r2anim");
                    if (droppedClip != null)
                        RecordImmediateEdit(() => animator.ClipPath = AssetDatabase.ToProjectRelativePath(droppedClip));
                    ImGui.SameLine();
                    if (!hasClip) ImGui.BeginDisabled();
                    if (ImGui.Button("X##ClearAnimation", new Vector2(clearWidth, 0.0f)))
                        RecordImmediateEdit(() => animator.ClipPath = "");
                    if (!hasClip) ImGui.EndDisabled();
                }

                ImGui.Checkbox("Play On Start##Animator", ref animator.PlayOnStart);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.Checkbox("Loop##Animator", ref animator.Loop);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();
                ImGui.DragFloat("Speed##Animator", ref animator.Speed, 0.01f, -10.0f, 10.0f);
                BeginInspectorEditIfNeeded(); FinishInspectorEditIfNeeded();

                if (!isSkeletal && hasClip)
                    DrawAnimationClipEditor(target, animator);
            }
        }

        ScriptComponent? scriptComponent =
            _selectedObject
                .GetComponent<ScriptComponent>();

        if (scriptComponent != null)
        {
            bool scriptOpen =
                InspectorComponentHeader(
                    PrefabSectionLabel("Script", "Script"),
                    ImGuiTreeNodeFlags.DefaultOpen |
                    ImGuiTreeNodeFlags.AllowOverlap);

            GameObject scriptOptionsTarget = _selectedObject;

            DrawComponentOptions(
                "Script",
                () => RecordImmediateEdit(
                    () => scriptComponent.FieldValues.Clear()),
                () => RecordImmediateEdit(
                    () => scriptOptionsTarget.RemoveComponent<ScriptComponent>()),
                scriptComponent);

            if (scriptOpen)
            {
                string scriptName = string.IsNullOrWhiteSpace(scriptComponent.ScriptPath)
                    ? "None"
                    : Path.GetFileName(scriptComponent.ScriptPath);

                ImGui.Text("Script Asset");

                string scriptFieldLabel =
                    string.IsNullOrWhiteSpace(scriptComponent.ScriptPath)
                        ? "None - drop a .cs script here"
                        : scriptName;

                bool hadScriptAsset =
                    !string.IsNullOrWhiteSpace(scriptComponent.ScriptPath);

                float clearScriptWidth =
                    ImGui.GetFrameHeight();

                float scriptFieldWidth =
                    Math.Max(
                        1.0f,
                        ImGui.GetContentRegionAvail().X -
                        clearScriptWidth -
                        ImGui.GetStyle().ItemSpacing.X);

                ImGui.Button(
                    $"{scriptFieldLabel}##ScriptAssetField",
                    new Vector2(scriptFieldWidth, 0.0f));

                string? droppedScript =
                    AcceptAssetDrop(".cs");

                if (droppedScript != null)
                {
                    string scriptPath =
                        AssetDatabase.ToProjectRelativePath(droppedScript);

                    GameObject scriptTarget = _selectedObject;
                    RecordImmediateEdit(
                        () => scriptTarget.GetComponent<ScriptComponent>()!.AssignScriptAsset(scriptPath));

                    _editorScriptsReady = false;
                    _statusMessage = $"Assigned script: {scriptPath}";
                }

                ImGui.SameLine();

                if (!hadScriptAsset)
                    ImGui.BeginDisabled();

                if (ImGui.Button(
                        "X##ClearScriptAsset",
                        new Vector2(clearScriptWidth, 0.0f)))
                {
                    GameObject target = _selectedObject;

                    RecordImmediateEdit(
                        () =>
                        {
                            ScriptComponent? targetScript =
                                target.GetComponent<ScriptComponent>();

                            if (targetScript != null)
                            {
                                targetScript.ScriptPath = "";
                                targetScript.FieldValues.Clear();
                            }
                        });

                    _editorScriptsReady = false;
                    _statusMessage = "Removed script from component.";
                }

                if (!hadScriptAsset)
                    ImGui.EndDisabled();

                ImGui.TextDisabled("Class name must match the .cs filename.");

                DrawScriptFields(
                    _selectedObject,
                    scriptComponent);

            }
        }

        ImGui.Separator();

        // -----------------------------------------------------
        // Add Component
        // -----------------------------------------------------

        float addComponentWidth = Math.Min(220.0f, ImGui.GetContentRegionAvail().X);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() +
            Math.Max(0.0f, (ImGui.GetContentRegionAvail().X - addComponentWidth) * 0.5f));
        if (ImGui.Button(
                "Add Component",
                new Vector2(addComponentWidth, 0.0f)))
        {
            ImGui.OpenPopup(
                "AddComponentPopup");
        }

        if (ImGui.BeginPopup(
                "AddComponentPopup"))
        {
            GameObject target = _selectedObject;
            ImGui.SetNextItemWidth(260.0f);
            ImGui.InputTextWithHint("##AddComponentSearch", "Search components", ref _addComponentSearch, 96);
            ImGui.Separator();
            bool searchingComponents = !string.IsNullOrWhiteSpace(_addComponentSearch);
            bool Matches(string label) => !searchingComponents ||
                label.Contains(_addComponentSearch, StringComparison.OrdinalIgnoreCase);
            void Item(string label, bool available, Action add)
            {
                if (!Matches(label)) return;
                if (!available) ImGui.BeginDisabled();
                if (ImGui.MenuItem(label) && available)
                    RecordImmediateEdit(add);
                if (!available) ImGui.EndDisabled();
            }
            bool Missing<T>() where T : Component => target.GetComponent<T>() == null;
            void Category(string label, Action contents)
            {
                if (searchingComponents)
                {
                    contents();
                    return;
                }
                if (!ImGui.BeginMenu(label)) return;
                contents();
                ImGui.EndMenu();
            }

            Category("Rendering", () =>
            {
                Item("Mesh Renderer", Missing<MeshRenderer>(), () => target.AddComponent<MeshRenderer>());
                Item("Camera", Missing<Camera>(), () => target.AddComponent<Camera>());
                Item("Light", Missing<Light>(), () => target.AddComponent<Light>());
            });

            Category("Physics", () =>
            {
                Item("Rigidbody", Missing<Rigidbody>(), () => target.AddComponent<Rigidbody>());
                ImGui.Separator();
                Item("Box Collider", Missing<BoxCollider>(), () => AddFittedBoxCollider(target));
                Item("Plane Collider", Missing<BoxCollider>() && Missing<PlaneCollider>(), () => AddFittedPlaneCollider(target));
                Item("Sphere Collider", Missing<SphereCollider>(), () => target.AddComponent<SphereCollider>());
                Item("Capsule Collider", Missing<CapsuleCollider>(), () =>
                {
                    CapsuleCollider collider = target.AddComponent<CapsuleCollider>();
                    if (target.GetComponent<MeshRenderer>() is { } renderer)
                    {
                        Mesh mesh = renderer.MeshData;
                        collider.Center = mesh.BoundsCenter;
                        collider.Height = Math.Max(0.02f, mesh.BoundsSize.Y);
                        collider.Radius = Math.Min(
                            Math.Max(0.01f, MathF.Max(mesh.BoundsSize.X, mesh.BoundsSize.Z) * 0.5f),
                            collider.Height * 0.5f);
                    }
                });
                Item("Mesh Collider", Missing<MeshCollider>(), () => target.AddComponent<MeshCollider>());
            });

            Category("Audio", () =>
            {
                Item("Audio Source", Missing<AudioSource>(), () => target.AddComponent<AudioSource>());
            });

            Category("Animation", () =>
            {
                Item("Animator", Missing<Animator>(), () => target.AddComponent<Animator>());
                Item("Rotator", Missing<Rotator>(), () => target.AddComponent<Rotator>());
                Item("Animator Trigger Zone", Missing<AnimatorTriggerZone>(), () =>
                {
                    target.AddComponent<AnimatorTriggerZone>();
                    BoxCollider box = target.GetComponent<BoxCollider>() ?? target.AddComponent<BoxCollider>();
                    box.IsTrigger = true;
                });
            });

            Category("UI", () =>
            {
                Item("Canvas", Missing<Canvas>(), () => target.AddComponent<Canvas>());
                Item("Panel", Missing<UIPanel>(), () => target.AddComponent<UIPanel>());
                Item("Button", Missing<UIButton>(), () => target.AddComponent<UIButton>());
                Item("Text", Missing<UIText>(), () => target.AddComponent<UIText>());
                Item("Image", Missing<UIImage>(), () => target.AddComponent<UIImage>());
            });

            Category("Gameplay", () =>
            {
                Item("Player Controller", Missing<PlayerController>(), () => target.AddComponent<PlayerController>());
                Item("Interactable", Missing<Interactable>(), () =>
                {
                    target.AddComponent<Interactable>();
                    BoxCollider box = target.GetComponent<BoxCollider>() ?? target.AddComponent<BoxCollider>();
                    box.IsTrigger = true;
                    box.Size = new Vector3(4, 3, 4);
                });
                Item("Sliding Door", Missing<SlidingDoor>(), () =>
                {
                    target.AddComponent<SlidingDoor>();
                    BoxCollider box = target.GetComponent<BoxCollider>() ?? target.AddComponent<BoxCollider>();
                    box.IsTrigger = false;
                });
                Item("Save Point", Missing<SavePoint>(), () => target.AddComponent<SavePoint>());
                Item("Persistent Object", Missing<PersistentObject>(), () => target.AddComponent<PersistentObject>());
            });

            Category("Scripts", () =>
            {
                Item("Script", Missing<ScriptComponent>(), () => target.AddComponent<ScriptComponent>());
            });

            ImGui.EndPopup();
        }

        _selectedObject = sceneSelection;
        ImGui.End();
    }

    private static void SetStaticRecursively(GameObject root, bool isStatic)
    {
        root.IsStatic = isStatic;
        foreach (GameObject child in root.Children.ToArray())
            SetStaticRecursively(child, isStatic);
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        root.Layer = layer;
        foreach (GameObject child in root.Children.ToArray())
            SetLayerRecursively(child, layer);
    }

    private static BoxCollider AddFittedBoxCollider(GameObject target)
    {
        BoxCollider collider = target.AddComponent<BoxCollider>();
        MeshRenderer? renderer = target.GetComponent<MeshRenderer>();
        if (renderer == null)
            return collider;

        (Vector3 center, Vector3 boundsSize) = GetRendererBounds(renderer);
        Vector3 size = Vector3.Abs(boundsSize);
        collider.Center = center;
        collider.Size = new Vector3(
            Math.Max(0.01f, size.X),
            Math.Max(0.01f, size.Y),
            Math.Max(0.01f, size.Z));
        return collider;
    }

    private static PlaneCollider AddFittedPlaneCollider(GameObject target)
    {
        PlaneCollider collider = target.AddComponent<PlaneCollider>();
        MeshRenderer? renderer = target.GetComponent<MeshRenderer>();
        if (renderer == null) return collider;
        (Vector3 center, Vector3 size) = GetRendererBounds(renderer);
        collider.Center = center;
        collider.Size = new Vector3(Math.Max(0.01f, MathF.Abs(size.X)),
            PlaneCollider.Thickness, Math.Max(0.01f, MathF.Abs(size.Z)));
        return collider;
    }

    private static (Vector3 Center, Vector3 Size) GetRendererBounds(MeshRenderer renderer)
    {
        Mesh mesh = renderer.MeshData;
        if (renderer.SubmeshIndex < 0) return (mesh.BoundsCenter, mesh.BoundsSize);
        MeshSubmesh section = renderer.GetSubmesh(0);
        Vector3 minimum = new(float.PositiveInfinity), maximum = new(float.NegativeInfinity);
        for (int i = section.IndexStart; i < section.IndexStart + section.IndexCount; i++)
        {
            Vector3 position = mesh.GetPosition((int)mesh.Indices[i]);
            minimum = Vector3.Min(minimum, position);
            maximum = Vector3.Max(maximum, position);
        }
        return ((minimum + maximum) * 0.5f, maximum - minimum);
    }

    private static bool InspectorComponentHeader(string label, ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.None)
    {
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.64f, 0.64f, 0.64f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.72f, 0.72f, 0.72f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.58f, 0.64f, 0.71f, 1.0f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(5.0f, 4.0f));
        bool open = ImGui.CollapsingHeader(label, flags);
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
        return open;
    }

    private void DrawComponentOptions(
        string id,
        Action reset,
        Action remove,
        ScriptComponent? script = null)
    {
        Vector2 headerMin = ImGui.GetItemRectMin();
        Vector2 headerMax = ImGui.GetItemRectMax();

        ImGui.SetCursorScreenPos(
            new Vector2(
                headerMax.X - 24.0f,
                headerMin.Y + 1.0f));

        if (ImGui.SmallButton($"...##{id}"))
            ImGui.OpenPopup($"ComponentOptions##{id}");

        if (ImGui.BeginPopup($"ComponentOptions##{id}"))
        {
            if (script != null)
            {
                if (ImGui.MenuItem("Reset Fields to Defaults"))
                    reset();

                if (ImGui.MenuItem("Copy Component"))
                {
                    _copiedScriptFieldValues =
                        new Dictionary<string, string>(
                            script.FieldValues,
                            StringComparer.Ordinal);
                }

                bool canPaste = _copiedScriptFieldValues != null;

                if (!canPaste)
                    ImGui.BeginDisabled();

                if (ImGui.MenuItem("Paste Component Values") && canPaste)
                {
                    Dictionary<string, string> copied =
                        new(_copiedScriptFieldValues!, StringComparer.Ordinal);

                    RecordImmediateEdit(
                        () => script.FieldValues = copied);
                }

                if (!canPaste)
                    ImGui.EndDisabled();
            }
            else if (ImGui.MenuItem("Reset"))
            {
                reset();
            }

            ImGui.Separator();

            if (ImGui.MenuItem("Remove Component"))
                remove();

            ImGui.EndPopup();
        }
    }

    // =========================================================
    // Project Browser
    // =========================================================

    private void DrawProjectBreadcrumbs(string scenesRoot)
    {
        string current = Path.GetFullPath(_projectBrowserPath);
        string scenes = Path.GetFullPath(scenesRoot);
        bool inScenes = current.Equals(scenes, StringComparison.OrdinalIgnoreCase) ||
            current.StartsWith(scenes + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        string root = inScenes ? scenes : Path.GetFullPath(AssetDatabase.AssetsRoot);
        var crumbs = new List<(string Label, string Path)> { ("Assets", AssetDatabase.AssetsRoot) };
        if (inScenes) crumbs.Add(("Scenes", scenes));
        string relative = Path.GetRelativePath(root, current);
        string destination = root;
        if (relative != ".")
            foreach (string part in relative.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries))
            {
                destination = Path.Combine(destination, part);
                crumbs.Add((part, destination));
            }

        float rightEdge = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        string? navigateTo = null;
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(3, 0));
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        for (int i = 0; i < crumbs.Count; i++)
        {
            var crumb = crumbs[i];
            if (i > 0)
            {
                // Wrap deep paths rather than hiding their ancestors offscreen.
                float needed = ImGui.CalcTextSize(crumb.Label).X + 30;
                if (ImGui.GetItemRectMax().X + needed < rightEdge) ImGui.SameLine();
                ImGui.TextDisabled(">");
                ImGui.SameLine();
            }
            ImGui.PushID(i);
            if (i == crumbs.Count - 1) ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
            if (ImGui.SmallButton(crumb.Label + "##crumb")) navigateTo = crumb.Path;
            if (i == crumbs.Count - 1) ImGui.PopStyleColor();
            if (_draggedAssetPath != null &&
                !string.Equals(Path.GetDirectoryName(_draggedAssetPath), crumb.Path, StringComparison.OrdinalIgnoreCase) &&
                ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem))
            {
                ImGui.GetWindowDrawList().AddRect(
                    ImGui.GetItemRectMin() - Vector2.One,
                    ImGui.GetItemRectMax() + Vector2.One,
                    ImGui.GetColorU32(ImGuiCol.DragDropTarget), 2.0f, ImDrawFlags.None, 2.0f);
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                {
                    string source = _draggedAssetPath;
                    MoveAssetAndRepairReferences(source, Path.Combine(crumb.Path, Path.GetFileName(source)));
                    _draggedAssetPath = null;
                }
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(crumb.Path);
            ImGui.PopID();
        }
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        if (navigateTo != null && Directory.Exists(navigateTo))
        {
            _projectBrowserPath = navigateTo;
            _selectedAssetPath = null;
        }
    }

    private void DrawProject()
    {
        ImGui.Begin(
            "Project");

        AssetDatabase.Initialize();
        ImportQueuedExternalAssets();

        string scenesRoot = Path.Combine(AssetDatabase.ProjectRoot, "Scenes");
        bool browsingScenes = string.Equals(Path.GetFullPath(_projectBrowserPath), Path.GetFullPath(scenesRoot), StringComparison.OrdinalIgnoreCase) ||
            Path.GetFullPath(_projectBrowserPath).StartsWith(Path.GetFullPath(scenesRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        string browserRoot = browsingScenes ? scenesRoot : AssetDatabase.AssetsRoot;

        bool atRoot =
            string.Equals(
                Path.GetFullPath(
                    _projectBrowserPath),
                Path.GetFullPath(
                    AssetDatabase.AssetsRoot),
                StringComparison.OrdinalIgnoreCase);

        if (atRoot)
            ImGui.BeginDisabled();

        if (ImGui.Button(
                "< Back"))
        {
            DirectoryInfo? parent =
                Directory.GetParent(
                    _projectBrowserPath);

            if (parent != null &&
                (string.Equals(parent.FullName, browserRoot, StringComparison.OrdinalIgnoreCase) ||
                 parent.FullName.StartsWith(browserRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            {
                _projectBrowserPath =
                    parent.FullName;
            }
            else
            {
                _projectBrowserPath =
                    AssetDatabase.AssetsRoot;
            }

            _selectedAssetPath =
                null;
        }

        if (atRoot)
            ImGui.EndDisabled();

        ImGui.SameLine();

        if (ImGui.Button("##ProjectHome", new Vector2(28, ImGui.GetFrameHeight())))
        {
            _projectBrowserPath = AssetDatabase.AssetsRoot;
            _selectedAssetPath = null;
        }
        Vector2 homeMin = ImGui.GetItemRectMin();
        Vector2 homeCenter = (homeMin + ImGui.GetItemRectMax()) * .5f;
        var homeDraw = ImGui.GetWindowDrawList();
        uint homeColor = ImGui.GetColorU32(ImGuiCol.Text);
        homeDraw.AddLine(homeCenter + new Vector2(-7, 0), homeCenter + new Vector2(0, -6), homeColor, 1.5f);
        homeDraw.AddLine(homeCenter + new Vector2(0, -6), homeCenter + new Vector2(7, 0), homeColor, 1.5f);
        homeDraw.AddRect(homeCenter + new Vector2(-5, 0), homeCenter + new Vector2(5, 6), homeColor);
        homeDraw.AddRect(homeCenter + new Vector2(-1, 2), homeCenter + new Vector2(1, 6), homeColor);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Home — Assets");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(MathF.Max(120.0f, ImGui.GetContentRegionAvail().X));
        if (_focusProjectSearch)
        {
            ImGui.SetKeyboardFocusHere();
            _focusProjectSearch = false;
        }
        ImGui.InputTextWithHint("##ProjectSearch", "Search assets...", ref _projectSearch, 128);

        ImGui.Separator();

        if (ImGui.BeginPopupModal("Create Asset Folder", ref _openNewFolderPopup, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted("Folder name");
            bool submit = ImGui.InputText("##NewFolderName", ref _newFolderName, 128, ImGuiInputTextFlags.EnterReturnsTrue);
            bool valid = IsValidAssetFolderName(_newFolderName);
            if (!valid) ImGui.TextColored(new Vector4(1, .35f, .3f, 1), "Use a normal folder name without path characters.");
            ImGui.BeginDisabled(!valid);
            if (submit || ImGui.Button("Create"))
            {
                string destination = Path.Combine(_projectBrowserPath, _newFolderName.Trim());
                if (Directory.Exists(destination)) _statusMessage = $"Folder already exists: {_newFolderName.Trim()}";
                else { Directory.CreateDirectory(destination); _statusMessage = $"Created {AssetDatabase.ToProjectRelativePath(destination)}"; }
                _openNewFolderPopup = false; ImGui.CloseCurrentPopup();
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) { _openNewFolderPopup = false; ImGui.CloseCurrentPopup(); }
            ImGui.EndPopup();
        }

        DrawProjectBreadcrumbs(scenesRoot);
        // Navigation can change roots during this frame (including the virtual Scenes folder).
        atRoot = string.Equals(Path.GetFullPath(_projectBrowserPath), Path.GetFullPath(AssetDatabase.AssetsRoot), StringComparison.OrdinalIgnoreCase);
        browsingScenes = string.Equals(Path.GetFullPath(_projectBrowserPath), Path.GetFullPath(scenesRoot), StringComparison.OrdinalIgnoreCase) ||
            Path.GetFullPath(_projectBrowserPath).StartsWith(Path.GetFullPath(scenesRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        ImGui.Separator();

        const float tileWidth =
            112.0f;

        const float thumbnailArea =
            88.0f;

        const float tileHeight =
            132.0f;

        float availableWidth =
            ImGui.GetContentRegionAvail().X;

        int columns =
            Math.Max(
                1,
                (int)(
                    availableWidth /
                    tileWidth));

        int itemIndex =
            0;
        float assetAreaTop = ImGui.GetCursorScreenPos().Y;
        bool searchingProject = !string.IsNullOrWhiteSpace(_projectSearch);
        string projectQuery = _projectSearch.Trim();
        IEnumerable<string> projectDirectories = searchingProject
            ? Directory.EnumerateDirectories(browserRoot, "*", SearchOption.AllDirectories)
                .Where(path => Path.GetFileName(path).Contains(projectQuery, StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            : AssetDatabase.GetDirectories(_projectBrowserPath);
        if (atRoot) projectDirectories = projectDirectories.Append(scenesRoot).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);

        foreach (
            string directory
            in projectDirectories)
        {
            if (itemIndex > 0 &&
                itemIndex % columns != 0)
            {
                ImGui.SameLine();
            }

            DrawFolderTile(
                directory,
                tileWidth,
                tileHeight,
                thumbnailArea);

            itemIndex++;
        }

        IEnumerable<string> projectFiles = searchingProject
            ? Directory.EnumerateFiles(browserRoot, "*", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(".r2import", StringComparison.OrdinalIgnoreCase) &&
                               !path.EndsWith(".r2fbximport", StringComparison.OrdinalIgnoreCase) &&
                               Path.GetFileName(path).Contains(projectQuery, StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            : AssetDatabase.GetFiles(_projectBrowserPath);
        foreach (string file in projectFiles)
        {
            if (itemIndex > 0 &&
                itemIndex % columns != 0)
            {
                ImGui.SameLine();
            }

            DrawAssetTile(
                file,
                tileWidth,
                tileHeight,
                thumbnailArea);

            itemIndex++;
        }

        if (itemIndex == 0)
        {
            ImGui.TextDisabled(
                "This folder is empty.");
        }


        bool requestNewFolder = false;
        if (ImGui.IsWindowHovered() && !ImGui.IsAnyItemHovered() &&
            ImGui.GetMousePos().Y >= assetAreaTop && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            ImGui.OpenPopup("ProjectEmptySpaceMenu");
        if (ImGui.BeginPopup("ProjectEmptySpaceMenu"))
        {
            if (ImGui.BeginMenu("Create"))
            {
                if (ImGui.MenuItem("Folder"))
                {
                    _newFolderName = "New Folder";
                    requestNewFolder = true;
                }
                if (!browsingScenes) ImGui.Separator();
                if (!browsingScenes && ImGui.MenuItem("C# Script"))
                    CreateScriptAsset(_projectBrowserPath);
                if (!browsingScenes && ImGui.MenuItem("Material"))
                    CreateMaterialAsset(_projectBrowserPath);
                if (!browsingScenes && ImGui.MenuItem("Animation Clip"))
                    CreateBlankAnimationAsset(_projectBrowserPath);
                if (!browsingScenes && ImGui.MenuItem("Animator Controller"))
                    CreateAnimatorControllerAsset(_projectBrowserPath);
                ImGui.EndMenu();
            }

            if (!browsingScenes && ImGui.BeginMenu("Import"))
            {
                if (ImGui.MenuItem("Texture..."))
                    ImportAssetForCurrentFolder("textures", _projectBrowserPath);
                if (ImGui.MenuItem("Audio..."))
                    ImportAssetForCurrentFolder("audio", _projectBrowserPath);
                if (ImGui.MenuItem("Script..."))
                    ImportAssetForCurrentFolder("scripts", _projectBrowserPath);
                if (ImGui.MenuItem("Font..."))
                    ImportAssetForCurrentFolder("fonts", _projectBrowserPath);
                ImGui.Separator();
                if (ImGui.MenuItem("OBJ Model...")) ImportModelAsset("OBJ", _projectBrowserPath);
                if (ImGui.MenuItem("FBX Model...")) ImportModelAsset("FBX", _projectBrowserPath);
                if (ImGui.MenuItem("GLB Model...")) ImportModelAsset("GLB", _projectBrowserPath);
                if (ImGui.MenuItem("glTF Model...")) ImportModelAsset("glTF", _projectBrowserPath);
                if (ImGui.MenuItem("Humanoid Animation FBX..."))
                    ImportHumanoidAnimationAsset(_projectBrowserPath);
                ImGui.EndMenu();
            }

            ImGui.Separator();
            if (ImGui.MenuItem("Show in File Explorer"))
                OpenProjectFolder(_projectBrowserPath);

            if (ImGui.MenuItem("Refresh"))
            {
                foreach (string reimportMessage in AssetDatabase.ReimportChangedFileAssets())
                    _scriptMessages.Add(reimportMessage);
                List<string> referenceMessages = AssetDatabase.ValidateSceneReferences(_scene);
                foreach (string referenceMessage in referenceMessages)
                    _scriptMessages.Add(referenceMessage);
                if (referenceMessages.Count > 0)
                    _statusMessage = $"Refresh found {referenceMessages.Count} missing or invalid asset reference(s). See Console.";
                _thumbnailCache.Clear();
                MaterialAssetCache.Clear();
                MeshAssetCache.Clear();
                _sceneRenderer.ClearAssetCaches();
                _gameRenderer.ClearAssetCaches();
                _cameraPreviewRenderer.ClearAssetCaches();
                _editorScriptsReady = false;
            }

            ImGui.EndPopup();
        }
        if (requestNewFolder)
        {
            _openNewFolderPopup = true;
            ImGui.OpenPopup("Create Asset Folder");
        }

        DrawAssetManagementPopups();

        ImGui.End();
    }

    public void QueueExternalFileDrop(IEnumerable<string> paths)
    {
        foreach (string path in paths) if (!string.IsNullOrWhiteSpace(path)) _externalAssetDrops.Enqueue(path);
    }

    private void ImportQueuedExternalAssets()
    {
        int imported = 0;
        while (_externalAssetDrops.TryDequeue(out string? source))
        {
            try
            {
                if (!File.Exists(source)) continue;
                string extension=Path.GetExtension(source).ToLowerInvariant();
                if(extension is ".ttf" or ".otf")
                {
                    string importedFont=FontAssetImporter.Import(source,_projectBrowserPath);
                    _selectedAssetPath=AssetDatabase.ToAbsolutePath(importedFont);
                    imported++;
                    continue;
                }
                if (extension is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga")
                {
                    string importedTexture = AssetDatabase.ImportTexture(source, _projectBrowserPath);
                    _selectedAssetPath = AssetDatabase.ToAbsolutePath(importedTexture);
                    imported++;
                    continue;
                }
                if (extension is ".wav" or ".mp3" or ".ogg" or ".flac" or ".aac" or ".m4a")
                {
                    string importedAudio = AssetDatabase.ImportAudio(source, _projectBrowserPath);
                    _selectedAssetPath = AssetDatabase.ToAbsolutePath(importedAudio);
                    imported++;
                    continue;
                }
                if (extension is ".obj" or ".fbx" or ".glb" or ".gltf")
                {
                    string importedModel = AssetDatabase.ImportModel(source, _projectBrowserPath);
                    _selectedAssetPath = AssetDatabase.ToAbsolutePath(importedModel);
                    imported++;
                    continue;
                }
                if (extension == ".cs")
                {
                    string importedScript = AssetDatabase.ImportScript(source, _projectBrowserPath);
                    _selectedAssetPath = AssetDatabase.ToAbsolutePath(importedScript);
                    _editorScriptsReady = false;
                    imported++;
                    continue;
                }
                string destination = GetAvailableAssetPath(Path.Combine(_projectBrowserPath, Path.GetFileName(source)));
                File.Copy(source, destination, false);
                string metadata = source + ".r2import";
                if (File.Exists(metadata)) File.Copy(metadata, destination + ".r2import", false);
                imported++;
            }
            catch (Exception exception) { _scriptMessages.Add($"Could not import '{source}': {exception.GetBaseException().Message}"); }
        }
        if (imported > 0)
        {
            _thumbnailCache.Clear(); MaterialAssetCache.Clear(); MeshAssetCache.Clear();
            _statusMessage = $"Imported {imported} file(s) into {AssetDatabase.ToProjectRelativePath(_projectBrowserPath)}.";
        }
    }

    private static string GetAvailableAssetPath(string desired)
    {
        if (!File.Exists(desired) && !Directory.Exists(desired)) return desired;
        string directory = Path.GetDirectoryName(desired)!; string name = Path.GetFileNameWithoutExtension(desired); string extension = Path.GetExtension(desired);
        for (int i = 1; ; i++) { string candidate = Path.Combine(directory, $"{name} ({i}){extension}"); if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate; }
    }

    private static bool IsValidAssetFolderName(string name) => !string.IsNullOrWhiteSpace(name) &&
        name.Trim() is not "." and not ".." && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        !name.Contains(Path.DirectorySeparatorChar) && !name.Contains(Path.AltDirectorySeparatorChar);

    private void DrawAssetManagementPopups()
    {
        if (_openRenameAssetPopup)
        {
            ImGui.OpenPopup("Rename Asset");
            _openRenameAssetPopup = false;
        }

        if (ImGui.BeginPopupModal("Rename Asset"))
        {
            ImGui.InputText("Name", ref _assetRenameBuffer, 260);

            if (ImGui.Button("Rename") && _pendingRenameAssetPath != null)
            {
                RenameAsset(_pendingRenameAssetPath, _assetRenameBuffer);
                _pendingRenameAssetPath = null;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel"))
            {
                _pendingRenameAssetPath = null;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        if (_openDeleteAssetPopup)
        {
            ImGui.OpenPopup("Delete Asset");
            _openDeleteAssetPopup = false;
        }

        if (ImGui.BeginPopupModal("Delete Asset"))
        {
            ImGui.TextWrapped(
                $"Move {Path.GetFileName(_pendingDeleteAssetPath)} to the Recycle Bin?");

            if (ImGui.Button("Yes") && _pendingDeleteAssetPath != null)
            {
                DeleteAsset(_pendingDeleteAssetPath);
                _pendingDeleteAssetPath = null;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();

            if (ImGui.Button("No"))
            {
                _pendingDeleteAssetPath = null;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void RenameAsset(string oldPath, string requestedName)
    {
        string cleanName = requestedName.Trim();

        if (string.IsNullOrWhiteSpace(cleanName) ||
            cleanName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            _statusMessage = "Asset rename failed: invalid filename.";
            return;
        }

        bool isDirectory = Directory.Exists(oldPath);
        string extension = isDirectory ? "" : Path.GetExtension(oldPath);
        string newPath = Path.Combine(Path.GetDirectoryName(oldPath)!, cleanName + extension);

        if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            return;

        if (File.Exists(newPath) || Directory.Exists(newPath))
        {
            _statusMessage = "Asset rename failed: that name already exists.";
            return;
        }

        if (!MoveAssetAndRepairReferences(oldPath, newPath))
            return;

        if (!isDirectory && string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase))
        {
            string oldClassName = Path.GetFileNameWithoutExtension(oldPath);
            string source = File.ReadAllText(newPath);
            source = System.Text.RegularExpressions.Regex.Replace(
                source,
                $@"\bclass\s+{System.Text.RegularExpressions.Regex.Escape(oldClassName)}\b",
                $"class {cleanName}",
                System.Text.RegularExpressions.RegexOptions.None,
                TimeSpan.FromSeconds(1.0));
            File.WriteAllText(newPath, source);
        }

        _selectedAssetPath = newPath;
        _editorScriptsReady = false;
        ClearAssetCaches();
        _statusMessage = $"Renamed {(isDirectory ? "folder" : "asset")} to {Path.GetFileName(newPath)}";
    }

    private void BeginInlineAssetRename(string path)
    {
        _inlineRenameAssetPath = path;
        _inlineRenameBuffer = Directory.Exists(path)
            ? Path.GetFileName(path)
            : Path.GetFileNameWithoutExtension(path);
        _focusInlineRename = true;
    }

    private void DrawInlineAssetName(string path, float width)
    {
        if (_focusInlineRename)
        {
            ImGui.SetKeyboardFocusHere();
            _focusInlineRename = false;
        }

        ImGui.SetNextItemWidth(width);
        bool submit = ImGui.InputText(
            $"##InlineRename_{path}",
            ref _inlineRenameBuffer,
            260,
            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);

        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            _inlineRenameAssetPath = null;
            return;
        }

        if (submit || ImGui.IsItemDeactivatedAfterEdit())
        {
            string requestedName = _inlineRenameBuffer;
            _inlineRenameAssetPath = null;
            RenameAsset(path, requestedName);
        }
        else if (ImGui.IsItemDeactivated() && !ImGui.IsItemActive())
        {
            _inlineRenameAssetPath = null;
        }
    }

    private void DuplicateAsset(string path)
    {
        if (Directory.Exists(path))
        {
            string duplicateDirectory = GetAvailableAssetPath(path);
            CopyDirectory(path, duplicateDirectory);
            _selectedAssetPath = duplicateDirectory;
            ClearAssetCaches();
            _statusMessage = $"Duplicated {Path.GetFileName(path)}";
            return;
        }

        string directory = Path.GetDirectoryName(path)!;
        string name = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        int number = 1;
        string duplicatePath;

        do
        {
            duplicatePath = Path.Combine(directory, $"{name} ({number++}){extension}");
        }
        while (File.Exists(duplicatePath));

        File.Copy(
            path,
            duplicatePath);
        if (File.Exists(path + ".r2import"))
            File.Copy(path + ".r2import", duplicatePath + ".r2import");

        if (string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase))
        {
            string source = File.ReadAllText(duplicatePath);
            source = System.Text.RegularExpressions.Regex.Replace(
                source,
                $@"\bclass\s+{System.Text.RegularExpressions.Regex.Escape(name)}\b",
                $"class {Path.GetFileNameWithoutExtension(duplicatePath)}",
                System.Text.RegularExpressions.RegexOptions.None,
                TimeSpan.FromSeconds(1.0));
            File.WriteAllText(duplicatePath, source);
        }

        _selectedAssetPath = duplicatePath;
        _editorScriptsReady = false;
        ClearAssetCaches();
        _statusMessage = $"Duplicated {Path.GetFileName(path)}";
    }

    private void DeleteAsset(string path)
    {
        if (Directory.Exists(path))
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            if (string.Equals(_selectedAssetPath, path, StringComparison.OrdinalIgnoreCase) ||
                _selectedAssetPath?.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) == true)
                _selectedAssetPath = null;
            ClearAssetCaches();
            _statusMessage = $"Moved {Path.GetFileName(path)} to the Recycle Bin.";
            return;
        }

        string relativePath = AssetDatabase.ToProjectRelativePath(path);

        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
            path,
            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);

        string metadataPath = path + ".r2import";
        if (File.Exists(metadataPath))
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                metadataPath,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }

        int detachedReferences = DetachOpenSceneAssetReference(relativePath);

        if (string.Equals(_selectedAssetPath, path, StringComparison.OrdinalIgnoreCase))
            _selectedAssetPath = null;

        _editorScriptsReady = false;
        ClearAssetCaches();
        _statusMessage = $"Moved {Path.GetFileName(path)} to the Recycle Bin." +
            (detachedReferences > 0
                ? $" Cleared {detachedReferences} scene reference(s)."
                : "");
    }

    private int DetachOpenSceneAssetReference(string assetPath)
    {
        int detached = 0;
        foreach (GameObject gameObject in _scene.GameObjects.ToArray())
        {
            if (string.Equals(gameObject.PrefabPath, assetPath, StringComparison.OrdinalIgnoreCase))
            {
                gameObject.PrefabPath = null;
                gameObject.PrefabBaseline = null;
                detached++;
            }

            MeshRenderer? renderer = gameObject.GetComponent<MeshRenderer>();
            if (renderer != null &&
                string.Equals(renderer.MeshPath, assetPath, StringComparison.OrdinalIgnoreCase))
            {
                gameObject.RemoveComponent<MeshRenderer>();
                detached++;
                renderer = null;
            }

            if (renderer != null &&
                string.Equals(renderer.MaterialPath, assetPath, StringComparison.OrdinalIgnoreCase))
            {
                renderer.ClearMaterialAsset(preserveAppearance: false);
                detached++;
            }

            ScriptComponent? script = gameObject.GetComponent<ScriptComponent>();
            if (script != null && string.Equals(script.ScriptPath, assetPath, StringComparison.OrdinalIgnoreCase))
            {
                script.ScriptPath = "";
                script.FieldValues.Clear();
                detached++;
            }

            AudioSource? audio = gameObject.GetComponent<AudioSource>();
            if (audio != null && string.Equals(audio.ClipPath, assetPath, StringComparison.OrdinalIgnoreCase))
            {
                audio.Stop();
                audio.ClipPath = "";
                detached++;
            }

            Animator? animator = gameObject.GetComponent<Animator>();
            if (animator != null && string.Equals(animator.ClipPath, assetPath, StringComparison.OrdinalIgnoreCase))
            {
                animator.Stop();
                animator.ClipPath = "";
                detached++;
            }
            if(gameObject.GetComponent<UIText>() is {} uiText && string.Equals(uiText.FontPath,assetPath,StringComparison.OrdinalIgnoreCase))
            { uiText.FontPath=""; detached++; }
            if(gameObject.GetComponent<UIButton>() is {} fontButton && string.Equals(fontButton.FontPath,assetPath,StringComparison.OrdinalIgnoreCase))
            { fontButton.FontPath=""; detached++; }
        }

        return detached;
    }

    private void RevealAsset(string path)
    {
        System.Diagnostics.ProcessStartInfo startInfo =
            new("explorer.exe")
            {
                UseShellExecute = true
            };

        startInfo.ArgumentList.Add($"/select,{path}");
        System.Diagnostics.Process.Start(startInfo);
    }

    private void ClearAssetCaches()
    {
        _thumbnailCache.Clear();
        MaterialAssetCache.Clear();
        MeshAssetCache.Clear();
        _sceneRenderer.ClearAssetCaches();
        _gameRenderer.ClearAssetCaches();
        _cameraPreviewRenderer.ClearAssetCaches();
    }

    private bool MoveAssetAndRepairReferences(string sourcePath, string destinationPath)
    {
        try
        {
            string source = Path.GetFullPath(sourcePath);
            string destination = Path.GetFullPath(destinationPath);
            string assetsRoot = Path.GetFullPath(AssetDatabase.AssetsRoot);
            string scenesRoot = Path.GetFullPath(Path.Combine(AssetDatabase.ProjectRoot, "Scenes"));
            bool isDirectory = Directory.Exists(source);
            bool sourceInAssets = source.StartsWith(assetsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            bool sourceInScenes = source.StartsWith(scenesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            bool destinationInAssets = destination.StartsWith(assetsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            bool destinationInScenes = destination.StartsWith(scenesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

            if ((!isDirectory && !File.Exists(source)) ||
                (!sourceInAssets && !sourceInScenes))
                throw new InvalidOperationException("Only project assets and scenes can be moved.");
            if ((sourceInAssets && !destinationInAssets) || (sourceInScenes && !destinationInScenes))
                throw new InvalidOperationException("Scenes stay within Scenes and other assets stay within Assets.");
            if (isDirectory && destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A folder cannot be moved inside itself.");
            if (File.Exists(destination) || Directory.Exists(destination))
                throw new IOException($"'{Path.GetFileName(destination)}' already exists in that folder.");
            if (!isDirectory)
                foreach (string suffix in new[] { ".r2import", ".r2fbximport" })
                    if (File.Exists(source + suffix) && File.Exists(destination + suffix))
                        throw new IOException($"Import settings for '{Path.GetFileName(destination)}' already exist in that folder.");

            string oldRelative = AssetDatabase.ToProjectRelativePath(source);
            string newRelative = AssetDatabase.ToProjectRelativePath(destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            if (isDirectory)
                Directory.Move(source, destination);
            else
            {
                File.Move(source, destination);
                foreach (string suffix in new[] { ".r2import", ".r2fbximport" })
                    if (File.Exists(source + suffix))
                        File.Move(source + suffix, destination + suffix);
            }

            RewriteSavedAssetReferences(oldRelative, newRelative, source, destination, isDirectory);
            ReplaceOpenSceneAssetReference(oldRelative, newRelative, isDirectory);

            _selectedAssetPath = RemapMovedPath(_selectedAssetPath, source, destination, isDirectory);
            _inspectedAssetPath = RemapMovedPath(_inspectedAssetPath, source, destination, isDirectory);
            _animatorControllerPath = RemapMovedPath(_animatorControllerPath, source, destination, isDirectory) ?? _animatorControllerPath;
            _projectBrowserPath = RemapMovedPath(_projectBrowserPath, source, destination, isDirectory) ?? _projectBrowserPath;
            _editorScriptsReady = false;
            ClearAssetCaches();
            _statusMessage = $"Moved {Path.GetFileName(source)} to {AssetDatabase.ToProjectRelativePath(Path.GetDirectoryName(destination)!)} and updated references.";
            return true;
        }
        catch (Exception exception)
        {
            _statusMessage = $"Move failed: {exception.GetBaseException().Message}";
            _scriptMessages.Add(_statusMessage);
            return false;
        }
    }

    private static string? RemapMovedPath(string? value, string oldPath, string newPath, bool folder)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        if (string.Equals(value, oldPath, StringComparison.OrdinalIgnoreCase)) return newPath;
        if (folder && value.StartsWith(oldPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return newPath + value[oldPath.Length..];
        return value;
    }

    private static string RemapAssetReference(string value, string oldPath, string newPath, bool folder)
    {
        if (string.Equals(value, oldPath, StringComparison.OrdinalIgnoreCase)) return newPath;
        if (folder && (value.StartsWith(oldPath + "\\", StringComparison.OrdinalIgnoreCase) ||
                       value.StartsWith(oldPath + "/", StringComparison.OrdinalIgnoreCase)))
            return newPath + value[oldPath.Length..];
        return value;
    }

    private static void RewriteSavedAssetReferences(
        string oldRelative, string newRelative, string oldAbsolute, string newAbsolute, bool folder)
    {
        var candidates = new List<string>();
        if (Directory.Exists(AssetDatabase.AssetsRoot))
            candidates.AddRange(Directory.EnumerateFiles(AssetDatabase.AssetsRoot, "*", SearchOption.AllDirectories));
        string scenes = Path.Combine(AssetDatabase.ProjectRoot, "Scenes");
        if (Directory.Exists(scenes))
            candidates.AddRange(Directory.EnumerateFiles(scenes, "*", SearchOption.AllDirectories));
        string projectSettings = Path.Combine(AssetDatabase.ProjectRoot, "ProjectSettings.json");
        if (File.Exists(projectSettings)) candidates.Add(projectSettings);

        string[] textExtensions = { ".r2scene", ".r2prefab", ".r2mat", ".r2controller", ".r2anim", ".r2font", ".r2import", ".r2fbximport", ".json" };
        foreach (string file in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!textExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;
            string text;
            try { text = File.ReadAllText(file); }
            catch { continue; }
            string updated = ReplaceSerializedPath(text, oldRelative, newRelative, folder);
            updated = ReplaceSerializedPath(updated, oldAbsolute, newAbsolute, folder);
            if (!string.Equals(text, updated, StringComparison.Ordinal))
                File.WriteAllText(file, updated);
        }
    }

    private static string ReplaceSerializedPath(string text, string oldPath, string newPath, bool folder)
    {
        string oldForward = oldPath.Replace('\\', '/');
        string newForward = newPath.Replace('\\', '/');
        text = ReplacePathVariant(text, oldForward, newForward, "/", folder);
        for (int escapeDepth = 0; escapeDepth <= 3; escapeDepth++)
        {
            string slash = new('\\', 1 << escapeDepth);
            string oldVariant = oldPath.Replace("\\", slash).Replace("/", slash);
            string newVariant = newPath.Replace("\\", slash).Replace("/", slash);
            text = ReplacePathVariant(text, oldVariant, newVariant, slash, folder);
        }
        return text;
    }

    private static string ReplacePathVariant(string text, string oldPath, string newPath, string separator, bool folder)
    {
        if (folder)
            return text.Replace(oldPath + separator, newPath + separator, StringComparison.OrdinalIgnoreCase);
        return text.Replace(oldPath, newPath, StringComparison.OrdinalIgnoreCase);
    }

    private void QueueTextureInvalidation(string path)
    {
        _pendingTextureInvalidations.Add(Path.GetFullPath(path));
    }

    private void ProcessPendingTextureInvalidations()
    {
        foreach (string path in _pendingTextureInvalidations)
        {
            _thumbnailCache.Invalidate(path);
            _sceneRenderer.InvalidateTexture(path);
            _gameRenderer.InvalidateTexture(path);
            _cameraPreviewRenderer.InvalidateTexture(path);
        }
        _pendingTextureInvalidations.Clear();
    }

    private void ReplaceOpenSceneAssetReference(string oldPath, string newPath, bool folder = false)
    {
        foreach (GameObject gameObject in _scene.GameObjects)
        {
            if (gameObject.PrefabPath != null)
                gameObject.PrefabPath = RemapAssetReference(gameObject.PrefabPath, oldPath, newPath, folder);

            MeshRenderer? renderer = gameObject.GetComponent<MeshRenderer>();

            if (renderer != null)
            {
                if (renderer.MeshPath != null) renderer.MeshPath = RemapAssetReference(renderer.MeshPath, oldPath, newPath, folder);
                for (int slot = 0; slot < renderer.MaterialSlotCount; slot++)
                {
                    string? materialPath = renderer.GetMaterialPath(slot);
                    if (materialPath != null) renderer.SetMaterialPath(slot, RemapAssetReference(materialPath, oldPath, newPath, folder));
                }
                if (renderer.LocalMaterial.TexturePath != null)
                    renderer.LocalMaterial.TexturePath = RemapAssetReference(renderer.LocalMaterial.TexturePath, oldPath, newPath, folder);
            }

            ScriptComponent? script = gameObject.GetComponent<ScriptComponent>();

            if (script != null)
            {
                script.ScriptPath = RemapAssetReference(script.ScriptPath, oldPath, newPath, folder);
                foreach (string fieldName in script.FieldValues.Keys.ToArray())
                    script.FieldValues[fieldName] = ReplaceSerializedPath(script.FieldValues[fieldName], oldPath, newPath, folder);
            }
            if (gameObject.GetComponent<AudioSource>() is { } audio)
                audio.ClipPath = RemapAssetReference(audio.ClipPath, oldPath, newPath, folder);
            if (gameObject.GetComponent<Animator>() is { } animator)
            {
                animator.ClipPath = RemapAssetReference(animator.ClipPath, oldPath, newPath, folder);
                animator.ControllerPath = RemapAssetReference(animator.ControllerPath, oldPath, newPath, folder);
                animator.SkeletalAnimationPath = RemapAssetReference(animator.SkeletalAnimationPath, oldPath, newPath, folder);
            }
            if(gameObject.GetComponent<UIText>() is {} uiText)
                uiText.FontPath=RemapAssetReference(uiText.FontPath,oldPath,newPath,folder);
            if(gameObject.GetComponent<UIButton>() is {} fontButton)
            {
                fontButton.FontPath=RemapAssetReference(fontButton.FontPath,oldPath,newPath,folder);
                fontButton.NormalSprite=RemapAssetReference(fontButton.NormalSprite,oldPath,newPath,folder);
                fontButton.HoverSprite=RemapAssetReference(fontButton.HoverSprite,oldPath,newPath,folder);
                fontButton.PressedSprite=RemapAssetReference(fontButton.PressedSprite,oldPath,newPath,folder);
            }
            if(gameObject.GetComponent<UIImage>() is {} uiImage)
                uiImage.TexturePath=RemapAssetReference(uiImage.TexturePath,oldPath,newPath,folder);
            if(gameObject.GetComponent<Interactable>() is {} interaction)
                interaction.PromptFontPath=RemapAssetReference(interaction.PromptFontPath,oldPath,newPath,folder);
        }
    }

    private void CreateScriptAsset(string? destinationDirectory = null)
    {
        string scriptsDirectory =
            destinationDirectory ?? Path.Combine(AssetDatabase.AssetsRoot, "Scripts");

        Directory.CreateDirectory(scriptsDirectory);

        string className = "NewScript";
        string path = Path.Combine(scriptsDirectory, className + ".cs");
        int suffix = 1;

        while (File.Exists(path))
        {
            className = $"NewScript{suffix++}";
            path = Path.Combine(scriptsDirectory, className + ".cs");
        }

        string source =
            "using R2Engine.Editor.Scene;\n\n" +
            $"public class {className} : ScriptBehaviour\n" +
            "{\n" +
            "    public override void Start()\n" +
            "    {\n" +
            "    }\n\n" +
            "    public override void Update(float deltaTime)\n" +
            "    {\n" +
            "        // Example: Transform.Rotation.Y += 90.0f * deltaTime;\n" +
            "    }\n" +
            "}\n";

        File.WriteAllText(path, source);

        _projectBrowserPath = scriptsDirectory;
        _selectedAssetPath = path;
        _editorScriptsReady = false;
        _statusMessage = $"Created {Path.GetFileName(path)}";
    }

    private void CreateMaterialAsset(string? destinationDirectory = null)
    {
        string directory = destinationDirectory ?? AssetDatabase.MaterialsDirectory;
        Directory.CreateDirectory(directory);
        string absolutePath = GetAvailableAssetPath(Path.Combine(directory, "New Material.r2mat"));
        MaterialSerializer.Save(new Material(Path.GetFileNameWithoutExtension(absolutePath)), absolutePath);
        _projectBrowserPath = directory;
        _selectedAssetPath = absolutePath;
        MaterialAssetCache.Clear();
        _statusMessage = $"Created {Path.GetFileName(absolutePath)}";
    }

    private void CreateBlankAnimationAsset(string? destinationDirectory = null)
    {
        string directory = destinationDirectory ?? AssetDatabase.AnimationsDirectory;
        Directory.CreateDirectory(directory);
        string path = GetAvailableAssetPath(Path.Combine(directory, "New Animation.r2anim"));

        new AnimationClip
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Duration = 1.0f,
            Keyframes =
            {
                new AnimationKeyframe
                {
                    Time = 0.0f,
                    Position = Vector3.Zero,
                    Rotation = Vector3.Zero,
                    Scale = Vector3.One
                }
            }
        }.Save(path);

        _projectBrowserPath = directory;
        _selectedAssetPath = path;
        _statusMessage = $"Created {Path.GetFileName(path)}";
    }

    private void CreateAnimatorControllerAsset(string? destinationDirectory = null)
    {
        string directory = destinationDirectory ?? AssetDatabase.AnimationsDirectory;
        Directory.CreateDirectory(directory);
        string path = GetAvailableAssetPath(Path.Combine(directory, "New Animator Controller.r2controller"));
        new AnimatorController { Name = Path.GetFileNameWithoutExtension(path) }.Save(path);
        _projectBrowserPath = directory;
        _selectedAssetPath = path;
        _animatorControllerPath = path;
        _showAnimatorControllerWindow = true;
        _statusMessage = $"Created {Path.GetFileName(path)}";
    }

    private void DrawAnimatorControllerWindow()
    {
        if (!_showAnimatorControllerWindow || _animatorControllerPath == null || !File.Exists(_animatorControllerPath)) return;
        bool open = _showAnimatorControllerWindow;
        if (!ImGui.Begin("Animator Controller", ref open))
        {
            ImGui.End(); _showAnimatorControllerWindow = open; return;
        }
        _showAnimatorControllerWindow = open;
        AnimatorController controller = AnimatorController.Load(_animatorControllerPath);
        bool changed = false;
        if (ImGui.BeginTabBar("AnimatorTabs"))
        {
            if (ImGui.TabItemButton("   ##InspectController", ImGuiTabItemFlags.Trailing))
                _inspectAnimatorController = true;
            var iconDraw = ImGui.GetWindowDrawList();
            Vector2 center = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) * .5f;
            uint color = ImGui.GetColorU32(ImGuiCol.Text);
            iconDraw.AddCircle(center, 6, color, 16, 1.4f);
            iconDraw.AddCircleFilled(center + new Vector2(0,-3), 1, color);
            iconDraw.AddLine(center + new Vector2(0,0), center + new Vector2(0,3), color, 1.5f);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Inspect Controller");
            if (ImGui.BeginTabItem("States"))
            {
                changed |= DrawAnimatorStateGraph(controller);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Parameters"))
            {
                changed |= DrawAnimatorParameters(controller);
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
        if (changed) controller.Save(_animatorControllerPath);
        ImGui.End();
    }

    private bool DrawAnimatorStateGraph(AnimatorController controller)
    {
        bool changed = false;
        ImGui.TextDisabled("Right-click to add • Middle-drag to pan • Mouse wheel to zoom");
        Vector2 canvas = ImGui.GetCursorScreenPos();
        Vector2 available = ImGui.GetContentRegionAvail();
        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(canvas, canvas + available, ImGui.GetColorU32(new Vector4(0.015f, 0.025f, 0.05f, 1)), 4);
        ImGui.InvisibleButton("##AnimatorGraphCanvas", available,
            ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight | ImGuiButtonFlags.MouseButtonMiddle);
        bool canvasHovered = ImGui.IsItemHovered();
        Vector2 mouse = ImGui.GetMousePos();
        if (canvasHovered && ImGui.IsMouseDragging(ImGuiMouseButton.Middle))
            _animatorGraphPan += ImGui.GetIO().MouseDelta;
        if (canvasHovered && MathF.Abs(ImGui.GetIO().MouseWheel) > 0.0001f)
        {
            float oldZoom = _animatorGraphZoom;
            float newZoom = Math.Clamp(oldZoom * MathF.Pow(1.15f, ImGui.GetIO().MouseWheel), 0.35f, 2.5f);
            Vector2 mouseInCanvas = mouse - canvas;
            Vector2 graphUnderMouse = (mouseInCanvas - _animatorGraphPan) / oldZoom;
            _animatorGraphPan = mouseInCanvas - graphUnderMouse * newZoom;
            _animatorGraphZoom = newZoom;
        }
        Vector2 nodeSize = new(140.0f, 44.0f);
        Vector2 scaledNodeSize = nodeSize * _animatorGraphZoom;
        draw.PushClipRect(canvas, canvas + available, true);
        foreach (AnimatorTransition transition in controller.Transitions)
        {
            AnimatorState? from = controller.States.FirstOrDefault(s => s.Name == transition.From);
            AnimatorState? to = controller.States.FirstOrDefault(s => s.Name == transition.To);
            if (from != null && to != null)
            {
                Vector2 a = canvas + _animatorGraphPan + (from.Position + nodeSize * 0.5f) * _animatorGraphZoom;
                Vector2 b = canvas + _animatorGraphPan + (to.Position + nodeSize * 0.5f) * _animatorGraphZoom;
                draw.AddLine(a, b, ImGui.GetColorU32(new Vector4(0.2f, 0.7f, 1, 1)), 2);
                Vector2 delta = b - a;
                float lengthSquared = delta.LengthSquared();
                Vector2 nearest = a + delta * Math.Clamp(Vector2.Dot(ImGui.GetMousePos() - a, delta) / Math.Max(1, lengthSquared), 0, 1);
                if (canvasHovered && Vector2.DistanceSquared(mouse, nearest) <= 36)
                {
                    ImGui.SetTooltip($"{transition.From} -> {transition.To}: click to edit in Inspector");
                    if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    {
                        _selectedAnimatorState = controller.States.IndexOf(from);
                        _inspectAnimatorController = true;
                    }
                }
            }
        }

        for (int index = 0; index < controller.States.Count; index++)
        {
            AnimatorState state = controller.States[index];
            ImGui.SetCursorScreenPos(canvas + _animatorGraphPan + state.Position * _animatorGraphZoom);
            bool isDefault = state.Name == controller.DefaultState;
            if (isDefault) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.08f, 0.55f, 0.35f, 1));
            if (ImGui.Button($"{state.Name}##state{index}", scaledNodeSize))
            {
                if (_transitionSourceState >= 0 && _transitionSourceState != index)
                {
                    controller.Transitions.Add(new AnimatorTransition
                    {
                        From = controller.States[_transitionSourceState].Name,
                        To = state.Name,
                        Condition = AnimatorCondition.Finished
                    });
                    _transitionSourceState = -1;
                    changed = true;
                }
                _selectedAnimatorState = index;
                _inspectAnimatorController = true;
            }
            if (isDefault) ImGui.PopStyleColor();
            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                state.Position += ImGui.GetIO().MouseDelta / _animatorGraphZoom;
                changed = true;
            }
        }
        draw.PopClipRect();

        if (canvasHovered &&
            mouse.X >= canvas.X && mouse.Y >= canvas.Y &&
            mouse.X < canvas.X + available.X && mouse.Y < canvas.Y + available.Y &&
            ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            _animatorNewStatePosition = (mouse - canvas - _animatorGraphPan) / _animatorGraphZoom;
            ImGui.OpenPopup("AnimatorGraphContext");
        }
        if (ImGui.BeginPopup("AnimatorGraphContext"))
        {
            if (ImGui.MenuItem("Add State"))
            {
                int suffix = controller.States.Count + 1;
                string name;
                do { name = $"State {suffix++}"; } while (controller.States.Any(s => s.Name == name));
                // MenuItem closes the popup: do not query its opening-position stack here.
                controller.States.Add(new AnimatorState { Name = name, Position = _animatorNewStatePosition });
                _selectedAnimatorState = controller.States.Count - 1;
                _inspectAnimatorController = true;
                if (controller.States.Count == 1) controller.DefaultState = name;
                changed = true;
            }
            ImGui.EndPopup();
        }
        ImGui.SetCursorScreenPos(canvas + available);
        return changed;
    }

    private bool DrawAnimatorParameters(AnimatorController controller)
    {
        bool changed = false;
        if (ImGui.Button("Add Parameter")) ImGui.OpenPopup("AddAnimatorParameter");
        if (ImGui.BeginPopup("AddAnimatorParameter"))
        {
            foreach (AnimatorParameterType parameterType in Enum.GetValues<AnimatorParameterType>())
            {
                if (ImGui.MenuItem(parameterType.ToString()))
                {
                    controller.Parameters.Add(new AnimatorParameter
                    {
                        Name = $"{parameterType} {controller.Parameters.Count + 1}",
                        Type = parameterType
                    });
                    _inspectAnimatorController = true;
                    changed = true;
                }
            }
            ImGui.EndPopup();
        }

        ImGui.TextDisabled("Parameter defaults — changes save automatically.");
        ImGui.Separator();

            int deleteParameter = -1;
            for (int parameterIndex = 0; parameterIndex < controller.Parameters.Count; parameterIndex++)
            {
                AnimatorParameter parameter = controller.Parameters[parameterIndex];
                ImGui.PushID($"AnimatorParameter{parameterIndex}");
                string parameterName = parameter.Name;
                ImGui.SetNextItemWidth(180.0f);
                if (ImGui.InputText("##Name", ref parameterName, 80) && !string.IsNullOrWhiteSpace(parameterName))
                {
                    string oldName = parameter.Name;
                    parameter.Name = parameterName;
                    foreach (AnimatorTransition transition in controller.Transitions.Where(item => item.Parameter == oldName))
                        transition.Parameter = parameterName;
                    changed = true;
                }
                ImGui.SameLine();
                int parameterType = (int)parameter.Type;
                ImGui.SetNextItemWidth(90.0f);
                if (ImGui.Combo("##Type", ref parameterType, Enum.GetNames<AnimatorParameterType>(), Enum.GetNames<AnimatorParameterType>().Length))
                {
                    parameter.Type = (AnimatorParameterType)parameterType;
                    changed = true;
                }
                ImGui.SameLine();
                switch (parameter.Type)
                {
                    case AnimatorParameterType.Float:
                        ImGui.SetNextItemWidth(100.0f);
                        float floatDefault = parameter.FloatValue;
                        if (ImGui.DragFloat("##Default", ref floatDefault, 0.01f))
                        {
                            parameter.FloatValue = floatDefault;
                            changed = true;
                        }
                        break;
                    case AnimatorParameterType.Integer:
                        ImGui.SetNextItemWidth(100.0f);
                        int integerDefault = parameter.IntegerValue;
                        if (ImGui.DragInt("##Default", ref integerDefault))
                        {
                            parameter.IntegerValue = integerDefault;
                            changed = true;
                        }
                        break;
                    case AnimatorParameterType.Boolean:
                        bool boolDefault = parameter.BoolValue;
                        if (ImGui.Checkbox("Default", ref boolDefault))
                        {
                            parameter.BoolValue = boolDefault;
                            changed = true;
                        }
                        break;
                    case AnimatorParameterType.Trigger:
                        ImGui.TextDisabled("One-shot");
                        break;
                }
                ImGui.SameLine();
                if (ImGui.SmallButton("X")) deleteParameter = parameterIndex;
                ImGui.PopID();
            }
            if (controller.Parameters.Count == 0)
                ImGui.TextDisabled("No parameters. Add one to drive transitions from scripts or components.");
            if (deleteParameter >= 0)
            {
                string deletedParameter = controller.Parameters[deleteParameter].Name;
                controller.Parameters.RemoveAt(deleteParameter);
                foreach (AnimatorTransition transition in controller.Transitions.Where(item => item.Parameter == deletedParameter))
                {
                    transition.Parameter = "";
                    transition.Condition = AnimatorCondition.Finished;
                }
                changed = true;
            }
        
        return changed;
    }

    private void DrawAnimatorSelectionInspector()
    {
        ImGui.TextUnformatted(Path.GetFileName(_animatorControllerPath));
        if (ImGui.Button("Back To Object Inspector")) { _inspectAnimatorController = false; _inspectedAssetPath = null; return; }
        ImGui.TextDisabled("Controller edits save automatically.");
        AnimatorController controller = AnimatorController.Load(_animatorControllerPath!);
        bool changed = false;

        string skeletonLabel = string.IsNullOrWhiteSpace(controller.SkeletonPath)
            ? "Target Skeleton: drop a .r2skel here"
            : $"Target Skeleton: {Path.GetFileName(controller.SkeletonPath)}";
        ImGui.Button($"{skeletonLabel}##ControllerSkeleton", new Vector2(Math.Max(260.0f, ImGui.GetContentRegionAvail().X), 0));
        string? droppedSkeleton = AcceptAssetDrop(".r2skel");
        if (droppedSkeleton != null)
        {
            controller.SkeletonPath = AssetDatabase.ToProjectRelativePath(droppedSkeleton);
            changed = true;
        }

        SkeletalAsset? controllerSkeleton = null;
        if (!string.IsNullOrWhiteSpace(controller.SkeletonPath))
        {
            string skeletonAbsolutePath = AssetDatabase.ToAbsolutePath(controller.SkeletonPath);
            if (File.Exists(skeletonAbsolutePath))
                controllerSkeleton = MeshAssetCache.GetSkeletalAsset(controller.SkeletonPath);
        }
        ImGui.Separator();

        bool deleteSelectedState = false;
        if (_selectedAnimatorState >= 0 && _selectedAnimatorState < controller.States.Count)
        {
            AnimatorState state = controller.States[_selectedAnimatorState];
            ImGui.BeginGroup();
            ImGui.Text("Selected State");
            ImGui.TextDisabled("State Name");
            string name = state.Name;
            ImGui.SetNextItemWidth(250.0f);
            if (ImGui.InputText("##StateName", ref name, 80) && !string.IsNullOrWhiteSpace(name))
            {
                string oldName = state.Name;
                state.Name = name;
                if (controller.DefaultState == oldName) controller.DefaultState = name;
                foreach (AnimatorTransition item in controller.Transitions)
                {
                    if (item.From == oldName) item.From = name;
                    if (item.To == oldName) item.To = name;
                }
                changed = true;
            }
            ImGui.TextDisabled("Animation Take");
            string animationAssetLabel = string.IsNullOrWhiteSpace(state.AnimationPath)
                ? "Embedded in target skeleton - or drop .r2sanim"
                : Path.GetFileName(state.AnimationPath);
            ImGui.Button($"{animationAssetLabel}##StateAnimationAsset", new Vector2(250.0f, 0));
            string? droppedAnimation = AcceptAssetDrop(".r2sanim");
            if (droppedAnimation != null)
            {
                state.AnimationPath = AssetDatabase.ToProjectRelativePath(droppedAnimation);
                HumanoidAnimationAsset droppedAsset = HumanoidAnimationAsset.Load(droppedAnimation);
                state.AnimationTake = droppedAsset.Clips.FirstOrDefault()?.Name ?? "";
                changed = true;
            }

            List<SkeletalAnimationClip> availableTakes = controllerSkeleton?.Animations ?? new();
            HumanoidAnimationAsset? stateAnimationAsset = null;
            string? stateAnimationAbsolute = null;
            if (!string.IsNullOrWhiteSpace(state.AnimationPath))
            {
                stateAnimationAbsolute = AssetDatabase.ToAbsolutePath(state.AnimationPath);
                if (File.Exists(stateAnimationAbsolute))
                {
                    stateAnimationAsset = HumanoidAnimationAsset.Load(stateAnimationAbsolute);
                    availableTakes = stateAnimationAsset.Clips;
                }
            }
            ImGui.SetNextItemWidth(250.0f);
            string takeLabel = string.IsNullOrWhiteSpace(state.AnimationTake) ? "None" : state.AnimationTake;
            if (ImGui.BeginCombo("##StateAnimationTake", takeLabel))
            {
                if (availableTakes.Count == 0)
                {
                    ImGui.TextDisabled("No animation takes found.");
                }
                else
                {
                    foreach (SkeletalAnimationClip animationTake in availableTakes)
                    {
                        bool takeSelected = state.AnimationTake == animationTake.Name;
                        if (ImGui.Selectable($"{animationTake.Name} ({animationTake.Duration:0.00}s)", takeSelected))
                        {
                            state.AnimationTake = animationTake.Name;
                            changed = true;
                        }
                        if (takeSelected) ImGui.SetItemDefaultFocus();
                    }
                }
                ImGui.EndCombo();
            }
            SkeletalAnimationClip? selectedTake = availableTakes.FirstOrDefault(item => item.Name == state.AnimationTake)
                ?? availableTakes.FirstOrDefault();
            if (stateAnimationAsset != null && stateAnimationAbsolute != null && selectedTake != null)
            {
                bool applyRootMotion = selectedTake.ApplyRootMotion;
                if (ImGui.Checkbox("Apply Root Motion", ref applyRootMotion))
                {
                    selectedTake.ApplyRootMotion = applyRootMotion;
                    stateAnimationAsset.Save(stateAnimationAbsolute);
                    _statusMessage = $"Updated root motion for {Path.GetFileName(stateAnimationAbsolute)}.";
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("This belongs to the animation clip, so every state using this clip shares the setting.");
            }
            bool loop = state.Loop;
            if (ImGui.Checkbox("Loop", ref loop)) { state.Loop = loop; changed = true; }
            bool reverse = state.Reverse;
            if (ImGui.Checkbox("Play Backwards", ref reverse)) { state.Reverse = reverse; changed = true; }
            bool lockMovement = state.LockMovement;
            if (ImGui.Checkbox("Lock Movement", ref lockMovement)) { state.LockMovement = lockMovement; changed = true; }
            ImGui.TextDisabled("Playback Speed");
            float stateSpeed = state.Speed;
            ImGui.SetNextItemWidth(250.0f);
            if (ImGui.DragFloat("##StateSpeed", ref stateSpeed, 0.01f, -10, 10)) { state.Speed = stateSpeed; changed = true; }
            if (ImGui.Button("Set Default")) { controller.DefaultState = state.Name; changed = true; }
            ImGui.SameLine();
            if (ImGui.Button("Connect")) _transitionSourceState = _selectedAnimatorState;
            ImGui.SameLine();
            if (ImGui.Button("Delete State")) deleteSelectedState = true;

            List<AnimatorTransition> stateTransitions = controller.Transitions
                .Where(item => item.From == state.Name)
                .ToList();
            ImGui.SeparatorText("Outgoing Transitions");
            if (stateTransitions.Count == 0)
                ImGui.TextWrapped("No outgoing transitions. Click Connect, then the destination state in the graph. Blend Duration belongs to each transition.");
            AnimatorTransition? transitionToDelete = null;
            for (int transitionIndex = 0; transitionIndex < stateTransitions.Count; transitionIndex++)
            {
                AnimatorTransition transition = stateTransitions[transitionIndex];
                ImGui.PushID(transitionIndex);
                ImGui.Separator();
                ImGui.Text($"To: {transition.To}");
                string parameterLabel = string.IsNullOrWhiteSpace(transition.Parameter)
                    ? transition.Condition == AnimatorCondition.RandomFinished ? "Animation Finished (Chance)" : "Animation Finished"
                    : transition.Parameter;
                if (ImGui.BeginCombo("Parameter", parameterLabel))
                {
                    if (ImGui.Selectable("Animation Finished", transition.Condition == AnimatorCondition.Finished))
                    {
                        transition.Parameter = "";
                        transition.Condition = AnimatorCondition.Finished;
                        changed = true;
                    }
                    if (ImGui.Selectable("Animation Finished (Chance)", transition.Condition == AnimatorCondition.RandomFinished))
                    {
                        transition.Parameter = "";
                        transition.Condition = AnimatorCondition.RandomFinished;
                        transition.Threshold = 0.2f;
                        changed = true;
                    }
                    foreach (AnimatorParameter candidate in controller.Parameters)
                    {
                        if (ImGui.Selectable(candidate.Name, candidate.Name == transition.Parameter))
                        {
                            transition.Parameter = candidate.Name;
                            transition.Condition = candidate.Type switch
                            {
                                AnimatorParameterType.Boolean => AnimatorCondition.IsTrue,
                                AnimatorParameterType.Trigger => AnimatorCondition.Triggered,
                                _ => AnimatorCondition.Greater
                            };
                            changed = true;
                        }
                    }
                    ImGui.EndCombo();
                }
                AnimatorParameter? selectedParameter = controller.Parameters.FirstOrDefault(item => item.Name == transition.Parameter);
                AnimatorCondition[] allowedConditions = selectedParameter?.Type switch
                {
                    AnimatorParameterType.Boolean => new[] { AnimatorCondition.IsTrue, AnimatorCondition.IsFalse },
                    AnimatorParameterType.Trigger => new[] { AnimatorCondition.Triggered },
                    AnimatorParameterType.Float or AnimatorParameterType.Integer => new[] { AnimatorCondition.Greater, AnimatorCondition.Less, AnimatorCondition.Equals },
                    _ => new[] { AnimatorCondition.Finished, AnimatorCondition.RandomFinished }
                };
                int conditionIndex = Math.Max(0, Array.IndexOf(allowedConditions, transition.Condition));
                string[] conditionNames = allowedConditions.Select(item => item.ToString()).ToArray();
                if (ImGui.Combo("Condition", ref conditionIndex, conditionNames, conditionNames.Length))
                {
                    transition.Condition = allowedConditions[conditionIndex];
                    changed = true;
                }
                if (transition.Condition is AnimatorCondition.Greater or AnimatorCondition.Less or AnimatorCondition.Equals)
                {
                    float threshold = transition.Threshold;
                    if (ImGui.DragFloat("Threshold", ref threshold, 0.01f)) { transition.Threshold = threshold; changed = true; }
                }
                else if (transition.Condition == AnimatorCondition.RandomFinished)
                {
                    float chance = Math.Clamp(transition.Threshold * 100.0f, 0.0f, 100.0f);
                    if (ImGui.DragFloat("Chance", ref chance, 1.0f, 0.0f, 100.0f, "%.0f%%"))
                    {
                        transition.Threshold = Math.Clamp(chance / 100.0f, 0.0f, 1.0f);
                        changed = true;
                    }
                }
                float blendDuration = transition.BlendDuration;
                if (ImGui.DragFloat("Blend Duration", ref blendDuration, 0.01f, 0.0f, 2.0f, "%.2f s"))
                {
                    transition.BlendDuration = Math.Max(0.0f, blendDuration);
                    changed = true;
                }
                if (ImGui.Button("Delete Transition")) transitionToDelete = transition;
                ImGui.PopID();
            }
            if (transitionToDelete != null)
            {
                controller.Transitions.Remove(transitionToDelete);
                changed = true;
            }
            ImGui.EndGroup();
        }

        if (deleteSelectedState && _selectedAnimatorState >= 0 && _selectedAnimatorState < controller.States.Count)
        {
            string deletedName = controller.States[_selectedAnimatorState].Name;
            controller.States.RemoveAt(_selectedAnimatorState);
            controller.Transitions.RemoveAll(transition =>
                transition.From == deletedName || transition.To == deletedName);
            if (controller.DefaultState == deletedName)
                controller.DefaultState = controller.States.FirstOrDefault()?.Name ?? "";
            _selectedAnimatorState = -1;
            _transitionSourceState = -1;
            changed = true;
        }

        if (_selectedAnimatorState < 0 || _selectedAnimatorState >= controller.States.Count)
            ImGui.TextWrapped("Select a state or a transition in the Animator Controller graph.");
        if (changed) controller.Save(_animatorControllerPath!);
    }

    private static string AssetFolderCategory(string path)
    {
        string full = Path.GetFullPath(path);
        bool In(string root) => full.Equals(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase) ||
            full.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        // Humanoid Animations is nested under Models, so test the more specific root first.
        if (In(AssetDatabase.HumanoidAnimationsDirectory)) return "humanoid animations";
        if (In(AssetDatabase.ModelsDirectory)) return "models";
        if (In(AssetDatabase.TexturesDirectory)) return "textures";
        if (In(AssetDatabase.MaterialsDirectory)) return "materials";
        if (In(AssetDatabase.AudioDirectory)) return "audio";
        if (In(AssetDatabase.AnimationsDirectory)) return "animations";
        if (In(AssetDatabase.FontsDirectory)) return "fonts";
        if (In(Path.Combine(AssetDatabase.AssetsRoot, "Scripts"))) return "scripts";
        return "";
    }

    private void ImportAssetForCurrentFolder(string folder, string destinationDirectory)
    {
        string? source = folder.ToLowerInvariant() switch
        {
            "audio" => WindowsFileDialog.OpenAudio(),
            "scripts" => WindowsFileDialog.OpenScript(),
            "textures" => WindowsFileDialog.OpenTexture(),
            "fonts" => WindowsFileDialog.OpenFont(),
            _ => null
        };
        if (source == null)
            return;

        try
        {
            string imported = folder.ToLowerInvariant() switch
            {
                "audio" => AssetDatabase.ImportAudio(source, destinationDirectory),
                "scripts" => AssetDatabase.ImportScript(source, destinationDirectory),
                "textures" => AssetDatabase.ImportTexture(source, destinationDirectory),
                "fonts" => FontAssetImporter.Import(source, destinationDirectory),
                _ => source
            };
            _selectedAssetPath = AssetDatabase.ToAbsolutePath(imported);
            _thumbnailCache.Clear();
            MeshAssetCache.Clear();
            if (string.Equals(folder, "scripts", StringComparison.OrdinalIgnoreCase))
                _editorScriptsReady = false;
            _statusMessage = string.Equals(folder, "models", StringComparison.OrdinalIgnoreCase)
                ? DescribeImportedModel(imported)
                : $"Imported {Path.GetFileName(imported)}";
        }
        catch (Exception exception)
        {
            _statusMessage = $"{folder.TrimEnd('s')} import failed: {exception.Message}";
        }
    }

    private void ImportModelAsset(string format, string? destinationDirectory = null)
    {
        string? source = WindowsFileDialog.OpenModelFormat(format);
        if (source == null)
            return;

        try
        {
            string imported = AssetDatabase.ImportModel(source, destinationDirectory);
            _selectedAssetPath = AssetDatabase.ToAbsolutePath(imported);
            _projectBrowserPath = destinationDirectory ?? AssetDatabase.ModelsDirectory;
            _thumbnailCache.Clear();
            MeshAssetCache.Clear();
            _statusMessage = DescribeImportedModel(imported);
        }
        catch (Exception exception)
        {
            _statusMessage = $"{format} import failed: {exception.Message}";
            _scriptMessages.Add(_statusMessage);
        }
    }

    private void ImportHumanoidAnimationAsset(string? destinationDirectory = null)
    {
        string? source = WindowsFileDialog.OpenModelFormat("FBX");
        if (source == null) return;
        try
        {
            string imported = AssetDatabase.ImportHumanoidAnimation(source, destinationDirectory);
            _selectedAssetPath = AssetDatabase.ToAbsolutePath(imported);
            _projectBrowserPath = destinationDirectory ?? AssetDatabase.HumanoidAnimationsDirectory;
            HumanoidAnimationAsset asset = HumanoidAnimationAsset.Load(_selectedAssetPath);
            _statusMessage = $"Imported humanoid animation: {asset.Clips.Count} take(s), {asset.BoneNames.Count} animated bones.";
        }
        catch (Exception exception)
        {
            _statusMessage = $"Animation FBX import failed: {exception.Message}";
            _scriptMessages.Add(_statusMessage);
        }
    }

    private void DrawSkeletonConfigurationWindow()
    {
        if (!_showSkeletonConfigurationWindow)
            return;

        ImGui.SetNextWindowSize(new Vector2(620.0f, 680.0f), ImGuiCond.FirstUseEver);
        bool open = _showSkeletonConfigurationWindow;
        if (!ImGui.Begin("Skeleton Configuration", ref open))
        {
            ImGui.End();
            _showSkeletonConfigurationWindow = open;
            return;
        }

        if (_skeletonConfigurationPath == null || !File.Exists(_skeletonConfigurationPath))
        {
            ImGui.TextDisabled("Select an .r2skel asset and click Configure Skeleton.");
        }
        else
        {
            ImGui.TextUnformatted(Path.GetFileNameWithoutExtension(_skeletonConfigurationPath));
            ImGui.TextDisabled(AssetDatabase.ToProjectRelativePath(_skeletonConfigurationPath));
            DrawSkeletonConfiguration(_skeletonConfigurationPath);
        }

        ImGui.End();
        _showSkeletonConfigurationWindow = open;
    }

    private void DrawImportSettingsInspector(string path)
    {
        if (!File.Exists(path))
        {
            ImGui.TextDisabled("Select an imported skeletal model or humanoid animation.");
            return;
        }

        string extension = Path.GetExtension(path).ToLowerInvariant();

        try
        {
            if (extension == ".r2skel")
            {
                SkeletalAsset asset = SkeletalAsset.Load(path);
                ModelImportSettings settings = asset.ImportSettings ??= new ModelImportSettings();
                bool settingsChanged = false;
                ImGui.TextDisabled("Original Source");
                ImGui.TextWrapped(string.IsNullOrWhiteSpace(settings.SourcePath) ? "Not linked (choose the original file)." : settings.SourcePath);
                if (ImGui.Button("Choose Source..."))
                {
                    string format = string.IsNullOrWhiteSpace(settings.SourcePath)
                        ? "FBX"
                        : Path.GetExtension(settings.SourcePath).TrimStart('.').ToUpperInvariant();
                    string? source = WindowsFileDialog.OpenModelFormat(format);
                    if (source != null) { settings.SourcePath = Path.GetFullPath(source); settingsChanged = true; }
                }
                float scale = settings.Scale;
                if (ImGui.DragFloat("Scale", ref scale, 0.01f, 0.0001f, 1000.0f)) { settings.Scale = Math.Max(0.0001f, scale); settingsChanged = true; }
                Vector3 rotation = settings.Rotation;
                if (ImGui.DragFloat3("Rotation", ref rotation, 0.5f, -360.0f, 360.0f)) { settings.Rotation = rotation; settingsChanged = true; }
                bool autoOrientation = settings.AutoCorrectOrientation;
                if (ImGui.Checkbox("Auto-correct upside-down characters", ref autoOrientation)) { settings.AutoCorrectOrientation = autoOrientation; settingsChanged = true; }
                int skeletonLimit = settings.SkeletonLimit;
                if (ImGui.DragInt("Skeleton Safety Limit", ref skeletonLimit, 1.0f, 1, 1024)) { settings.SkeletonLimit = Math.Max(1, skeletonLimit); settingsChanged = true; }
                if (settingsChanged) asset.Save(path);
                ImGui.Separator();
                if (ImGui.Button("Reimport Model"))
                    ReimportSelectedAsset(path, false);
            }
            else if (extension == ".r2sanim")
            {
                HumanoidAnimationAsset asset = HumanoidAnimationAsset.Load(path);
                AnimationImportSettings settings = asset.ImportSettings ??= new AnimationImportSettings();
                bool settingsChanged = false;
                ImGui.TextDisabled("Original Source");
                ImGui.TextWrapped(string.IsNullOrWhiteSpace(settings.SourcePath) ? "Not linked (choose the original FBX)." : settings.SourcePath);
                if (ImGui.Button("Choose Source..."))
                {
                    string? source = WindowsFileDialog.OpenModelFormat("FBX");
                    if (source != null) { settings.SourcePath = Path.GetFullPath(source); settingsChanged = true; }
                }
                float speedScale = settings.SpeedScale;
                if (ImGui.DragFloat("Playback Speed Scale", ref speedScale, 0.01f, 0.01f, 10.0f)) { settings.SpeedScale = Math.Max(0.01f, speedScale); settingsChanged = true; }
                if (settingsChanged) asset.Save(path);
                ImGui.Separator();
                if (ImGui.Button("Reimport Animation"))
                    ReimportSelectedAsset(path, true);
            }
            else if (extension is ".obj" or ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga" or ".wav")
            {
                AssetDatabase.FileImportMetadata? metadata = AssetDatabase.GetFileImportMetadata(path);
                bool isTexture = AssetThumbnailCache.IsTextureFile(path);
                ImGui.TextDisabled("Original Source");
                ImGui.TextWrapped(metadata == null || string.IsNullOrWhiteSpace(metadata.SourcePath)
                    ? "Not linked (choose the original file)."
                    : metadata.SourcePath);
                if (ImGui.Button("Choose Source..."))
                {
                    string? source = extension switch
                    {
                        ".obj" => WindowsFileDialog.OpenModelFormat("OBJ"),
                        ".wav" => WindowsFileDialog.OpenAudio(),
                        _ => WindowsFileDialog.OpenTexture()
                    };
                    if (source != null)
                    {
                        AssetDatabase.SaveFileImportMetadata(path, source);
                        metadata = AssetDatabase.GetFileImportMetadata(path);
                    }
                }
                if (metadata != null && !string.IsNullOrWhiteSpace(metadata.SourcePath))
                {
                    ImGui.TextDisabled(AssetDatabase.HasChangedSource(path)
                        ? "The source file has changed and is ready to reimport."
                        : "Source is up to date.");
                    if (ImGui.Button("Reimport Asset"))
                    {
                        try
                        {
                            AssetDatabase.ReimportFileAsset(path);
                            ClearAssetCaches();
                            _statusMessage = $"Reimported {Path.GetFileName(path)} successfully.";
                            _scriptMessages.Add(_statusMessage);
                        }
                        catch (Exception exception)
                        {
                            _statusMessage = $"Reimport failed: {exception.Message}";
                            _scriptMessages.Add(_statusMessage);
                        }
                    }
                }
                ImGui.TextDisabled("Changed linked sources are also checked when you click Refresh.");

                if (extension == ".wav")
                {
                    ImGui.Separator();
                    ImGui.TextUnformatted("PS2 Audio Profile");
                    AudioImportSettings audioSettings = metadata?.Audio ?? new AudioImportSettings();
                    bool audioSettingsChanged = false;

                    int[] bitDepths = { 8, 16 };
                    string[] bitDepthNames = { "8-bit PCM (smaller / faster)", "16-bit PCM (higher quality)" };
                    int bitDepthIndex = Array.IndexOf(bitDepths, audioSettings.BitDepth);
                    if (bitDepthIndex < 0) bitDepthIndex = 1;
                    if (ImGui.Combo("Bit Depth", ref bitDepthIndex, bitDepthNames, bitDepthNames.Length))
                    {
                        audioSettings.BitDepth = bitDepths[bitDepthIndex];
                        audioSettingsChanged = true;
                    }

                    int[] sampleRates = { 22050, 32000, 44100, 48000 };
                    string[] sampleRateNames = { "22,050 Hz", "32,000 Hz", "44,100 Hz", "48,000 Hz" };
                    int sampleRateIndex = Array.IndexOf(sampleRates, audioSettings.MaximumSampleRate);
                    if (sampleRateIndex < 0) sampleRateIndex = sampleRates.Length - 1;
                    if (ImGui.Combo("Maximum Sample Rate", ref sampleRateIndex, sampleRateNames, sampleRateNames.Length))
                    {
                        audioSettings.MaximumSampleRate = sampleRates[sampleRateIndex];
                        audioSettingsChanged = true;
                    }

                    if (audioSettingsChanged)
                    {
                        AssetDatabase.SaveAudioImportSettings(path, audioSettings);
                        metadata = AssetDatabase.GetFileImportMetadata(path);
                    }

                    ImGui.TextWrapped("Choose the profile, then click Reimport Asset above. For long PS2 ambience, 8-bit and 22,050 or 32,000 Hz use much less memory and bandwidth.");
                }

                if (isTexture)
                {
                    ImGui.Separator();
                    ImGui.TextUnformatted("PS2 Texture Profile");
                    TextureImportSettings textureSettings = metadata?.Texture ?? new TextureImportSettings();
                    bool textureSettingsChanged = false;

                    int[] maxSizes = { 0, 64, 128, 256, 512, 1024, 2048 };
                    string[] maxSizeNames = { "Original", "64", "128", "256", "512", "1024", "2048" };
                    int maxSizeIndex = Array.IndexOf(maxSizes, textureSettings.MaxSize);
                    if (maxSizeIndex < 0) maxSizeIndex = 0;
                    if (ImGui.Combo("Maximum Size", ref maxSizeIndex, maxSizeNames, maxSizeNames.Length))
                    {
                        textureSettings.MaxSize = maxSizes[maxSizeIndex];
                        textureSettingsChanged = true;
                    }

                    int format = (int)textureSettings.Format;
                    string[] formats = { "32-bit RGBA", "16-bit RGBA", "8-bit Indexed", "4-bit Indexed" };
                    if (ImGui.Combo("PS2 Color Format", ref format, formats, formats.Length))
                    {
                        textureSettings.Format = (Ps2TextureFormat)format;
                        textureSettingsChanged = true;
                    }

                    int filtering = (int)textureSettings.Filtering;
                    string[] filteringNames = { "Project Default", "Nearest", "Bilinear" };
                    if (ImGui.Combo("Filtering", ref filtering, filteringNames, filteringNames.Length))
                    {
                        textureSettings.Filtering = (TextureFilterOverride)filtering;
                        textureSettingsChanged = true;
                    }

                    bool mipmaps = textureSettings.GenerateMipmaps;
                    if (ImGui.Checkbox("Generate Mipmaps", ref mipmaps))
                    {
                        textureSettings.GenerateMipmaps = mipmaps;
                        textureSettingsChanged = true;
                    }

                    if (textureSettingsChanged)
                    {
                        AssetDatabase.SaveTextureImportSettings(path, textureSettings);
                        QueueTextureInvalidation(path);
                        metadata = AssetDatabase.GetFileImportMetadata(path);
                    }

                    TextureImportInfo info = TextureImportUtility.Analyze(path, textureSettings);
                    ImGui.TextDisabled($"Source: {info.SourceWidth} x {info.SourceHeight}");
                    ImGui.TextDisabled($"Imported: {info.ImportedWidth} x {info.ImportedHeight}");
                    ImGui.TextDisabled($"Estimated texture VRAM: {FormatBytes(info.EstimatedVramBytes)}");
                    ImGui.TextWrapped("Indexed previews approximate the reduced palette; final PS2 export will encode the native indexed layout.");
                }
            }
            else
                ImGui.TextDisabled("This asset type does not have import settings yet.");
        }
        catch (Exception exception)
        {
            ImGui.TextWrapped($"Could not read import settings: {exception.Message}");
        }

    }

    private void ReimportSelectedAsset(string path, bool animation)
    {
        try
        {
            if (animation) AssetDatabase.ReimportHumanoidAnimation(path);
            else AssetDatabase.ReimportSkeletalModel(path);
            _thumbnailCache.Clear();
            MeshAssetCache.Clear();
            _statusMessage = $"Reimported {Path.GetFileName(path)} successfully.";
            _scriptMessages.Add(_statusMessage);
        }
        catch (Exception exception)
        {
            _statusMessage = $"Reimport failed: {exception.Message}";
            _scriptMessages.Add(_statusMessage);
        }
    }

    private void DrawSkeletonConfiguration(string path)
    {
        SkeletalAsset asset;
        try
        {
            asset = SkeletalAsset.Load(path);
        }
        catch (Exception exception)
        {
            ImGui.TextDisabled($"Could not read skeleton: {exception.Message}");
            return;
        }

        asset.BoneMappings ??= new Dictionary<string, string>(StringComparer.Ordinal);
        int mappedCount = asset.Bones.Count(bone =>
            asset.BoneMappings.TryGetValue(bone.Name, out string? role) && !string.IsNullOrWhiteSpace(role));
        string[] duplicateRoles = asset.BoneMappings.Values
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .GroupBy(role => role, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        ImGui.Separator();
        ImGui.Text("Target Humanoid Mapping");
        ImGui.TextDisabled($"{mappedCount}/{asset.Bones.Count} skeleton nodes mapped");
        if (duplicateRoles.Length > 0)
            ImGui.TextColored(
                new Vector4(1.0f, 0.65f, 0.2f, 1.0f),
                $"Duplicate target roles: {string.Join(", ", duplicateRoles)}");
        ImGui.TextWrapped(
            "Assign the role each model bone represents. Root, armature and helper nodes should usually remain Ignore.");

        bool changed = false;
        if (ImGui.Button("Auto Map Bones"))
        {
            foreach (SkeletalBone bone in asset.Bones)
                asset.BoneMappings[bone.Name] = HumanoidRig.DetectTargetRole(bone.Name);
            changed = true;
        }

        bool listVisible = ImGui.BeginChild(
            "SkeletonConfigurationList",
            new Vector2(0.0f, 430.0f),
            ImGuiChildFlags.None);
        if (listVisible)
        {
            foreach (SkeletalBone bone in asset.Bones)
            {
                asset.BoneMappings.TryGetValue(bone.Name, out string? currentRole);
                currentRole ??= "";
                ImGui.PushID(bone.Name);
                ImGui.TextUnformatted(bone.Name);
                ImGui.SetNextItemWidth(-1.0f);
                string preview = string.IsNullOrWhiteSpace(currentRole) ? "Ignore" : currentRole;
                if (ImGui.BeginCombo("##SkeletonBoneRole", preview))
                {
                    if (ImGui.Selectable("Ignore", string.IsNullOrWhiteSpace(currentRole)))
                    {
                        asset.BoneMappings[bone.Name] = "";
                        changed = true;
                    }
                    foreach (string role in HumanoidRig.BoneRoles)
                    {
                        if (ImGui.Selectable(role, string.Equals(currentRole, role, StringComparison.Ordinal)))
                        {
                            asset.BoneMappings[bone.Name] = role;
                            changed = true;
                        }
                    }
                    ImGui.EndCombo();
                }
                ImGui.PopID();
            }
        }
        ImGui.EndChild();
        DrawAnimationEvents(asset.Animations, ref changed);

        if (changed)
        {
            asset.Save(path);
            MeshAssetCache.Clear();
            _statusMessage = $"Saved skeleton configuration for {asset.Name}.";
        }
    }

    private void DrawHumanoidBoneMappingWindow()
    {
        if (!_showHumanoidBoneMappingWindow)
            return;

        ImGui.SetNextWindowSize(new Vector2(620.0f, 680.0f), ImGuiCond.FirstUseEver);
        bool open = _showHumanoidBoneMappingWindow;
        if (!ImGui.Begin("Humanoid Bone Mapping", ref open))
        {
            ImGui.End();
            _showHumanoidBoneMappingWindow = open;
            return;
        }

        if (_humanoidBoneMappingPath == null || !File.Exists(_humanoidBoneMappingPath))
        {
            ImGui.TextDisabled("Select a humanoid animation asset and click Edit Bone Mapping.");
        }
        else
        {
            ImGui.TextUnformatted(Path.GetFileNameWithoutExtension(_humanoidBoneMappingPath));
            ImGui.TextDisabled(AssetDatabase.ToProjectRelativePath(_humanoidBoneMappingPath));
            DrawHumanoidAnimationBoneMapping(_humanoidBoneMappingPath);
        }

        ImGui.End();
        _showHumanoidBoneMappingWindow = open;
    }

    private void DrawHumanoidAnimationBoneMapping(string path)
    {
        HumanoidAnimationAsset asset;
        try
        {
            asset = HumanoidAnimationAsset.Load(path);
        }
        catch (Exception exception)
        {
            ImGui.TextDisabled($"Could not read bone mapping: {exception.Message}");
            return;
        }

        asset.BoneMappings ??= new Dictionary<string, string>(StringComparer.Ordinal);
        int mappedCount = asset.BoneNames.Count(name =>
            asset.BoneMappings.TryGetValue(name, out string? role) && !string.IsNullOrWhiteSpace(role));
        int duplicateCount = asset.BoneMappings.Values
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .GroupBy(role => role, StringComparer.Ordinal)
            .Count(group => group.Count() > 1);
        bool hasUnbakedFbxPivots = asset.BoneNames.Any(name =>
            name.Contains("_$AssimpFbx$", StringComparison.OrdinalIgnoreCase));
        bool missingSourceBindPose = asset.RetargetVersion < 2 || asset.SourceBindRotations == null ||
                                     asset.SourceBindRotations.Count == 0;

        ImGui.Separator();
        ImGui.Text("Humanoid Bone Mapping");
        ImGui.TextDisabled($"{mappedCount}/{asset.BoneNames.Count} tracks mapped");
        if (hasUnbakedFbxPivots)
        {
            ImGui.TextColored(
                new Vector4(1.0f, 0.35f, 0.3f, 1.0f),
                "This asset contains legacy FBX pivot tracks and must be reimported.");
            ImGui.TextWrapped("Reimport the original FBX with the updated importer before using this animation.");
        }
        else if (missingSourceBindPose)
        {
            ImGui.TextColored(
                new Vector4(1.0f, 0.35f, 0.3f, 1.0f),
                "This asset does not contain its source rig bind pose and must be reimported.");
            ImGui.TextWrapped("The bind pose is required to convert rotations safely onto a different character skeleton.");
        }
        if (duplicateCount > 0)
            ImGui.TextColored(new Vector4(1.0f, 0.65f, 0.2f, 1.0f), $"Warning: {duplicateCount} target bone(s) are mapped more than once.");
        ImGui.TextWrapped("Choose which humanoid bone each imported animation track controls. Set helpers and unwanted tracks to Ignore.");

        bool changed = false;
        if (ImGui.Button("Auto Map Bones"))
        {
            foreach (string boneName in asset.BoneNames)
                asset.BoneMappings[boneName] = HumanoidRig.DetectAnimationRole(boneName);
            changed = true;
        }

        bool mappingListVisible = ImGui.BeginChild(
            "HumanoidBoneMappingList",
            new Vector2(0.0f, 310.0f),
            ImGuiChildFlags.None);
        if (mappingListVisible)
        {
            foreach (string boneName in asset.BoneNames)
            {
                asset.BoneMappings.TryGetValue(boneName, out string? currentRole);
                currentRole ??= "";
                ImGui.PushID(boneName);
                ImGui.TextUnformatted(boneName);
                ImGui.SetNextItemWidth(-1.0f);
                string preview = string.IsNullOrWhiteSpace(currentRole) ? "Ignore" : currentRole;
                if (ImGui.BeginCombo("##BoneRole", preview))
                {
                    if (ImGui.Selectable("Ignore", string.IsNullOrWhiteSpace(currentRole)))
                    {
                        asset.BoneMappings[boneName] = "";
                        changed = true;
                    }
                    foreach (string role in HumanoidRig.BoneRoles)
                    {
                        if (ImGui.Selectable(role, string.Equals(currentRole, role, StringComparison.Ordinal)))
                        {
                            asset.BoneMappings[boneName] = role;
                            changed = true;
                        }
                    }
                    ImGui.EndCombo();
                }
                ImGui.PopID();
            }
        }
        ImGui.EndChild();
        DrawAnimationEvents(asset.Clips, ref changed);

        if (changed)
        {
            asset.Save(path);
            _statusMessage = $"Saved humanoid bone mapping for {asset.Name}.";
        }
    }

    private static void DrawAnimationEvents(List<SkeletalAnimationClip> clips, ref bool changed)
    {
        ImGui.Separator();
        ImGui.Text("Animation Events");
        ImGui.TextWrapped(
            "Events call OnAnimationEvent(string eventName) on scripts attached to the animated object.");
        if (clips.Count == 0)
        {
            ImGui.TextDisabled("This asset contains no animation takes.");
            return;
        }

        foreach (SkeletalAnimationClip clip in clips)
        {
            clip.Events ??= new List<AnimationEventMarker>();
            ImGui.PushID(clip.Name);
            if (ImGui.CollapsingHeader($"{clip.Name} ({clip.Duration:0.00}s)", ImGuiTreeNodeFlags.DefaultOpen))
            {
                bool applyRootMotion = clip.ApplyRootMotion;
                if (ImGui.Checkbox("Apply Root Motion", ref applyRootMotion))
                {
                    clip.ApplyRootMotion = applyRootMotion;
                    changed = true;
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Preserve this clip's imported hips/root translation. Disable for controller-driven walking and running.");
                int deleteEvent = -1;
                for (int eventIndex = 0; eventIndex < clip.Events.Count; eventIndex++)
                {
                    AnimationEventMarker marker = clip.Events[eventIndex];
                    ImGui.PushID(eventIndex);
                    string eventName = marker.Name;
                    ImGui.SetNextItemWidth(210.0f);
                    if (ImGui.InputText("Name", ref eventName, 80))
                    {
                        marker.Name = eventName;
                        changed = true;
                    }
                    ImGui.SameLine();
                    float eventTime = marker.Time;
                    ImGui.SetNextItemWidth(130.0f);
                    if (ImGui.DragFloat("Time", ref eventTime, 0.01f, 0.0f, clip.Duration, "%.2f s"))
                    {
                        marker.Time = Math.Clamp(eventTime, 0.0f, clip.Duration);
                        changed = true;
                    }
                    ImGui.SameLine();
                    if (ImGui.SmallButton("X")) deleteEvent = eventIndex;
                    ImGui.PopID();
                }
                if (deleteEvent >= 0)
                {
                    clip.Events.RemoveAt(deleteEvent);
                    changed = true;
                }
                if (ImGui.Button("Add Event"))
                {
                    clip.Events.Add(new AnimationEventMarker
                    {
                        Name = "Event",
                        Time = Math.Min(clip.Duration, clip.Duration * 0.5f)
                    });
                    changed = true;
                }
            }
            ImGui.PopID();
        }
    }

    private static string DescribeImportedModel(string projectRelativePath)
    {
        if (!string.Equals(Path.GetExtension(projectRelativePath), ".r2skel", StringComparison.OrdinalIgnoreCase))
            return $"Imported model: {projectRelativePath}";

        SkeletalAsset asset = SkeletalAsset.Load(AssetDatabase.ToAbsolutePath(projectRelativePath));
        return $"Imported skeletal model: {asset.Vertices.Count} vertices, " +
               $"{asset.DeformBoneCount} deform bones ({asset.Bones.Count} hierarchy nodes), " +
               $"{asset.Animations.Count} animation take(s).";
    }

    private static void OpenProjectFolder(string path)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(Path.GetFullPath(path));
        System.Diagnostics.Process.Start(startInfo);
    }

    private void CompileEditorScripts()
    {
        ScriptCompilationResult compilation =
            ScriptCompiler.Compile(
                Path.Combine(
                    AssetDatabase.AssetsRoot,
                    "Scripts"));

        _compiledScriptTypes = compilation.Types;
        _editorScriptsReady = compilation.Success;
        _scriptMessages.Clear();
        _scriptMessages.AddRange(compilation.Messages);
    }

    private void DrawScriptFields(
        GameObject target,
        ScriptComponent scriptComponent)
    {
        if (string.IsNullOrWhiteSpace(scriptComponent.ScriptPath))
            return;

        if (!_editorScriptsReady)
            CompileEditorScripts();

        string className =
            Path.GetFileNameWithoutExtension(
                scriptComponent.ScriptPath);

        if (!_compiledScriptTypes.TryGetValue(className, out Type? scriptType))
        {
            ImGui.TextDisabled("Script class unavailable. Check Console.");
            return;
        }

        object? defaults;

        try
        {
            defaults = Activator.CreateInstance(scriptType);
        }
        catch (Exception exception)
        {
            ImGui.TextWrapped($"Could not inspect fields: {exception.GetBaseException().Message}");
            return;
        }

        FieldInfo[] fields = scriptType
            .GetFields(BindingFlags.Instance | BindingFlags.Public)
            .Where(field => ScriptFieldUtility.IsSupported(field.FieldType))
            .ToArray();

        if (fields.Length == 0)
        {
            ImGui.TextDisabled("No supported public fields.");
            return;
        }

        ImGui.SeparatorText("Script Fields");

        foreach (FieldInfo field in fields)
        {
            object? value = defaults == null
                ? null
                : field.GetValue(defaults);

            if (scriptComponent.FieldValues.TryGetValue(field.Name, out string? stored) &&
                field.FieldType == typeof(GameObject))
            {
                value = _scene.FindGameObject(stored);
            }
            else if (stored != null &&
                ScriptFieldUtility.TryDeserialize(stored, field.FieldType, out object? overrideValue))
            {
                value = overrideValue;
            }

            bool changed = false;
            object? editedValue = value;

            ImGui.PushID(field.Name);

            if (field.FieldType == typeof(float))
            {
                float number = (float)(value ?? 0.0f);
                changed = ImGui.DragFloat(field.Name, ref number, 0.1f);
                editedValue = number;
            }
            else if (field.FieldType == typeof(int))
            {
                int number = (int)(value ?? 0);
                changed = ImGui.DragInt(field.Name, ref number, 1.0f);
                editedValue = number;
            }
            else if (field.FieldType == typeof(bool))
            {
                bool flag = (bool)(value ?? false);
                changed = ImGui.Checkbox(field.Name, ref flag);
                editedValue = flag;
            }
            else if (field.FieldType == typeof(string))
            {
                string text = (string?)value ?? "";
                bool sceneReference = field.Name is "SceneName" or "TargetScene";
                changed = sceneReference
                    ? ImGui.InputTextWithHint(field.Name, "Scene name or drop a scene", ref text, 512)
                    : ImGui.InputText(field.Name, ref text, 512);
                if (sceneReference)
                {
                    string? droppedScene = AcceptAssetDrop(".r2scene");
                    if (droppedScene != null)
                    {
                        text = Path.GetFileNameWithoutExtension(droppedScene);
                        changed = true;
                        _draggedAssetPath = null;
                    }
                }
                editedValue = text;
            }
            else if (field.FieldType == typeof(Vector2))
            {
                Vector2 vector = (Vector2)(value ?? Vector2.Zero);
                changed = ImGui.DragFloat2(field.Name, ref vector, 0.1f);
                editedValue = vector;
            }
            else if (field.FieldType == typeof(Vector3))
            {
                Vector3 vector = (Vector3)(value ?? Vector3.Zero);
                changed = ImGui.DragFloat3(field.Name, ref vector, 0.1f);
                editedValue = vector;
            }
            else if (field.FieldType == typeof(Vector4))
            {
                Vector4 vector = (Vector4)(value ?? Vector4.Zero);
                changed = ImGui.DragFloat4(field.Name, ref vector, 0.1f);
                editedValue = vector;
            }
            else if (field.FieldType == typeof(GameObject))
            {
                GameObject? referencedObject = value as GameObject;
                string preview = referencedObject?.Name ?? "None";
                bool canvasReference = field.Name.Contains("Canvas", StringComparison.OrdinalIgnoreCase);

                if (ImGui.BeginCombo(field.Name, preview))
                {
                    if (ImGui.Selectable("None", referencedObject == null))
                    {
                        editedValue = null;
                        changed = true;
                    }

                    foreach (GameObject gameObject in _scene.GameObjects)
                    {
                        if (canvasReference && gameObject.GetComponent<Canvas>() == null) continue;
                        if (ImGui.Selectable(gameObject.Name, referencedObject == gameObject))
                        {
                            editedValue = gameObject;
                            changed = true;
                        }
                    }

                    ImGui.EndCombo();
                }
                if (_hierarchyDragActive && _draggedHierarchyObject is { } dropped &&
                    ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem) &&
                    ImGui.IsMouseReleased(ImGuiMouseButton.Left) &&
                    (!canvasReference || dropped.GetComponent<Canvas>() != null))
                {
                    editedValue = dropped;
                    changed = true;
                }
            }

            ImGui.PopID();

            if (changed)
            {
                string serialized =
                    ScriptFieldUtility.Serialize(editedValue, field.FieldType);

                RecordImmediateEdit(
                    () =>
                    {
                        ScriptComponent? targetScript =
                            target.GetComponent<ScriptComponent>();

                        if (targetScript != null)
                            targetScript.FieldValues[field.Name] = serialized;
                    });
            }
        }
    }

    private void DrawFolderTile(
        string directory,
        float tileWidth,
        float tileHeight,
        float thumbnailArea)
    {
        string name =
            Path.GetFileName(
                directory);

        ImGui.PushID(
            directory);

        ImGui.BeginGroup();

        Vector2 cursor =
            ImGui.GetCursorScreenPos();

        ImGui.Dummy(
            new Vector2(
                thumbnailArea,
                thumbnailArea));

        bool previewHovered =
            ImGui.IsItemHovered();

        bool previewClicked =
            ImGui.IsItemClicked(
                ImGuiMouseButton.Left);

        bool previewDoubleClicked =
            previewHovered &&
            ImGui.IsMouseDoubleClicked(
                ImGuiMouseButton.Left);

        ImDrawListPtr drawList =
            ImGui.GetWindowDrawList();

        Vector2 previewMax =
            cursor +
            new Vector2(
                thumbnailArea,
                thumbnailArea);

        drawList.AddRect(
            cursor,
            previewMax,
            ImGui.GetColorU32(
                ImGuiCol.Border),
            4.0f);

        string folderIconPath = GetFolderIconPath(name);
        if (_thumbnailCache.TryGetThumbnail(
                folderIconPath,
                out uint folderTexture,
                out int folderWidth,
                out int folderHeight))
        {
            float iconScale = MathF.Min(
                (thumbnailArea - 6.0f) / folderWidth,
                (thumbnailArea - 6.0f) / folderHeight);
            Vector2 iconSize = new(folderWidth * iconScale, folderHeight * iconScale);
            Vector2 iconMin = cursor + (new Vector2(thumbnailArea) - iconSize) * 0.5f;
            drawList.AddImage(
                (nint)folderTexture,
                iconMin,
                iconMin + iconSize,
                Vector2.Zero,
                Vector2.One);

            DrawFolderGlyph(drawList, name, cursor, thumbnailArea);
        }

        if (previewClicked)
        {
            _selectedAssetPath =
                directory;
            _selectionArea = SelectionArea.Assets;
        }

        if (previewDoubleClicked)
        {
            _projectBrowserPath =
                directory;

            _selectedAssetPath =
                null;
        }

        bool selected =
            string.Equals(
                _selectedAssetPath,
                directory,
                StringComparison.OrdinalIgnoreCase);

        bool renaming = string.Equals(_inlineRenameAssetPath, directory, StringComparison.OrdinalIgnoreCase);
        if (renaming)
        {
            DrawInlineAssetName(directory, tileWidth);
        }
        else if (ImGui.Selectable(
                     name,
                     selected,
                     ImGuiSelectableFlags.AllowDoubleClick,
                     new Vector2(tileWidth, 0.0f)))
        {
            bool doubleClicked = ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left);
            _selectedAssetPath = directory;
            _selectionArea = SelectionArea.Assets;
            if (doubleClicked)
            {
                _projectBrowserPath = directory;
                _selectedAssetPath = null;
            }
            else if (selected)
                BeginInlineAssetRename(directory);
        }

        ImGui.EndGroup();

        if (!renaming && ImGui.BeginPopupContextItem("FolderContextMenu"))
        {
            if (ImGui.MenuItem("Rename", "F2"))
                BeginInlineAssetRename(directory);
            if (ImGui.MenuItem("Duplicate", "Ctrl+D"))
                DuplicateAsset(directory);
            if (ImGui.MenuItem("Delete", "Delete"))
            {
                _pendingDeleteAssetPath = directory;
                _openDeleteAssetPopup = true;
            }
            ImGui.Separator();
            if (ImGui.MenuItem("Show in File Explorer"))
                RevealAsset(directory);
            ImGui.EndPopup();
        }

        // Project items use the editor's existing lightweight drag state instead of an
        // ImGui payload because the same drag can also be dropped into scene/inspector UI.
        if (!renaming && ImGui.IsItemClicked(ImGuiMouseButton.Left))
            _draggedAssetPath = directory;

        bool canAcceptMove = _draggedAssetPath != null &&
            !string.Equals(_draggedAssetPath, directory, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Path.GetDirectoryName(_draggedAssetPath), directory, StringComparison.OrdinalIgnoreCase);
        bool moveHovered = canAcceptMove &&
            ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
        if (moveHovered)
        {
            ImGui.GetWindowDrawList().AddRect(
                ImGui.GetItemRectMin() - new Vector2(2),
                ImGui.GetItemRectMax() + new Vector2(2),
                ImGui.GetColorU32(ImGuiCol.DragDropTarget), 4.0f, ImDrawFlags.None, 2.0f);
            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                string source = _draggedAssetPath!;
                string destination = Path.Combine(directory, Path.GetFileName(source));
                MoveAssetAndRepairReferences(source, destination);
                _draggedAssetPath = null;
            }
        }
        else if (string.Equals(_draggedAssetPath, directory, StringComparison.OrdinalIgnoreCase) &&
                 ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            ImGui.BeginTooltip();
            ImGui.Text(Path.GetFileName(directory));
            ImGui.TextDisabled("Folder");
            ImGui.EndTooltip();
        }
        ImGui.PopID();
    }

    private static string GetFolderIconPath(string folderName)
    {
        return Path.Combine(FindEngineRoot(), "R2Engine.Editor", "Editor", "Icons", "folder-base.png");
    }

    private static void DrawFolderGlyph(
        ImDrawListPtr drawList,
        string folderName,
        Vector2 tileMin,
        float tileSize,
        bool onFolder = true)
    {
        Vector2 center = tileMin + new Vector2(
            tileSize * 0.52f,
            tileSize * (onFolder ? 0.59f : 0.50f));
        uint bright = ImGui.ColorConvertFloat4ToU32(new Vector4(0.72f, 0.92f, 1.0f, 1.0f));
        uint cyan = ImGui.ColorConvertFloat4ToU32(new Vector4(0.10f, 0.72f, 1.0f, 1.0f));
        uint dark = ImGui.ColorConvertFloat4ToU32(new Vector4(0.015f, 0.08f, 0.20f, 0.9f));

        switch (folderName.ToLowerInvariant())
        {
            case "materials":
                drawList.AddCircleFilled(center, 13.0f, dark, 24);
                drawList.AddCircleFilled(center, 10.5f, cyan, 24);
                drawList.AddCircle(center, 13.0f, bright, 24, 2.0f);
                drawList.AddCircleFilled(center - new Vector2(3.5f, 3.5f), 2.5f, bright, 12);
                break;

            case "models":
                Vector2 top = center + new Vector2(0.0f, -13.0f);
                Vector2 left = center + new Vector2(-12.0f, -5.0f);
                Vector2 right = center + new Vector2(12.0f, -5.0f);
                Vector2 bottom = center + new Vector2(0.0f, 13.0f);
                drawList.AddLine(top, left, bright, 2.5f);
                drawList.AddLine(top, right, bright, 2.5f);
                drawList.AddLine(left, center, cyan, 2.5f);
                drawList.AddLine(right, center, cyan, 2.5f);
                drawList.AddLine(center, bottom, bright, 2.5f);
                drawList.AddLine(left, bottom, cyan, 2.5f);
                drawList.AddLine(right, bottom, cyan, 2.5f);
                break;

            case "prefabs":
                drawList.AddLine(center - new Vector2(5.0f, 1.0f), center + new Vector2(5.0f, 1.0f), bright, 4.0f);
                drawList.AddRectFilled(center - new Vector2(15.0f, 10.0f), center + new Vector2(-3.0f, 2.0f), dark, 2.0f);
                drawList.AddRect(center - new Vector2(15.0f, 10.0f), center + new Vector2(-3.0f, 2.0f), cyan, 2.0f, ImDrawFlags.None, 2.5f);
                drawList.AddRectFilled(center + new Vector2(3.0f, -2.0f), center + new Vector2(15.0f, 10.0f), dark, 2.0f);
                drawList.AddRect(center + new Vector2(3.0f, -2.0f), center + new Vector2(15.0f, 10.0f), bright, 2.0f, ImDrawFlags.None, 2.5f);
                break;

            case "scripts":
                drawList.AddLine(center + new Vector2(-3.0f, -10.0f), center + new Vector2(-12.0f, 0.0f), bright, 2.5f);
                drawList.AddLine(center + new Vector2(-12.0f, 0.0f), center + new Vector2(-3.0f, 10.0f), bright, 2.5f);
                drawList.AddLine(center + new Vector2(3.0f, -10.0f), center + new Vector2(12.0f, 0.0f), cyan, 2.5f);
                drawList.AddLine(center + new Vector2(12.0f, 0.0f), center + new Vector2(3.0f, 10.0f), cyan, 2.5f);
                break;

            case "textures":
                const float cell = 7.0f;
                Vector2 gridMin = center - new Vector2(cell * 1.5f);
                for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                {
                    Vector2 min = gridMin + new Vector2(x * cell, y * cell);
                    drawList.AddRectFilled(min, min + new Vector2(cell - 1.0f), (x + y) % 2 == 0 ? bright : cyan, 1.0f);
                }
                drawList.AddRect(gridMin - Vector2.One, gridMin + new Vector2(cell * 3.0f), dark, 1.0f, ImDrawFlags.None, 2.0f);
                break;

            default:
                drawList.AddLine(center + new Vector2(0.0f, -11.0f), center + new Vector2(11.0f, 0.0f), bright, 2.5f);
                drawList.AddLine(center + new Vector2(11.0f, 0.0f), center + new Vector2(0.0f, 11.0f), cyan, 2.5f);
                drawList.AddLine(center + new Vector2(0.0f, 11.0f), center + new Vector2(-11.0f, 0.0f), bright, 2.5f);
                drawList.AddLine(center + new Vector2(-11.0f, 0.0f), center + new Vector2(0.0f, -11.0f), cyan, 2.5f);
                break;
        }
    }

    private void DrawAssetTile(
        string file,
        float tileWidth,
        float tileHeight,
        float thumbnailArea)
    {
        string name =
            Path.GetFileName(
                file);

        ImGui.PushID(
            file);

        ImGui.BeginGroup();

        if (_thumbnailCache.TryGetThumbnail(
                file,
                out uint thumbnailTexture,
                out int thumbnailWidth,
                out int thumbnailHeight))
        {
            float scale =
                MathF.Min(
                    thumbnailArea /
                    thumbnailWidth,
                    thumbnailArea /
                    thumbnailHeight);

            Vector2 imageSize =
                new Vector2(
                    thumbnailWidth *
                    scale,
                    thumbnailHeight *
                    scale);

            Vector2 start =
                ImGui.GetCursorScreenPos();

            float horizontalPadding =
                (
                    thumbnailArea -
                    imageSize.X
                ) *
                0.5f;

            float verticalPadding =
                (
                    thumbnailArea -
                    imageSize.Y
                ) *
                0.5f;

            ImGui.SetCursorScreenPos(
                start +
                new Vector2(
                    horizontalPadding,
                    verticalPadding));

            ImGui.Image(
                (nint)thumbnailTexture,
                imageSize,
                Vector2.Zero,
                Vector2.One);

            if (ImGui.IsItemClicked(
                    ImGuiMouseButton.Left))
            {
                _selectedAssetPath =
                    file;
                _selectionArea = SelectionArea.Assets;
            }

            ImGui.SetCursorScreenPos(
                start +
                new Vector2(
                    0.0f,
                    thumbnailArea));
        }
        else
        {
            Vector2 cursor =
                ImGui.GetCursorScreenPos();

            ImGui.Dummy(
                new Vector2(
                    thumbnailArea,
                    thumbnailArea));

            if (ImGui.IsItemClicked(
                    ImGuiMouseButton.Left))
            {
                _selectedAssetPath =
                    file;
                _selectionArea = SelectionArea.Assets;
            }

            ImDrawListPtr drawList =
                ImGui.GetWindowDrawList();

            Vector2 previewMax =
                cursor +
                new Vector2(
                    thumbnailArea,
                    thumbnailArea);

            drawList.AddRect(
                cursor,
                previewMax,
                ImGui.GetColorU32(
                    ImGuiCol.Border),
                4.0f);

            string type =
                AssetDatabase
                    .GetAssetTypeLabel(
                        file)
                    .ToUpperInvariant();

            string? glyphCategory = Path.GetExtension(file).ToLowerInvariant() switch
            {
                ".r2mat" => "materials",
                ".obj" or ".fbx" or ".gltf" or ".glb" or ".r2skel" => "models",
                ".r2prefab" => "prefabs",
                ".cs" => "scripts",
                ".r2scene" => "generic",
                _ => null
            };

            if (glyphCategory != null)
            {
                DrawFolderGlyph(drawList, glyphCategory, cursor, thumbnailArea, onFolder: false);
            }
            else
            {
                Vector2 textSize = ImGui.CalcTextSize(type);
                drawList.AddText(
                    cursor + (new Vector2(thumbnailArea) - textSize) * 0.5f,
                    ImGui.GetColorU32(ImGuiCol.TextDisabled),
                    type);
            }
        }

        bool selected =
            string.Equals(
                _selectedAssetPath,
                file,
                StringComparison.OrdinalIgnoreCase);

        bool renaming = string.Equals(_inlineRenameAssetPath, file, StringComparison.OrdinalIgnoreCase);
        if (renaming)
        {
            DrawInlineAssetName(file, tileWidth);
        }
        else if (ImGui.Selectable(
                     name,
                     selected,
                     ImGuiSelectableFlags.AllowDoubleClick,
                     new Vector2(tileWidth, 0.0f)))
        {
            bool doubleClicked = ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left);
            _selectedAssetPath = file;
            _selectionArea = SelectionArea.Assets;
            if (selected && !doubleClicked)
                BeginInlineAssetRename(file);
        }

        ImGui.EndGroup();

        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) &&
            string.Equals(Path.GetExtension(file), ".r2anim", StringComparison.OrdinalIgnoreCase))
        {
            OpenAnimationEditor(file);
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) &&
            string.Equals(Path.GetExtension(file), ".r2controller", StringComparison.OrdinalIgnoreCase))
        {
            _animatorControllerPath = file;
            _showAnimatorControllerWindow = true;
            _inspectAnimatorController = true;
            _selectedAnimatorState = -1;
        }

        if (ImGui.BeginPopupContextItem("AssetContextMenu"))
        {
            if (ImGui.MenuItem("Rename", "F2"))
                BeginInlineAssetRename(file);

            if (ImGui.MenuItem("Duplicate", "Ctrl+D"))
                DuplicateAsset(file);

            if (ImGui.MenuItem("Delete", "Delete"))
            {
                _pendingDeleteAssetPath = file;
                _openDeleteAssetPopup = true;
            }

            ImGui.Separator();

            if (ImGui.MenuItem("Show in File Explorer"))
                RevealAsset(file);

            if (AssetThumbnailCache.SupportsPreview(file) && ImGui.MenuItem("Refresh Thumbnail"))
                _thumbnailCache.Invalidate(file);

            ImGui.EndPopup();
        }

        if (!renaming && ImGui.IsItemClicked(ImGuiMouseButton.Left))
            _draggedAssetPath = file;

        if (string.Equals(
                _draggedAssetPath,
                file,
                StringComparison.OrdinalIgnoreCase) &&
            ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            ImGui.BeginTooltip();
            ImGui.Text(Path.GetFileName(file));
            ImGui.TextDisabled(AssetDatabase.GetAssetTypeLabel(file));
            ImGui.EndTooltip();
        }

        ImGui.PopID();
    }

    private string? AcceptAssetDrop(params string[] extensions)
    {
        if (_draggedAssetPath == null ||
            !ImGui.IsItemHovered(
                ImGuiHoveredFlags.AllowWhenBlockedByActiveItem) ||
            !ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            return null;
        }

        string? acceptedPath = null;

        if (File.Exists(_draggedAssetPath))
        {
            string extension =
                Path.GetExtension(_draggedAssetPath);

            if (extensions.Any(
                    expected => string.Equals(
                        extension,
                        expected,
                        StringComparison.OrdinalIgnoreCase)))
            {
                acceptedPath = _draggedAssetPath;
            }
            else
            {
                _statusMessage =
                    $"Cannot drop {extension} here. Expected: {string.Join(", ", extensions)}";
            }
        }

        return acceptedPath;
    }

    private string? AcceptTextureAssetDrop() => AcceptAssetDrop(".png", ".jpg", ".jpeg", ".bmp", ".tga");

    // =========================================================
    // Scene View
    // =========================================================

    // Draw vector icons rather than font glyphs so these work with the editor's pixel font.
    private static bool RuntimeToolButton(string icon, string tooltip, bool active, float side)
    {
        if (active)
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.44f, 0.65f, 0.88f, 1.0f));
        bool clicked = ImGui.Button("##RuntimeTool" + icon, new Vector2(side, side));
        if (active)
            ImGui.PopStyleColor();

        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        Vector2 center = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) * 0.5f;
        float scale = side / 22.0f;
        uint color = ImGui.GetColorU32(ImGuiCol.Text);
        Vector2 P(float x, float y) => center + new Vector2(x, y) * scale;

        switch (icon)
        {
            case "Play":
                draw.AddTriangleFilled(P(-4.0f, -6.0f), P(6.0f, 0.0f), P(-4.0f, 6.0f), color);
                break;
            case "Pause":
                draw.AddRectFilled(P(-5.0f, -6.0f), P(-1.5f, 6.0f), color, 0.75f);
                draw.AddRectFilled(P(1.5f, -6.0f), P(5.0f, 6.0f), color, 0.75f);
                break;
            case "Step":
                draw.AddTriangleFilled(P(-6.0f, -5.5f), P(2.5f, 0.0f), P(-6.0f, 5.5f), color);
                draw.AddRectFilled(P(3.5f, -6.0f), P(6.0f, 6.0f), color, 0.65f);
                break;
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(tooltip);
        return clicked;
    }

    private static bool SceneToolButton(string icon, string tooltip, bool active)
    {
        if (active)
            ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);
        float side = Math.Max(26, ImGui.GetFrameHeight());
        bool clicked = ImGui.Button("##SceneTool" + icon, new Vector2(side));
        if (active) ImGui.PopStyleColor();
        var draw = ImGui.GetWindowDrawList();
        Vector2 center = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) * 0.5f;
        uint color = ImGui.GetColorU32(ImGuiCol.Text);
        Vector2 P(float x, float y) => center + new Vector2(x, y) * (side / 26f);
        void Line(float x, float y, float xx, float yy) => draw.AddLine(P(x,y), P(xx,yy), color, 1.5f);
        void Arrow(float x, float y, float xx, float yy)
        {
            Vector2 a = P(x,y), b = P(xx,yy);
            Vector2 back = Vector2.Normalize(a-b) * 3.5f;
            Vector2 cross = new(-back.Y, back.X);
            draw.AddLine(a,b,color,1.5f);
            draw.AddLine(b,b+back+cross*0.65f,color,1.5f);
            draw.AddLine(b,b+back-cross*0.65f,color,1.5f);
        }
        switch (icon)
        {
            case "Move":
                Arrow(0,0,0,-8); Arrow(0,0,0,8);
                Arrow(0,0,-8,0); Arrow(0,0,8,0);
                break;
            case "View":
                draw.AddCircle(P(0, 2), 4.0f * side / 26.0f, color, 18, 1.5f);
                Line(-4,2,-7,-2); Line(-7,-2,-5,-4); Line(-5,-4,-3,-2);
                Line(-3,-2,-3,-8); Line(-3,-8,-1,-9); Line(-1,-9,0,-7);
                Line(0,-7,1,-9); Line(1,-9,3,-8); Line(3,-8,3,-5);
                break;
            case "Rotate":
                for (int i=0; i<20; i++)
                {
                    float a = 0.25f + i*0.26f, b = a+0.26f;
                    Line(MathF.Cos(a)*7, MathF.Sin(a)*7, MathF.Cos(b)*7, MathF.Sin(b)*7);
                }
                Arrow(2,-7,6,-5);
                break;
            case "Scale":
                Arrow(-5,5,7,-7);
                draw.AddRect(P(-8,3),P(-3,8),color,0,ImDrawFlags.None,1.5f);
                Line(2,-8,8,-8); Line(8,-8,8,-2);
                break;
            case "Rect":
                draw.AddRect(P(-7,-6), P(7,6), color, 0, ImDrawFlags.None, 1.5f);
                draw.AddRectFilled(P(-9,-8), P(-5,-4), color);
                draw.AddRectFilled(P(5,-8), P(9,-4), color);
                draw.AddRectFilled(P(-9,4), P(-5,8), color);
                draw.AddRectFilled(P(5,4), P(9,8), color);
                break;
            case "Transform":
                Arrow(0,1,0,-8); Arrow(0,1,8,1);
                draw.AddCircle(center + new Vector2(-3, 3) * (side / 26f), 5*side/26f, color, 14, 1.5f);
                draw.AddRect(P(4,-9), P(8,-5), color, 0, ImDrawFlags.None, 1.5f);
                break;
            case "Global":
                draw.AddCircle(center,8*side/26f,color,24,1.5f);
                Line(-8,0,8,0);
                for (int i=0; i<24; i++)
                {
                    float a=i*MathF.Tau/24, b=(i+1)*MathF.Tau/24;
                    Line(MathF.Cos(a)*3.5f,MathF.Sin(a)*8,MathF.Cos(b)*3.5f,MathF.Sin(b)*8);
                }
                break;
            case "Local":
                Arrow(-4,4,-4,-8); Arrow(-4,4,8,4); Arrow(-4,4,3,-3);
                break;
            case "Snap":
                Line(-7,-7,-7,2); Line(-3,-7,-3,2);
                Line(7,-7,7,2); Line(3,-7,3,2);
                Line(-7,-7,-3,-7); Line(3,-7,7,-7);
                Line(-7,-3,-3,-3); Line(3,-3,7,-3);
                for (int i=0; i<12; i++)
                {
                    float a=i*MathF.PI/12, b=(i+1)*MathF.PI/12;
                    Line(MathF.Cos(a)*7,2+MathF.Sin(a)*6,MathF.Cos(b)*7,2+MathF.Sin(b)*6);
                    Line(MathF.Cos(a)*3,2+MathF.Sin(a)*2,MathF.Cos(b)*3,2+MathF.Sin(b)*2);
                }
                break;
            case "Canvas":
                draw.AddRect(P(-8,-6),P(8,5),color,1,ImDrawFlags.None,1.5f);
                Line(0,5,0,8); Line(-4,8,4,8);
                break;
            case "Helpers":
                draw.AddCircle(center, 4*side/26f, color, 16, 1.5f);
                for (int i = 0; i < 8; i++)
                {
                    float angle = i*MathF.Tau/8;
                    Line(MathF.Cos(angle)*6, MathF.Sin(angle)*6,
                        MathF.Cos(angle)*9, MathF.Sin(angle)*9);
                }
                break;
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(tooltip);
        return clicked;
    }

    private void DrawSceneView()
    {
        ImGui.Begin(
            "Scene");

        bool sceneEditingEnabled = _runtimeScene == null;
        Scene.Scene sceneViewScene = _runtimeScene ?? _scene;
        GameObject? sceneViewSelected = sceneEditingEnabled
            ? _selectedObject
            : _selectedObject == null
                ? null
                : sceneViewScene.FindGameObject(_selectedObject.Name);

        bool sceneFocused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) ||
                            ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows);
        if (sceneEditingEnabled && sceneFocused && !ImGui.GetIO().WantTextInput)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.Q)) _transformGizmo.Mode = TransformGizmoMode.View;
            if (ImGui.IsKeyPressed(ImGuiKey.W)) _transformGizmo.Mode = TransformGizmoMode.Move;
            if (ImGui.IsKeyPressed(ImGuiKey.E)) _transformGizmo.Mode = TransformGizmoMode.Rotate;
            if (ImGui.IsKeyPressed(ImGuiKey.R)) _transformGizmo.Mode = TransformGizmoMode.Scale;
            if (ImGui.IsKeyPressed(ImGuiKey.T)) _transformGizmo.Mode = TransformGizmoMode.Rect;
            if (ImGui.IsKeyPressed(ImGuiKey.Y)) _transformGizmo.Mode = TransformGizmoMode.Transform;
        }

        if (!sceneEditingEnabled)
        {
            ImGui.TextDisabled("PLAY MODE - Live Runtime Scene");
            ImGui.SameLine();
            ImGui.BeginDisabled();
        }

        bool snap =
            _transformGizmo.SnappingEnabled;

        if (SceneToolButton("Snap", snap ? "Snapping: On" : "Snapping: Off", snap))
        {
            _transformGizmo.SnappingEnabled =
                !snap;
        }

        ImGui.SameLine();
        if (SceneToolButton("Canvas", "Canvas Preview (Editor Only)", _canvasPreview.ShowInScene))
            _canvasPreview.ShowInScene = !_canvasPreview.ShowInScene;

        ImGui.SameLine();
        if (SceneToolButton("Helpers", "Helper Gizmos: " + (_sceneViewPreferences.ShowHelperGizmos ? "On" : "Off") +
                "\nLight markers, collider outlines and camera preview.\nObject selection and transform handles stay available.", _sceneViewPreferences.ShowHelperGizmos))
        {
            _sceneViewPreferences.ShowHelperGizmos = !_sceneViewPreferences.ShowHelperGizmos;
            try { _sceneViewPreferences.Save(Path.Combine(AssetDatabase.ProjectRoot, "SceneView.editor.json")); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _statusMessage = $"Could not save Scene view preferences: {exception.Message}";
                _scriptMessages.Add(_statusMessage);
            }
        }

        if (IsUiObject(sceneViewSelected) && _canvasPreview.ShowInScene)
        {
            ImGui.SameLine();
            if (ImGui.Button(_uiAnchorEditing ? "UI Anchor" : "UI Move/Resize"))
                _uiAnchorEditing = !_uiAnchorEditing;
        }

        if (!sceneEditingEnabled)
            ImGui.EndDisabled();

        ImGui.Separator();

        Vector2 availableSize =
            ImGui.GetContentRegionAvail();

        int width =
            Math.Max(
                1,
                (int)availableSize.X);

        int height =
            Math.Max(
                1,
                (int)availableSize.Y);

        Vector2 imagePosition =
            ImGui.GetCursorScreenPos();

        bool sceneHovered =
            ImGui.IsWindowHovered(
                ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);

        _editorCamera.Update(
            sceneHovered,
            sceneViewSelected,
            _transformGizmo.Mode == TransformGizmoMode.View);

        _sceneRenderer.Render(
            sceneViewScene,
            _editorCamera,
            sceneViewSelected,
            width,
            height);

        ImGui.Image(
            (nint)
            _sceneRenderer.ColorTexture,
            availableSize,
            new Vector2(
                0.0f,
                1.0f),
            new Vector2(
                1.0f,
                0.0f));

        bool imageHovered =
            ImGui.IsItemHovered();

        bool uiWidgetUsingMouse = sceneEditingEnabled &&
            DrawSceneUiWidget(sceneViewScene, sceneViewSelected, imagePosition, availableSize, imageHovered);

        string? droppedPrefab = sceneEditingEnabled
            ? AcceptAssetDrop(".r2prefab")
            : null;
        if (sceneEditingEnabled && droppedPrefab != null)
        {
            InstantiatePrefab(droppedPrefab);
            _draggedAssetPath = null;
        }

        // -----------------------------------------------------
        // Gizmo Undo
        // -----------------------------------------------------

        bool gizmoUsingMouse =
            false;

        bool draggingBefore =
            _transformGizmo.IsDragging;

        Vector3 primaryPositionBefore = _selectedObject?.Transform.WorldPosition ?? Vector3.Zero;
        Vector3 primaryRotationBefore = _selectedObject?.Transform.Rotation ?? Vector3.Zero;
        Vector3 primaryScaleBefore = _selectedObject?.Transform.Scale ?? Vector3.One;

        if (sceneEditingEnabled &&
            !draggingBefore &&
            imageHovered &&
            _selectedObject !=
            null)
        {
            _activeGizmoEditSnapshot =
                SceneSerializer.Serialize(
                    _scene);
        }

        if (sceneEditingEnabled &&
            (imageHovered ||
             _transformGizmo.IsDragging) &&
            !IsUiObject(_selectedObject))
        {
            gizmoUsingMouse =
                _transformGizmo
                    .UpdateAndDraw(
                        _selectedObject,
                        _editorCamera,
                        imagePosition,
                        availableSize,
                        imageHovered);
        }

        bool draggingAfter =
            _transformGizmo.IsDragging;

        if (sceneEditingEnabled && draggingAfter && _selectedObject != null && _selectedObjects.Count > 1)
        {
            Vector3 positionDelta = _selectedObject.Transform.WorldPosition - primaryPositionBefore;
            Vector3 rotationDelta = _selectedObject.Transform.Rotation - primaryRotationBefore;
            Vector3 scaleRatio = SafeScaleRatio(_selectedObject.Transform.Scale, primaryScaleBefore);

            foreach (GameObject selected in _selectedObjects.ToArray())
            {
                if (ReferenceEquals(selected, _selectedObject) || HasSelectedAncestor(selected))
                    continue;

                selected.Transform.SetWorldPosition(selected.Transform.WorldPosition + positionDelta);
                selected.Transform.Rotation += rotationDelta;
                selected.Transform.Scale *= scaleRatio;
            }
        }

        if (sceneEditingEnabled &&
            _wasGizmoDragging &&
            !draggingAfter &&
            _activeGizmoEditSnapshot !=
            null)
        {
            string current =
                SceneSerializer.Serialize(
                    _scene);

            if (current !=
                _activeGizmoEditSnapshot)
            {
                RecordUndoSnapshot(
                    _activeGizmoEditSnapshot);
            }

            _activeGizmoEditSnapshot =
                null;
        }

        _wasGizmoDragging = sceneEditingEnabled && draggingAfter;
        if (!sceneEditingEnabled)
            _activeGizmoEditSnapshot = null;

        // -----------------------------------------------------
        // Light Gizmos
        // -----------------------------------------------------

        if (sceneEditingEnabled && _sceneViewPreferences.ShowHelperGizmos)
        {
            DrawLightGizmos(
                imagePosition,
                availableSize,
                width,
                height);

            DrawColliderGizmos(
                imagePosition,
                width,
                height);

            DrawCameraPreview(
                imagePosition,
                availableSize);
        }

        // -----------------------------------------------------
        // Selection
        // -----------------------------------------------------

        bool altHeld =
            ImGui.IsKeyDown(
                ImGuiKey.LeftAlt) ||
            ImGui.IsKeyDown(
                ImGuiKey.RightAlt);

        if (sceneEditingEnabled && imageHovered && !altHeld && !gizmoUsingMouse && !uiWidgetUsingMouse &&
            ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _boxSelectionCandidate = true;
            _boxSelectionStart = ImGui.GetMousePos();
        }

        if (sceneEditingEnabled && _boxSelectionCandidate && ImGui.IsMouseDown(ImGuiMouseButton.Left) &&
            Vector2.Distance(_boxSelectionStart, ImGui.GetMousePos()) > 6.0f)
        {
            _boxSelectionActive = true;
        }

        if (sceneEditingEnabled && _boxSelectionActive)
        {
            Vector2 current = ImGui.GetMousePos();
            Vector2 minimum = Vector2.Min(_boxSelectionStart, current);
            Vector2 maximum = Vector2.Max(_boxSelectionStart, current);
            ImDrawListPtr drawList = ImGui.GetWindowDrawList();
            uint fill = ImGui.ColorConvertFloat4ToU32(new Vector4(0.10f, 0.48f, 1.0f, 0.16f));
            uint border = ImGui.ColorConvertFloat4ToU32(new Vector4(0.25f, 0.70f, 1.0f, 0.95f));
            drawList.AddRectFilled(minimum, maximum, fill);
            drawList.AddRect(minimum, maximum, border, 0.0f, ImDrawFlags.None, 1.5f);
        }

        if (sceneEditingEnabled && _boxSelectionCandidate && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            if (_boxSelectionActive)
                CompleteBoxSelection(imagePosition, width, height);
            _boxSelectionCandidate = false;
            _boxSelectionActive = false;
        }

        if (sceneEditingEnabled &&
            imageHovered &&
            !altHeld &&
            !gizmoUsingMouse &&
            !uiWidgetUsingMouse &&
            !_boxSelectionActive &&
            ImGui.IsMouseClicked(
                ImGuiMouseButton.Left))
        {
            Vector2 mousePosition =
                ImGui.GetMousePos();

            float localMouseX =
                mousePosition.X -
                imagePosition.X;

            float localMouseY =
                mousePosition.Y -
                imagePosition.Y;

            GameObject? picked = _sceneRenderer.PickGameObject(
                _scene,
                _editorCamera,
                localMouseX,
                localMouseY,
                width,
                height);
            bool additive = ImGui.IsKeyDown(ImGuiKey.LeftCtrl) || ImGui.IsKeyDown(ImGuiKey.RightCtrl);
            SelectObject(picked, additive);
        }

        ImGui.End();
    }

    private bool HasSelectedAncestor(GameObject gameObject)
    {
        for (GameObject? parent = gameObject.Parent; parent != null; parent = parent.Parent)
        {
            if (_selectedObjects.Contains(parent))
                return true;
        }

        return false;
    }

    private static Vector3 SafeScaleRatio(Vector3 value, Vector3 previous) => new(
        MathF.Abs(previous.X) < 0.00001f ? 1.0f : value.X / previous.X,
        MathF.Abs(previous.Y) < 0.00001f ? 1.0f : value.Y / previous.Y,
        MathF.Abs(previous.Z) < 0.00001f ? 1.0f : value.Z / previous.Z);

    private void CompleteBoxSelection(Vector2 imagePosition, int width, int height)
    {
        Vector2 current = ImGui.GetMousePos();
        Vector2 minimum = Vector2.Min(_boxSelectionStart, current) - imagePosition;
        Vector2 maximum = Vector2.Max(_boxSelectionStart, current) - imagePosition;
        bool additive = ImGui.IsKeyDown(ImGuiKey.LeftCtrl) || ImGui.IsKeyDown(ImGuiKey.RightCtrl);

        if (!additive)
            _selectedObjects.Clear();

        foreach (GameObject gameObject in _scene.GameObjects)
        {
            if (!gameObject.IsVisibleInEditor || gameObject.IsLockedInEditor)
                continue;

            if (!_sceneRenderer.TryWorldToViewport(
                    _editorCamera,
                    gameObject.Transform.WorldPosition,
                    width,
                    height,
                    out Vector2 point))
            {
                continue;
            }

            if (point.X >= minimum.X && point.X <= maximum.X &&
                point.Y >= minimum.Y && point.Y <= maximum.Y)
            {
                _selectedObjects.Add(gameObject);
                _selectedObject = gameObject;
            }
        }

        if (_selectedObjects.Count == 0)
            _selectedObject = null;
    }

    private void DrawCameraPreview(
        Vector2 sceneImagePosition,
        Vector2 sceneImageSize)
    {
        if (_selectedObject == null)
            return;

        Camera? camera =
            _selectedObject.GetComponent<Camera>();

        if (camera == null ||
            sceneImageSize.X < 260.0f ||
            sceneImageSize.Y < 190.0f)
        {
            return;
        }

        const float margin = 12.0f;
        const float titleHeight = 22.0f;

        float previewWidth =
            Math.Clamp(
                sceneImageSize.X * 0.32f,
                200.0f,
                320.0f);

        float previewHeight =
            previewWidth *
            9.0f /
            16.0f;

        int renderWidth =
            Math.Max(1, (int)previewWidth);

        int renderHeight =
            Math.Max(1, (int)previewHeight);

        _cameraPreviewRenderer.RenderGame(
            _scene,
            _selectedObject,
            camera,
            renderWidth,
            renderHeight);

        Vector2 panelMin =
            new(
                sceneImagePosition.X +
                sceneImageSize.X -
                previewWidth -
                margin,
                sceneImagePosition.Y +
                margin);

        Vector2 imageMin =
            panelMin +
            new Vector2(0.0f, titleHeight);

        Vector2 imageMax =
            imageMin +
            new Vector2(previewWidth, previewHeight);

        ImDrawListPtr drawList =
            ImGui.GetWindowDrawList();

        uint backgroundColor =
            ImGui.ColorConvertFloat4ToU32(
                new Vector4(0.055f, 0.06f, 0.075f, 0.96f));

        uint borderColor =
            ImGui.GetColorU32(ImGuiCol.Border);

        uint textColor =
            ImGui.GetColorU32(ImGuiCol.Text);

        drawList.AddRectFilled(
            panelMin,
            imageMax,
            backgroundColor,
            3.0f);

        drawList.AddText(
            panelMin +
            new Vector2(7.0f, 3.0f),
            textColor,
            $"Camera Preview - {_selectedObject.Name}");

        drawList.AddImage(
            (nint)_cameraPreviewRenderer.ColorTexture,
            imageMin,
            imageMax,
            new Vector2(0.0f, 1.0f),
            new Vector2(1.0f, 0.0f));

        drawList.AddRect(
            panelMin,
            imageMax,
            borderColor,
            3.0f,
            ImDrawFlags.None,
            1.0f);
    }

    private static bool IsUiObject(GameObject? gameObject) => gameObject != null &&
        (gameObject.GetComponent<Canvas>() != null || gameObject.GetComponent<UIPanel>() != null || gameObject.GetComponent<UIButton>() != null || gameObject.GetComponent<UIText>() != null || gameObject.GetComponent<UIImage>() != null);

    private static int GetUiSortOrder(GameObject gameObject) => gameObject.GetComponent<UIPanel>()?.SortOrder ??
        gameObject.GetComponent<UIButton>()?.SortOrder ?? gameObject.GetComponent<UIText>()?.SortOrder ?? gameObject.GetComponent<UIImage>()?.SortOrder ?? int.MinValue;

    private bool DrawImportedFontText(ImDrawListPtr list, string fontPath, string value, Vector4 color, Vector2 center, float height)
    {
        if(string.IsNullOrWhiteSpace(fontPath)) return false;
        try
        {
            FontAsset font=FontAsset.Load(AssetDatabase.ToAbsolutePath(fontPath));
            if(!TryGetUiThumbnail(font.AtlasPath,out uint texture,out int atlasWidth,out int atlasHeight)) return false;
            value ??= ""; float width=height*font.CellWidth/font.CellHeight;
            float x=center.X-value.Length*width*.5f; uint tint=ImGui.ColorConvertFloat4ToU32(color);
            for(int i=0;i<value.Length;i++)
            {
                int code=value[i]; if(code<font.FirstCharacter || code>=font.FirstCharacter+font.CharacterCount) code='?';
                int index=code-font.FirstCharacter, column=index%font.Columns, row=index/font.Columns;
                // Half-texel inset prevents bilinear sampling from pulling a
                // neighboring glyph into this cell at small preview sizes.
                Vector2 uv0=new((column*font.CellWidth+.5f)/atlasWidth,(row*font.CellHeight+.5f)/atlasHeight);
                Vector2 uv1=new(((column+1)*font.CellWidth-.5f)/atlasWidth,((row+1)*font.CellHeight-.5f)/atlasHeight);
                list.AddImage((nint)texture,new Vector2(x+i*width,center.Y-height*.5f),new Vector2(x+(i+1)*width,center.Y+height*.5f),uv0,uv1,tint);
            }
            return true;
        }
        catch { return false; }
    }

    private bool DrawImportedFontText(ImDrawListPtr list, UIText text, Vector2 center, float height) =>
        DrawImportedFontText(list, text.FontPath, text.Text, text.Color, center, height);

    private static void DrawSlicedUiImage(ImDrawListPtr list, uint texture, string path, Vector2 min, Vector2 max, Vector4 borders, Vector2 scale, uint tint, Vector4 sourceRect = default)
    {
        Vector4 region = UiNineSlice.UvRegion(path, sourceRect);
        foreach (var quad in UiNineSlice.Quads(min, max, borders, UiNineSlice.UvBorders(path, borders, sourceRect), scale))
            list.AddImage((nint)texture, quad.Min, quad.Max, UiNineSlice.MapUv(quad.UvMin, region), UiNineSlice.MapUv(quad.UvMax, region), tint);
    }

    private bool TryGetUiThumbnail(string path, out uint texture, out int width, out int height)
    {
        string resolved = Path.IsPathRooted(path) ? path : Path.Combine(AssetDatabase.ProjectRoot, path);
        return _thumbnailCache.TryGetFullTexture(resolved, out texture, out width, out height);
    }

    private bool DrawSceneUiWidget(Scene.Scene scene, GameObject? selected, Vector2 imageMin, Vector2 imageSize, bool imageHovered)
    {
        if (!_canvasPreview.ShowInScene)
        {
            _uiWidgetDragging = false;
            _uiWidgetEditSnapshot = null;
            return false;
        }
        Canvas? canvas = _canvasPreview.Isolate ? _canvasPreview.Resolve(scene, selected)
            : selected == null ? null : Canvas.FindOwner(selected);
        if (canvas == null)
        {
            _uiWidgetDragging = false;
            _uiWidgetEditSnapshot = null;
            return false;
        }

        Vector2 reference = new(MathF.Max(1, canvas.ReferenceResolution.X), MathF.Max(1, canvas.ReferenceResolution.Y));
        float fit = MathF.Min(imageSize.X / reference.X, imageSize.Y / reference.Y);
        Vector2 canvasSize = reference * fit;
        Vector2 canvasMin = imageMin + (imageSize - canvasSize) * .5f;
        Vector2 canvasMax = canvasMin + canvasSize;
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(imageMin, imageMin + imageSize, true);

        drawList.AddRectFilled(canvasMin, canvasMax, ImGui.ColorConvertFloat4ToU32(new Vector4(.03f, .12f, .24f, .08f)));
        drawList.AddRect(canvasMin, canvasMax, ImGui.ColorConvertFloat4ToU32(new Vector4(.12f, .7f, 1f, .9f)), 0, ImDrawFlags.None, 1.5f);
        drawList.AddText(canvasMin + new Vector2(6, 5), ImGui.ColorConvertFloat4ToU32(new Vector4(.35f, .8f, 1f, 1f)), $"{canvas.GameObject?.Name}  {reference.X:0} x {reference.Y:0}");

        Vector2 selectedCenter = Vector2.Zero;
        Vector2 selectedSize = Vector2.Zero;
        Vector2 selectedAnchor = Vector2.Zero;
        bool canMove = false;
        foreach (GameObject item in scene.GameObjects.Where(item => item.IsActiveInHierarchy && ReferenceEquals(Canvas.FindOwner(item), canvas)).OrderBy(GetUiSortOrder))
        {
            UIPanel? panel = item.GetComponent<UIPanel>();
            UIButton? button = item.GetComponent<UIButton>();
            UIText? text = item.GetComponent<UIText>();
            UIImage? image = item.GetComponent<UIImage>();
            if (panel == null && button == null && text == null && image == null) continue;
            Vector2 anchor = panel?.Anchor ?? button?.Anchor ?? text?.Anchor ?? image!.Anchor;
            Vector2 offset = panel?.Offset ?? button?.Offset ?? text?.Offset ?? image!.Offset;
            Vector2 size = panel?.Size ?? button?.Size ?? text?.Size ?? image!.Size;
            Vector2 center = canvasMin + (anchor * reference + offset) * fit;
            Vector2 half = size * fit * .5f;
            bool isSelected = ReferenceEquals(item, selected);
            Vector4 fillColor = panel?.Color ?? button?.NormalColor ?? Vector4.Zero;
            if (text == null) drawList.AddRectFilled(center - half, center + half, ImGui.ColorConvertFloat4ToU32(fillColor), panel != null ? 3f : 4f);
            if (button != null)
            {
                if (TryGetUiThumbnail(button.NormalSprite, out uint buttonTexture, out _, out _))
                    DrawSlicedUiImage(drawList, buttonTexture, button.NormalSprite, center - half, center + half, button.SpriteBorders, new Vector2(fit), 0xffffffff);
                float fontSize = MathF.Max(4f, button.FontSize * fit);
                if (!DrawImportedFontText(drawList, button.FontPath, button.Text, Vector4.One, center, fontSize))
                { Vector2 textSize = ImGui.CalcTextSize(button.Text); drawList.AddText(center - textSize * .5f, 0xffffffff, button.Text); }
            }
            if (text != null)
            {
                float fontSize = MathF.Max(4f, text.FontSize * fit);
                if(!DrawImportedFontText(drawList,text,center,fontSize))
                { Vector2 textSize = ImGui.CalcTextSize(text.Text) * (fontSize / ImGui.GetFontSize()); drawList.AddText(ImGui.GetFont(), fontSize, center - textSize * .5f, ImGui.ColorConvertFloat4ToU32(text.Color), text.Text); }
            }
            if (image != null && TryGetUiThumbnail(image.TexturePath, out uint imageTexture, out _, out _))
                DrawSlicedUiImage(drawList, imageTexture, image.TexturePath, center - half, center + half, image.SpriteBorders, new Vector2(fit), ImGui.ColorConvertFloat4ToU32(image.Tint), image.SourceRect);
            uint color = ImGui.ColorConvertFloat4ToU32(isSelected ? new Vector4(1f, .72f, .15f, 1f) : new Vector4(.2f, .65f, 1f, .42f));
            drawList.AddRect(center - half, center + half, color, 2, ImDrawFlags.None, isSelected ? 2f : 1f);
            if (isSelected)
            {
                selectedCenter = center;
                selectedSize = size;
                selectedAnchor = anchor;
                canMove = true;
            }
        }

        bool usingMouse = false;
        if (canMove && selected != null)
        {
            const float handleRadius = 7f;
            Vector2 mouse = ImGui.GetMousePos();
            Vector2 resizeHandle = selectedCenter + selectedSize * fit * .5f;
            Vector2 anchorHandle = canvasMin + selectedAnchor * canvasSize;
            bool overHandle = !_uiAnchorEditing && Vector2.Distance(mouse, selectedCenter) <= handleRadius + 3f;
            bool overResize = !_uiAnchorEditing && Vector2.Distance(mouse, resizeHandle) <= handleRadius + 3f;
            bool overAnchor = _uiAnchorEditing && Vector2.Distance(mouse, anchorHandle) <= handleRadius + 3f;
            if (_uiAnchorEditing)
            {
                drawList.AddCircle(anchorHandle, 7, ImGui.ColorConvertFloat4ToU32(overAnchor ? new Vector4(1f, .82f, .25f, 1f) : new Vector4(1f, .35f, .7f, 1f)), 4, 2f);
                drawList.AddLine(anchorHandle, selectedCenter, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, .35f, .7f, .55f)), 1f);
            }
            else
            {
                drawList.AddCircleFilled(selectedCenter, handleRadius, ImGui.ColorConvertFloat4ToU32(overHandle || _uiWidgetDragMode == UiWidgetDragMode.Move ? new Vector4(1f, .82f, .25f, 1f) : new Vector4(.15f, .65f, 1f, 1f)));
                drawList.AddLine(selectedCenter - new Vector2(12, 0), selectedCenter + new Vector2(12, 0), 0xffffffff, 1.5f);
                drawList.AddLine(selectedCenter - new Vector2(0, 12), selectedCenter + new Vector2(0, 12), 0xffffffff, 1.5f);
                drawList.AddRectFilled(resizeHandle - new Vector2(6), resizeHandle + new Vector2(6), ImGui.ColorConvertFloat4ToU32(overResize ? new Vector4(1f, .82f, .25f, 1f) : new Vector4(.2f, .8f, 1f, 1f)));
            }

            if (!_uiWidgetDragging && imageHovered && (overHandle || overResize || overAnchor) && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                _uiWidgetDragging = true;
                _uiWidgetDragMode = overResize ? UiWidgetDragMode.Resize : overAnchor ? UiWidgetDragMode.Anchor : UiWidgetDragMode.Move;
                _uiWidgetDragStartMouse = mouse;
                _uiWidgetDragStartOffset = selected.GetComponent<UIPanel>()?.Offset ?? selected.GetComponent<UIButton>()?.Offset ?? selected.GetComponent<UIText>()?.Offset ?? selected.GetComponent<UIImage>()!.Offset;
                _uiWidgetDragStartSize = selectedSize;
                _uiWidgetDragStartAnchor = selectedAnchor;
                _uiWidgetEditSnapshot = SceneSerializer.Serialize(_scene);
            }
            if (_uiWidgetDragging)
            {
                usingMouse = true;
                Vector2 delta = (mouse - _uiWidgetDragStartMouse) / MathF.Max(.0001f, fit);
                Vector2 offset = _uiWidgetDragStartOffset;
                Vector2 size = _uiWidgetDragStartSize;
                Vector2 anchor = _uiWidgetDragStartAnchor;
                if (_uiWidgetDragMode == UiWidgetDragMode.Move)
                {
                    offset += delta;
                    offset = Vector2.Round(offset / 8f) * 8f;
                    Vector2 movingCenter = anchor * reference + offset;
                    if (MathF.Abs(movingCenter.X - reference.X * .5f) < 6f) { offset.X = reference.X * .5f - anchor.X * reference.X; drawList.AddLine(new(canvasMin.X + canvasSize.X * .5f, canvasMin.Y), new(canvasMin.X + canvasSize.X * .5f, canvasMax.Y), 0xff33ccff, 1f); }
                    if (MathF.Abs(movingCenter.Y - reference.Y * .5f) < 6f) { offset.Y = reference.Y * .5f - anchor.Y * reference.Y; drawList.AddLine(new(canvasMin.X, canvasMin.Y + canvasSize.Y * .5f), new(canvasMax.X, canvasMin.Y + canvasSize.Y * .5f), 0xff33ccff, 1f); }
                }
                else if (_uiWidgetDragMode == UiWidgetDragMode.Resize)
                    size = Vector2.Max(new Vector2(8), Vector2.Round((_uiWidgetDragStartSize + delta * 2f) / 8f) * 8f);
                else if (_uiWidgetDragMode == UiWidgetDragMode.Anchor)
                {
                    anchor = Vector2.Clamp(_uiWidgetDragStartAnchor + delta / reference, Vector2.Zero, Vector2.One);
                    anchor = Vector2.Round(anchor * 20f) / 20f;
                    offset = _uiWidgetDragStartOffset + (_uiWidgetDragStartAnchor - anchor) * reference;
                }
                if (selected.GetComponent<UIPanel>() is { } panel) { panel.Offset = offset; panel.Size = size; panel.Anchor = anchor; }
                if (selected.GetComponent<UIButton>() is { } button) { button.Offset = offset; button.Size = size; button.Anchor = anchor; }
                if (selected.GetComponent<UIText>() is { } text) { text.Offset = offset; text.Size = size; text.Anchor = anchor; }
                if (selected.GetComponent<UIImage>() is { } image) { image.Offset = offset; image.Size = size; image.Anchor = anchor; }
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                {
                    _uiWidgetDragging = false;
                    _uiWidgetDragMode = UiWidgetDragMode.None;
                    string current = SceneSerializer.Serialize(_scene);
                    if (_uiWidgetEditSnapshot != null && current != _uiWidgetEditSnapshot) RecordUndoSnapshot(_uiWidgetEditSnapshot);
                    _uiWidgetEditSnapshot = null;
                }
            }
            usingMouse |= overHandle || overResize || overAnchor;
        }

        drawList.PopClipRect();
        return usingMouse;
    }

    private void DrawGameView()
    {
        ImGui.Begin("Game");

        bool gameWindowFocused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);

        if (IsPlaying && !gameWindowFocused)
            ImGui.TextDisabled("Click the Game view to send gameplay input.");
        else if (_runtimePaused)
            ImGui.TextDisabled("PAUSED");

        Vector2 availableSize = ImGui.GetContentRegionAvail();
        int width = Math.Max(1, _projectSettings.RenderSettings.TargetWidth);
        int height = Math.Max(1, _projectSettings.RenderSettings.TargetHeight);
        float previewScale = MathF.Min(availableSize.X / width, availableSize.Y / height);
        Vector2 previewSize = new(
            MathF.Max(1.0f, width * previewScale),
            MathF.Max(1.0f, height * previewScale));

        GameObject? cameraObject = null;
        Camera? camera = null;

        Scene.Scene displayedScene =
            _runtimeScene ??
            _scene;

        foreach (GameObject gameObject in displayedScene.GameObjects)
        {
            Camera? candidate = gameObject.GetComponent<Camera>();
            if (candidate != null && candidate.IsPrimary)
            {
                cameraObject = gameObject;
                camera = candidate;
                break;
            }
        }

        if (cameraObject == null)
        {
            foreach (GameObject gameObject in displayedScene.GameObjects)
            {
                Camera? candidate = gameObject.GetComponent<Camera>();
                if (candidate != null)
                {
                    cameraObject = gameObject;
                    camera = candidate;
                    break;
                }
            }
        }

        if (cameraObject != null && camera != null)
        {
            _gameRenderer.RenderGame(displayedScene, cameraObject, camera, width, height);

            Vector2 previewOffset = Vector2.Max(Vector2.Zero, (availableSize - previewSize) * 0.5f);
            ImGui.SetCursorPos(ImGui.GetCursorPos() + previewOffset);

            ImGui.Image(
                (nint)_gameRenderer.ColorTexture,
                previewSize,
                new Vector2(0.0f, 1.0f),
                new Vector2(1.0f, 0.0f));
            DrawRuntimeUI(displayedScene, ImGui.GetItemRectMin(), ImGui.GetItemRectSize(), width, height);
            if (_loadingRequest != null) _loadingPresented = true;
        }
        else
        {
            Vector2 textSize = ImGui.CalcTextSize("No Camera in scene");
            ImGui.SetCursorPos(new Vector2(
                Math.Max(0.0f, (availableSize.X - textSize.X) * 0.5f),
                Math.Max(0.0f, (availableSize.Y - textSize.Y) * 0.5f)));
            ImGui.TextDisabled("No Camera in scene");
        }

        _gameViewHasInputFocus = IsPlaying && gameWindowFocused;
        ImGui.End();
    }

    private void DrawRuntimeUI(Scene.Scene scene, Vector2 imageMin, Vector2 imageSize, int width, int height)
    {
        Canvas[] canvases = scene.GameObjects.Where(item => item.IsActiveInHierarchy).Select(item => item.GetComponent<Canvas>())
            .Where(item => item != null).Cast<Canvas>().ToArray();
        if (canvases.Length == 0) return;
        if (IsPlaying && _loadingRequest == null && ImGui.IsKeyPressed(ImGuiKey.Escape) &&
            (scene.MenuNavigation.Current != null || Interactable.ActiveCanvas(scene) != null || canvases.Any(c => !c.IsLoadingScreen && c.ToggleWithMenuInput)))
        {
            if (Interactable.ActiveCanvas(scene) is {} dialogue) scene.MenuNavigation.Cancel(dialogue);
            else if (scene.MenuNavigation.Current != null) scene.MenuNavigation.Cancel(null);
            else
            {
                Canvas toggle = canvases.FirstOrDefault(item => !item.IsLoadingScreen && item.ToggleWithMenuInput && item.PauseGameplayWhenVisible) ?? canvases.First(c => !c.IsLoadingScreen && c.ToggleWithMenuInput);
                toggle.IsVisible = !toggle.IsVisible;
            }
        }
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(imageMin, imageMin + imageSize, true);

        UIButton[] navigationButtons = scene.GameObjects.Where(item => item.IsActiveInHierarchy && Canvas.FindOwner(item)?.IsVisible == true)
            .Select(item => item.GetComponent<UIButton>()).Where(button => button?.Interactable == true).Cast<UIButton>().OrderBy(button => button.SortOrder).ToArray();
        if (IsPlaying && navigationButtons.Length > 0)
        {
            _runtimeUiFocusIndex = scene.MenuNavigation.ResolveFocus(navigationButtons, _runtimeUiFocusIndex);
            float controllerY = RuntimeInput.ControllerAxes.Count > 1 ? RuntimeInput.ControllerAxes[1] : 0f;
            int direction = ImGui.IsKeyPressed(ImGuiKey.UpArrow) || ImGui.IsKeyPressed(ImGuiKey.W) ? -1 : ImGui.IsKeyPressed(ImGuiKey.DownArrow) || ImGui.IsKeyPressed(ImGuiKey.S) ? 1 : 0;
            if (!_runtimeUiAxisLatched && MathF.Abs(controllerY) > .55f) direction = controllerY > 0 ? 1 : -1;
            _runtimeUiAxisLatched = MathF.Abs(controllerY) > .35f;
            if (direction != 0)
            {
                _runtimeUiFocusIndex = (_runtimeUiFocusIndex + direction + navigationButtons.Length) % navigationButtons.Length;
                Canvas.FindOwner(navigationButtons[_runtimeUiFocusIndex].GameObject!)?.PlayNavigateSound();
            }
            _runtimeUiFocusIndex = Math.Clamp(_runtimeUiFocusIndex, 0, navigationButtons.Length - 1);
            bool activate = ImGui.IsKeyDown(ImGuiKey.Enter) || ImGui.IsKeyDown(ImGuiKey.Space) || (RuntimeInput.ControllerButtons.Count > 0 && RuntimeInput.ControllerButtons[0]);
            bool cancel = RuntimeInput.ControllerButtons.Count > 1 && RuntimeInput.ControllerButtons[1];
            for (int i = 0; i < navigationButtons.Length; i++) navigationButtons[i].IsHovered = i == _runtimeUiFocusIndex;
            navigationButtons[_runtimeUiFocusIndex].IsPressed = activate;
            if (activate && !_runtimeUiPreviousActivate && !RuntimeInput.InteractionConsumed)
            {
                navigationButtons[_runtimeUiFocusIndex].WasClicked = true;
                Canvas.FindOwner(navigationButtons[_runtimeUiFocusIndex].GameObject!)?.PlaySubmitSound();
            }
            if (cancel && !_runtimeUiPreviousCancel)
            {
                Canvas? owner = Canvas.FindOwner(navigationButtons[_runtimeUiFocusIndex].GameObject!);
                scene.MenuNavigation.Cancel(owner);
            }
            _runtimeUiPreviousActivate = activate; _runtimeUiPreviousCancel = cancel;
        }
        else if (IsPlaying)
        {
            bool cancel = RuntimeInput.ControllerButtons.Count > 1 && RuntimeInput.ControllerButtons[1];
            if (cancel && !_runtimeUiPreviousCancel) scene.MenuNavigation.Cancel(Interactable.ActiveCanvas(scene));
            _runtimeUiPreviousCancel = cancel;
        }

        Vector2 mouse = ImGui.GetMousePos();
        Canvas? focusedPreview = IsPlaying ? null : _canvasPreview.Resolve(scene, _selectedObject);
        foreach (Canvas canvas in canvases.Where(item => IsPlaying ? item.IsVisible :
                     !_canvasPreview.Isolate || ReferenceEquals(item, focusedPreview)))
        {
        Vector2 reference = new(MathF.Max(1, canvas.ReferenceResolution.X), MathF.Max(1, canvas.ReferenceResolution.Y));
        Vector2 scale = imageSize / reference;
        foreach (GameObject item in scene.GameObjects.Where(item => item.IsActiveInHierarchy && ReferenceEquals(Canvas.FindOwner(item), canvas)).OrderBy(GetUiSortOrder))
        {
            UIPanel? panel = item.GetComponent<UIPanel>();
            UIButton? button = item.GetComponent<UIButton>();
            UIText? text = item.GetComponent<UIText>();
            UIImage? image = item.GetComponent<UIImage>();
            if (panel != null)
            {
                Vector2 center = imageMin + panel.Anchor * imageSize + panel.Offset * scale;
                Vector2 half = panel.Size * scale * .5f;
                drawList.AddRectFilled(center - half, center + half, ImGui.ColorConvertFloat4ToU32(panel.Color), 3);
            }
            else if (button != null)
            {
            bool wasHovered = button.IsHovered;
            bool navigationFocused = wasHovered;
            bool navigationPressed = button.IsPressed;
            bool navigationClicked = button.WasClicked;
            button.WasClicked = false;
            Vector2 center = imageMin + button.Anchor * imageSize + button.Offset * scale;
            Vector2 half = button.Size * scale * .5f;
            Vector2 min = center - half;
            Vector2 max = center + half;
            bool hovered = button.Interactable && (navigationFocused || (mouse.X >= min.X && mouse.X <= max.X && mouse.Y >= min.Y && mouse.Y <= max.Y));
            bool pressed = navigationPressed || (hovered && ImGui.IsMouseDown(ImGuiMouseButton.Left));
            button.IsHovered = hovered;
            button.IsPressed = pressed;
            bool mouseClicked = IsPlaying && hovered && ImGui.IsMouseReleased(ImGuiMouseButton.Left);
            button.WasClicked = navigationClicked || mouseClicked;
            Canvas? buttonCanvas = Canvas.FindOwner(button.GameObject!);
            if (IsPlaying && hovered && !wasHovered) buttonCanvas?.PlayNavigateSound();
            if (mouseClicked) buttonCanvas?.PlaySubmitSound();
            if (button.WasClicked) Canvas.ApplyCanvasAction(scene, button);
            Vector4 color = pressed ? button.PressedColor : hovered ? button.HoverColor : button.NormalColor;
            drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(color), 4);
            string sprite = pressed && !string.IsNullOrWhiteSpace(button.PressedSprite) ? button.PressedSprite : hovered && !string.IsNullOrWhiteSpace(button.HoverSprite) ? button.HoverSprite : button.NormalSprite;
            if (TryGetUiThumbnail(sprite, out uint buttonTexture, out _, out _)) DrawSlicedUiImage(drawList, buttonTexture, sprite, min, max, button.SpriteBorders, scale, 0xffffffff);
            drawList.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(.1f, .65f, 1, 1)), 4);
            float buttonFontSize = MathF.Max(4f, button.FontSize * scale.Y);
            if (!DrawImportedFontText(drawList, button.FontPath, button.Text, Vector4.One, center, buttonFontSize))
            { Vector2 textSize = ImGui.CalcTextSize(button.Text); drawList.AddText(center - textSize * .5f, 0xffffffff, button.Text); }
            }
            else if (text != null)
            {
            Vector2 center = imageMin + text.Anchor * imageSize + text.Offset * scale;
            float fontSize = MathF.Max(4f, text.FontSize * scale.Y);
            if(!DrawImportedFontText(drawList,text,center,fontSize))
            { Vector2 textSize = ImGui.CalcTextSize(text.Text) * (fontSize / ImGui.GetFontSize()); drawList.AddText(ImGui.GetFont(), fontSize, center - textSize * .5f, ImGui.ColorConvertFloat4ToU32(text.Color), text.Text); }
            }
            else if (image != null)
            {
                Vector2 center = imageMin + image.Anchor * imageSize + image.Offset * scale; Vector2 half = image.Size * scale * .5f;
                if (TryGetUiThumbnail(image.TexturePath, out uint imageTexture, out _, out _)) DrawSlicedUiImage(drawList, imageTexture, image.TexturePath, center - half, center + half, image.SpriteBorders, scale, ImGui.ColorConvertFloat4ToU32(image.Tint), image.SourceRect);
            }
        }
        }

        drawList.PopClipRect();
    }

    // =========================================================
    // Scene Light Gizmos
    // =========================================================

    private void DrawLightGizmos(
        Vector2 imagePosition,
        Vector2 imageSize,
        int width,
        int height)
    {
        ImDrawListPtr drawList =
            ImGui.GetWindowDrawList();

        Vector2 mouse =
            ImGui.GetMousePos();

        GameObject? clickedLight =
            null;

        foreach (
            GameObject gameObject
            in _scene.GameObjects)
        {
            Light? light =
                gameObject
                    .GetComponent<Light>();

            if (light ==
                null)
            {
                continue;
            }

            if (!_sceneRenderer.TryWorldToViewport(
                    _editorCamera,
                    gameObject.Transform.WorldPosition,
                    width,
                    height,
                    out Vector2 localPosition))
            {
                continue;
            }

            Vector2 screenPosition =
                imagePosition +
                localPosition;

            bool selected =
                _selectedObject ==
                gameObject;

            float iconRadius =
                selected
                    ? 8.0f
                    : 6.0f;

            uint iconColor =
                ImGui.ColorConvertFloat4ToU32(
                    new Vector4(
                        light.Color,
                        1.0f));

            uint outlineColor =
                ImGui.GetColorU32(
                    selected
                        ? ImGuiCol.Text
                        : ImGuiCol.Border);

            if (light.Type ==
                LightType.Point)
            {
                drawList.AddCircleFilled(
                    screenPosition,
                    iconRadius,
                    iconColor,
                    16);

                drawList.AddCircle(
                    screenPosition,
                    iconRadius +
                    2.0f,
                    outlineColor,
                    16,
                    selected
                        ? 2.0f
                        : 1.0f);

                if (selected)
                {
                    Vector3 edgeWorld =
                        gameObject.Transform.WorldPosition +
                        new Vector3(
                            light.Range,
                            0.0f,
                            0.0f);

                    if (_sceneRenderer.TryWorldToViewport(
                            _editorCamera,
                            edgeWorld,
                            width,
                            height,
                            out Vector2 edgeLocal))
                    {
                        float radiusPixels =
                            Vector2.Distance(
                                localPosition,
                                edgeLocal);

                        if (radiusPixels >
                                1.0f &&
                            radiusPixels <
                                2000.0f)
                        {
                            drawList.AddCircle(
                                screenPosition,
                                radiusPixels,
                                ImGui.GetColorU32(
                                    ImGuiCol.TextDisabled),
                                48,
                                1.0f);
                        }
                    }
                }
            }
            else if (light.Type ==
                LightType.Spot)
            {
                drawList.AddCircleFilled(
                    screenPosition,
                    iconRadius,
                    iconColor,
                    16);

                drawList.AddCircle(
                    screenPosition,
                    iconRadius +
                    2.0f,
                    outlineColor,
                    16,
                    selected
                        ? 2.0f
                        : 1.0f);

                Vector3 direction =
                    _sceneRenderer.GetLightDirection(
                        gameObject);

                Vector3 coneEndWorld =
                    gameObject.Transform.WorldPosition +
                    direction *
                    light.Range;

                if (_sceneRenderer.TryWorldToViewport(
                        _editorCamera,
                        coneEndWorld,
                        width,
                        height,
                        out Vector2 coneEndLocal))
                {
                    Vector2 coneEnd =
                        imagePosition +
                        coneEndLocal;

                    drawList.AddLine(
                        screenPosition,
                        coneEnd,
                        iconColor,
                        selected
                            ? 2.0f
                            : 1.0f);

                    if (selected)
                    {
                        float halfAngleRadians =
                            light.SpotAngle *
                            0.5f *
                            (MathF.PI /
                             180.0f);

                        float coneRadiusWorld =
                            MathF.Tan(
                                halfAngleRadians) *
                            light.Range;

                        // Build a simple perpendicular in world space.
                        Vector3 perpendicular =
                            Vector3.Cross(
                                direction,
                                Vector3.UnitY);

                        if (perpendicular.LengthSquared() <
                            0.0001f)
                        {
                            perpendicular =
                                Vector3.UnitX;
                        }
                        else
                        {
                            perpendicular =
                                Vector3.Normalize(
                                    perpendicular);
                        }

                        Vector3 coneLeftWorld =
                            coneEndWorld +
                            perpendicular *
                            coneRadiusWorld;

                        Vector3 coneRightWorld =
                            coneEndWorld -
                            perpendicular *
                            coneRadiusWorld;

                        if (_sceneRenderer.TryWorldToViewport(
                                _editorCamera,
                                coneLeftWorld,
                                width,
                                height,
                                out Vector2 coneLeftLocal) &&
                            _sceneRenderer.TryWorldToViewport(
                                _editorCamera,
                                coneRightWorld,
                                width,
                                height,
                                out Vector2 coneRightLocal))
                        {
                            Vector2 coneLeft =
                                imagePosition +
                                coneLeftLocal;

                            Vector2 coneRight =
                                imagePosition +
                                coneRightLocal;

                            drawList.AddLine(
                                screenPosition,
                                coneLeft,
                                iconColor,
                                1.0f);

                            drawList.AddLine(
                                screenPosition,
                                coneRight,
                                iconColor,
                                1.0f);

                            drawList.AddLine(
                                coneLeft,
                                coneRight,
                                ImGui.GetColorU32(
                                    ImGuiCol.TextDisabled),
                                1.0f);
                        }
                    }
                }
            }
            else
            {
                // Sun-like circle.
                drawList.AddCircleFilled(
                    screenPosition,
                    iconRadius,
                    iconColor,
                    16);

                drawList.AddCircle(
                    screenPosition,
                    iconRadius +
                    2.0f,
                    outlineColor,
                    16,
                    selected
                        ? 2.0f
                        : 1.0f);

                // Direction arrow.
                Vector3 direction =
                    _sceneRenderer.GetLightDirection(
                        gameObject);

                Vector3 arrowEndWorld =
                    gameObject.Transform.WorldPosition +
                    direction *
                    1.5f;

                if (_sceneRenderer.TryWorldToViewport(
                        _editorCamera,
                        arrowEndWorld,
                        width,
                        height,
                        out Vector2 arrowEndLocal))
                {
                    Vector2 arrowEnd =
                        imagePosition +
                        arrowEndLocal;

                    drawList.AddLine(
                        screenPosition,
                        arrowEnd,
                        iconColor,
                        selected
                            ? 2.0f
                            : 1.0f);

                    Vector2 delta =
                        arrowEnd -
                        screenPosition;

                    if (delta.LengthSquared() >
                        0.001f)
                    {
                        Vector2 dir =
                            Vector2.Normalize(
                                delta);

                        Vector2 perp =
                            new Vector2(
                                -dir.Y,
                                dir.X);

                        Vector2 arrowBase =
                            arrowEnd -
                            dir *
                            8.0f;

                        drawList.AddTriangleFilled(
                            arrowEnd,
                            arrowBase +
                            perp *
                            4.0f,
                            arrowBase -
                            perp *
                            4.0f,
                            iconColor);
                    }
                }
            }

            // Clickable area around the icon.
            float clickRadius =
                12.0f;

            if (ImGui.IsMouseClicked(
                    ImGuiMouseButton.Left) &&
                Vector2.DistanceSquared(
                    mouse,
                    screenPosition) <=
                clickRadius *
                clickRadius)
            {
                clickedLight =
                    gameObject;
            }
        }

        if (clickedLight !=
            null)
        {
            _selectedObject =
                clickedLight;
        }
    }

    private void DrawColliderGizmos(
        Vector2 imagePosition,
        int width,
        int height)
    {
        if (_selectedObject == null)
            return;

        Collider? collider =
            _selectedObject.GetComponent<BoxCollider>() ??
            _selectedObject.GetComponent<SphereCollider>() ??
            _selectedObject.GetComponent<CapsuleCollider>() ??
            (Collider?)_selectedObject.GetComponent<MeshCollider>();

        if (collider == null)
            return;

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        uint color = ImGui.ColorConvertFloat4ToU32(
            collider.IsTrigger
                ? new Vector4(0.95f, 0.65f, 0.15f, 1.0f)
                : new Vector4(0.2f, 0.9f, 0.75f, 1.0f));

        Vector3 center =
            collider.GameObject.Transform.WorldPosition + collider.Center;

        bool Project(Vector3 world, out Vector2 screen)
        {
            if (_sceneRenderer.TryWorldToViewport(_editorCamera, world, width, height, out Vector2 local))
            {
                screen = imagePosition + local;
                return true;
            }
            screen = default;
            return false;
        }

        void DrawRing(Vector3 ringCenter, Vector3 axisA, Vector3 axisB, float radius, int segments = 32)
        {
            Vector2 previous = default;
            bool hadPrevious = false;
            for (int index = 0; index <= segments; index++)
            {
                float angle = index / (float)segments * MathF.Tau;
                Vector3 point = ringCenter + (axisA * MathF.Cos(angle) + axisB * MathF.Sin(angle)) * radius;
                bool projected = Project(point, out Vector2 current);
                if (projected && hadPrevious) drawList.AddLine(previous, current, color, 2.0f);
                previous = current;
                hadPrevious = projected;
            }
        }

        if (collider is SphereCollider sphere)
        {
            Vector3 scale = Vector3.Abs(collider.GameObject.Transform.WorldScale);
            float radius = MathF.Abs(sphere.Radius) * MathF.Max(scale.X, MathF.Max(scale.Y, scale.Z));

            if (_sceneRenderer.TryWorldToViewport(_editorCamera, center, width, height, out Vector2 centerLocal) &&
                _sceneRenderer.TryWorldToViewport(_editorCamera, center + Vector3.UnitX * radius, width, height, out Vector2 edgeLocal))
            {
                float radiusPixels = Vector2.Distance(centerLocal, edgeLocal);
                drawList.AddCircle(imagePosition + centerLocal, radiusPixels, color, 48, 2.0f);
            }

            return;
        }

        if (collider is CapsuleCollider capsule)
        {
            Vector3 scale = Vector3.Abs(collider.GameObject.Transform.WorldScale);
            float radius = MathF.Abs(capsule.Radius) * MathF.Max(scale.X, scale.Z);
            float capsuleHeight = MathF.Max(radius * 2.0f, MathF.Abs(capsule.Height * scale.Y));
            float halfLine = MathF.Max(0.0f, capsuleHeight * 0.5f - radius);
            Vector3 bottom = center - Vector3.UnitY * halfLine;
            Vector3 top = center + Vector3.UnitY * halfLine;
            DrawRing(bottom, Vector3.UnitX, Vector3.UnitZ, radius);
            DrawRing(top, Vector3.UnitX, Vector3.UnitZ, radius);
            DrawRing(bottom, Vector3.UnitX, Vector3.UnitY, radius);
            DrawRing(top, Vector3.UnitX, Vector3.UnitY, radius);
            DrawRing(bottom, Vector3.UnitZ, Vector3.UnitY, radius);
            DrawRing(top, Vector3.UnitZ, Vector3.UnitY, radius);
            foreach (Vector3 side in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ })
                if (Project(bottom + side * radius, out Vector2 from) && Project(top + side * radius, out Vector2 to))
                    drawList.AddLine(from, to, color, 2.0f);
            return;
        }

        if (collider is MeshCollider)
        {
            MeshRenderer? renderer = collider.GameObject.GetComponent<MeshRenderer>();
            if (renderer == null) return;
            Mesh mesh = renderer.MeshData;
            Vector3 worldScale = collider.GameObject.Transform.WorldScale;
            Vector3 worldRotation = collider.GameObject.Transform.WorldRotation;
            const float degreesToRadians = MathF.PI / 180.0f;
            Quaternion orientation = Quaternion.CreateFromYawPitchRoll(
                worldRotation.Y * degreesToRadians,
                worldRotation.X * degreesToRadians,
                worldRotation.Z * degreesToRadians);
            Vector3 WorldVertex(uint vertexIndex)
            {
                int offset = checked((int)vertexIndex * Mesh.FloatsPerVertex);
                Vector3 local = new(mesh.VertexData[offset], mesh.VertexData[offset + 1], mesh.VertexData[offset + 2]);
                return center + Vector3.Transform(local * worldScale, orientation);
            }
            MeshSubmesh section = renderer.SubmeshIndex >= 0
                ? renderer.GetSubmesh(0)
                : new MeshSubmesh("Collider", 0, mesh.Indices.Length);
            int indexLimit = Math.Min(section.IndexStart + section.IndexCount,
                section.IndexStart + 6000);
            for (int index = section.IndexStart; index + 2 < indexLimit; index += 3)
            {
                Vector3 a = WorldVertex(mesh.Indices[index]);
                Vector3 b = WorldVertex(mesh.Indices[index + 1]);
                Vector3 c = WorldVertex(mesh.Indices[index + 2]);
                if (Project(a, out Vector2 pa) && Project(b, out Vector2 pb) && Project(c, out Vector2 pc))
                {
                    drawList.AddLine(pa, pb, color, 1.0f);
                    drawList.AddLine(pb, pc, color, 1.0f);
                    drawList.AddLine(pc, pa, color, 1.0f);
                }
            }
            return;
        }

        BoxCollider box = (BoxCollider)collider;
        Vector3 half = Vector3.Abs(box.Size * box.GameObject.Transform.WorldScale) * 0.5f;
        Vector3[] corners =
        {
            center + new Vector3(-half.X, -half.Y, -half.Z),
            center + new Vector3( half.X, -half.Y, -half.Z),
            center + new Vector3( half.X,  half.Y, -half.Z),
            center + new Vector3(-half.X,  half.Y, -half.Z),
            center + new Vector3(-half.X, -half.Y,  half.Z),
            center + new Vector3( half.X, -half.Y,  half.Z),
            center + new Vector3( half.X,  half.Y,  half.Z),
            center + new Vector3(-half.X,  half.Y,  half.Z)
        };

        Vector2[] projected = new Vector2[8];
        for (int index = 0; index < corners.Length; index++)
        {
            if (!_sceneRenderer.TryWorldToViewport(_editorCamera, corners[index], width, height, out projected[index]))
                return;

            projected[index] += imagePosition;
        }

        int[] edges =
        {
            0,1, 1,2, 2,3, 3,0,
            4,5, 5,6, 6,7, 7,4,
            0,4, 1,5, 2,6, 3,7
        };

        for (int index = 0; index < edges.Length; index += 2)
            drawList.AddLine(projected[edges[index]], projected[edges[index + 1]], color, 2.0f);
    }

    // =========================================================
    // Cleanup
    // =========================================================

    public void Dispose()
    {
        _splashTextureCache.Dispose();

        _thumbnailCache.Dispose();

        _cameraPreviewRenderer.Dispose();

        _gameRenderer.Dispose();

        _sceneRenderer.Dispose();
    }
}
