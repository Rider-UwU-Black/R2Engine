using System.Numerics;
using System.Text;

namespace R2Engine.Editor.Scene;

public sealed class Ps2AnimationPackageExportResult
{
    public int AnimationCount { get; init; }
    public long TotalBytes { get; init; }
}

public static class Ps2AnimationPackageExporter
{
    private const int CompactVersion = 9;
    private const int ExpandedVersion = 10;
    private const int CompactSkeletalVertexThreshold = 2048;
    private const float FramesPerSecond = 12.0f;
    private const float MaximumDuration = 12.0f;
    private sealed record CookedState(uint Kind, float Duration, List<float[]> Frames, bool Loop, float Speed, uint FinishedTarget, float BlendDuration, float JumpLaunchTime, bool LockMovement);

    public static List<AnimatorTransition> FinishedTransitions(Animator animator) =>
        string.IsNullOrWhiteSpace(animator.ControllerPath) ? new() :
        AnimatorController.Load(AssetDatabase.ToAbsolutePath(animator.ControllerPath)).Transitions
            .Where(t => t.Condition == AnimatorCondition.Finished).ToList();
    private static AnimatorController Controller(Animator animator) => string.IsNullOrWhiteSpace(animator.ControllerPath)
        ? new() : AnimatorController.Load(AssetDatabase.ToAbsolutePath(animator.ControllerPath));
    private static string[] Bindings(MeshRenderer renderer)
    {
        var p = renderer.GameObject?.GetComponent<PlayerController>();
        return p == null ? Array.Empty<string>() : new[] { p.SpeedParameter, p.VerticalSpeedParameter,
            p.MovingParameter, p.MovingBackwardParameter, p.SprintingParameter, p.GroundedParameter,
            p.JumpAnimationTrigger, p.IdleTimeParameter };
    }
    public static string MeshKey(MeshRenderer renderer)
    {
        string key = string.IsNullOrWhiteSpace(renderer.MeshPath) ? $"BuiltIn/{renderer.Mesh}.r2mesh" : Path.ChangeExtension(renderer.MeshPath.Replace('\\', '/'), ".r2mesh");
        Animator? animator = renderer.GameObject?.GetComponent<Animator>();
        if (animator == null || !string.Equals(Path.GetExtension(renderer.MeshPath), ".r2skel", StringComparison.OrdinalIgnoreCase)) return key;
        var states = PlaybackStates(animator);
        if (states.Count == 0) return key;
        var controller = Controller(animator);
        var player = renderer.GameObject?.GetComponent<PlayerController>();
        var eventSignature = states.Select(state => new { state.AnimationPath, Events = LoadClip(state)?.Events });
        string signature = System.Text.Json.JsonSerializer.Serialize(new { States = states, controller.Parameters, controller.Transitions,
            Bindings = Bindings(renderer), LaunchFromEvent = player?.LaunchJumpFromAnimationEvent,
            LaunchEvent = player?.JumpLaunchEvent, Events = eventSignature });
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(signature)))[..16];
        return Path.ChangeExtension(key, null) + "-" + hash + ".r2mesh";
    }

    public static List<AnimatorState> PlaybackStates(Animator animator)
    {
        if (!string.IsNullOrWhiteSpace(animator.ControllerPath))
        {
            var controller = AnimatorController.Load(AssetDatabase.ToAbsolutePath(animator.ControllerPath));
            return controller.States.OrderBy(s => s.Name == controller.DefaultState ? 0 : 1).ToList();
        }
        if (!string.IsNullOrWhiteSpace(animator.SkeletalAnimationPath))
            return new() { new AnimatorState { Name = "Default", AnimationPath = animator.SkeletalAnimationPath,
                AnimationTake = animator.SkeletalTake, Loop = animator.Loop, Speed = animator.Speed } };
        return new();
    }

    public static Ps2AnimationPackageExportResult Export(IEnumerable<string> scenePaths, string buildDirectory)
    {
        HashSet<string> exported = new(StringComparer.OrdinalIgnoreCase);
        int count = 0; long totalBytes = 0;
        foreach (string scenePath in scenePaths)
        {
            Scene sourceScene = SceneSerializer.Deserialize(File.ReadAllText(scenePath));
            foreach (GameObject sourceRoot in sourceScene.GameObjects)
            {
                MeshRenderer? renderer = sourceRoot.GetComponent<MeshRenderer>();
                Animator? sourceAnimator = sourceRoot.GetComponent<Animator>();
                if (renderer?.MeshPath == null || sourceAnimator == null ||
                    !string.Equals(Path.GetExtension(renderer.MeshPath), ".r2skel", StringComparison.OrdinalIgnoreCase) ||
                    !exported.Add(MeshKey(renderer))) continue;
                List<CookedState> states = new();
                var playbackStates = PlaybackStates(sourceAnimator);
                var controller = Controller(sourceAnimator);
                ValidateController(controller, playbackStates);
                var finishedTransitions = FinishedTransitions(sourceAnimator);
                foreach (var transition in finishedTransitions)
                {
                    if (!playbackStates.Any(s => s.Name == transition.From) || !playbackStates.Any(s => s.Name == transition.To))
                        throw new InvalidOperationException($"PS2 Animator '{sourceRoot.Name}' Finished transition references a missing state: '{transition.From}' -> '{transition.To}'.");
                    if (!float.IsFinite(transition.BlendDuration)) throw new InvalidOperationException("Transition blend duration must be finite.");
                }
                foreach (AnimatorState state in playbackStates)
                {
                    uint kind = StateKind(state.Name) ?? uint.MaxValue; SkeletalAnimationClip? clip = LoadClip(state);
                    if (clip == null || clip.Duration <= 0) throw new InvalidOperationException($"PS2 Animator '{sourceRoot.Name}' state '{state.Name}' has no supported skeletal clip.");
                    if (!float.IsFinite(state.Speed)) throw new InvalidOperationException("Animation speed must be finite.");
                    Scene scene = SceneSerializer.Deserialize(File.ReadAllText(scenePath));
                    GameObject? root = scene.GameObjects.FirstOrDefault(item => item.Name == sourceRoot.Name &&
                        string.Equals(item.GetComponent<MeshRenderer>()?.MeshPath, renderer.MeshPath, StringComparison.OrdinalIgnoreCase));
                    Animator? animator = root?.GetComponent<Animator>(); if (root == null || animator == null) continue;
                    animator.ControllerPath = ""; animator.SkeletalAnimationPath = state.AnimationPath;
                    animator.SkeletalTake = state.AnimationTake; animator.Loop = false; animator.Speed = 1; animator.PlayOnStart = true;
                    SkeletalAsset asset = MeshAssetCache.GetSkeletalAsset(renderer.MeshPath);
                    bool compactSkeletal = asset.Vertices.Count > CompactSkeletalVertexThreshold;
                    if (clip.Duration > MaximumDuration) throw new InvalidOperationException($"PS2 animation '{state.Name}' exceeds the {MaximumDuration}s baked-clip limit.");
                    float duration = clip.Duration;
                    int frameCount = Math.Max(2, (int)MathF.Ceiling(duration * FramesPerSecond) + 1);
                    List<float[]> frames = new(frameCount); animator.StartRuntime(); float step = 1.0f / FramesPerSecond;
                    for (int frame = 0; frame < frameCount; ++frame)
                    { animator.UpdateRuntime(frame == 0 ? 0 : step); frames.Add(compactSkeletal
                        ? BuildSkinFrame(scene, root, asset)
                        : BuildSkinnedFrame(scene, root, asset)); }
                    if (state.Reverse) frames.Reverse();
                    var finished = finishedTransitions.FirstOrDefault(t => t.From == state.Name);
                    uint targetIndex = finished == null ? uint.MaxValue : (uint)playbackStates.FindIndex(s => s.Name == finished.To);
                    PlayerController? player = sourceRoot.GetComponent<PlayerController>();
                    float jumpLaunchTime = -1.0f;
                    if (kind == 4u && player?.LaunchJumpFromAnimationEvent == true &&
                        !string.IsNullOrWhiteSpace(player.JumpLaunchEvent))
                    {
                        AnimationEventMarker? marker = clip.Events.FirstOrDefault(item =>
                            string.Equals(item.Name.Trim(), player.JumpLaunchEvent.Trim(), StringComparison.OrdinalIgnoreCase));
                        if (marker != null) jumpLaunchTime = Math.Clamp(marker.Time, 0.0f, duration);
                    }
                    states.Add(new CookedState(kind, duration, frames, state.Loop, state.Speed, targetIndex,
                        Math.Max(0, finished?.BlendDuration ?? 0), jumpLaunchTime, state.LockMovement));
                }
                if (states.Count == 0) continue;
                if (states.Count > 32) throw new InvalidOperationException("PS2 supports up to 32 baked animator states.");
                SkeletalAsset target = MeshAssetCache.GetSkeletalAsset(renderer.MeshPath);
                string relative = Path.ChangeExtension(MeshKey(renderer), null)!;
                string output = Path.Combine(buildDirectory, "R2Data", "Meshes", relative + ".r2anim");
                string baseMesh = Path.Combine(buildDirectory, "R2Data", "Meshes", Path.ChangeExtension(renderer.MeshPath, ".r2mesh"));
                string variantMesh = Path.ChangeExtension(output, ".r2mesh");
                Directory.CreateDirectory(Path.GetDirectoryName(variantMesh)!);
                File.Copy(baseMesh, variantMesh, true);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!); WritePackage(output, target, states, controller, playbackStates, Bindings(renderer));
                ++count; totalBytes += new FileInfo(output).Length;
            }
        }
        return new() { AnimationCount = count, TotalBytes = totalBytes };
    }

    private static void ValidateController(AnimatorController controller, List<AnimatorState> states)
    {
        if (controller.Parameters.Count > 32 || controller.Transitions.Count > 128)
            throw new InvalidOperationException("PS2 Animator supports 32 parameters and 128 transitions per controller.");
        if (states.Select(s => s.Name).Distinct(StringComparer.Ordinal).Count() != states.Count)
            throw new InvalidOperationException("PS2 Animator state names must be unique.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in controller.Parameters)
        {
            if (string.IsNullOrWhiteSpace(p.Name) || p.Name.Contains('\0') || Encoding.UTF8.GetByteCount(p.Name) > 63 || !names.Add(p.Name))
                throw new InvalidOperationException("PS2 Animator parameter names must be unique and fit in 63 UTF-8 bytes.");
            if (!Enum.IsDefined(p.Type) || !float.IsFinite(p.FloatValue)) throw new InvalidOperationException("Invalid Animator parameter type or value.");
        }
        foreach (var t in controller.Transitions)
        {
            if (!states.Any(s => s.Name == t.From) || !states.Any(s => s.Name == t.To))
                throw new InvalidOperationException($"Animator transition references a missing state: {t.From} -> {t.To}.");
            if (!Enum.IsDefined(t.Condition) || !float.IsFinite(t.Threshold) || !float.IsFinite(t.BlendDuration))
                throw new InvalidOperationException("Invalid Animator transition condition, threshold or blend.");
            if (t.Condition is AnimatorCondition.Finished or AnimatorCondition.RandomFinished) continue;
            var p = controller.Parameters.FirstOrDefault(p => p.Name == t.Parameter);
            bool compatible = p != null && (t.Condition switch {
                AnimatorCondition.IsTrue or AnimatorCondition.IsFalse => p.Type == AnimatorParameterType.Boolean,
                AnimatorCondition.Triggered => p.Type == AnimatorParameterType.Trigger,
                _ => p.Type is AnimatorParameterType.Float or AnimatorParameterType.Integer });
            if (!compatible) throw new InvalidOperationException($"Animator transition parameter '{t.Parameter}' is missing or has the wrong type.");
        }
    }

    private static void WritePackage(string path, SkeletalAsset asset, List<CookedState> states, AnimatorController controller, List<AnimatorState> names, string[] bindings)
    {
        int vertexCount = asset.Vertices.Count;
        int boneCount = asset.Bones.Count;
        bool compactSkeletal = vertexCount > CompactSkeletalVertexThreshold;
        if (boneCount is <= 0 or > 128) throw new InvalidOperationException("PS2 skeletal animation supports 1 to 128 bones.");
        uint dataOffset = checked((uint)(16 + states.Count * 44 + (compactSkeletal ? 4 + vertexCount * 8 : 0) +
            8 + controller.Parameters.Count * 72 + controller.Transitions.Count * 24));
        using BinaryWriter writer = new(File.Create(path), Encoding.UTF8, false);
        writer.Write(Encoding.ASCII.GetBytes("R2AN")); writer.Write(compactSkeletal ? CompactVersion : ExpandedVersion); writer.Write(vertexCount); writer.Write(states.Count);
        foreach (CookedState state in states)
        { writer.Write(state.Kind); writer.Write(state.Frames.Count); writer.Write(FramesPerSecond); writer.Write(state.Duration); writer.Write(dataOffset); writer.Write(state.Loop ? 1u : 0u); writer.Write(state.Speed); writer.Write(state.FinishedTarget); writer.Write(state.BlendDuration); writer.Write(state.JumpLaunchTime); writer.Write(state.LockMovement ? 1u : 0u); dataOffset += checked((uint)(state.Frames.Count * (compactSkeletal ? boneCount * 12 : vertexCount * 6) * sizeof(float))); }
        if (compactSkeletal)
        {
            writer.Write(boneCount);
            foreach (SkinnedVertex vertex in asset.Vertices)
            {
                for (int influence = 0; influence < 4; ++influence)
                {
                    int index = vertex.BoneIndices[influence];
                    writer.Write((byte)Math.Clamp(index, 0, boneCount - 1));
                }
                int remaining = 255;
                for (int influence = 0; influence < 4; ++influence)
                {
                    int weight = influence == 3 ? remaining : Math.Clamp(
                        (int)MathF.Round(Math.Clamp(vertex.BoneWeights[influence], 0, 1) * 255), 0, remaining);
                    writer.Write((byte)weight);
                    remaining -= weight;
                }
            }
        }
        writer.Write(controller.Parameters.Count); writer.Write(controller.Transitions.Count);
        foreach (var p in controller.Parameters)
        {
            int binding = Array.IndexOf(bindings, p.Name) + 1;
            bool matchingType = binding is 1 or 2 or 8 ? p.Type == AnimatorParameterType.Float : binding == 7 ? p.Type == AnimatorParameterType.Trigger : p.Type == AnimatorParameterType.Boolean;
            writer.Write((uint)p.Type | (uint)(matchingType ? binding : 0) << 8);
            writer.Write(p.Type == AnimatorParameterType.Float ? p.FloatValue : p.Type == AnimatorParameterType.Integer ? p.IntegerValue : p.Type == AnimatorParameterType.Boolean && p.BoolValue ? 1f : 0f);
            byte[] name = new byte[64]; Encoding.UTF8.GetBytes(p.Name).CopyTo(name, 0); writer.Write(name);
        }
        foreach (var t in controller.Transitions)
        {
            writer.Write(names.FindIndex(s => s.Name == t.From)); writer.Write(names.FindIndex(s => s.Name == t.To));
            writer.Write((uint)t.Condition); writer.Write(controller.Parameters.FindIndex(p => p.Name == t.Parameter));
            writer.Write(t.Threshold); writer.Write(Math.Max(0, t.BlendDuration));
        }
        foreach (CookedState state in states) foreach (float[] frame in state.Frames) foreach (float value in frame) writer.Write(value);
    }

    private static uint? StateKind(string name)
    { string value = name.ToLowerInvariant(); if (value.Contains("back")) return 3; if (value.Contains("jump")) return 4; if (value.Contains("run") || value.Contains("sprint")) return 2; if (value.Contains("walk")) return 1; if (value.Contains("idle")) return 0; return null; }

    private static SkeletalAnimationClip? LoadClip(AnimatorState state)
    { if (string.IsNullOrWhiteSpace(state.AnimationPath)) return null; HumanoidAnimationAsset asset = HumanoidAnimationAsset.Load(AssetDatabase.ToAbsolutePath(state.AnimationPath)); return asset.Clips.FirstOrDefault(item => item.Name == state.AnimationTake) ?? asset.Clips.FirstOrDefault(); }

    private static float[] BuildSkinFrame(Scene scene, GameObject root, SkeletalAsset asset)
    {
        int boneCount = asset.Bones.Count; Matrix4x4[] bind = new Matrix4x4[boneCount], current = new Matrix4x4[boneCount], skin = new Matrix4x4[boneCount];
        Dictionary<int, GameObject> objects = new();
        foreach (GameObject candidate in scene.GameObjects.Where(item => IsDescendantOf(item, root))) { int index = candidate.GetComponent<SkeletonBone>()?.BoneIndex ?? -1; if (index >= 0 && index < boneCount) objects.TryAdd(index, candidate); }
        for (int index = 0; index < boneCount; ++index)
        {
            SkeletalBone bone = asset.Bones[index]; Matrix4x4 localBind = bone.LocalBindTransform;
            Matrix4x4 local = objects.TryGetValue(index, out GameObject? item) ? CreateLocalMatrix(item.Transform) : localBind;
            if (bone.ParentIndex >= 0) { bind[index] = localBind * bind[bone.ParentIndex]; current[index] = local * current[bone.ParentIndex]; } else { bind[index] = localBind; current[index] = local; }
            skin[index] = Matrix4x4.Invert(bind[index], out Matrix4x4 inverse) ? inverse * current[index] : Matrix4x4.Identity;
        }
        float[] result = new float[boneCount * 12];
        for (int bone = 0; bone < boneCount; ++bone)
        {
            Matrix4x4 matrix = skin[bone]; int offset = bone * 12;
            result[offset] = matrix.M11; result[offset + 1] = matrix.M12; result[offset + 2] = matrix.M13;
            result[offset + 3] = matrix.M21; result[offset + 4] = matrix.M22; result[offset + 5] = matrix.M23;
            result[offset + 6] = matrix.M31; result[offset + 7] = matrix.M32; result[offset + 8] = matrix.M33;
            result[offset + 9] = matrix.M41; result[offset + 10] = matrix.M42; result[offset + 11] = matrix.M43;
        }
        return result;
    }

    private static float[] BuildSkinnedFrame(Scene scene, GameObject root, SkeletalAsset asset)
    {
        int boneCount = asset.Bones.Count;
        Matrix4x4[] bind = new Matrix4x4[boneCount], current = new Matrix4x4[boneCount], skin = new Matrix4x4[boneCount];
        Dictionary<int, GameObject> objects = new();
        foreach (GameObject candidate in scene.GameObjects.Where(item => IsDescendantOf(item, root)))
        { int index = candidate.GetComponent<SkeletonBone>()?.BoneIndex ?? -1; if (index >= 0 && index < boneCount) objects.TryAdd(index, candidate); }
        for (int index = 0; index < boneCount; ++index)
        {
            SkeletalBone bone = asset.Bones[index]; Matrix4x4 localBind = bone.LocalBindTransform;
            Matrix4x4 local = objects.TryGetValue(index, out GameObject? item) ? CreateLocalMatrix(item.Transform) : localBind;
            if (bone.ParentIndex >= 0) { bind[index] = localBind * bind[bone.ParentIndex]; current[index] = local * current[bone.ParentIndex]; }
            else { bind[index] = localBind; current[index] = local; }
            skin[index] = Matrix4x4.Invert(bind[index], out Matrix4x4 inverse) ? inverse * current[index] : Matrix4x4.Identity;
        }
        float[] result = new float[asset.Vertices.Count * 6];
        for (int vertex = 0; vertex < asset.Vertices.Count; ++vertex)
        {
            SkinnedVertex source = asset.Vertices[vertex]; Vector3 position = Vector3.Zero, normal = Vector3.Zero; float total = 0;
            for (int influence = 0; influence < 4; ++influence)
            { float weight = source.BoneWeights[influence]; int bone = source.BoneIndices[influence]; if (weight <= 0 || bone < 0 || bone >= boneCount) continue; position += Vector3.Transform(source.Position, skin[bone]) * weight; normal += Vector3.TransformNormal(source.Normal, skin[bone]) * weight; total += weight; }
            if (total <= 0.00001f) { position = source.Position; normal = source.Normal; }
            else if (normal.LengthSquared() > 0.000001f) normal = Vector3.Normalize(normal);
            int offset = vertex * 6; result[offset] = position.X; result[offset + 1] = position.Y; result[offset + 2] = position.Z;
            result[offset + 3] = normal.X; result[offset + 4] = normal.Y; result[offset + 5] = normal.Z;
        }
        return result;
    }

    private static bool IsDescendantOf(GameObject item, GameObject root) { for (GameObject? current = item.Parent; current != null; current = current.Parent) if (ReferenceEquals(current, root)) return true; return false; }
    private static Matrix4x4 CreateLocalMatrix(Transform transform) { const float d = MathF.PI / 180; Quaternion rotation = Quaternion.CreateFromYawPitchRoll(transform.Rotation.Y * d, transform.Rotation.X * d, transform.Rotation.Z * d); return Matrix4x4.CreateScale(transform.Scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(transform.Position); }
}
