using System.Numerics;
using R2Engine.Runtime.Platform;

namespace R2Engine.Editor.Scene;

public class Animator : Component, IRuntimeAnimator
{
    private static readonly Random TransitionRandom = new();
    public string ClipPath = "";
    public bool PlayOnStart = true;
    public bool Loop = true;
    public float Speed = 1.0f;
    public string SkeletalTake = "";
    public string ControllerPath = "";
    public string SkeletalAnimationPath = "";

    private AnimationClip? _clip;
    private string _loadedPath = "";
    private float _time;
    private bool _playing;
    private bool _started;
    private SkeletalAsset? _skeletalAsset;
    private SkeletalAnimationClip? _skeletalClip;
    private HumanoidAnimationAsset? _humanoidAnimationAsset;
    private string _loadedSkeletalPath = "";
    private string _loadedSkeletalTake = "";
    private string _loadedSkeletalAnimationPath = "";
    private AnimatorController? _controller;
    private string _loadedControllerPath = "";
    private string _currentState = "";
    private bool _reversePlayback;
    private readonly Dictionary<string, float> _floatParameters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _integerParameters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _boolParameters = new(StringComparer.Ordinal);
    private readonly HashSet<string> _triggers = new(StringComparer.Ordinal);
    private readonly Dictionary<GameObject, Quaternion> _targetBindRotations = new();
    private readonly Dictionary<string, Quaternion> _sourceGlobalBindRotations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Quaternion> _sourceLocalBindRotations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Quaternion> _targetGlobalBindRotations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Quaternion> _targetParentGlobalBindRotations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Vector3> _targetLocalBindPositions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Matrix4x4> _sourceLocalBindMatrices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Matrix4x4> _sourceGlobalBindMatrices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Matrix4x4> _targetGlobalBindMatrices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Matrix4x4> _targetParentGlobalBindMatrices = new(StringComparer.Ordinal);
    private readonly Dictionary<GameObject, Quaternion> _blendFromRotations = new();
    private readonly Dictionary<GameObject, Vector3> _blendFromPositions = new();
    public bool MovementLocked => _controller?.States.FirstOrDefault(item => item.Name == _currentState)?.LockMovement ?? false;
    private float _blendDuration;
    private float _blendElapsed;

    public bool IsPlaying => _playing;
    public float Time => _time;
    public string CurrentState => _currentState;
    public void SetFloat(string name, float value) => _floatParameters[name] = value;
    public void SetInteger(string name, int value) => _integerParameters[name] = value;
    public void SetBool(string name, bool value) => _boolParameters[name] = value;
    public void SetTrigger(string name) => _triggers.Add(name);
    public void ResetTrigger(string name) => _triggers.Remove(name);

    public void Play()
    {
        EnsureClip();
        _playing = _clip != null || _skeletalClip != null;
    }

    public void Pause() => _playing = false;

    public void Stop()
    {
        _playing = false;
        _time = 0.0f;
    }

    public void Restart()
    {
        _time = 0.0f;
        Play();
    }

    public void StartRuntime()
    {
        if (_started)
            return;
        _started = true;
        _time = 0.0f;
        _playing = false;
        EnsureClip();
        if (PlayOnStart)
            Play();
    }

    public void UpdateRuntime(float deltaTime)
    {
        EnsureClip();
        if (_skeletalClip != null)
        {
            EvaluateControllerTransitions();
            if (!_playing)
                return;
            UpdateSkeletalAnimation(deltaTime);
            return;
        }

        if (!_playing)
            return;

        if (_clip == null || _clip.Keyframes.Count == 0)
            return;

        _time += deltaTime * Speed;
        float duration = Math.Max(0.0001f, _clip.Duration);
        if (_time > duration)
        {
            if (Loop)
                _time %= duration;
            else
            {
                _time = duration;
                _playing = false;
            }
        }

        _clip.Sample(_time, out var position, out var rotation, out var scale);
        GameObject.Transform.Position = position;
        GameObject.Transform.Rotation = rotation;
        GameObject.Transform.Scale = scale;
    }

    private void EnsureClip()
    {
        if (!string.Equals(_loadedPath, ClipPath, StringComparison.OrdinalIgnoreCase))
        {
            _loadedPath = ClipPath;
            _clip = string.IsNullOrWhiteSpace(ClipPath)
                ? null
                : AnimationClip.Load(RuntimeAssetServices.ResolvePath(ClipPath));
        }

        string skeletalPath = GameObject.GetComponent<MeshRenderer>()?.MeshPath ?? "";
        if (!string.Equals(Path.GetExtension(skeletalPath), ".r2skel", StringComparison.OrdinalIgnoreCase))
            skeletalPath = "";

        bool controllerChanged = !string.Equals(
            _loadedControllerPath,
            ControllerPath,
            StringComparison.OrdinalIgnoreCase);

        if (string.Equals(_loadedSkeletalPath, skeletalPath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_loadedSkeletalTake, SkeletalTake, StringComparison.Ordinal) &&
            string.Equals(_loadedSkeletalAnimationPath, SkeletalAnimationPath, StringComparison.OrdinalIgnoreCase) &&
            !controllerChanged)
            return;

        _loadedSkeletalPath = skeletalPath;
        _loadedSkeletalTake = SkeletalTake;
        _loadedSkeletalAnimationPath = SkeletalAnimationPath;
        _targetBindRotations.Clear();
        _sourceGlobalBindRotations.Clear();
        _sourceLocalBindRotations.Clear();
        _targetGlobalBindRotations.Clear();
        _targetParentGlobalBindRotations.Clear();
        _targetLocalBindPositions.Clear();
        _sourceLocalBindMatrices.Clear();
        _sourceGlobalBindMatrices.Clear();
        _targetGlobalBindMatrices.Clear();
        _targetParentGlobalBindMatrices.Clear();
        _skeletalAsset = string.IsNullOrWhiteSpace(skeletalPath)
            ? null
            : RuntimeAssetServices.LoadSkeletalAsset(skeletalPath);
        List<SkeletalAnimationClip> availableClips = _skeletalAsset?.Animations ?? new();
        _humanoidAnimationAsset = null;
        if (!string.IsNullOrWhiteSpace(SkeletalAnimationPath))
        {
            string animationPath = RuntimeAssetServices.ResolvePath(SkeletalAnimationPath);
            if (RuntimePlatform.Files.FileExists(animationPath))
            {
                _humanoidAnimationAsset = HumanoidAnimationAsset.Load(animationPath);
                availableClips = _humanoidAnimationAsset.Clips;
            }
        }
        _skeletalClip = availableClips.FirstOrDefault(animation =>
            string.Equals(animation.Name, SkeletalTake, StringComparison.Ordinal));
        _skeletalClip ??= availableClips.FirstOrDefault();
        if (_skeletalClip != null && string.IsNullOrWhiteSpace(SkeletalTake))
            SkeletalTake = _skeletalClip.Name;

        if (controllerChanged)
        {
            _loadedControllerPath = ControllerPath;
            _controller = string.IsNullOrWhiteSpace(ControllerPath) ? null : AnimatorController.Load(RuntimeAssetServices.ResolvePath(ControllerPath));
            InitializeControllerParameters();
            _currentState = _controller?.DefaultState ?? "";
            ApplyControllerState(_currentState);
        }
    }

    private void EvaluateControllerTransitions()
    {
        if (_controller == null) return;
        AnimatorTransition? transition = _controller.Transitions.FirstOrDefault(item =>
            item.From == _currentState && TransitionPasses(item));
        if (transition == null) return;
        if (transition.Condition == AnimatorCondition.Triggered) _triggers.Remove(transition.Parameter);
        BeginTransitionBlend(transition.BlendDuration);
        _currentState = transition.To;
        _time = 0.0f;
        ApplyControllerState(_currentState);
    }

    private void BeginTransitionBlend(float duration)
    {
        _blendFromRotations.Clear();
        _blendFromPositions.Clear();
        _blendDuration = Math.Max(0.0f, duration);
        _blendElapsed = 0.0f;
        if (_blendDuration <= 0.0f)
            return;
        foreach (GameObject candidate in GameObject.Scene?.GameObjects ?? Enumerable.Empty<GameObject>())
        {
            if (IsDescendantOf(candidate, GameObject) && candidate.GetComponent<SkeletonBone>() != null)
            {
                _blendFromRotations[candidate] = EulerToQuaternion(candidate.Transform.Rotation);
                _blendFromPositions[candidate] = candidate.Transform.Position;
            }
        }
    }

    private bool TransitionPasses(AnimatorTransition transition) => transition.Condition switch
    {
        AnimatorCondition.IsTrue => _boolParameters.GetValueOrDefault(transition.Parameter),
        AnimatorCondition.IsFalse => !_boolParameters.GetValueOrDefault(transition.Parameter),
        AnimatorCondition.Greater => GetNumericParameter(transition.Parameter) > transition.Threshold,
        AnimatorCondition.Less => GetNumericParameter(transition.Parameter) < transition.Threshold,
        AnimatorCondition.Equals => MathF.Abs(GetNumericParameter(transition.Parameter) - transition.Threshold) < 0.0001f,
        AnimatorCondition.Triggered => _triggers.Contains(transition.Parameter),
        AnimatorCondition.Finished => _skeletalClip != null && !Loop &&
                                      (_reversePlayback ? _time <= 0.0f :
                                       _time >= Math.Max(0.0001f, _skeletalClip.Duration)),
        AnimatorCondition.RandomFinished => _skeletalClip != null && !Loop &&
                                            (_reversePlayback ? _time <= 0.0f :
                                             _time >= Math.Max(0.0001f, _skeletalClip.Duration)) &&
                                            TransitionRandom.NextDouble() < Math.Clamp(transition.Threshold, 0.0f, 1.0f),
        _ => false
    };

    private float GetNumericParameter(string name) =>
        _integerParameters.TryGetValue(name, out int integerValue)
            ? integerValue
            : _floatParameters.GetValueOrDefault(name);

    private void InitializeControllerParameters()
    {
        _floatParameters.Clear();
        _integerParameters.Clear();
        _boolParameters.Clear();
        _triggers.Clear();
        foreach (AnimatorParameter parameter in _controller?.Parameters ?? Enumerable.Empty<AnimatorParameter>())
        {
            switch (parameter.Type)
            {
                case AnimatorParameterType.Float:
                    _floatParameters[parameter.Name] = parameter.FloatValue;
                    break;
                case AnimatorParameterType.Integer:
                    _integerParameters[parameter.Name] = parameter.IntegerValue;
                    break;
                case AnimatorParameterType.Boolean:
                    _boolParameters[parameter.Name] = parameter.BoolValue;
                    break;
            }
        }
    }

    private void ApplyControllerState(string stateName)
    {
        AnimatorState? state = _controller?.States.FirstOrDefault(item => item.Name == stateName);
        if (state == null) return;
        SkeletalTake = state.AnimationTake;
        SkeletalAnimationPath = state.AnimationPath;
        Loop = state.Loop;
        Speed = state.Speed;
        _reversePlayback = state.Reverse;
        _loadedSkeletalTake = "";
        _loadedSkeletalAnimationPath = "";
        EnsureClip();
        _time = _reversePlayback && _skeletalClip != null ? _skeletalClip.Duration : 0.0f;
        if (_started)
            _playing = _skeletalClip != null;
    }

    private void UpdateSkeletalAnimation(float deltaTime)
    {
        SkeletalAnimationClip clip = _skeletalClip!;
        float previousTime = _time;
        _time += deltaTime * Speed * (_reversePlayback ? -1.0f : 1.0f);
        float duration = Math.Max(0.0001f, clip.Duration);
        bool wrapped = false;
        if (_time > duration)
        {
            if (Loop) { _time %= duration; wrapped = true; }
            else { _time = duration; _playing = false; }
        }
        else if (_time < 0.0f)
        {
            if (Loop) { _time = duration + _time % duration; wrapped = true; }
            else { _time = 0.0f; _playing = false; }
        }
        DispatchAnimationEvents(clip, previousTime, _time, wrapped);

        if (_blendDuration > 0.0f)
            _blendElapsed = Math.Min(_blendDuration, _blendElapsed + deltaTime);

        bool externalHumanoidClip = !string.IsNullOrWhiteSpace(SkeletalAnimationPath);
        if (externalHumanoidClip)
        {
            ApplyRetargetedExternalPose(clip);
            return;
        }

        foreach (GameObject candidate in GameObject.Scene?.GameObjects ?? Enumerable.Empty<GameObject>())
        {
            if (!IsDescendantOf(candidate, GameObject))
                continue;
            SkeletonBone? marker = candidate.GetComponent<SkeletonBone>();
            string boneName = marker?.BoneName ?? candidate.Name;
            string targetRole = GetTargetBoneRole(boneName);
            BoneAnimationTrack? track = clip.Tracks.FirstOrDefault(item =>
                string.Equals(GetTrackRole(item.BoneName), targetRole, StringComparison.Ordinal));
            if (track == null)
                continue;

            if (track.Positions.Count > 0)
                candidate.Transform.Position = SampleVector(track.Positions, _time);
            if (track.Rotations.Count > 0)
                candidate.Transform.Rotation = QuaternionToEuler(SampleQuaternion(track.Rotations, _time));
            if (track.Scales.Count > 0)
                candidate.Transform.Scale = SampleVector(track.Scales, _time);
        }
    }

    private void DispatchAnimationEvents(
        SkeletalAnimationClip clip,
        float previousTime,
        float currentTime,
        bool wrapped)
    {
        if (clip.Events == null || clip.Events.Count == 0 || MathF.Abs(Speed) < 0.00001f)
            return;

        IEnumerable<AnimationEventMarker> crossed = Speed > 0.0f
            ? clip.Events.Where(marker => wrapped
                ? marker.Time > previousTime || marker.Time <= currentTime
                : marker.Time > previousTime && marker.Time <= currentTime)
            : clip.Events.Where(marker => wrapped
                ? marker.Time < previousTime || marker.Time >= currentTime
                : marker.Time < previousTime && marker.Time >= currentTime);

        foreach (AnimationEventMarker marker in crossed.OrderBy(marker => marker.Time))
        {
            GameObject.GetComponent<PlayerController>()?.NotifyAnimationEvent(marker.Name);
        foreach (ScriptComponent script in GameObject.Components.OfType<ScriptComponent>())
            script.NotifyAnimationEvent(marker.Name);
        }
    }

    private void ApplyRetargetedExternalPose(SkeletalAnimationClip clip)
    {
        EnsureRetargetBindBases(clip);
        Dictionary<string, BoneAnimationTrack> tracks = clip.Tracks
            .Select(track => (Role: GetTrackRole(track.BoneName), Track: track))
            .Where(item => !string.IsNullOrWhiteSpace(item.Role))
            .GroupBy(item => item.Role, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Track, StringComparer.Ordinal);
        Dictionary<string, GameObject> targets = new(StringComparer.Ordinal);
        foreach (GameObject candidate in GameObject.Scene?.GameObjects ?? Enumerable.Empty<GameObject>())
        {
            if (!IsDescendantOf(candidate, GameObject)) continue;
            SkeletonBone? marker = candidate.GetComponent<SkeletonBone>();
            if (marker == null) continue;
            string role = GetTargetBoneRole(marker.BoneName);
            if (!string.IsNullOrWhiteSpace(role)) targets.TryAdd(role, candidate);
        }

        Dictionary<string, Matrix4x4> sourceAnimatedGlobals = new(StringComparer.Ordinal);
        Dictionary<string, Matrix4x4> targetAnimatedGlobals = new(StringComparer.Ordinal);
        foreach (string role in HumanoidRig.BoneRoles)
        {
            if (!_sourceGlobalBindMatrices.TryGetValue(role, out Matrix4x4 sourceBindGlobal) ||
                !_targetGlobalBindMatrices.TryGetValue(role, out Matrix4x4 targetBindGlobal) ||
                !targets.TryGetValue(role, out GameObject? target))
                continue;

            Matrix4x4 sourceLocal = tracks.TryGetValue(role, out BoneAnimationTrack? track) && track.Rotations.Count > 0
                ? Matrix4x4.CreateFromQuaternion(SampleQuaternion(track.Rotations, _time))
                : _sourceLocalBindMatrices.GetValueOrDefault(role, Matrix4x4.Identity);
            string parentRole = HumanoidRig.ParentRole(role);
            Matrix4x4 sourceAnimatedGlobal = !string.IsNullOrWhiteSpace(parentRole) &&
                                                  sourceAnimatedGlobals.TryGetValue(parentRole, out Matrix4x4 sourceParent)
                ? sourceLocal * sourceParent
                : sourceLocal;
            sourceAnimatedGlobals[role] = sourceAnimatedGlobal;

            if (!Matrix4x4.Invert(sourceBindGlobal, out Matrix4x4 inverseSourceBind))
                continue;
            Matrix4x4 globalDelta = inverseSourceBind * sourceAnimatedGlobal;
            Matrix4x4 targetAnimatedGlobal = targetBindGlobal * globalDelta;
            targetAnimatedGlobals[role] = targetAnimatedGlobal;

            Matrix4x4 targetParentGlobal;
            if (!string.IsNullOrWhiteSpace(parentRole) &&
                targetAnimatedGlobals.TryGetValue(parentRole, out Matrix4x4 animatedParent))
                targetParentGlobal = animatedParent;
            else
                targetParentGlobal = _targetParentGlobalBindMatrices.GetValueOrDefault(role, Matrix4x4.Identity);
            if (!Matrix4x4.Invert(targetParentGlobal, out Matrix4x4 inverseTargetParent))
                continue;
            Matrix4x4 targetLocal = targetAnimatedGlobal * inverseTargetParent;
            if (Matrix4x4.Decompose(targetLocal, out _, out Quaternion targetRotation, out _))
            {
                targetRotation = Quaternion.Normalize(targetRotation);
                if (_blendDuration > 0.0f && _blendElapsed < _blendDuration &&
                    _blendFromRotations.TryGetValue(target, out Quaternion fromRotation))
                {
                    float blendAmount = _blendElapsed / _blendDuration;
                    targetRotation = Quaternion.Normalize(Quaternion.Slerp(fromRotation, targetRotation, blendAmount));
                }
                target.Transform.Rotation = QuaternionToEuler(targetRotation);
            }
            if (clip.ApplyRootMotion && string.Equals(role, "Hips", StringComparison.Ordinal) && track?.Positions.Count > 0 &&
                _humanoidAnimationAsset?.SourceBindPositions.TryGetValue(track.BoneName, out Vector3 sourceBindPosition) == true &&
                _targetLocalBindPositions.TryGetValue(role, out Vector3 targetBindPosition))
            {
                Vector3 sourcePosition = SampleVector(track.Positions, _time);
                Vector3 sourceDelta = sourcePosition - sourceBindPosition;
                Quaternion sourceParentRotation = _sourceGlobalBindRotations.GetValueOrDefault(parentRole, Quaternion.Identity);
                Quaternion targetParent = _targetParentGlobalBindRotations.GetValueOrDefault(role, Quaternion.Identity);
                Vector3 worldDelta = Vector3.Transform(sourceDelta, sourceParentRotation);
                Vector3 localDelta = Vector3.Transform(worldDelta, Quaternion.Inverse(targetParent));
                float sourceLength = sourceBindPosition.Length();
                float scale = sourceLength > 0.0001f ? targetBindPosition.Length() / sourceLength : 1.0f;
                Vector3 targetPosition = targetBindPosition + localDelta * scale;
                if (_blendDuration > 0.0f && _blendElapsed < _blendDuration &&
                    _blendFromPositions.TryGetValue(target, out Vector3 fromPosition))
                    targetPosition = Vector3.Lerp(fromPosition, targetPosition, _blendElapsed / _blendDuration);
                target.Transform.Position = targetPosition;
            }
        }
        if (_blendDuration > 0.0f && _blendElapsed >= _blendDuration)
        {
            _blendDuration = 0.0f;
            _blendFromRotations.Clear();
            _blendFromPositions.Clear();
        }
    }

    private static Vector3 SampleVector(List<VectorKeyframe> keys, float time)
    {
        if (keys.Count == 1 || time <= keys[0].Time) return keys[0].Value;
        for (int index = 1; index < keys.Count; index++)
        {
            if (time <= keys[index].Time)
            {
                VectorKeyframe previous = keys[index - 1];
                VectorKeyframe next = keys[index];
                float amount = (time - previous.Time) / Math.Max(0.00001f, next.Time - previous.Time);
                return Vector3.Lerp(previous.Value, next.Value, amount);
            }
        }
        return keys[^1].Value;
    }

    private static Quaternion SampleQuaternion(List<QuaternionKeyframe> keys, float time)
    {
        if (keys.Count == 1 || time <= keys[0].Time) return keys[0].Value;
        for (int index = 1; index < keys.Count; index++)
        {
            if (time <= keys[index].Time)
            {
                QuaternionKeyframe previous = keys[index - 1];
                QuaternionKeyframe next = keys[index];
                float amount = (time - previous.Time) / Math.Max(0.00001f, next.Time - previous.Time);
                return Quaternion.Normalize(Quaternion.Slerp(previous.Value, next.Value, amount));
            }
        }
        return keys[^1].Value;
    }

    private static bool IsDescendantOf(GameObject candidate, GameObject root)
    {
        for (GameObject? parent = candidate.Parent; parent != null; parent = parent.Parent)
            if (ReferenceEquals(parent, root)) return true;
        return false;
    }

    private string GetTrackRole(string boneName)
    {
        if (_humanoidAnimationAsset?.BoneMappings.TryGetValue(boneName, out string? mappedRole) == true)
            return mappedRole;
        return HumanoidRig.DetectAnimationRole(boneName);
    }

    private string GetTargetBoneRole(string boneName)
    {
        if (_skeletalAsset?.BoneMappings.TryGetValue(boneName, out string? mappedRole) == true)
            return mappedRole;
        return HumanoidRig.DetectTargetRole(boneName);
    }

    private Quaternion GetTargetBindRotation(SkeletonBone? marker, GameObject candidate)
    {
        if (marker != null && _skeletalAsset != null &&
            marker.BoneIndex >= 0 && marker.BoneIndex < _skeletalAsset.Bones.Count &&
            Matrix4x4.Decompose(
                _skeletalAsset.Bones[marker.BoneIndex].LocalBindTransform,
                out _, out Quaternion rotation, out _))
        {
            return Quaternion.Normalize(rotation);
        }
        return EulerToQuaternion(candidate.Transform.Rotation);
    }

    private Quaternion ConvertRetargetDelta(
        string role,
        Quaternion sourceLocalDelta,
        SkeletalAnimationClip clip)
    {
        EnsureRetargetBindBases(clip);
        if (!_sourceGlobalBindRotations.TryGetValue(role, out Quaternion sourceBasis) ||
            !_targetGlobalBindRotations.TryGetValue(role, out Quaternion targetBasis))
            return sourceLocalDelta;

        Matrix4x4 source = Matrix4x4.CreateFromQuaternion(sourceBasis);
        Matrix4x4 target = Matrix4x4.CreateFromQuaternion(targetBasis);
        Matrix4x4 delta = Matrix4x4.CreateFromQuaternion(sourceLocalDelta);
        if (!Matrix4x4.Invert(source, out Matrix4x4 inverseSource) ||
            !Matrix4x4.Invert(target, out Matrix4x4 inverseTarget))
            return sourceLocalDelta;

        Matrix4x4 converted = target * inverseSource * delta * source * inverseTarget;
        return Matrix4x4.Decompose(converted, out _, out Quaternion rotation, out _)
            ? Quaternion.Normalize(rotation)
            : sourceLocalDelta;
    }

    private Quaternion GetSourceLocalBind(string role, SkeletalAnimationClip clip)
    {
        EnsureRetargetBindBases(clip);
        return _sourceLocalBindRotations.GetValueOrDefault(role, Quaternion.Identity);
    }

    private void EnsureRetargetBindBases(SkeletalAnimationClip clip)
    {
        if (_sourceGlobalBindRotations.Count > 0 || _targetGlobalBindRotations.Count > 0)
            return;

        Dictionary<string, Quaternion> sourceLocalByRole = new(StringComparer.Ordinal);
        foreach (BoneAnimationTrack track in clip.Tracks)
        {
            string role = GetTrackRole(track.BoneName);
            if (string.IsNullOrWhiteSpace(role) || sourceLocalByRole.ContainsKey(role))
                continue;
            Quaternion local = _humanoidAnimationAsset?.SourceBindRotations
                .GetValueOrDefault(track.BoneName, Quaternion.Identity) ?? Quaternion.Identity;
            if (_humanoidAnimationAsset?.RetargetVersion == 2)
                local = Quaternion.Inverse(local);
            sourceLocalByRole[role] = Quaternion.Normalize(local);
            _sourceLocalBindRotations[role] = Quaternion.Normalize(local);
            _sourceLocalBindMatrices[role] = Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(local));
        }

        foreach (string role in HumanoidRig.BoneRoles)
        {
            if (!sourceLocalByRole.TryGetValue(role, out Quaternion local))
                continue;
            string parentRole = HumanoidRig.ParentRole(role);
            _sourceGlobalBindRotations[role] = !string.IsNullOrWhiteSpace(parentRole) &&
                                                       _sourceGlobalBindRotations.TryGetValue(parentRole, out Quaternion parent)
                ? Quaternion.Normalize(local * parent)
                : local;
            Matrix4x4 localMatrix = _sourceLocalBindMatrices[role];
            _sourceGlobalBindMatrices[role] = !string.IsNullOrWhiteSpace(parentRole) &&
                                                       _sourceGlobalBindMatrices.TryGetValue(parentRole, out Matrix4x4 parentMatrix)
                ? localMatrix * parentMatrix
                : localMatrix;
        }

        if (_skeletalAsset == null)
            return;
        Matrix4x4[] globals = new Matrix4x4[_skeletalAsset.Bones.Count];
        for (int index = 0; index < _skeletalAsset.Bones.Count; index++)
        {
            SkeletalBone bone = _skeletalAsset.Bones[index];
            globals[index] = bone.ParentIndex >= 0
                ? bone.LocalBindTransform * globals[bone.ParentIndex]
                : bone.LocalBindTransform;
            string role = GetTargetBoneRole(bone.Name);
            if (!string.IsNullOrWhiteSpace(role))
                _targetLocalBindPositions[role] = new Vector3(
                    bone.LocalBindTransform.M41, bone.LocalBindTransform.M42, bone.LocalBindTransform.M43);
            if (!string.IsNullOrWhiteSpace(role) &&
                Matrix4x4.Decompose(globals[index], out _, out Quaternion globalRotation, out _))
            {
                _targetGlobalBindMatrices[role] = globals[index];
                _targetParentGlobalBindMatrices[role] = bone.ParentIndex >= 0
                    ? globals[bone.ParentIndex]
                    : Matrix4x4.Identity;
                _targetGlobalBindRotations[role] = Quaternion.Normalize(globalRotation);
                if (bone.ParentIndex >= 0 &&
                    Matrix4x4.Decompose(globals[bone.ParentIndex], out _, out Quaternion parentRotation, out _))
                    _targetParentGlobalBindRotations[role] = Quaternion.Normalize(parentRotation);
                else
                    _targetParentGlobalBindRotations[role] = Quaternion.Identity;
            }
        }
    }

    private static Quaternion EulerToQuaternion(Vector3 eulerDegrees)
    {
        Vector3 radians = eulerDegrees * (MathF.PI / 180.0f);
        return Quaternion.CreateFromYawPitchRoll(radians.Y, radians.X, radians.Z);
    }

    private static Vector3 QuaternionToEuler(Quaternion rotation)
    {
        rotation = Quaternion.Normalize(rotation);
        float sinPitch = 2.0f * (rotation.W * rotation.X - rotation.Z * rotation.Y);
        float pitch = MathF.Abs(sinPitch) >= 1.0f
            ? (sinPitch < 0.0f ? -MathF.PI * 0.5f : MathF.PI * 0.5f)
            : MathF.Asin(sinPitch);
        float yaw = MathF.Atan2(2.0f * (rotation.W * rotation.Y + rotation.X * rotation.Z),
            1.0f - 2.0f * (rotation.X * rotation.X + rotation.Y * rotation.Y));
        float roll = MathF.Atan2(2.0f * (rotation.W * rotation.Z + rotation.X * rotation.Y),
            1.0f - 2.0f * (rotation.X * rotation.X + rotation.Z * rotation.Z));
        return new Vector3(pitch, yaw, roll) * (180.0f / MathF.PI);
    }
}
