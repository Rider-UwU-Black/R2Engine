using System.Numerics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace R2Engine.Editor.Scene;

public sealed class ScenePackageExportResult
{
    public int SceneCount { get; init; }
    public int InstanceCount { get; init; }
    public long TotalBytes { get; init; }
    public IReadOnlyList<string> ScriptWarnings { get; init; } = Array.Empty<string>();
}

public static class ScenePackageExporter
{
    private const uint MissingAsset = uint.MaxValue;

    public static ScenePackageExportResult Export(IEnumerable<string> scenePaths, string buildDirectory, ProjectSettings? settings = null)
    {
        bool disableAdvancedPs2Lighting = string.Equals(
            Environment.GetEnvironmentVariable("R2_PS2_DISABLE_ADVANCED_LIGHTING"),
            "1", StringComparison.Ordinal);
        ProjectSettings projectSettings = settings ?? ProjectSettings.Load(
            Path.Combine(AssetDatabase.ProjectRoot, "ProjectSettings.json"));
        string root = Path.Combine(buildDirectory, "R2Data", "Scenes");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        Directory.CreateDirectory(root);
        int sceneCount = 0;
        int maximumVersion = 24;
        int instanceCount = 0;
        long totalBytes = 0;
        string[] includedScenes = scenePaths.ToArray();
        var scriptReport = new List<Ps2ScriptExport.Entry>();
        var scriptWarnings = new List<string>();
        foreach (string scenePath in includedScenes)
        {
            Scene scene = SceneSerializer.Deserialize(File.ReadAllText(scenePath));
            LoadingScreen.Attach(scene, projectSettings.LoadingScreenPrefab);
            var scriptMotions = Ps2ScriptExport.CompileScene(scene, scenePath, scriptReport, scriptWarnings, projectSettings.InputActions);
            var doors=scene.GameObjects.Where(o=>o.IsActiveInHierarchy && o.GetComponent<SlidingDoor>()!=null).ToArray();
            if(doors.Length>64)throw new InvalidOperationException("PS2 supports 64 sliding doors per scene.");
            bool hasScripts = scriptMotions.Count > 0;
            bool hasTimers = scriptMotions.Values.Any(m => m.Timer != null);
            bool hasInputScripts = scriptMotions.Values.Any(m => m.Timer?.Comparison == 7);
            bool hasPressScripts = scriptMotions.Values.Any(m => m.Timer?.Comparison == 8);
            bool hasTriggerScripts = scriptMotions.Values.Any(m => m.Timer?.Comparison == 9);
            bool hasExitScripts = scriptMotions.Values.Any(m => m.Timer?.Comparison is 10 or 11);
            bool hasToggleScripts = scriptMotions.Values.Any(m => m.Timer?.Comparison == 12);
            bool hasLoopScripts = scriptMotions.Values.Any(m => m.Timer?.Comparison == 13);
            bool hasDualPressScripts = scriptMotions.Values.Any(m => m.Timer?.Comparison == 14);
            bool hasReleaseScripts = scriptMotions.Values.Any(m => m.Timer?.Comparison == 15);
            bool hasMixedEdgeScripts = scriptMotions.Values.Any(m => m.Timer?.Comparison == 16);
            bool hasDualHeldScripts = scriptMotions.Values.Any(m => m.Timer?.Comparison == 17);
            bool hasStartScripts = scriptMotions.Values.Any(m => m.Timer?.Comparison == 18);
            bool hasStartHeldScripts = scriptMotions.Values.Any(m => m.Timer?.Comparison == 19);
            bool hasStartupBlock = scriptMotions.Values.Any(m => m.Startup != null);
            if(doors.Length>0){hasScripts=true;hasTimers=true;hasStartupBlock=true;maximumVersion=Math.Max(maximumVersion,44);}
            GameObject[] persistentObjects = scene.GameObjects
                .Where(gameObject => gameObject.IsActiveInHierarchy &&
                    gameObject.GetComponent<PersistentObject>() is { SaveTransform: true })
                .ToArray();
            GameObject? missingPersistentRenderer = persistentObjects.FirstOrDefault(gameObject =>
                gameObject.GetComponent<MeshRenderer>() == null);
            if (missingPersistentRenderer != null)
                throw new InvalidOperationException(
                    $"Persistent Object '{missingPersistentRenderer.Name}' requires a Mesh Renderer for PS2 cooking.");
            string? duplicateSaveId = persistentObjects
                .Select(gameObject => gameObject.GetComponent<PersistentObject>()!.SaveId.Trim())
                .Where(saveId => !string.IsNullOrWhiteSpace(saveId))
                .GroupBy(saveId => saveId, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1)?.Key;
            if (duplicateSaveId != null)
                throw new InvalidOperationException(
                    $"Persistent Object Save ID '{duplicateSaveId}' is duplicated in {Path.GetFileName(scenePath)}.");
            var renderers = scene.GameObjects
                .Select(gameObject => (GameObject: gameObject, Renderer: gameObject.GetComponent<MeshRenderer>()))
                .Where(item => item.Renderer != null && item.GameObject.IsActiveInHierarchy)
                .ToArray();
            // Box collision does not require render geometry. Keep collider-only
            // objects in the native instance table so level collision can stay
            // independent from the detailed visible mesh.
            var instances = renderers
                .Select(item => (GameObject: item.GameObject, Renderer: (MeshRenderer?)item.Renderer))
                .Concat(scene.GameObjects
                    .Where(gameObject => gameObject.IsActiveInHierarchy &&
                        gameObject.GetComponent<MeshRenderer>() == null &&
                        gameObject.GetComponent<BoxCollider>() != null)
                    .Select(gameObject => (GameObject: gameObject, Renderer: (MeshRenderer?)null)))
                .ToArray();
            bool hasPlayerController = scene.GameObjects.Any(gameObject =>
                gameObject.IsActiveInHierarchy && gameObject.GetComponent<PlayerController>() != null);
            bool hasCollisionLayers = instances.Any(item => item.GameObject.Components.OfType<Collider>().Any());
            if (hasPlayerController) maximumVersion = Math.Max(maximumVersion, 54);
            else if (hasCollisionLayers) maximumVersion = Math.Max(maximumVersion, 52);
            bool hasMultiMaterial = renderers.Any(item => item.Renderer!.MaterialSlotCount > 1);
            bool hasUvTransform = renderers.Any(item =>
                Enumerable.Range(0, item.Renderer!.MaterialSlotCount)
                    .Select(item.Renderer.GetMaterial)
                    .Any(material => material.TextureTiling != Vector2.One || material.TextureOffset != Vector2.Zero));
            bool hasMaterialDraws = hasMultiMaterial || hasUvTransform;
            var bakeLights = scene.GameObjects
                .Where(gameObject => gameObject.IsActiveInHierarchy &&
                    gameObject.GetComponent<Light>() is { BakeOnExport: true })
                .Select(gameObject => (GameObject: gameObject, Light: gameObject.GetComponent<Light>()!))
                .ToArray();
            var localLights = scene.GameObjects
                .Where(gameObject => gameObject.IsActiveInHierarchy &&
                    gameObject.GetComponent<Light>() is { Type: not LightType.Directional, RealtimeEnabled: true })
                .Select(gameObject => (GameObject: gameObject, Light: gameObject.GetComponent<Light>()!))
                .Take(Math.Clamp(projectSettings.RenderSettings.MaxLocalLights, 0, 3))
                .ToArray();
            bool layeredRealtimeLighting = renderers.Length > 0 && scene.GameObjects.Any(gameObject =>
                gameObject.IsActiveInHierarchy && gameObject.GetComponent<Light>() is { RealtimeEnabled: true });
            bool hasObjectSmoothLighting = !disableAdvancedPs2Lighting && renderers.Any(item =>
                Enumerable.Range(0, item.Renderer!.MaterialSlotCount).Any(slot =>
                    item.Renderer.GetMaterial(slot).LightingMode == MaterialLightingMode.ObjectSmooth));
            if (hasObjectSmoothLighting) maximumVersion = Math.Max(maximumVersion, 50);
            // UI-only/menu scenes have no vertices to bake and must not be
            // promoted into a world-rendering scene format unnecessarily.
            bool bakeStaticVertexLighting = !disableAdvancedPs2Lighting && projectSettings.Ps2BakeStaticVertexLighting &&
                bakeLights.Length > 0 && renderers.Any(item => item.GameObject.IsStatic);
            if (disableAdvancedPs2Lighting)
            {
                localLights = Array.Empty<(GameObject GameObject, Light Light)>();
                layeredRealtimeLighting = false;
            }
            if (bakeStaticVertexLighting || localLights.Length > 0 || layeredRealtimeLighting)
            {
                hasMaterialDraws = true;
                maximumVersion = Math.Max(maximumVersion, layeredRealtimeLighting ? 49 : localLights.Length > 0 ? 48 : 47);
            }
            if (hasMaterialDraws)
            {
                maximumVersion = Math.Max(maximumVersion, hasUvTransform ? 46 : 45);
                // R2SC versions are cumulative. V45 must include the v29 motion,
                // v30 condition, and v43 startup records even when they are zero.
                hasScripts = true;
                hasTimers = true;
                hasStartupBlock = true;
            }
            var animatorZones = scene.GameObjects.Where(o => o.IsActiveInHierarchy && o.GetComponent<AnimatorTriggerZone>() != null).ToArray();
            var interactables = scene.GameObjects.Where(o => o.IsActiveInHierarchy && o.GetComponent<Interactable>() != null).ToArray();
            if (interactables.Length > 64) throw new InvalidOperationException("PS2 supports 64 Interactables per scene.");
            if (animatorZones.Length > 64) throw new InvalidOperationException("PS2 supports 64 Animator Trigger Zones per scene.");
            if (animatorZones.Length > 0) maximumVersion = Math.Max(maximumVersion,25);
            if (interactables.Length > 0) maximumVersion = Math.Max(maximumVersion, 28);
            if (hasScripts) maximumVersion = Math.Max(maximumVersion, hasStartupBlock ? 43 : hasStartHeldScripts ? 42 : hasStartScripts ? 41 : hasDualHeldScripts ? 40 : hasMixedEdgeScripts ? 39 : hasReleaseScripts ? 38 : hasDualPressScripts ? 37 : hasLoopScripts ? 36 : hasToggleScripts ? 35 : hasExitScripts ? 34 : hasTriggerScripts ? 33 : hasPressScripts ? 32 : hasInputScripts ? 31 : hasTimers ? 30 : 29);
            string[] meshes = renderers.Select(item => GetMeshPackageKey(item.Renderer!))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            // This table is consumed directly by 3D mesh material slots on PS2.
            // UI sprites are cooked as assets separately and must not shift these indices.
            string[] meshTextures = renderers.SelectMany(item =>
                    Enumerable.Range(0, item.Renderer!.MaterialSlotCount)
                        .Select(slot => item.Renderer.GetMaterial(slot).TexturePath))
                .Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => Normalize(path!))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            string[] uiTextures = scene.GameObjects.SelectMany(gameObject =>
                {
                    List<string> paths = new();
                    if (gameObject.GetComponent<UIImage>() is { } image) paths.Add(image.TexturePath);
                    if (gameObject.GetComponent<UIButton>() is { } button)
                    { paths.Add(button.NormalSprite); paths.Add(button.HoverSprite); paths.Add(button.PressedSprite); }
                    if(gameObject.GetComponent<UIButton>() is { FontPath.Length:>0 } fontButton)
                        try { paths.Add(FontAsset.Load(AssetDatabase.ToAbsolutePath(fontButton.FontPath)).AtlasPath); } catch { }
                    if(gameObject.GetComponent<Interactable>() is { PromptFontPath.Length:>0 } interaction)
                        try { paths.Add(FontAsset.Load(AssetDatabase.ToAbsolutePath(interaction.PromptFontPath)).AtlasPath); } catch { }
                    if(gameObject.GetComponent<UIText>() is { FontPath.Length: >0 } text)
                        try { paths.Add(FontAsset.Load(AssetDatabase.ToAbsolutePath(text.FontPath)).AtlasPath); } catch { }
                    return paths;
                })
                .Where(path => !string.IsNullOrWhiteSpace(path)).Select(Normalize)
                .Where(path => !meshTextures.Contains(path, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            // Append UI-only textures after the stable mesh-material table. Re-sorting the combined
            // table changes existing material slots and makes world geometry sample the wrong images.
            string[] textures = meshTextures.Concat(uiTextures).ToArray();
            if (textures.Length > 16)
                throw new InvalidOperationException(
                    $"{Path.GetFileName(scenePath)} uses {textures.Length} PS2 textures including UI sprites; the native limit is 16.");
            string output = Path.Combine(root, Path.GetFileNameWithoutExtension(scenePath) + ".r2scene");
            using BinaryWriter writer = new(File.Create(output), Encoding.UTF8, false);
            GameObject? cameraObject = scene.GameObjects.FirstOrDefault(gameObject =>
                gameObject.IsActiveInHierarchy && gameObject.GetComponent<Camera>()?.IsPrimary == true)
                ?? scene.GameObjects.FirstOrDefault(gameObject =>
                    gameObject.IsActiveInHierarchy && gameObject.GetComponent<Camera>() != null);
            Camera? camera = cameraObject?.GetComponent<Camera>();
            Vector3 cameraPosition = cameraObject?.Transform.WorldPosition ?? new Vector3(0.0f, 0.0f, 6.5f);
            Vector3 cameraRotation = cameraObject?.Transform.WorldRotation ?? Vector3.Zero;

            writer.Write(Encoding.ASCII.GetBytes("R2SC"));
            int sceneVersion = hasPlayerController ? 54 : hasCollisionLayers ? 52 : hasObjectSmoothLighting ? 50 : layeredRealtimeLighting ? 49 : localLights.Length > 0 ? 48 : bakeStaticVertexLighting ? 47 : hasUvTransform ? 46 : hasMultiMaterial ? 45 : doors.Length>0 ? 44 : hasStartupBlock ? 43 : hasStartHeldScripts ? 42 : hasStartScripts ? 41 : hasDualHeldScripts ? 40 : hasMixedEdgeScripts ? 39 : hasReleaseScripts ? 38 : hasDualPressScripts ? 37 : hasLoopScripts ? 36 : hasToggleScripts ? 35 : hasExitScripts ? 34 : hasTriggerScripts ? 33 : hasPressScripts ? 32 : hasInputScripts ? 31 : hasTimers ? 30 : hasScripts ? 29 : interactables.Length > 0 ? 28 : animatorZones.Length > 0 ? 25 : 24;
            writer.Write(sceneVersion); writer.Write(meshes.Length); writer.Write(textures.Length); writer.Write(instances.Length);
            WriteVector(writer, cameraPosition);
            WriteVector(writer, cameraRotation);
            writer.Write(camera?.FieldOfView ?? 60.0f);
            float nearClip = MathF.Max(0.01f, camera?.NearClip ?? 0.1f);
            writer.Write(nearClip);
            writer.Write(MathF.Max(nearClip + 0.01f, camera?.FarClip ?? 1000.0f));
            writer.Write(projectSettings.Ps2AspectRatio == Ps2DisplayAspect.Widescreen16x9
                ? 16.0f / 9.0f : 4.0f / 3.0f);
            WriteVector(writer, scene.Environment.BackgroundColor);
            WriteVector(writer, scene.Environment.AmbientColor);
            GameObject? directionalObject = scene.GameObjects.FirstOrDefault(gameObject =>
                gameObject.IsActiveInHierarchy && gameObject.GetComponent<Light>() is
                    { Type: LightType.Directional, RealtimeEnabled: true });
            Light? directional = directionalObject?.GetComponent<Light>();
            Vector3 directionToLight = DirectionToLight(directionalObject);
            WriteVector(writer, directionToLight);
            WriteVector(writer, directional?.Color ?? Vector3.One);
            writer.Write(MathF.Max(0.0f, directional?.Intensity ?? 0.0f));
            writer.Write(directional == null ? 0u : 1u);
            WriteVector(writer, scene.Environment.FogColor);
            writer.Write(MathF.Max(0.0f, scene.Environment.FogStart));
            writer.Write(MathF.Max(scene.Environment.FogStart + 0.1f, scene.Environment.FogEnd));
            writer.Write(scene.Environment.FogEnabled ? 1u : 0u);
            foreach (string path in meshes) WriteString(writer, Path.ChangeExtension(path, ".r2mesh"));
            foreach (string path in textures) WriteString(writer, Path.ChangeExtension(path, ".r2tex"));
            foreach (var item in instances)
            {
                MeshRenderer? renderer = item.Renderer;
                string? meshKey = renderer == null ? null : GetMeshPackageKey(renderer);
                writer.Write(meshKey == null ? MissingAsset :
                    (uint)Array.FindIndex(meshes, path => string.Equals(path, meshKey, StringComparison.OrdinalIgnoreCase)));
                string? texture = renderer?.Material.TexturePath;
                writer.Write(texture == null ? MissingAsset : (uint)Array.FindIndex(textures, path => string.Equals(path, Normalize(texture), StringComparison.OrdinalIgnoreCase)));
                WriteVector(writer, item.GameObject.Transform.WorldPosition);
                WriteVector(writer, item.GameObject.Transform.WorldRotation);
                WriteVector(writer, item.GameObject.Transform.WorldScale);
                Vector4 color = renderer?.Material.BaseColor ?? Vector4.Zero;
                writer.Write(color.X); writer.Write(color.Y); writer.Write(color.Z); writer.Write(color.W);
            }
            foreach (var item in instances)
            {
                Material material = item.Renderer?.Material ?? new Material();
                writer.Write(material.LightingMode == MaterialLightingMode.Unlit ? 0u :
                    material.LightingMode == MaterialLightingMode.ObjectSmooth && !disableAdvancedPs2Lighting ? 2u : 1u);
                writer.Write((uint)material.SurfaceMode);
                writer.Write(material.DoubleSided ? 1u : 0u);
                writer.Write(material.ReceiveFog ? 1u : 0u);
                writer.Write(Math.Clamp(material.AlphaCutoff, 0.0f, 1.0f));
            }
            foreach (var item in instances)
            {
                CapsuleCollider? capsule = item.GameObject.GetComponent<CapsuleCollider>();
                MeshCollider? mesh = item.GameObject.GetComponent<MeshCollider>();
                PlaneCollider? plane = item.GameObject.GetComponent<PlaneCollider>();
                BoxCollider? box = item.GameObject.GetComponent<BoxCollider>();
                uint type = capsule != null && !capsule.IsTrigger ? 1u :
                    mesh != null && !mesh.IsTrigger ? 2u :
                    plane != null && !plane.IsTrigger ? 4u :
                    box != null && !box.IsTrigger ? 3u : 0u;
                writer.Write(type);
                Vector3 center = capsule?.Center ?? mesh?.Center ?? box?.Center ?? Vector3.Zero;
                WriteVector(writer, center);
                MeshSubmesh? colliderSection = mesh != null && item.Renderer?.SubmeshIndex >= 0
                    ? item.Renderer.GetSubmesh(0) : null;
                writer.Write(box?.Size.X ?? capsule?.Radius ?? colliderSection?.IndexStart ?? 0.0f);
                writer.Write(box?.Size.Y ?? capsule?.Height ?? colliderSection?.IndexCount ?? 0.0f);
                writer.Write(box?.Size.Z ?? 45.0f); // box Z size / character slope limit
                writer.Write(0.35f); // character step height
                if (sceneVersion >= 52)
                {
                    Collider? collider = (Collider?)capsule ?? (Collider?)mesh ?? (Collider?)plane ?? box;
                    writer.Write((uint)Math.Clamp(collider?.Layer ?? item.GameObject.Layer, 0, 31));
                    writer.Write(collider?.CollisionMask ?? uint.MaxValue);
                }
            }
            foreach (var item in instances)
            {
                BoxCollider? box = item.GameObject.GetComponent<BoxCollider>();
                ScriptComponent? script = item.GameObject.GetComponent<ScriptComponent>();
                string targetScene = "";
                bool isSceneTrigger = false;
                bool isInteractivePortal = box?.IsTrigger == true && script != null &&
                    string.Equals(Path.GetFileName(script.ScriptPath), "InteractScenePortal.cs",
                        StringComparison.OrdinalIgnoreCase);
                if (box?.IsTrigger == true && script != null &&
                    (string.Equals(Path.GetFileName(script.ScriptPath), "TriggerSceneLoader.cs",
                        StringComparison.OrdinalIgnoreCase) || isInteractivePortal) &&
                    script.FieldValues.TryGetValue("SceneName", out string? configuredTarget) &&
                    !string.IsNullOrWhiteSpace(configuredTarget))
                {
                    targetScene = configuredTarget;
                    isSceneTrigger = true;
                    if (isInteractivePortal)
                    {
                        string markerName = script.FieldValues.TryGetValue("DestinationMarker", out string? marker)
                            ? marker.Trim() : "";
                        if (markerName.Length == 0)
                            throw new InvalidOperationException($"Interact portal '{item.GameObject.Name}' needs a Destination Marker.");
                        string destinationPath = ResolveIncludedScene(configuredTarget, includedScenes);
                        Scene destination = SceneSerializer.Deserialize(File.ReadAllText(destinationPath));
                        GameObject destinationMarker = destination.GameObjects.FirstOrDefault(candidate =>
                            string.Equals(candidate.Name, markerName, StringComparison.Ordinal))
                            ?? throw new InvalidOperationException(
                                $"Interact portal '{item.GameObject.Name}' cannot find marker '{markerName}' in scene '{configuredTarget}'.");
                        InputActionBinding? action = FindAction(projectSettings,
                            script.FieldValues.TryGetValue("InputAction", out string? actionName) ? actionName : "Interact");
                        int button = action?.ControllerButton ?? 0;
                        if (button < 0 || button > 15 || action is { Type: not InputActionType.Button })
                            throw new InvalidOperationException($"Interact portal '{item.GameObject.Name}' needs a button-bound input action.");
                        Vector3 position = destinationMarker.Transform.WorldPosition;
                        Vector3 rotation = destinationMarker.Transform.WorldRotation;
                        int promptCanvas = -1;
                        if (script.FieldValues.TryGetValue("PromptCanvas", out string? promptCanvasName) &&
                            !string.IsNullOrWhiteSpace(promptCanvasName))
                        {
                            GameObject[] canvasObjects = scene.GameObjects.Where(candidate =>
                                candidate.IsActiveInHierarchy && candidate.GetComponent<Canvas>() != null).ToArray();
                            promptCanvas = Array.FindIndex(canvasObjects, candidate =>
                                string.Equals(candidate.Name, promptCanvasName, StringComparison.OrdinalIgnoreCase));
                            if (promptCanvas < 0)
                                throw new InvalidOperationException(
                                    $"Interact portal '{item.GameObject.Name}' cannot find prompt Canvas '{promptCanvasName}'.");
                        }
                        targetScene = string.Join("|", configuredTarget, "I", button.ToString(CultureInfo.InvariantCulture),
                            position.X.ToString("R", CultureInfo.InvariantCulture), position.Y.ToString("R", CultureInfo.InvariantCulture),
                            position.Z.ToString("R", CultureInfo.InvariantCulture), rotation.X.ToString("R", CultureInfo.InvariantCulture),
                            rotation.Y.ToString("R", CultureInfo.InvariantCulture), rotation.Z.ToString("R", CultureInfo.InvariantCulture),
                            promptCanvas.ToString(CultureInfo.InvariantCulture));
                        if (Encoding.UTF8.GetByteCount(targetScene) >= 128)
                            throw new InvalidOperationException($"Interact portal '{item.GameObject.Name}' has a destination path too long for PS2 export.");
                    }
                }
                writer.Write(isSceneTrigger ? 1u : 0u);
                WriteVector(writer, box?.Center ?? Vector3.Zero);
                WriteVector(writer, box?.Size ?? Vector3.Zero);
                WriteFixedString(writer, isSceneTrigger ? targetScene : "", 128);
            }
            WriteControls(writer, scene, projectSettings, sceneVersion);
            WriteAudio(writer, scene, instances);
            foreach (var item in instances)
            {
                SavePoint? savePoint = item.GameObject.GetComponent<SavePoint>();
                BoxCollider? box = item.GameObject.GetComponent<BoxCollider>();
                bool enabled = savePoint is { SaveOnTriggerEnter: true } && box?.IsTrigger == true;
                writer.Write(enabled ? 1u : 0u);
                WriteVector(writer, box?.Center ?? Vector3.Zero);
                WriteVector(writer, box?.Size ?? Vector3.Zero);
                WriteFixedString(writer, enabled ? savePoint!.SlotName : "", 32);
            }
            foreach (var item in instances)
            {
                PersistentObject? persistent = item.GameObject.GetComponent<PersistentObject>();
                bool enabled = persistent != null && !string.IsNullOrWhiteSpace(persistent.SaveId);
                writer.Write(enabled ? 1u : 0u);
                WriteFixedString(writer, enabled ? persistent!.SaveId : "", 32);
                writer.Write(persistent?.SaveTransform == true ? 1u : 0u);
                writer.Write(persistent?.SaveActiveState == true ? 1u : 0u);
                writer.Write(persistent?.SaveInteger == true ? 1u : 0u);
                writer.Write(persistent?.SaveBoolean == true ? 1u : 0u);
                writer.Write(item.GameObject.IsActive ? 1u : 0u);
                writer.Write(persistent?.IntegerValue ?? 0);
                writer.Write(persistent?.BooleanValue == true ? 1u : 0u);
            }
            var canvases = scene.GameObjects.Where(item => item.IsActiveInHierarchy && item.GetComponent<Canvas>() != null)
                .Select(item => (Object: item, Canvas: item.GetComponent<Canvas>()!)).ToArray();
            if (canvases.Length > 32) throw new InvalidOperationException("PS2 supports 32 canvases per scene, including the loading screen.");
            Canvas? canvas = canvases.FirstOrDefault().Canvas;
            var uiElements = scene.GameObjects.Where(item => item.IsActiveInHierarchy)
                .Select(item => (Object: item, Panel: item.GetComponent<UIPanel>(), Button: item.GetComponent<UIButton>(), Text: item.GetComponent<UIText>(), Image: item.GetComponent<UIImage>()))
                .Where(item => item.Panel != null || item.Button != null || item.Text != null || item.Image != null)
                .OrderBy(item => item.Panel?.SortOrder ?? item.Button?.SortOrder ?? item.Text?.SortOrder ?? item.Image?.SortOrder ?? 0).ToArray();
            Vector2 reference = canvas?.ReferenceResolution ?? new Vector2(640, 448);
            if (uiElements.Length > 64) throw new InvalidOperationException("PS2 supports 64 UI elements per scene, including the loading screen.");
            writer.Write(reference.X); writer.Write(reference.Y); writer.Write(uiElements.Length);
            uint visibleMask = 0, pauseMask = 0;
            for (int i = 0; i < canvases.Length; i++) { if (canvases[i].Canvas.StartsVisible) visibleMask |= 1u << i; if (canvases[i].Canvas.PauseGameplayWhenVisible) pauseMask |= 1u << i; }
            writer.Write(visibleMask); writer.Write(pauseMask);
            uint loadingMask = 0;
            for (int i = 0; i < canvases.Length; i++) if (canvases[i].Canvas.IsLoadingScreen) loadingMask |= 1u << i;
            writer.Write(loadingMask);
            uint toggleMask = 0, cancelMask = 0;
            for (int i = 0; i < canvases.Length; i++)
            {
                if (!canvases[i].Canvas.IsLoadingScreen && canvases[i].Canvas.ToggleWithMenuInput) toggleMask |= 1u << i;
                if (!canvases[i].Canvas.IsLoadingScreen && canvases[i].Canvas.CloseWithCancelInput) cancelMask |= 1u << i;
            }
            writer.Write(toggleMask); writer.Write(cancelMask);
            foreach (var element in uiElements)
            {
                UIButton? button = element.Button;
                UIPanel? panel = element.Panel;
                UIText? text = element.Text;
                UIImage? image = element.Image;
                Canvas? owner = Canvas.FindOwner(element.Object);
                int canvasIndex = Math.Max(0, Array.FindIndex(canvases, entry => ReferenceEquals(entry.Canvas, owner)));
                Vector2 ownerReference = owner?.ReferenceResolution ?? reference;
                Vector2 conversion = new(reference.X / MathF.Max(1, ownerReference.X), reference.Y / MathF.Max(1, ownerReference.Y));
                writer.Write(image != null ? 4u : text != null ? 3u : button != null ? 2u : 1u);
                writer.Write((button?.Interactable == true ? 1u : 0u) | ((uint)canvasIndex << 16));
                WriteVector2(writer, image?.Anchor ?? text?.Anchor ?? button?.Anchor ?? panel!.Anchor);
                WriteVector2(writer, (image?.Offset ?? text?.Offset ?? button?.Offset ?? panel!.Offset) * conversion);
                WriteVector2(writer, (image?.Size ?? text?.Size ?? button?.Size ?? panel!.Size) * conversion);
                WriteColor(writer, image?.Tint ?? text?.Color ?? button?.NormalColor ?? panel!.Color);
                WriteColor(writer, button?.HoverColor ?? Vector4.Zero);
                WriteColor(writer, button?.PressedColor ?? Vector4.Zero);
                WriteFixedString(writer, text?.Text ?? button?.Text ?? "", 64);
                int targetIndex = button == null ? 0 : Array.FindIndex(canvases, entry => string.Equals(entry.Object.Name, button.TargetCanvas, StringComparison.OrdinalIgnoreCase));
                if (button?.Action == UIButtonAction.OpenSubmenu && (targetIndex < 0 || targetIndex == canvasIndex))
                    throw new InvalidOperationException($"Button '{element.Object.Name}' needs a different active Target Canvas for OpenSubmenu.");
                writer.Write((uint)(button?.Action ?? UIButtonAction.None) | ((uint)Math.Max(0, targetIndex) << 16));
                int TextureSlot(string? path) => string.IsNullOrWhiteSpace(path) ? -1 :
                    Array.FindIndex(textures, candidate => string.Equals(candidate, Normalize(path), StringComparison.OrdinalIgnoreCase));
                string metadata = text != null && !string.IsNullOrWhiteSpace(text.FontPath)
                    ? $"#F{TextureSlot(FontAsset.Load(AssetDatabase.ToAbsolutePath(text.FontPath)).AtlasPath)}" : image != null ? $"@{TextureSlot(image.TexturePath)}" : button != null &&
                    (!string.IsNullOrWhiteSpace(button.NormalSprite) || !string.IsNullOrWhiteSpace(button.HoverSprite) || !string.IsNullOrWhiteSpace(button.PressedSprite))
                    ? $"{button.SaveSlot}|{TextureSlot(button.NormalSprite)},{TextureSlot(button.HoverSprite)},{TextureSlot(button.PressedSprite)}"
                    : button?.SaveSlot ?? "";
                WriteFixedString(writer, metadata, 32);
                writer.Write(text?.Ps2SaveLoadFeedback == true ? 1u : 0u);
                string buttonFontMetadata = button != null && !string.IsNullOrWhiteSpace(button.FontPath)
                    ? $"#F{TextureSlot(FontAsset.Load(AssetDatabase.ToAbsolutePath(button.FontPath)).AtlasPath)},{MathF.Max(4,button.FontSize)*conversion.Y}"
                    : "";
                WriteFixedString(writer, text?.SavedMessage ?? buttonFontMetadata, 64);
                WriteFixedString(writer, text?.SaveFailedMessage ?? "", 64);
                WriteFixedString(writer, text?.LoadedMessage ?? "", 64);
                WriteFixedString(writer, text?.LoadFailedMessage ?? "", 64);
                Vector4 borders = text != null ? new Vector4(text.FontSize,0,0,0) : Vector4.Max(image?.SpriteBorders ?? button?.SpriteBorders ?? Vector4.Zero, Vector4.Zero);
                WriteColor(writer, borders * new Vector4(conversion.X, conversion.Y, conversion.X, conversion.Y));
                WriteColor(writer, UiNineSlice.UvBorders(image?.TexturePath ?? button?.NormalSprite, borders, image?.SourceRect ?? Vector4.Zero));
                WriteColor(writer, UiNineSlice.UvBorders(button?.HoverSprite, borders));
                WriteColor(writer, UiNineSlice.UvBorders(button?.PressedSprite, borders));
                Vector4 sourceRegion = image != null
                    ? UiNineSlice.UvRegion(image.TexturePath, image.SourceRect)
                    : new Vector4(0,0,1,1);
                WriteColor(writer, sourceRegion);
                string sceneTarget = "";
                if (button?.Action == UIButtonAction.GoToScene)
                {
                    string requested = Path.GetFileNameWithoutExtension(button.TargetScene.Trim().Replace('\\', '/'));
                    string? included = includedScenes.FirstOrDefault(path => string.Equals(Path.GetFileNameWithoutExtension(path), requested, StringComparison.OrdinalIgnoreCase));
                    if (string.IsNullOrWhiteSpace(requested) || included == null)
                        throw new InvalidOperationException($"Button '{element.Object.Name}' target scene '{button.TargetScene}' is not included in the build.");
                    sceneTarget = Path.GetFileName(included);
                    if (Encoding.UTF8.GetByteCount(sceneTarget) >= 128)
                        throw new InvalidOperationException($"Button '{element.Object.Name}' target scene name is too long for PS2.");
                }
                WriteFixedString(writer, sceneTarget, 128);
            }
            if (animatorZones.Length > 0 || interactables.Length > 0 || hasScripts)
            {
                writer.Write(animatorZones.Length);
                foreach (var obj in animatorZones)
                {
                    var zone = obj.GetComponent<AnimatorTriggerZone>()!;
                    var box = obj.GetComponent<BoxCollider>();
                    var matches = renderers.Where(r => r.GameObject.Name == zone.TargetAnimator).ToArray();
                    var animator = matches.Length == 1 ? matches[0].GameObject.GetComponent<Animator>() : null;
                    if (box?.IsTrigger != true || animator == null || string.IsNullOrWhiteSpace(animator.ControllerPath) ||
                        !string.Equals(Path.GetExtension(matches[0].Renderer!.MeshPath), ".r2skel", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Animator Trigger Zone '{obj.Name}' needs a trigger Box Collider and a unique target with an Animator Controller.");
                    var controller = AnimatorController.Load(AssetDatabase.ToAbsolutePath(animator.ControllerPath));
                    if (!controller.Parameters.Any(p => p.Name == zone.TriggerName && p.Type == AnimatorParameterType.Trigger) ||
                        Encoding.UTF8.GetByteCount(zone.TriggerName) > 63)
                        throw new InvalidOperationException($"Animator Trigger Zone '{obj.Name}': target controller has no Trigger parameter '{zone.TriggerName}'.");
                    writer.Write(Array.FindIndex(renderers, r => r.GameObject == matches[0].GameObject));
                    WriteVector(writer, obj.Transform.WorldPosition + box.Center);
                    WriteVector(writer, Vector3.Abs(box.Size * obj.Transform.WorldScale));
                    WriteFixedString(writer, zone.TriggerName, 64);
                    var playerCollider = scene.GameObjects.FirstOrDefault(o => o.GetComponent<PlayerController>() != null)?.Components.OfType<Collider>().FirstOrDefault(c => !c.IsTrigger);
                    bool allowed = playerCollider == null || ((box.CollisionMask & (1u << Math.Clamp(playerCollider.Layer,0,31))) != 0 &&
                        (playerCollider.CollisionMask & (1u << Math.Clamp(box.Layer,0,31))) != 0);
                    writer.Write(allowed ? 1u : 0u);
                }
            }
            if (interactables.Length > 0 || hasScripts)
            {
                writer.Write(interactables.Length);
                foreach(var obj in interactables)
                {
                    var item=obj.GetComponent<Interactable>()!;
                    var box=obj.GetComponent<BoxCollider>();
                    int target=Array.FindIndex(canvases,c => string.Equals(c.Object.Name,item.TargetCanvas,StringComparison.OrdinalIgnoreCase));
                    if(canvases.Count(c=>string.Equals(c.Object.Name,item.TargetCanvas,StringComparison.OrdinalIgnoreCase))!=1)
                        throw new InvalidOperationException($"Interactable '{obj.Name}' needs a uniquely named target Canvas.");
                    var binding=FindAction(projectSettings,item.InputAction);
                    int button=binding?.ControllerButton ?? (string.Equals(item.InputAction,"Interact",StringComparison.OrdinalIgnoreCase) ? 0 : -1);
                    if(target<0 || box?.IsTrigger != true || button<0 || button>15)
                        throw new InvalidOperationException($"Interactable '{obj.Name}' needs a trigger Box Collider, valid Canvas and gamepad-bound input action.");
                    string hint=button switch { 0 => "X", 1 => "Circle", 2 => "Square", 3 => "Triangle", _ => "Button " + button };
                    string prompt=hint + " - " + item.Prompt;
                    if(Encoding.UTF8.GetByteCount(prompt)>63) throw new InvalidOperationException("PS2 interaction prompt must fit in 63 UTF-8 bytes including button hint.");
                    int source=Array.FindIndex(renderers,r => r.GameObject==obj);
                    Vector3 scale=obj.Transform.WorldScale;
                    WriteVector(writer,source>=0 ? box.Center*scale : obj.Transform.WorldPosition+box.Center*scale);
                    WriteVector(writer,Vector3.Abs(box.Size*scale));
                    writer.Write(target); writer.Write(button); WriteFixedString(writer,prompt,64);
                    writer.Write(source);
                    string[] pages=item.DialoguePages.Replace("\r","").Split('\n',StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    if(pages.Length>8) throw new InvalidOperationException($"Interactable '{obj.Name}' supports up to 8 dialogue pages on PS2.");
                    if(pages.Any(page=>Encoding.UTF8.GetByteCount(page)>63))
                        throw new InvalidOperationException($"Interactable '{obj.Name}' dialogue pages must each fit in 63 UTF-8 bytes on PS2.");
                    writer.Write(pages.Length);
                    for(int page=0;page<8;page++) WriteFixedString(writer,page<pages.Length ? pages[page] : "",64);
                    int fontSlot=string.IsNullOrWhiteSpace(item.PromptFontPath) ? -1 :
                        Array.FindIndex(textures,path=>string.Equals(path,Normalize(FontAsset.Load(AssetDatabase.ToAbsolutePath(item.PromptFontPath)).AtlasPath),StringComparison.OrdinalIgnoreCase));
                    writer.Write(fontSlot);
                    writer.Write(MathF.Max(4,item.PromptFontSize));
                }
            }
            // V45 inherits the v44 door table even when this scene has zero doors.
            if(doors.Length>0 || hasMaterialDraws)
            {
                writer.Write(doors.Length);
                var binding=FindAction(projectSettings,"Interact");int button=binding?.ControllerButton??0;
                if(button<0||button>15||binding!=null&&binding.Type!=InputActionType.Button)throw new InvalidOperationException("Sliding doors require a button-bound Interact action.");
                foreach(var obj in doors)
                {
                    var door=obj.GetComponent<SlidingDoor>()!;
                    for(var ancestor=obj.Parent;ancestor!=null;ancestor=ancestor.Parent)
                        if(ancestor.Components.Any(c=>c is ScriptComponent or SlidingDoor or Rigidbody or Animator or Rotator))
                            throw new InvalidOperationException($"Sliding door '{obj.Name}' requires static parents on PS2.");
                    int source=Array.FindIndex(renderers,r=>r.GameObject==obj);
                    if(source<0 || obj.Children.Any() || obj.GetComponent<BoxCollider>() is not {IsTrigger:false} || obj.Components.Any(c=>c is not Transform && c is not MeshRenderer && c is not BoxCollider && c is not SlidingDoor) ||
                        !float.IsFinite(door.Lift)||door.Lift<=0||!float.IsFinite(door.Speed)||door.Speed<=0||!float.IsFinite(door.Range)||door.Range<=0 ||
                        !float.IsFinite(door.Range*door.Range)||!float.IsFinite(obj.Transform.WorldPosition.Y+door.Lift))
                        throw new InvalidOperationException($"Sliding door '{obj.Name}' needs a childless mesh, solid Box Collider and positive finite Lift/Speed/Range, without other behavior components.");
                    writer.Write(source);writer.Write(button);writer.Write(door.Lift);writer.Write(door.Speed);writer.Write(door.Range);WriteVector(writer,obj.Transform.WorldPosition);
                }
            }
            if (hasMaterialDraws)
            {
                int drawCount = renderers.Sum(item => item.Renderer!.MaterialSlotCount);
                writer.Write(drawCount);
                for (int source = 0; source < renderers.Length; source++)
                {
                    MeshRenderer renderer = renderers[source].Renderer!;
                    Mesh mesh = renderer.MeshData;
                    int meshSlot = Array.FindIndex(meshes, path => string.Equals(path,
                        GetMeshPackageKey(renderer), StringComparison.OrdinalIgnoreCase));
                    for (int slot = 0; slot < renderer.MaterialSlotCount; slot++)
                    {
                        MeshSubmesh submesh = renderer.GetSubmesh(slot);
                        Material material = renderer.GetMaterial(slot);
                        string? texture = material.TexturePath;
                        writer.Write(source);
                        writer.Write(meshSlot);
                        writer.Write(texture == null ? MissingAsset : (uint)Array.FindIndex(textures,
                            path => string.Equals(path, Normalize(texture), StringComparison.OrdinalIgnoreCase)));
                        writer.Write(submesh.IndexStart);
                        writer.Write(submesh.IndexCount);
                        writer.Write(material.LightingMode == MaterialLightingMode.Unlit ? 0u :
                            material.LightingMode == MaterialLightingMode.ObjectSmooth && !disableAdvancedPs2Lighting ? 2u : 1u);
                        writer.Write((uint)material.SurfaceMode);
                        writer.Write(material.DoubleSided ? 1u : 0u);
                        writer.Write(material.ReceiveFog ? 1u : 0u);
                        writer.Write(Math.Clamp(material.AlphaCutoff, 0.0f, 1.0f));
                        WriteColor(writer, material.BaseColor);
                        if (sceneVersion >= 46)
                        {
                            WriteVector2(writer, material.TextureTiling);
                            WriteVector2(writer, material.TextureOffset);
                        }
                    }
                }
            }
            if (sceneVersion >= 47)
            {
                foreach (var item in instances)
                {
                    byte[] baked = item.Renderer != null && bakeStaticVertexLighting && CanBakeStaticLighting(item.GameObject)
                        ? BakeVertexLighting(item.Renderer!.MeshData, item.GameObject,
                            scene.Environment.AmbientColor, bakeLights)
                        : Array.Empty<byte>();
                    writer.Write(baked.Length / 3);
                    writer.Write(baked);
                }
            }
            if (sceneVersion >= 48)
            {
                writer.Write(localLights.Length);
                foreach (var item in localLights)
                {
                    writer.Write(item.Light.Type == LightType.Spot ? 1u : 0u);
                    WriteVector(writer, item.GameObject.Transform.WorldPosition);
                    WriteVector(writer, TransformForward(item.GameObject));
                    WriteVector(writer, item.Light.Color);
                    writer.Write(MathF.Max(0.0f, item.Light.Intensity));
                    writer.Write(MathF.Max(0.001f, item.Light.Range));
                    writer.Write(MathF.Cos(Math.Clamp(item.Light.SpotAngle, 1.0f, 179.0f) *
                        0.5f * MathF.PI / 180.0f));
                    if (sceneVersion >= 49) writer.Write(item.Light.RealtimeLayerMask);
                }
            }
            if (sceneVersion >= 49)
            {
                writer.Write(directional?.RealtimeLayerMask ?? 0u);
                foreach (var item in instances)
                    writer.Write((uint)Math.Clamp(item.GameObject.Layer, 0, 31));
            }
            foreach (var item in instances)
            {
                Animator? animator = item.GameObject.GetComponent<Animator>();
                bool animated = animator != null && Ps2AnimationPackageExporter.PlaybackStates(animator).Count > 0;
                writer.Write((animated ? 1u : 0u) | (animated && animator!.PlayOnStart ? 2u : 0u) |
                    (item.GameObject.GetComponent<PlayerController>() != null ? 4u : 0u));
                writer.Write(0u); // default state is first in the baked controller
            }
            if (hasScripts)
                foreach (var item in instances)
                {
                    scriptMotions.TryGetValue(item.GameObject, out var motion);
                    WriteVector(writer, motion.Position);
                    WriteVector(writer, motion.Rotation);
                }
            if (hasTimers)
                foreach (var item in instances)
                {
                    scriptMotions.TryGetValue(item.GameObject, out var motion);
                    var timer = motion.Timer;
                    writer.Write(timer?.Initial ?? 0f);
                    writer.Write(timer?.Threshold ?? 0f);
                    writer.Write(timer?.Comparison ?? 0u);
                    WriteVector(writer, timer?.ElsePosition ?? Vector3.Zero);
                    WriteVector(writer, timer?.ElseRotation ?? Vector3.Zero);
                }
            if (hasStartupBlock)
                foreach (var item in instances)
                {
                    scriptMotions.TryGetValue(item.GameObject, out var motion);
                    WriteVector(writer, motion.Startup?.Position ?? Vector3.Zero);
                    WriteVector(writer, motion.Startup?.Rotation ?? Vector3.Zero);
                }
            ++sceneCount; instanceCount += instances.Length; totalBytes += new FileInfo(output).Length;
        }
        File.WriteAllText(Path.Combine(root, "scene-manifest.json"), JsonSerializer.Serialize(new
        { Version = maximumVersion, SceneCount = sceneCount, InstanceCount = instanceCount, TotalBytes = totalBytes },
        new JsonSerializerOptions { WriteIndented = true }));
        Ps2ScriptExport.WriteReport(buildDirectory, scriptReport);
        return new ScenePackageExportResult { SceneCount = sceneCount, InstanceCount = instanceCount, TotalBytes = totalBytes, ScriptWarnings = scriptWarnings };
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static string ResolveIncludedScene(string sceneName, IEnumerable<string> scenePaths)
    {
        string? match = scenePaths.FirstOrDefault(path =>
            string.Equals(path, sceneName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(path), sceneName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileNameWithoutExtension(path), sceneName, StringComparison.OrdinalIgnoreCase));
        if (match == null)
            throw new InvalidOperationException($"Destination scene '{sceneName}' must be included in Build Scenes.");
        return match;
    }
    private static bool CanBakeStaticLighting(GameObject gameObject) => gameObject.IsStatic;

    private static Vector3 DirectionToLight(GameObject? lightObject)
    {
        if (lightObject == null) return Vector3.UnitY;
        Vector3 radians = lightObject.Transform.WorldRotation * (MathF.PI / 180.0f);
        Quaternion orientation = Quaternion.CreateFromYawPitchRoll(radians.Y, radians.X, radians.Z);
        return -Vector3.Transform(-Vector3.UnitZ, orientation);
    }

    private static byte[] BakeVertexLighting(Mesh mesh, GameObject gameObject,
        Vector3 ambient, IReadOnlyList<(GameObject GameObject, Light Light)> lights)
    {
        byte[] output = new byte[checked(mesh.VertexCount * 3)];
        Vector3 radians = gameObject.Transform.WorldRotation * (MathF.PI / 180.0f);
        Quaternion rotation = Quaternion.CreateFromYawPitchRoll(radians.Y, radians.X, radians.Z);
        for (int vertex = 0; vertex < mesh.VertexCount; vertex++)
        {
            Vector3 normal = Vector3.Transform(Vector3.Normalize(mesh.GetNormal(vertex)), rotation);
            Vector3 localPosition = mesh.GetPosition(vertex) * gameObject.Transform.WorldScale;
            Vector3 worldPosition = gameObject.Transform.WorldPosition + Vector3.Transform(localPosition, rotation);
            Vector3 lightValue = ambient;
            foreach (var item in lights)
            {
                Vector3 toLight;
                float attenuation = 1.0f;
                float cone = 1.0f;
                if (item.Light.Type == LightType.Directional)
                    toLight = DirectionToLight(item.GameObject);
                else
                {
                    toLight = item.GameObject.Transform.WorldPosition - worldPosition;
                    float distance = toLight.Length();
                    float range = MathF.Max(0.001f, item.Light.Range);
                    if (distance >= range || distance <= 0.000001f) continue;
                    toLight /= distance;
                    attenuation = Math.Clamp(1.0f - distance / range, 0.0f, 1.0f);
                    attenuation *= attenuation;
                    if (item.Light.Type == LightType.Spot)
                    {
                        float spotCos = MathF.Cos(Math.Clamp(item.Light.SpotAngle, 1.0f, 179.0f) *
                            0.5f * MathF.PI / 180.0f);
                        cone = Vector3.Dot(TransformForward(item.GameObject), -toLight) >= spotCos ? 1.0f : 0.0f;
                    }
                }
                float diffuse = MathF.Max(0.0f, Vector3.Dot(normal, Vector3.Normalize(toLight)));
                lightValue += item.Light.Color * diffuse * attenuation * cone * MathF.Max(0.0f, item.Light.Intensity);
            }
            for (int channel = 0; channel < 3; channel++)
            {
                float light = MathF.Max(0.0f, lightValue[channel]);
                // Store gamma-corrected normalized intensity. Native rendering only
                // multiplies this by material color, avoiding per-vertex sqrt work.
                output[vertex * 3 + channel] = (byte)Math.Clamp(
                    (int)MathF.Round(MathF.Sqrt(light) * 255.0f), 0, 255);
            }
        }
        return output;
    }

    private static Vector3 TransformForward(GameObject gameObject)
    {
        Vector3 radians = gameObject.Transform.WorldRotation * (MathF.PI / 180.0f);
        Quaternion orientation = Quaternion.CreateFromYawPitchRoll(radians.Y, radians.X, radians.Z);
        return Vector3.Transform(-Vector3.UnitZ, orientation);
    }
    private static string GetMeshPackageKey(MeshRenderer renderer) => Ps2AnimationPackageExporter.MeshKey(renderer);
    private static void WriteString(BinaryWriter writer, string value)
    { byte[] bytes = Encoding.UTF8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes); }
    private static void WriteFixedString(BinaryWriter writer, string value, int byteCount)
    {
        byte[] output = new byte[byteCount];
        byte[] encoded = Encoding.UTF8.GetBytes(value);
        Array.Copy(encoded, output, Math.Min(encoded.Length, byteCount - 1));
        writer.Write(output);
    }
    private static void WriteVector(BinaryWriter writer, Vector3 value)
    { writer.Write(value.X); writer.Write(value.Y); writer.Write(value.Z); }
    private static void WriteVector2(BinaryWriter writer, Vector2 value)
    { writer.Write(value.X); writer.Write(value.Y); }
    private static void WriteColor(BinaryWriter writer, Vector4 value)
    { writer.Write(value.X); writer.Write(value.Y); writer.Write(value.Z); writer.Write(value.W); }

    private static void WriteControls(BinaryWriter writer, Scene scene, ProjectSettings settings, int sceneVersion)
    {
        PlayerController? controller = scene.GameObjects
            .Where(gameObject => gameObject.IsActiveInHierarchy)
            .Select(gameObject => gameObject.GetComponent<PlayerController>())
            .FirstOrDefault(component => component != null);
        InputActionBinding? move = FindAction(settings, controller?.MoveAction ?? "Move");
        InputActionBinding? turn = FindAction(settings, controller?.TurnAction ?? "Turn");
        InputActionBinding? sprint = FindAction(settings, controller?.SprintAction ?? "Sprint");
        InputActionBinding? jump = FindAction(settings, controller?.JumpAction ?? "Jump");

        writer.Write(controller != null ? 1u : 0u);
        writer.Write(move?.ControllerAxisX ?? 0);
        writer.Write(move?.ControllerAxisY ?? 1);
        writer.Write(turn?.ControllerAxis ?? 2);
        writer.Write(sprint?.ControllerButton ?? 1);
        writer.Write(jump?.ControllerButton ?? 0);
        writer.Write(Math.Clamp(move?.DeadZone ?? 0.2f, 0.0f, 0.95f));
        writer.Write(Math.Clamp(turn?.DeadZone ?? 0.2f, 0.0f, 0.95f));
        writer.Write(MathF.Max(0.0f, controller?.MoveSpeed ?? 4.0f));
        writer.Write(MathF.Max(0.0f, controller?.TurnSpeed ?? 120.0f));
        writer.Write(MathF.Max(1.0f, controller?.SprintMultiplier ?? 1.75f));
        writer.Write(MathF.Max(0.0f, controller?.JumpStrength ?? 5.0f));
        writer.Write((move?.InvertControllerX == true) ^ (controller?.InvertStrafe == true) ? 1u : 0u);
        writer.Write(move?.InvertControllerY == true ? 1u : 0u);
        writer.Write(controller?.InvertTurn == true ? 1u : 0u);
        writer.Write(controller?.EnableSprinting == true ? 1u : 0u);
        writer.Write(controller?.EnableJumping == true ? 1u : 0u);
        writer.Write(MathF.Max(0, controller?.CameraPitchSpeed ?? 90));
        float minPitch = Math.Clamp(controller?.CameraMinPitch ?? -75, -85, 85);
        writer.Write(minPitch);
        writer.Write(Math.Clamp(controller?.CameraMaxPitch ?? 60, minPitch, 85));
        writer.Write(controller?.InvertCameraPitch == true ? 1u : 0u);
        writer.Write(controller?.CameraCollision == true ? 1u : 0u);
        writer.Write(MathF.Max(0.01f, controller?.CameraCollisionRadius ?? 0.2f));
        writer.Write(MathF.Max(0, controller?.CameraCollisionClearance ?? 0.05f));
        writer.Write(MathF.Max(0, controller?.CameraReturnSpeed ?? 6));
        writer.Write(controller?.CameraTargetHeight ?? 1);
        if (sceneVersion >= 51)
        {
            writer.Write(controller?.EnableKillFloor == true ? 1u : 0u);
            writer.Write(controller?.KillFloorHeight ?? -100.0f);
        }
        if (sceneVersion >= 53)
            writer.Write(controller?.CameraCollisionMask ?? uint.MaxValue);
        if (sceneVersion >= 54)
        {
            writer.Write(controller?.LaunchJumpFromAnimationEvent == true ? 1u : 0u);
            writer.Write(MathF.Max(0.0f, controller?.JumpEventTimeout ?? 0.35f));
        }
    }

    private static InputActionBinding? FindAction(ProjectSettings settings, string name) =>
        settings.InputActions.LastOrDefault(action =>
            string.Equals(action.Name, name, StringComparison.OrdinalIgnoreCase));

    private static void WriteAudio(
        BinaryWriter writer,
        Scene scene,
        (GameObject GameObject, MeshRenderer? Renderer)[] instances)
    {
        (GameObject GameObject, AudioSource Source)? selected = scene.GameObjects
            .Where(gameObject => gameObject.IsActiveInHierarchy)
            .Select(gameObject => (GameObject: gameObject, Source: gameObject.GetComponent<AudioSource>()))
            .Where(item => item.Source is { PlayOnStart: true } &&
                !string.IsNullOrWhiteSpace(item.Source.ClipPath))
            .Select(item => (item.GameObject, item.Source!))
            .Cast<(GameObject GameObject, AudioSource Source)?>()
            .FirstOrDefault();
        AudioSource? source = selected?.Source;
        writer.Write(source != null ? 1u : 0u);
        writer.Write(source?.Loop == true ? 1u : 0u);
        writer.Write(source?.Spatial == true ? 1u : 0u);
        writer.Write(Math.Clamp(source?.Volume ?? 1.0f, 0.0f, 1.0f));
        writer.Write(Math.Clamp(source?.Pitch ?? 1.0f, 0.25f, 4.0f));
        writer.Write(MathF.Max(0.01f, source?.MinDistance ?? 1.0f));
        writer.Write(MathF.Max(source?.MinDistance ?? 1.0f, source?.MaxDistance ?? 25.0f));
        WriteVector(writer, selected?.GameObject.Transform.WorldPosition ?? Vector3.Zero);
        GameObject? primaryCamera = scene.GameObjects.FirstOrDefault(gameObject =>
            gameObject.IsActiveInHierarchy && gameObject.GetComponent<Camera>()?.IsPrimary == true)
            ?? scene.GameObjects.FirstOrDefault(gameObject =>
                gameObject.IsActiveInHierarchy && gameObject.GetComponent<Camera>() != null);
        GameObject? listener = primaryCamera?.GetComponent<Camera>()?.AudioListenerOverride;
        if (listener?.IsActiveInHierarchy != true)
            listener = null;
        GameObject? listenerAnchor = listener;
        int listenerIndex = -1;
        while (listenerAnchor != null && listenerIndex < 0)
        {
            listenerIndex = Array.FindIndex(instances,
                item => ReferenceEquals(item.GameObject, listenerAnchor));
            if (listenerIndex < 0)
                listenerAnchor = listenerAnchor.Parent;
        }
        writer.Write(listenerIndex);
        WriteVector(writer, listener == null || listenerAnchor == null
            ? Vector3.Zero
            : listener.Transform.WorldPosition - listenerAnchor.Transform.WorldPosition);
        WriteFixedString(writer, source == null ? "" : Normalize(source.ClipPath), 256);
        Canvas? soundCanvas = scene.GameObjects
            .Where(gameObject => gameObject.IsActiveInHierarchy)
            .Select(gameObject => gameObject.GetComponent<Canvas>())
            .FirstOrDefault(canvas => canvas != null && !canvas.IsLoadingScreen &&
                (!string.IsNullOrWhiteSpace(canvas.NavigateSound) ||
                 !string.IsNullOrWhiteSpace(canvas.SubmitSound) ||
                 !string.IsNullOrWhiteSpace(canvas.CancelSound) ||
                 !string.IsNullOrWhiteSpace(canvas.ErrorSound) ||
                 !string.IsNullOrWhiteSpace(canvas.OpenSound)));
        WriteFixedString(writer, Normalize(soundCanvas?.NavigateSound ?? ""), 256);
        WriteFixedString(writer, Normalize(soundCanvas?.SubmitSound ?? ""), 256);
        WriteFixedString(writer, Normalize(soundCanvas?.CancelSound ?? ""), 256);
        WriteFixedString(writer, Normalize(soundCanvas?.ErrorSound ?? ""), 256);
        WriteFixedString(writer, Normalize(soundCanvas?.OpenSound ?? ""), 256);
        writer.Write(soundCanvas?.PlayOpenSoundOnSceneStart == true ? 1u : 0u);
    }
}
