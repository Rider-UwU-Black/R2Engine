using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using R2Engine.Runtime.Platform;

namespace R2Engine.Editor.Scene;

public static class SceneSerializer
{
    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented =
                true,

            DefaultIgnoreCondition =
                JsonIgnoreCondition.WhenWritingNull
        };

    public static void Save(
        Scene scene,
        string filePath)
    {
        string json =
            Serialize(
                scene);

        string? directory =
            Path.GetDirectoryName(
                filePath);

        if (!string.IsNullOrWhiteSpace(
                directory))
        {
            Directory.CreateDirectory(
                directory);
        }

        File.WriteAllText(
            filePath,
            json);
    }

    public static Scene Load(
        string filePath)
    {
        return Deserialize(
            RuntimePlatform.Files.ReadAllText(
                filePath));
    }

    public static string Serialize(
        Scene scene)
    {
        return JsonSerializer.Serialize(
            CreateSceneData(
                scene),
            JsonOptions);
    }

    public static Scene Deserialize(
        string json)
    {
        SceneFileData? data =
            JsonSerializer.Deserialize<SceneFileData>(
                json,
                JsonOptions);

        if (data == null)
        {
            throw new Exception(
                "Failed to deserialize scene.");
        }

        return CreateSceneFromData(
            data);
    }

    private static SceneFileData CreateSceneData(
        Scene scene)
    {
        SceneFileData data =
            new()
            {
                Name =
                    scene.Name,

                Environment = new EnvironmentData
                {
                    BackgroundColor = Vector3Data.FromVector3(scene.Environment.BackgroundColor),
                    AmbientColor = Vector3Data.FromVector3(scene.Environment.AmbientColor),
                    FogEnabled = scene.Environment.FogEnabled,
                    FogColor = Vector3Data.FromVector3(scene.Environment.FogColor),
                    FogStart = scene.Environment.FogStart,
                    FogEnd = scene.Environment.FogEnd
                }
            };

        foreach (
            GameObject gameObject
            in scene.GameObjects)
        {
            GameObjectData objectData =
                new()
                {
                    Name =
                        gameObject.Name,

                    PrefabPath = gameObject.PrefabPath,

                    PrefabBaseline = gameObject.PrefabBaseline,

                    IsActive = gameObject.IsActive,
                    IsVisibleInEditor = gameObject.IsVisibleInEditor,
                    IsLockedInEditor = gameObject.IsLockedInEditor,
                    IsStatic = gameObject.IsStatic,
                    Layer = gameObject.Layer,

                    Parent = gameObject.Parent?.Name,

                    Position =
                        Vector3Data.FromVector3(
                            gameObject.Transform.Position),

                    Rotation =
                        Vector3Data.FromVector3(
                            gameObject.Transform.Rotation),

                    Scale =
                        Vector3Data.FromVector3(
                            gameObject.Transform.Scale)
                };

            MeshRenderer? meshRenderer =
                gameObject.GetComponent<MeshRenderer>();

            if (meshRenderer !=
                null)
            {
                objectData.MeshRenderer =
                    new MeshRendererData
                    {
                        Mesh =
                            meshRenderer.Mesh.ToString(),

                        MeshPath =
                            meshRenderer.MeshPath,

                        SubmeshIndex =
                            meshRenderer.SubmeshIndex,

                        MaterialPath =
                            meshRenderer.MaterialPath,

                        MaterialPaths =
                            Enumerable.Range(0, meshRenderer.MaterialSlotCount)
                                .Select(meshRenderer.GetMaterialPath)
                                .ToList(),

                        Material =
                            new MaterialData
                            {
                                Name =
                                    meshRenderer.Material.Name,

                                BaseColor =
                                    Vector4Data.FromVector4(
                                        meshRenderer.Material.BaseColor),

                                TexturePath =
                                    meshRenderer.Material.TexturePath,

                                TextureTiling = Vector2Data.FromVector2(meshRenderer.Material.TextureTiling),
                                TextureOffset = Vector2Data.FromVector2(meshRenderer.Material.TextureOffset),

                                LightingMode = meshRenderer.Material.LightingMode.ToString(),
                                SurfaceMode = meshRenderer.Material.SurfaceMode.ToString(),
                                AlphaCutoff = meshRenderer.Material.AlphaCutoff,
                                DoubleSided = meshRenderer.Material.DoubleSided,
                                ReceiveFog = meshRenderer.Material.ReceiveFog
                            }
                    };
            }

            Camera? camera =
                gameObject.GetComponent<Camera>();

            if (camera !=
                null)
            {
                objectData.Camera =
                    new CameraData
                    {
                        IsPrimary =
                            camera.IsPrimary,

                        FieldOfView =
                            camera.FieldOfView,

                        NearClip =
                            camera.NearClip,

                        FarClip =
                            camera.FarClip,

                        AudioListenerOverride = camera.AudioListenerOverride?.Name
                    };
            }

            Light? light =
                gameObject.GetComponent<Light>();

            if (light !=
                null)
            {
                objectData.Light =
                    new LightData
                    {
                        Type =
                            light.Type.ToString(),

                        Color =
                            Vector3Data.FromVector3(
                                light.Color),

                        Intensity =
                            light.Intensity,

                        Range =
                            light.Range,

                        SpotAngle =
                            light.SpotAngle,

                        BakeOnExport = light.BakeOnExport,
                        RealtimeEnabled = light.RealtimeEnabled,
                        Mode = light.Mode.ToString(),
                        RealtimeLayerMask = light.RealtimeLayerMask
                    };
            }

            Rotator? rotator =
                gameObject.GetComponent<Rotator>();

            if (rotator != null)
            {
                objectData.Rotator =
                    new RotatorData
                    {
                        Axis =
                            Vector3Data.FromVector3(
                                rotator.Axis),

                        DegreesPerSecond =
                            rotator.DegreesPerSecond
                    };
            }

            PlayerController? playerController =
                gameObject.GetComponent<PlayerController>();

            if (playerController != null)
            {
                objectData.PlayerController =
                    new PlayerControllerData
                    {
                        MoveSpeed = playerController.MoveSpeed,
                        Acceleration = playerController.Acceleration,
                        Deceleration = playerController.Deceleration,
                        AirControl = playerController.AirControl,
                        MaxSlopeAngle = playerController.MaxSlopeAngle,
                        StepHeight = playerController.StepHeight,
                        GroundSnapDistance = playerController.GroundSnapDistance,
                        EnableKillFloor = playerController.EnableKillFloor,
                        KillFloorHeight = playerController.KillFloorHeight,
                        TurnSpeed = playerController.TurnSpeed,
                        CameraPitchSpeed = playerController.CameraPitchSpeed,
                        CameraMinPitch = playerController.CameraMinPitch,
                        CameraMaxPitch = playerController.CameraMaxPitch,
                        InvertCameraPitch = playerController.InvertCameraPitch,
                        CameraCollision = playerController.CameraCollision,
                        CameraCollisionMask = playerController.CameraCollisionMask,
                        CameraCollisionRadius = playerController.CameraCollisionRadius,
                        CameraCollisionClearance = playerController.CameraCollisionClearance,
                        CameraReturnSpeed = playerController.CameraReturnSpeed,
                        CameraTargetHeight = playerController.CameraTargetHeight,
                        InvertStrafe = playerController.InvertStrafe,
                        InvertTurn = playerController.InvertTurn,
                        EnableSprinting = playerController.EnableSprinting,
                        EnableJumping = playerController.EnableJumping,
                        SprintMultiplier = playerController.SprintMultiplier,
                        JumpStrength = playerController.JumpStrength,
                        CoyoteTime = playerController.CoyoteTime,
                        JumpBufferTime = playerController.JumpBufferTime,
                        LaunchJumpFromAnimationEvent = playerController.LaunchJumpFromAnimationEvent,
                        JumpEventTimeout = playerController.JumpEventTimeout,
                        MoveAction = playerController.MoveAction,
                        TurnAction = playerController.TurnAction,
                        SprintAction = playerController.SprintAction,
                        JumpAction = playerController.JumpAction,
                        JumpAnimationTrigger = playerController.JumpAnimationTrigger,
                        JumpLaunchEvent = playerController.JumpLaunchEvent,
                        SpeedParameter = playerController.SpeedParameter,
                        MovingParameter = playerController.MovingParameter,
                        MovingBackwardParameter = playerController.MovingBackwardParameter,
                        SprintingParameter = playerController.SprintingParameter,
                        GroundedParameter = playerController.GroundedParameter,
                        VerticalSpeedParameter = playerController.VerticalSpeedParameter,
                        IdleTimeParameter = playerController.IdleTimeParameter
                    };
            }

            ScriptComponent? scriptComponent =
                gameObject.GetComponent<ScriptComponent>();

            if (scriptComponent != null)
            {
                objectData.Script =
                    new ScriptData
                    {
                        ScriptPath = scriptComponent.ScriptPath,
                        FieldValues = new Dictionary<string, string>(
                            scriptComponent.FieldValues,
                            StringComparer.Ordinal)
                    };
            }

            PlaneCollider? planeCollider = gameObject.GetComponent<PlaneCollider>();
            BoxCollider? boxCollider = planeCollider == null ? gameObject.GetComponent<BoxCollider>() : null;
            if (planeCollider != null)
            {
                objectData.PlaneCollider = new BoxColliderData
                {
                    Center = Vector3Data.FromVector3(planeCollider.Center),
                    Size = Vector3Data.FromVector3(planeCollider.Size),
                    IsTrigger = planeCollider.IsTrigger,
                    Layer = planeCollider.Layer,
                    CollisionMask = planeCollider.CollisionMask
                };
            }
            else if (boxCollider != null)
            {
                objectData.BoxCollider = new BoxColliderData
                {
                    Center = Vector3Data.FromVector3(boxCollider.Center),
                    Size = Vector3Data.FromVector3(boxCollider.Size),
                    IsTrigger = boxCollider.IsTrigger,
                    Layer = boxCollider.Layer,
                    CollisionMask = boxCollider.CollisionMask
                };
            }

            SavePoint? savePoint = gameObject.GetComponent<SavePoint>();
            if (gameObject.GetComponent<SlidingDoor>() is { } door)
                objectData.SlidingDoor = new SlidingDoorData { Lift=door.Lift, Speed=door.Speed, Range=door.Range };
            if (gameObject.GetComponent<Interactable>() is { } interactable)
                objectData.Interactable = new InteractableData { Prompt=interactable.Prompt, InputAction=interactable.InputAction, TargetCanvas=interactable.TargetCanvas, DialoguePages=interactable.DialoguePages, PromptFontPath=interactable.PromptFontPath, PromptFontSize=interactable.PromptFontSize };
            if (gameObject.GetComponent<AnimatorTriggerZone>() is { } zone)
                objectData.AnimatorTriggerZone = new AnimatorTriggerZoneData { TargetAnimator = zone.TargetAnimator, TriggerName = zone.TriggerName };
            if (savePoint != null)
            {
                objectData.SavePoint = new SavePointData
                {
                    SlotName = savePoint.SlotName,
                    SaveOnTriggerEnter = savePoint.SaveOnTriggerEnter
                };
            }

            PersistentObject? persistentObject = gameObject.GetComponent<PersistentObject>();
            if (persistentObject != null)
            {
                objectData.PersistentObject = new PersistentObjectData
                {
                    SaveId = persistentObject.SaveId,
                    SaveTransform = persistentObject.SaveTransform,
                    SaveActiveState = persistentObject.SaveActiveState,
                    SaveInteger = persistentObject.SaveInteger,
                    IntegerValue = persistentObject.IntegerValue,
                    SaveBoolean = persistentObject.SaveBoolean,
                    BooleanValue = persistentObject.BooleanValue
                };
            }

            Canvas? canvas = gameObject.GetComponent<Canvas>();
            if (canvas != null)
                objectData.Canvas = new CanvasData { ReferenceResolution = Vector2Data.FromVector2(canvas.ReferenceResolution),
                    StartsVisible = canvas.StartsVisible, PauseGameplayWhenVisible = canvas.PauseGameplayWhenVisible,
                    ToggleWithMenuInput = canvas.ToggleWithMenuInput, CloseWithCancelInput = canvas.CloseWithCancelInput,
                    NavigateSound = canvas.NavigateSound, SubmitSound = canvas.SubmitSound,
                    CancelSound = canvas.CancelSound, ErrorSound = canvas.ErrorSound,
                    OpenSound = canvas.OpenSound,
                    PlayOpenSoundOnSceneStart = canvas.PlayOpenSoundOnSceneStart };
            UIPanel? uiPanel = gameObject.GetComponent<UIPanel>();
            if (uiPanel != null)
                objectData.UIPanel = new UIPanelData
                {
                    Anchor = Vector2Data.FromVector2(uiPanel.Anchor), Offset = Vector2Data.FromVector2(uiPanel.Offset),
                    Size = Vector2Data.FromVector2(uiPanel.Size), Color = Vector4Data.FromVector4(uiPanel.Color), SortOrder = uiPanel.SortOrder
                };
            UIButton? uiButton = gameObject.GetComponent<UIButton>();
            if (uiButton != null)
                objectData.UIButton = new UIButtonData
                {
                    Anchor = Vector2Data.FromVector2(uiButton.Anchor), Offset = Vector2Data.FromVector2(uiButton.Offset),
                    Size = Vector2Data.FromVector2(uiButton.Size), NormalColor = Vector4Data.FromVector4(uiButton.NormalColor),
                    HoverColor = Vector4Data.FromVector4(uiButton.HoverColor), PressedColor = Vector4Data.FromVector4(uiButton.PressedColor),
                    Text = uiButton.Text, FontPath = uiButton.FontPath, FontSize = uiButton.FontSize, Interactable = uiButton.Interactable,
                    Action = uiButton.Action.ToString(), SaveSlot = uiButton.SaveSlot, TargetCanvas = uiButton.TargetCanvas, TargetScene = uiButton.TargetScene, SortOrder = uiButton.SortOrder,
                    NormalSprite = uiButton.NormalSprite, HoverSprite = uiButton.HoverSprite, PressedSprite = uiButton.PressedSprite,
                    SpriteBorders = Vector4Data.FromVector4(uiButton.SpriteBorders)
                };
            UIImage? uiImage = gameObject.GetComponent<UIImage>();
            if (uiImage != null) objectData.UIImage = new UIImageData { Anchor = Vector2Data.FromVector2(uiImage.Anchor), Offset = Vector2Data.FromVector2(uiImage.Offset), Size = Vector2Data.FromVector2(uiImage.Size), TexturePath = uiImage.TexturePath, Tint = Vector4Data.FromVector4(uiImage.Tint), PreserveAspect = uiImage.PreserveAspect, SortOrder = uiImage.SortOrder, SpriteBorders = Vector4Data.FromVector4(uiImage.SpriteBorders) };
            if (uiImage != null) objectData.UIImage!.SourceRect = Vector4Data.FromVector4(uiImage.SourceRect);
            UIText? uiText = gameObject.GetComponent<UIText>();
            if (uiText != null)
                objectData.UIText = new UITextData
                {
                    Anchor = Vector2Data.FromVector2(uiText.Anchor), Offset = Vector2Data.FromVector2(uiText.Offset),
                    Size = Vector2Data.FromVector2(uiText.Size), Text = uiText.Text,
                    Color = Vector4Data.FromVector4(uiText.Color), FontSize = uiText.FontSize, FontPath = uiText.FontPath, SortOrder = uiText.SortOrder,
                    Ps2SaveLoadFeedback = uiText.Ps2SaveLoadFeedback, SavedMessage = uiText.SavedMessage,
                    SaveFailedMessage = uiText.SaveFailedMessage, LoadedMessage = uiText.LoadedMessage,
                    LoadFailedMessage = uiText.LoadFailedMessage
                };

            Rigidbody? rigidbody = gameObject.GetComponent<Rigidbody>();

            if (rigidbody != null)
            {
                objectData.Rigidbody = new RigidbodyData
                {
                    BodyType = rigidbody.BodyType.ToString(),
                    Velocity = Vector3Data.FromVector3(rigidbody.Velocity),
                    GravityScale = rigidbody.GravityScale,
                    LinearDrag = rigidbody.LinearDrag,
                    Mass = rigidbody.Mass,
                    Restitution = rigidbody.Restitution,
                    Friction = rigidbody.Friction
                };
            }

            AudioSource? audioSource = gameObject.GetComponent<AudioSource>();
            if (audioSource != null)
            {
                objectData.AudioSource = new AudioSourceData
                {
                    ClipPath = audioSource.ClipPath,
                    PlayOnStart = audioSource.PlayOnStart,
                    Loop = audioSource.Loop,
                    Spatial = audioSource.Spatial,
                    Volume = audioSource.Volume,
                    Pitch = audioSource.Pitch,
                    MinDistance = audioSource.MinDistance,
                    MaxDistance = audioSource.MaxDistance
                };
            }

            Animator? animator = gameObject.GetComponent<Animator>();
            if (animator != null)
            {
                objectData.Animator = new AnimatorData
                {
                    ClipPath = animator.ClipPath,
                    PlayOnStart = animator.PlayOnStart,
                    Loop = animator.Loop,
                    Speed = animator.Speed,
                    SkeletalTake = animator.SkeletalTake,
                    ControllerPath = animator.ControllerPath
                    ,SkeletalAnimationPath = animator.SkeletalAnimationPath
                };
            }

            SkeletonBone? skeletonBone = gameObject.GetComponent<SkeletonBone>();
            if (skeletonBone != null)
            {
                objectData.SkeletonBone = new SkeletonBoneData
                {
                    BoneIndex = skeletonBone.BoneIndex,
                    BoneName = skeletonBone.BoneName
                };
            }

            SphereCollider? sphereCollider = gameObject.GetComponent<SphereCollider>();
            if (sphereCollider != null)
            {
                objectData.SphereCollider = new SphereColliderData
                {
                    Center = Vector3Data.FromVector3(sphereCollider.Center),
                    Radius = sphereCollider.Radius,
                    IsTrigger = sphereCollider.IsTrigger,
                    Layer = sphereCollider.Layer,
                    CollisionMask = sphereCollider.CollisionMask
                };
            }

            CapsuleCollider? capsuleCollider = gameObject.GetComponent<CapsuleCollider>();
            if (capsuleCollider != null)
            {
                objectData.CapsuleCollider = new CapsuleColliderData
                {
                    Center = Vector3Data.FromVector3(capsuleCollider.Center),
                    Radius = capsuleCollider.Radius,
                    Height = capsuleCollider.Height,
                    IsTrigger = capsuleCollider.IsTrigger,
                    Layer = capsuleCollider.Layer,
                    CollisionMask = capsuleCollider.CollisionMask
                };
            }

            MeshCollider? meshCollider = gameObject.GetComponent<MeshCollider>();
            if (meshCollider != null)
            {
                objectData.MeshCollider = new MeshColliderData
                {
                    Center = Vector3Data.FromVector3(meshCollider.Center),
                    IsTrigger = meshCollider.IsTrigger,
                    Layer = meshCollider.Layer,
                    CollisionMask = meshCollider.CollisionMask
                };
            }

            data.GameObjects.Add(
                objectData);
        }

        return data;
    }

    private static Scene CreateSceneFromData(
        SceneFileData data)
    {
        Scene scene =
            new Scene(
                string.IsNullOrWhiteSpace(
                    data.Name)
                    ? "Untitled Scene"
                    : data.Name);

        if (data.Environment != null)
        {
            scene.Environment.BackgroundColor = data.Environment.BackgroundColor.ToVector3();
            scene.Environment.AmbientColor = data.Environment.AmbientColor.ToVector3();
            scene.Environment.FogEnabled = data.Environment.FogEnabled;
            scene.Environment.FogColor = data.Environment.FogColor.ToVector3();
            scene.Environment.FogStart = Math.Max(0.0f, data.Environment.FogStart);
            scene.Environment.FogEnd = Math.Max(scene.Environment.FogStart + 0.1f, data.Environment.FogEnd);
        }

        foreach (
            GameObjectData objectData
            in data.GameObjects)
        {
            GameObject gameObject =
                scene.CreateGameObject(
                    string.IsNullOrWhiteSpace(
                        objectData.Name)
                        ? "GameObject"
                        : objectData.Name);

            gameObject.Transform.Position =
                objectData.Position.ToVector3();

            gameObject.PrefabPath = objectData.PrefabPath;
            gameObject.PrefabBaseline = objectData.PrefabBaseline;
            gameObject.IsActive = objectData.IsActive ?? true;
            gameObject.IsVisibleInEditor = objectData.IsVisibleInEditor ?? true;
            gameObject.IsLockedInEditor = objectData.IsLockedInEditor ?? false;
            gameObject.IsStatic = objectData.IsStatic ?? false;
            gameObject.Layer = Math.Clamp(objectData.Layer ?? 0, 0, 31);

            gameObject.Transform.Rotation =
                objectData.Rotation.ToVector3();

            gameObject.Transform.Scale =
                objectData.Scale.ToVector3();

            if (objectData.MeshRenderer !=
                null)
            {
                MeshRenderer meshRenderer =
                    gameObject.AddComponent<MeshRenderer>();

                if (Enum.TryParse(
                        objectData.MeshRenderer.Mesh,
                        out PrimitiveMesh mesh))
                {
                    meshRenderer.Mesh =
                        mesh;
                }

                meshRenderer.MeshPath =
                    string.IsNullOrWhiteSpace(
                        objectData.MeshRenderer.MeshPath)
                        ? null
                        : objectData.MeshRenderer.MeshPath;

                meshRenderer.SubmeshIndex = objectData.MeshRenderer.SubmeshIndex;

                meshRenderer.MaterialPath =
                    string.IsNullOrWhiteSpace(
                        objectData.MeshRenderer.MaterialPath)
                        ? null
                        : objectData.MeshRenderer.MaterialPath;

                if (objectData.MeshRenderer.MaterialPaths != null)
                    for (int slot = 0; slot < objectData.MeshRenderer.MaterialPaths.Count; slot++)
                        meshRenderer.SetMaterialPath(slot, objectData.MeshRenderer.MaterialPaths[slot]);

                MaterialData? materialData =
                    objectData.MeshRenderer.Material;

                if (materialData !=
                    null)
                {
                    if (!string.IsNullOrWhiteSpace(
                            materialData.Name))
                    {
                        meshRenderer.LocalMaterial.Name =
                            materialData.Name;
                    }

                    meshRenderer.LocalMaterial.BaseColor =
                        materialData.BaseColor.ToVector4();

                    meshRenderer.LocalMaterial.TexturePath =
                        string.IsNullOrWhiteSpace(
                            materialData.TexturePath)
                            ? null
                            : materialData.TexturePath;

                    meshRenderer.LocalMaterial.TextureTiling = materialData.TextureTiling?.ToVector2() ?? Vector2.One;
                    meshRenderer.LocalMaterial.TextureOffset = materialData.TextureOffset?.ToVector2() ?? Vector2.Zero;

                    if (Enum.TryParse(materialData.LightingMode, true, out MaterialLightingMode lightingMode))
                        meshRenderer.LocalMaterial.LightingMode = lightingMode;

                    if (Enum.TryParse(materialData.SurfaceMode, true, out MaterialSurfaceMode surfaceMode))
                        meshRenderer.LocalMaterial.SurfaceMode = surfaceMode;

                    meshRenderer.LocalMaterial.AlphaCutoff = Math.Clamp(materialData.AlphaCutoff, 0.0f, 1.0f);
                    meshRenderer.LocalMaterial.DoubleSided = materialData.DoubleSided;
                    meshRenderer.LocalMaterial.ReceiveFog = materialData.ReceiveFog;
                }
            }

            if (objectData.AudioSource != null)
            {
                AudioSource audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.ClipPath = objectData.AudioSource.ClipPath ?? "";
                audioSource.PlayOnStart = objectData.AudioSource.PlayOnStart;
                audioSource.Loop = objectData.AudioSource.Loop;
                audioSource.Spatial = objectData.AudioSource.Spatial;
                audioSource.Volume = objectData.AudioSource.Volume;
                audioSource.Pitch = objectData.AudioSource.Pitch;
                audioSource.MinDistance = objectData.AudioSource.MinDistance;
                audioSource.MaxDistance = objectData.AudioSource.MaxDistance;
            }

            if (objectData.Animator != null)
            {
                Animator animator = gameObject.AddComponent<Animator>();
                animator.ClipPath = objectData.Animator.ClipPath ?? "";
                animator.PlayOnStart = objectData.Animator.PlayOnStart;
                animator.Loop = objectData.Animator.Loop;
                animator.Speed = objectData.Animator.Speed;
                animator.SkeletalTake = objectData.Animator.SkeletalTake ?? "";
                animator.ControllerPath = objectData.Animator.ControllerPath ?? "";
                animator.SkeletalAnimationPath = objectData.Animator.SkeletalAnimationPath ?? "";
            }

            if (objectData.SkeletonBone != null)
            {
                SkeletonBone skeletonBone = gameObject.AddComponent<SkeletonBone>();
                skeletonBone.BoneIndex = objectData.SkeletonBone.BoneIndex;
                skeletonBone.BoneName = objectData.SkeletonBone.BoneName ?? "Bone";
            }

            if (objectData.Camera !=
                null)
            {
                Camera camera =
                    gameObject.AddComponent<Camera>();

                camera.IsPrimary =
                    objectData.Camera.IsPrimary;

                camera.FieldOfView =
                    objectData.Camera.FieldOfView;

                camera.NearClip =
                    objectData.Camera.NearClip;

                camera.FarClip =
                    objectData.Camera.FarClip;
            }

            if (objectData.Light !=
                null)
            {
                Light light =
                    gameObject.AddComponent<Light>();

                if (Enum.TryParse(
                        objectData.Light.Type,
                        out LightType lightType))
                {
                    light.Type =
                        lightType;
                }

                light.Color =
                    objectData.Light.Color.ToVector3();

                light.Intensity =
                    objectData.Light.Intensity;

                light.Range =
                    objectData.Light.Range;

                light.SpotAngle =
                    objectData.Light.SpotAngle;

                if (Enum.TryParse(objectData.Light.Mode, true, out LightBakeMode lightMode))
                    light.Mode = lightMode;
                else
                {
                    light.BakeOnExport = objectData.Light.BakeOnExport;
                    light.RealtimeEnabled = objectData.Light.RealtimeEnabled ?? true;
                }
                light.RealtimeLayerMask = objectData.Light.RealtimeLayerMask ?? uint.MaxValue;
            }

            if (objectData.Rotator != null)
            {
                Rotator rotator =
                    gameObject.AddComponent<Rotator>();

                rotator.Axis =
                    objectData.Rotator.Axis.ToVector3();

                rotator.DegreesPerSecond =
                    objectData.Rotator.DegreesPerSecond;
            }

            if (objectData.PlayerController != null)
            {
                PlayerController playerController =
                    gameObject.AddComponent<PlayerController>();

                playerController.MoveSpeed =
                    objectData.PlayerController.MoveSpeed;
                playerController.Acceleration = objectData.PlayerController.Acceleration;
                playerController.Deceleration = objectData.PlayerController.Deceleration;
                playerController.AirControl = objectData.PlayerController.AirControl;
                playerController.MaxSlopeAngle = objectData.PlayerController.MaxSlopeAngle;
                playerController.StepHeight = objectData.PlayerController.StepHeight;
                playerController.GroundSnapDistance = objectData.PlayerController.GroundSnapDistance;
                playerController.EnableKillFloor = objectData.PlayerController.EnableKillFloor;
                playerController.KillFloorHeight = objectData.PlayerController.KillFloorHeight;

                playerController.TurnSpeed =
                    objectData.PlayerController.TurnSpeed;
                playerController.CameraPitchSpeed = objectData.PlayerController.CameraPitchSpeed;
                playerController.CameraMinPitch = objectData.PlayerController.CameraMinPitch;
                playerController.CameraMaxPitch = objectData.PlayerController.CameraMaxPitch;
                playerController.InvertCameraPitch = objectData.PlayerController.InvertCameraPitch;
                playerController.CameraCollision = objectData.PlayerController.CameraCollision;
                playerController.CameraCollisionMask = objectData.PlayerController.CameraCollisionMask;
                playerController.CameraCollisionRadius = objectData.PlayerController.CameraCollisionRadius;
                playerController.CameraCollisionClearance = objectData.PlayerController.CameraCollisionClearance;
                playerController.CameraReturnSpeed = objectData.PlayerController.CameraReturnSpeed;
                playerController.CameraTargetHeight = objectData.PlayerController.CameraTargetHeight;

                playerController.InvertStrafe =
                    objectData.PlayerController.InvertStrafe;

                playerController.InvertTurn =
                    objectData.PlayerController.InvertTurn;

                playerController.EnableSprinting = objectData.PlayerController.EnableSprinting;
                playerController.EnableJumping = objectData.PlayerController.EnableJumping;
                playerController.SprintMultiplier = objectData.PlayerController.SprintMultiplier;
                playerController.JumpStrength = objectData.PlayerController.JumpStrength;
                playerController.CoyoteTime = objectData.PlayerController.CoyoteTime;
                playerController.JumpBufferTime = objectData.PlayerController.JumpBufferTime;
                playerController.LaunchJumpFromAnimationEvent = objectData.PlayerController.LaunchJumpFromAnimationEvent;
                playerController.JumpEventTimeout = objectData.PlayerController.JumpEventTimeout;
                playerController.MoveAction = objectData.PlayerController.MoveAction;
                playerController.TurnAction = objectData.PlayerController.TurnAction;
                playerController.SprintAction = objectData.PlayerController.SprintAction;
                playerController.JumpAction = objectData.PlayerController.JumpAction;
                playerController.JumpAnimationTrigger = objectData.PlayerController.JumpAnimationTrigger;
                playerController.JumpLaunchEvent = objectData.PlayerController.JumpLaunchEvent;
                playerController.SpeedParameter = objectData.PlayerController.SpeedParameter;
                playerController.MovingParameter = objectData.PlayerController.MovingParameter;
                playerController.MovingBackwardParameter = objectData.PlayerController.MovingBackwardParameter;
                playerController.SprintingParameter = objectData.PlayerController.SprintingParameter;
                playerController.GroundedParameter = objectData.PlayerController.GroundedParameter;
                playerController.VerticalSpeedParameter = objectData.PlayerController.VerticalSpeedParameter;
                playerController.IdleTimeParameter = objectData.PlayerController.IdleTimeParameter;
            }

            if (objectData.Rigidbody != null)
            {
                Rigidbody rigidbody = gameObject.AddComponent<Rigidbody>();

                if (Enum.TryParse(objectData.Rigidbody.BodyType, true, out RigidbodyBodyType bodyType))
                    rigidbody.BodyType = bodyType;

                rigidbody.Velocity = objectData.Rigidbody.Velocity.ToVector3();
                rigidbody.GravityScale = objectData.Rigidbody.GravityScale;
                rigidbody.LinearDrag = objectData.Rigidbody.LinearDrag;
                rigidbody.Mass = objectData.Rigidbody.Mass;
                rigidbody.Restitution = objectData.Rigidbody.Restitution;
                rigidbody.Friction = objectData.Rigidbody.Friction;
            }

            if (objectData.Script != null)
            {
                ScriptComponent script =
                    gameObject.AddComponent<ScriptComponent>();

                script.ScriptPath =
                    objectData.Script.ScriptPath ?? "";

                script.FieldValues =
                    objectData.Script.FieldValues ??
                    new Dictionary<string, string>(StringComparer.Ordinal);
            }

            if (objectData.BoxCollider != null)
            {
                BoxCollider collider = gameObject.AddComponent<BoxCollider>();
                collider.Center = objectData.BoxCollider.Center.ToVector3();
                collider.Size = objectData.BoxCollider.Size.ToVector3();
                collider.IsTrigger = objectData.BoxCollider.IsTrigger;
                collider.Layer = objectData.BoxCollider.Layer;
                collider.CollisionMask = objectData.BoxCollider.CollisionMask;
            }

            if (objectData.PlaneCollider != null)
            {
                PlaneCollider collider = gameObject.AddComponent<PlaneCollider>();
                collider.Center = objectData.PlaneCollider.Center.ToVector3();
                collider.Size = objectData.PlaneCollider.Size.ToVector3();
                collider.IsTrigger = objectData.PlaneCollider.IsTrigger;
                collider.Layer = objectData.PlaneCollider.Layer;
                collider.CollisionMask = objectData.PlaneCollider.CollisionMask;
            }

            if (objectData.SavePoint != null)
            {
                SavePoint savePoint = gameObject.AddComponent<SavePoint>();
                savePoint.SlotName = objectData.SavePoint.SlotName ?? "AUTOSAVE";
                savePoint.SaveOnTriggerEnter = objectData.SavePoint.SaveOnTriggerEnter;
            }

            if (objectData.AnimatorTriggerZone is { } zoneData)
            {
                var zone = gameObject.AddComponent<AnimatorTriggerZone>();
                zone.TargetAnimator = zoneData.TargetAnimator;
                zone.TriggerName = zoneData.TriggerName;
            }
            if (objectData.Interactable is { } interactionData)
            {
                var item=gameObject.AddComponent<Interactable>();
                item.Prompt=interactionData.Prompt;
                item.InputAction=interactionData.InputAction; item.TargetCanvas=interactionData.TargetCanvas; item.DialoguePages=interactionData.DialoguePages;
                item.PromptFontPath=interactionData.PromptFontPath ?? "";
                item.PromptFontSize=interactionData.PromptFontSize<=0 ? 18.0f : interactionData.PromptFontSize;
            }

            if(objectData.SlidingDoor is { } doorData)
            { var door=gameObject.AddComponent<SlidingDoor>();door.Lift=doorData.Lift;door.Speed=doorData.Speed;door.Range=doorData.Range; }

            if (objectData.PersistentObject != null)
            {
                PersistentObject persistentObject = gameObject.AddComponent<PersistentObject>();
                persistentObject.SaveId = objectData.PersistentObject.SaveId ?? "OBJECT";
                persistentObject.SaveTransform = objectData.PersistentObject.SaveTransform;
                persistentObject.SaveActiveState = objectData.PersistentObject.SaveActiveState;
                persistentObject.SaveInteger = objectData.PersistentObject.SaveInteger;
                persistentObject.IntegerValue = objectData.PersistentObject.IntegerValue;
                persistentObject.SaveBoolean = objectData.PersistentObject.SaveBoolean;
                persistentObject.BooleanValue = objectData.PersistentObject.BooleanValue;
            }
            if (objectData.Canvas != null)
            {
                Canvas canvas = gameObject.AddComponent<Canvas>();
                canvas.ReferenceResolution = objectData.Canvas.ReferenceResolution?.ToVector2() ?? new Vector2(640, 448);
                canvas.StartsVisible = objectData.Canvas.StartsVisible;
                canvas.PauseGameplayWhenVisible = objectData.Canvas.PauseGameplayWhenVisible;
                canvas.ToggleWithMenuInput = objectData.Canvas.ToggleWithMenuInput;
                canvas.CloseWithCancelInput = objectData.Canvas.CloseWithCancelInput;
                canvas.NavigateSound = objectData.Canvas.NavigateSound ?? "";
                canvas.SubmitSound = objectData.Canvas.SubmitSound ?? "";
                canvas.CancelSound = objectData.Canvas.CancelSound ?? "";
                canvas.ErrorSound = objectData.Canvas.ErrorSound ?? "";
                canvas.OpenSound = objectData.Canvas.OpenSound ?? "";
                canvas.PlayOpenSoundOnSceneStart = objectData.Canvas.PlayOpenSoundOnSceneStart;
                canvas.IsVisible = canvas.StartsVisible;
            }
            if (objectData.UIPanel != null)
            {
                UIPanel panel = gameObject.AddComponent<UIPanel>();
                panel.Anchor = objectData.UIPanel.Anchor?.ToVector2() ?? new Vector2(.5f);
                panel.Offset = objectData.UIPanel.Offset?.ToVector2() ?? Vector2.Zero;
                panel.Size = objectData.UIPanel.Size?.ToVector2() ?? new Vector2(240, 100);
                panel.Color = objectData.UIPanel.Color?.ToVector4() ?? new Vector4(.03f, .08f, .18f, .9f);
                panel.SortOrder = objectData.UIPanel.SortOrder;
            }
            if (objectData.UIButton != null)
            {
                UIButton button = gameObject.AddComponent<UIButton>();
                button.Anchor = objectData.UIButton.Anchor?.ToVector2() ?? new Vector2(.5f);
                button.Offset = objectData.UIButton.Offset?.ToVector2() ?? Vector2.Zero;
                button.Size = objectData.UIButton.Size?.ToVector2() ?? new Vector2(180, 42);
                button.NormalColor = objectData.UIButton.NormalColor?.ToVector4() ?? button.NormalColor;
                button.HoverColor = objectData.UIButton.HoverColor?.ToVector4() ?? button.HoverColor;
                button.PressedColor = objectData.UIButton.PressedColor?.ToVector4() ?? button.PressedColor;
                button.Text = objectData.UIButton.Text ?? "Button";
                button.FontPath = objectData.UIButton.FontPath ?? "";
                button.FontSize = objectData.UIButton.FontSize <= 0 ? 16.0f : objectData.UIButton.FontSize;
                button.Interactable = objectData.UIButton.Interactable;
                if (Enum.TryParse(objectData.UIButton.Action, out UIButtonAction action)) button.Action = action;
                button.SaveSlot = objectData.UIButton.SaveSlot ?? "CHECKPOINT";
                button.TargetCanvas = objectData.UIButton.TargetCanvas ?? "Canvas";
                button.TargetScene = objectData.UIButton.TargetScene ?? "";
                button.NormalSprite = objectData.UIButton.NormalSprite ?? ""; button.HoverSprite = objectData.UIButton.HoverSprite ?? ""; button.PressedSprite = objectData.UIButton.PressedSprite ?? "";
                button.SpriteBorders = objectData.UIButton.SpriteBorders?.ToVector4() ?? Vector4.Zero;
                button.SortOrder = objectData.UIButton.SortOrder;
            }
            if (objectData.UIImage != null) { UIImage image = gameObject.AddComponent<UIImage>(); image.Anchor = objectData.UIImage.Anchor?.ToVector2() ?? new(.5f); image.Offset = objectData.UIImage.Offset?.ToVector2() ?? Vector2.Zero; image.Size = objectData.UIImage.Size?.ToVector2() ?? new(256,128); image.TexturePath = objectData.UIImage.TexturePath ?? ""; image.Tint = objectData.UIImage.Tint?.ToVector4() ?? Vector4.One; image.PreserveAspect = objectData.UIImage.PreserveAspect; image.SortOrder = objectData.UIImage.SortOrder; image.SpriteBorders = objectData.UIImage.SpriteBorders?.ToVector4() ?? Vector4.Zero; }
            if (objectData.UIImage != null) gameObject.GetComponent<UIImage>()!.SourceRect = objectData.UIImage.SourceRect?.ToVector4() ?? Vector4.Zero;
            if (objectData.UIText != null)
            {
                UIText text = gameObject.AddComponent<UIText>();
                text.Anchor = objectData.UIText.Anchor?.ToVector2() ?? new Vector2(.5f);
                text.Offset = objectData.UIText.Offset?.ToVector2() ?? Vector2.Zero;
                text.Size = objectData.UIText.Size?.ToVector2() ?? new Vector2(240, 36);
                text.Text = objectData.UIText.Text ?? "Text";
                text.Color = objectData.UIText.Color?.ToVector4() ?? Vector4.One;
                text.FontSize = objectData.UIText.FontSize;
                text.FontPath = objectData.UIText.FontPath ?? "";
                text.SortOrder = objectData.UIText.SortOrder;
                text.Ps2SaveLoadFeedback = objectData.UIText.Ps2SaveLoadFeedback;
                text.SavedMessage = objectData.UIText.SavedMessage;
                text.SaveFailedMessage = objectData.UIText.SaveFailedMessage;
                text.LoadedMessage = objectData.UIText.LoadedMessage;
                text.LoadFailedMessage = objectData.UIText.LoadFailedMessage;
            }

            if (objectData.SphereCollider != null)
            {
                SphereCollider collider = gameObject.AddComponent<SphereCollider>();
                collider.Center = objectData.SphereCollider.Center.ToVector3();
                collider.Radius = objectData.SphereCollider.Radius;
                collider.IsTrigger = objectData.SphereCollider.IsTrigger;
                collider.Layer = objectData.SphereCollider.Layer;
                collider.CollisionMask = objectData.SphereCollider.CollisionMask;
            }

            if (objectData.CapsuleCollider != null)
            {
                CapsuleCollider collider = gameObject.AddComponent<CapsuleCollider>();
                collider.Center = objectData.CapsuleCollider.Center.ToVector3();
                collider.Radius = objectData.CapsuleCollider.Radius;
                collider.Height = objectData.CapsuleCollider.Height;
                collider.IsTrigger = objectData.CapsuleCollider.IsTrigger;
                collider.Layer = objectData.CapsuleCollider.Layer;
                collider.CollisionMask = objectData.CapsuleCollider.CollisionMask;
            }

            if (objectData.MeshCollider != null)
            {
                MeshCollider collider = gameObject.AddComponent<MeshCollider>();
                collider.Center = objectData.MeshCollider.Center.ToVector3();
                collider.IsTrigger = objectData.MeshCollider.IsTrigger;
                collider.Layer = objectData.MeshCollider.Layer;
                collider.CollisionMask = objectData.MeshCollider.CollisionMask;
            }
        }

        foreach (GameObjectData objectData in data.GameObjects)
        {
            if (string.IsNullOrWhiteSpace(objectData.Parent))
                continue;

            GameObject? child = scene.FindGameObject(objectData.Name);
            GameObject? parent = scene.FindGameObject(objectData.Parent);
            child?.SetParent(parent, keepWorldPosition: false);
        }

        foreach (GameObjectData objectData in data.GameObjects)
        {
            if (string.IsNullOrWhiteSpace(objectData.Camera?.AudioListenerOverride))
                continue;
            Camera? camera = scene.FindGameObject(objectData.Name)?.GetComponent<Camera>();
            if (camera != null)
                camera.AudioListenerOverride = scene.FindGameObject(objectData.Camera.AudioListenerOverride);
        }

        return scene;
    }

    private class SceneFileData
    {
        public string Name { get; set; } =
            "Untitled Scene";

        public EnvironmentData? Environment { get; set; }

        public List<GameObjectData> GameObjects { get; set; } =
            new();
    }

    private class EnvironmentData
    {
        public Vector3Data BackgroundColor { get; set; } = new() { X = 0.08f, Y = 0.09f, Z = 0.12f };
        public Vector3Data AmbientColor { get; set; } = new() { X = 0.22f, Y = 0.22f, Z = 0.22f };
        public bool FogEnabled { get; set; }
        public Vector3Data FogColor { get; set; } = new() { X = 0.45f, Y = 0.5f, Z = 0.58f };
        public float FogStart { get; set; } = 20.0f;
        public float FogEnd { get; set; } = 80.0f;
    }

    private class GameObjectData
    {
        public string Name { get; set; } =
            "GameObject";

        public string? PrefabPath { get; set; }
        public string? PrefabBaseline { get; set; }
        public string? Parent { get; set; }
        public bool? IsActive { get; set; }
        public bool? IsVisibleInEditor { get; set; }
        public bool? IsLockedInEditor { get; set; }
        public bool? IsStatic { get; set; }
        public int? Layer { get; set; }

        public Vector3Data Position { get; set; } =
            new();

        public Vector3Data Rotation { get; set; } =
            new();

        public Vector3Data Scale { get; set; } =
            new()
            {
                X = 1.0f,
                Y = 1.0f,
                Z = 1.0f
            };

        public MeshRendererData? MeshRenderer { get; set; }

        public CameraData? Camera { get; set; }

        public LightData? Light { get; set; }

        public RotatorData? Rotator { get; set; }

        public PlayerControllerData? PlayerController { get; set; }

        public RigidbodyData? Rigidbody { get; set; }
        public AudioSourceData? AudioSource { get; set; }
        public AnimatorData? Animator { get; set; }
        public SkeletonBoneData? SkeletonBone { get; set; }

        public ScriptData? Script { get; set; }
        public BoxColliderData? BoxCollider { get; set; }
        public BoxColliderData? PlaneCollider { get; set; }
        public SavePointData? SavePoint { get; set; }
        public AnimatorTriggerZoneData? AnimatorTriggerZone { get; set; }
        public InteractableData? Interactable { get; set; }
        public SlidingDoorData? SlidingDoor { get; set; }
        public PersistentObjectData? PersistentObject { get; set; }
        public CanvasData? Canvas { get; set; }
        public UIPanelData? UIPanel { get; set; }
        public UIButtonData? UIButton { get; set; }
        public UITextData? UIText { get; set; }
        public UIImageData? UIImage { get; set; }
        public SphereColliderData? SphereCollider { get; set; }
        public CapsuleColliderData? CapsuleCollider { get; set; }
        public MeshColliderData? MeshCollider { get; set; }
    }

    private class Vector2Data
    {
        public float X { get; set; }
        public float Y { get; set; }
        public static Vector2Data FromVector2(Vector2 value) => new() { X = value.X, Y = value.Y };
        public Vector2 ToVector2() => new(X, Y);
    }

    private class Vector3Data
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public static Vector3Data FromVector3(
            Vector3 value)
        {
            return new Vector3Data
            {
                X = value.X,
                Y = value.Y,
                Z = value.Z
            };
        }

        public Vector3 ToVector3()
        {
            return new Vector3(
                X,
                Y,
                Z);
        }
    }

    private class Vector4Data
    {
        public float X { get; set; } =
            0.72f;

        public float Y { get; set; } =
            0.74f;

        public float Z { get; set; } =
            0.78f;

        public float W { get; set; } =
            1.0f;

        public static Vector4Data FromVector4(
            Vector4 value)
        {
            return new Vector4Data
            {
                X = value.X,
                Y = value.Y,
                Z = value.Z,
                W = value.W
            };
        }

        public Vector4 ToVector4()
        {
            return new Vector4(
                X,
                Y,
                Z,
                W);
        }
    }

    private class MeshRendererData
    {
        public string Mesh { get; set; } =
            PrimitiveMesh.Cube.ToString();

        public string? MeshPath { get; set; }

        public int SubmeshIndex { get; set; } = -1;

        public string? MaterialPath { get; set; }

        public List<string?>? MaterialPaths { get; set; }

        public MaterialData? Material { get; set; }
    }

    private class MaterialData
    {
        public string Name { get; set; } =
            "Default Material";

        public Vector4Data BaseColor { get; set; } =
            new();

        public string? TexturePath { get; set; }

        public Vector2Data? TextureTiling { get; set; }
        public Vector2Data? TextureOffset { get; set; }

        public string LightingMode { get; set; } = "Lit";
        public string SurfaceMode { get; set; } = "Opaque";
        public float AlphaCutoff { get; set; } = 0.5f;
        public bool DoubleSided { get; set; }
        public bool ReceiveFog { get; set; } = true;
    }

    private class CameraData
    {
        public bool IsPrimary { get; set; }

        public float FieldOfView { get; set; } =
            60.0f;

        public float NearClip { get; set; } =
            0.1f;

        public float FarClip { get; set; } =
            1000.0f;

        public string? AudioListenerOverride { get; set; }
    }

    private class LightData
    {
        public string Type { get; set; } =
            LightType.Directional.ToString();

        public Vector3Data Color { get; set; } =
            new()
            {
                X = 1.0f,
                Y = 1.0f,
                Z = 1.0f
            };

        public float Intensity { get; set; } =
            1.0f;

        public float Range { get; set; } =
            10.0f;

        public float SpotAngle { get; set; } =
            45.0f;

        public bool BakeOnExport { get; set; }
        public bool? RealtimeEnabled { get; set; }
        public string? Mode { get; set; }
        public uint? RealtimeLayerMask { get; set; }
    }

    private class RotatorData
    {
        public Vector3Data Axis { get; set; } =
            new()
            {
                Y = 1.0f
            };

        public float DegreesPerSecond { get; set; } =
            90.0f;
    }

    private class PlayerControllerData
    {
        public float MoveSpeed { get; set; } = 4.0f;
        public float Acceleration { get; set; } = 24.0f;
        public float Deceleration { get; set; } = 30.0f;
        public float AirControl { get; set; } = 0.35f;
        public float MaxSlopeAngle { get; set; } = 50.0f;
        public float StepHeight { get; set; } = 0.3f;
        public float GroundSnapDistance { get; set; } = 0.2f;
        public bool EnableKillFloor { get; set; } = true;
        public float KillFloorHeight { get; set; } = -100.0f;
        public float TurnSpeed { get; set; } = 120.0f;
        public float CameraPitchSpeed { get; set; } = 90.0f;
        public float CameraMinPitch { get; set; } = -75.0f;
        public float CameraMaxPitch { get; set; } = 60.0f;
        public bool InvertCameraPitch { get; set; }
        public bool CameraCollision { get; set; } = true;
        public uint CameraCollisionMask { get; set; } = uint.MaxValue;
        public float CameraCollisionRadius { get; set; } = 0.2f;
        public float CameraCollisionClearance { get; set; } = 0.05f;
        public float CameraReturnSpeed { get; set; } = 6.0f;
        public float CameraTargetHeight { get; set; } = 1.0f;
        public bool InvertStrafe { get; set; }
        public bool InvertTurn { get; set; }
        public bool EnableSprinting { get; set; } = true;
        public bool EnableJumping { get; set; } = true;
        public float SprintMultiplier { get; set; } = 1.75f;
        public float JumpStrength { get; set; } = 5.0f;
        public float CoyoteTime { get; set; } = 0.12f;
        public float JumpBufferTime { get; set; } = 0.12f;
        public bool LaunchJumpFromAnimationEvent { get; set; } = true;
        public float JumpEventTimeout { get; set; } = 0.35f;
        public string MoveAction { get; set; } = "Move";
        public string TurnAction { get; set; } = "Turn";
        public string SprintAction { get; set; } = "Sprint";
        public string JumpAction { get; set; } = "Jump";
        public string JumpAnimationTrigger { get; set; } = "Jump";
        public string JumpLaunchEvent { get; set; } = "JumpLaunch";
        public string SpeedParameter { get; set; } = "Speed";
        public string MovingParameter { get; set; } = "Moving";
        public string MovingBackwardParameter { get; set; } = "MovingBackward";
        public string SprintingParameter { get; set; } = "Sprinting";
        public string GroundedParameter { get; set; } = "Grounded";
        public string VerticalSpeedParameter { get; set; } = "VerticalSpeed";
        public string IdleTimeParameter { get; set; } = "IdleTime";
    }

    private class RigidbodyData
    {
        public string BodyType { get; set; } = RigidbodyBodyType.Dynamic.ToString();
        public Vector3Data Velocity { get; set; } = new();
        public float GravityScale { get; set; } = 1.0f;
        public float LinearDrag { get; set; }
        public float Mass { get; set; } = 1.0f;
        public float Restitution { get; set; }
        public float Friction { get; set; } = 0.5f;
    }

    private class AudioSourceData
    {
        public string? ClipPath { get; set; }
        public bool PlayOnStart { get; set; } = true;
        public bool Loop { get; set; }
        public bool Spatial { get; set; }
        public float Volume { get; set; } = 1.0f;
        public float Pitch { get; set; } = 1.0f;
        public float MinDistance { get; set; } = 1.0f;
        public float MaxDistance { get; set; } = 25.0f;
    }


    private class InteractableData
    {
        public string Prompt { get; set; } = "Talk";
        public string InputAction { get; set; } = "Interact";
        public string TargetCanvas { get; set; } = "";
        public string DialoguePages { get; set; } = "";
        public string? PromptFontPath { get; set; }
        public float PromptFontSize { get; set; } = 18.0f;
    }

    private class SlidingDoorData
    {
        public float Lift {get;set;}=3.3f;
        public float Speed {get;set;}=2f;
        public float Range {get;set;}=3f;
    }

    private class AnimatorTriggerZoneData
    {
        public string TargetAnimator { get; set; } = "";
        public string TriggerName { get; set; } = "Wave";
    }

    private class SavePointData
    {
        public string SlotName { get; set; } = "AUTOSAVE";
        public bool SaveOnTriggerEnter { get; set; } = true;
    }

    private class PersistentObjectData
    {
        public string SaveId { get; set; } = "OBJECT";
        public bool SaveTransform { get; set; } = true;
        public bool SaveActiveState { get; set; } = true;
        public bool SaveInteger { get; set; }
        public int IntegerValue { get; set; }
        public bool SaveBoolean { get; set; }
        public bool BooleanValue { get; set; }
    }
    private class CanvasData
    {
        public Vector2Data? ReferenceResolution { get; set; }
        public bool StartsVisible { get; set; }
        public bool PauseGameplayWhenVisible { get; set; } = true;
        public bool ToggleWithMenuInput { get; set; } = true;
        public bool CloseWithCancelInput { get; set; } = true;
        public string? NavigateSound { get; set; }
        public string? SubmitSound { get; set; }
        public string? CancelSound { get; set; }
        public string? ErrorSound { get; set; }
        public string? OpenSound { get; set; }
        public bool PlayOpenSoundOnSceneStart { get; set; }
    }
    private class UIPanelData
    {
        public Vector2Data? Anchor { get; set; }
        public Vector2Data? Offset { get; set; }
        public Vector2Data? Size { get; set; }
        public Vector4Data? Color { get; set; }
        public int SortOrder { get; set; }
    }
    private class UIButtonData : UIPanelData
    {
        public Vector4Data? SpriteBorders { get; set; }
        public Vector4Data? NormalColor { get; set; }
        public Vector4Data? HoverColor { get; set; }
        public Vector4Data? PressedColor { get; set; }
        public string? Text { get; set; }
        public string? FontPath { get; set; }
        public float FontSize { get; set; } = 16.0f;
        public bool Interactable { get; set; } = true;
        public string? Action { get; set; }
        public string? SaveSlot { get; set; } = "CHECKPOINT";
        public string? TargetCanvas { get; set; } = "Canvas";
        public string? TargetScene { get; set; } = "";
        public string? NormalSprite { get; set; }
        public string? HoverSprite { get; set; }
        public string? PressedSprite { get; set; }
    }
    private class UIImageData { public Vector4Data? SourceRect { get; set; } public Vector4Data? SpriteBorders { get; set; } public Vector2Data? Anchor { get; set; } public Vector2Data? Offset { get; set; } public Vector2Data? Size { get; set; } public string? TexturePath { get; set; } public Vector4Data? Tint { get; set; } public bool PreserveAspect { get; set; } = true; public int SortOrder { get; set; } }
    private class UITextData
    {
        public bool Ps2SaveLoadFeedback { get; set; }
        public string SavedMessage { get; set; } = "Saved";
        public string SaveFailedMessage { get; set; } = "Save failed";
        public string LoadedMessage { get; set; } = "Loaded";
        public string LoadFailedMessage { get; set; } = "Load failed";
        public Vector2Data? Anchor { get; set; }
        public Vector2Data? Offset { get; set; }
        public Vector2Data? Size { get; set; }
        public string? Text { get; set; }
        public Vector4Data? Color { get; set; }
        public float FontSize { get; set; } = 24.0f;
        public string? FontPath { get; set; }
        public int SortOrder { get; set; }
    }

    private class AnimatorData
    {
        public string? ClipPath { get; set; }
        public bool PlayOnStart { get; set; } = true;
        public bool Loop { get; set; } = true;
        public float Speed { get; set; } = 1.0f;
        public string? SkeletalTake { get; set; }
        public string? ControllerPath { get; set; }
        public string? SkeletalAnimationPath { get; set; }
    }

    private class SkeletonBoneData
    {
        public int BoneIndex { get; set; } = -1;
        public string? BoneName { get; set; }
    }

    private class ScriptData
    {
        public string? ScriptPath { get; set; }
        public Dictionary<string, string>? FieldValues { get; set; }
    }

    private class BoxColliderData
    {
        public Vector3Data Center { get; set; } = new();
        public Vector3Data Size { get; set; } = new() { X = 1.0f, Y = 1.0f, Z = 1.0f };
        public bool IsTrigger { get; set; }
        public int Layer { get; set; }
        public uint CollisionMask { get; set; } = uint.MaxValue;
    }

    private class SphereColliderData
    {
        public Vector3Data Center { get; set; } = new();
        public float Radius { get; set; } = 0.5f;
        public bool IsTrigger { get; set; }
        public int Layer { get; set; }
        public uint CollisionMask { get; set; } = uint.MaxValue;
    }

    private class CapsuleColliderData
    {
        public Vector3Data Center { get; set; } = new();
        public float Radius { get; set; } = 0.5f;
        public float Height { get; set; } = 2.0f;
        public bool IsTrigger { get; set; }
        public int Layer { get; set; }
        public uint CollisionMask { get; set; } = uint.MaxValue;
    }

    private class MeshColliderData
    {
        public Vector3Data Center { get; set; } = new();
        public bool IsTrigger { get; set; }
        public int Layer { get; set; }
        public uint CollisionMask { get; set; } = uint.MaxValue;
    }
}
