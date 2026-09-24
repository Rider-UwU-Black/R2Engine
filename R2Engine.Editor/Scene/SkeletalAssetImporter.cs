using System.Numerics;
using Assimp;
using Assimp.Configs;

namespace R2Engine.Editor.Scene;

public static class SkeletalAssetImporter
{
    public const int DefaultBonePaletteLimit = 40;
    public const int DefaultSkeletonLimit = 128;

    public static SkeletalAsset Import(string sourcePath, ModelImportSettings? settings = null)
    {
        settings ??= new ModelImportSettings();
        settings.SourcePath = Path.GetFullPath(sourcePath);
        settings.SourceLastWriteUtcTicks = File.GetLastWriteTimeUtc(settings.SourcePath).Ticks;
        int skeletonLimit = Math.Max(1, settings.SkeletonLimit);
        ValidateFbxVersion(sourcePath);

        using AssimpContext context = new();
        Assimp.Scene scene = context.ImportFile(
            sourcePath,
            PostProcessSteps.Triangulate |
            PostProcessSteps.GenerateSmoothNormals |
            PostProcessSteps.JoinIdenticalVertices |
            PostProcessSteps.LimitBoneWeights |
            PostProcessSteps.FlipUVs);

        if (scene.MeshCount == 0)
            throw new InvalidDataException("The imported character contains no meshes.");

        Node rootNode = scene.RootNode
            ?? throw new InvalidDataException("The imported character has no node hierarchy.");

        HashSet<string> weightedBoneNames = scene.Meshes
            .SelectMany(mesh => mesh.Bones)
            .Select(bone => bone.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (weightedBoneNames.Count == 0)
            throw new InvalidDataException("The imported model has no skin weights or bones.");

        HashSet<string> includedNodes = new(StringComparer.Ordinal);
        IncludeSkeletonNodes(rootNode, weightedBoneNames, includedNodes);

        SkeletalAsset asset = new()
        {
            Name = Path.GetFileNameWithoutExtension(sourcePath),
            ImportSettings = settings,
            BonePaletteLimit = DefaultBonePaletteLimit,
            DeformBoneCount = weightedBoneNames.Count
        };
        Dictionary<string, int> boneIndices = new(StringComparer.Ordinal);
        AddBones(rootNode, -1, includedNodes, asset, boneIndices);
        foreach (SkeletalBone bone in asset.Bones)
            asset.BoneMappings[bone.Name] = HumanoidRig.DetectTargetRole(bone.Name);
        if (asset.Bones.Count > skeletonLimit)
            throw new InvalidDataException(
                $"Skeleton has {asset.Bones.Count} hierarchy nodes; the configured safety limit is {skeletonLimit}. " +
                "Reduce non-deforming helper bones in Blender before importing.");

        Dictionary<string, Matrix4x4> inverseBindByName = scene.Meshes
            .SelectMany(mesh => mesh.Bones)
            .GroupBy(bone => bone.Name, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => Matrix4x4.Transpose(group.First().OffsetMatrix),
                StringComparer.Ordinal);
        foreach (SkeletalBone bone in asset.Bones)
        {
            if (inverseBindByName.TryGetValue(bone.Name, out Matrix4x4 inverseBind))
                bone.InverseBindTransform = inverseBind;
        }

        Matrix4x4[] meshTransforms = Enumerable
            .Repeat(Matrix4x4.Identity, scene.MeshCount)
            .ToArray();
        CollectMeshTransforms(rootNode, Matrix4x4.Identity, meshTransforms);

        for (int meshIndex = 0; meshIndex < scene.MeshCount; meshIndex++)
            AppendMesh(scene.Meshes[meshIndex], meshTransforms[meshIndex], asset, boneIndices);

        foreach (Assimp.Animation animation in scene.Animations)
            asset.Animations.Add(ConvertAnimation(animation));

        ApplyModelImportTransform(asset, settings);
        if (settings.AutoCorrectOrientation)
            CorrectUpsideDownCharacter(asset);

        return asset;
    }

    private static void ApplyModelImportTransform(SkeletalAsset asset, ModelImportSettings settings)
    {
        float scale = Math.Max(0.0001f, settings.Scale);
        const float degreesToRadians = MathF.PI / 180.0f;
        Matrix4x4 rotation = Matrix4x4.CreateFromYawPitchRoll(
            settings.Rotation.Y * degreesToRadians,
            settings.Rotation.X * degreesToRadians,
            settings.Rotation.Z * degreesToRadians);
        Matrix4x4 correction = Matrix4x4.CreateScale(scale) * rotation;
        foreach (SkinnedVertex vertex in asset.Vertices)
        {
            vertex.Position = Vector3.Transform(vertex.Position, correction);
            vertex.Normal = Vector3.Normalize(Vector3.TransformNormal(vertex.Normal, rotation));
        }
        foreach (SkeletalBone bone in asset.Bones.Where(item => item.ParentIndex < 0))
            bone.LocalBindTransform *= correction;
    }

    private static void CorrectUpsideDownCharacter(SkeletalAsset asset)
    {
        if (asset.Vertices.Count == 0)
            return;

        float minimumY = asset.Vertices.Min(vertex => vertex.Position.Y);
        float maximumY = asset.Vertices.Max(vertex => vertex.Position.Y);
        float height = maximumY - minimumY;
        if (height <= 0.0001f)
            return;

        // Character exports normally place their feet near Y=0 and extend upward.
        // Some Blender FBX files arrive with that whole range below zero after
        // their declared axis conversion is applied. Rotate the complete imported
        // coordinate space, including skeleton roots, rather than altering the
        // scene object's Transform or discarding its joint translations.
        bool predominantlyBelowOrigin = maximumY <= height * 0.1f && -minimumY > maximumY;
        if (!predominantlyBelowOrigin)
            return;

        Matrix4x4 correction = Matrix4x4.CreateRotationX(MathF.PI);
        foreach (SkinnedVertex vertex in asset.Vertices)
        {
            vertex.Position = Vector3.Transform(vertex.Position, correction);
            vertex.Normal = Vector3.Normalize(Vector3.TransformNormal(vertex.Normal, correction));
        }

        for (int index = 0; index < asset.Bones.Count; index++)
        {
            SkeletalBone bone = asset.Bones[index];
            if (bone.ParentIndex < 0)
                bone.LocalBindTransform *= correction;
        }
    }

    public static HumanoidAnimationAsset ImportAnimation(string sourcePath, AnimationImportSettings? settings = null)
    {
        settings ??= new AnimationImportSettings();
        settings.SourcePath = Path.GetFullPath(sourcePath);
        settings.SourceLastWriteUtcTicks = File.GetLastWriteTimeUtc(settings.SourcePath).Ticks;
        ValidateFbxVersion(sourcePath);
        using AssimpContext context = new();
        // FBX stores a bone rotation as a stack of pivot, pre-rotation, rotation,
        // post-rotation and inverse-pivot nodes. Keeping those synthetic nodes
        // produces incomplete tracks such as "_$AssimpFbx$_Rotation" that cannot
        // be safely retargeted on another skeleton. Bake the stack into the real
        // bone channel during import instead.
        context.SetConfig(new FBXPreservePivotsConfig(false));
        Assimp.Scene scene = context.ImportFile(sourcePath, PostProcessSteps.None);
        if (scene.AnimationCount == 0)
            throw new InvalidDataException("The FBX contains no animation takes.");

        HumanoidAnimationAsset asset = new()
        {
            RetargetVersion = 4,
            Name = Path.GetFileNameWithoutExtension(sourcePath),
            ImportSettings = settings
        };
        CollectSourceBindPose(scene.RootNode, asset.SourceBindRotations, asset.SourceBindPositions);
        foreach (Assimp.Animation animation in scene.Animations)
            asset.Clips.Add(ConvertAnimation(animation));
        asset.BoneNames = asset.Clips
            .SelectMany(clip => clip.Tracks)
            .Select(track => track.BoneName)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        foreach (string boneName in asset.BoneNames)
            asset.BoneMappings[boneName] = HumanoidRig.DetectAnimationRole(boneName);
        float speedScale = Math.Max(0.0001f, settings.SpeedScale);
        if (MathF.Abs(speedScale - 1.0f) > 0.0001f)
        {
            foreach (SkeletalAnimationClip clip in asset.Clips)
            {
                clip.Duration /= speedScale;
                foreach (BoneAnimationTrack track in clip.Tracks)
                {
                    foreach (VectorKeyframe key in track.Positions) key.Time /= speedScale;
                    foreach (QuaternionKeyframe key in track.Rotations) key.Time /= speedScale;
                    foreach (VectorKeyframe key in track.Scales) key.Time /= speedScale;
                }
            }
        }
        return asset;
    }

    private static void CollectSourceBindPose(
        Node? node,
        Dictionary<string, Quaternion> rotations,
        Dictionary<string, Vector3> positions)
    {
        if (node == null)
            return;
        Matrix4x4 localTransform = Matrix4x4.Transpose(node.Transform);
        if (Matrix4x4.Decompose(localTransform, out _, out Quaternion rotation, out Vector3 position))
        {
            rotations[node.Name] = Quaternion.Normalize(rotation);
            positions[node.Name] = position;
        }
        foreach (Node child in node.Children)
            CollectSourceBindPose(child, rotations, positions);
    }

    private static void ValidateFbxVersion(string sourcePath)
    {
        if (!string.Equals(Path.GetExtension(sourcePath), ".fbx", StringComparison.OrdinalIgnoreCase))
            return;

        using FileStream stream = File.OpenRead(sourcePath);
        Span<byte> header = stackalloc byte[27];
        if (stream.Read(header) != header.Length ||
            !header[..18].SequenceEqual("Kaydara FBX Binary"u8))
        {
            return;
        }

        int version = BitConverter.ToInt32(header[23..27]);
        if (version < 7100)
        {
            throw new InvalidDataException(
                $"This is legacy FBX {version / 1000.0:0.0}. " +
                "R2Engine requires FBX 2011 or newer. In Mixamo, download using " +
                "'FBX Binary' rather than 'FBX for Unity', then import that file.");
        }
    }

    private static bool IncludeSkeletonNodes(Node node, HashSet<string> weightedBones, HashSet<string> included)
    {
        bool include = weightedBones.Contains(node.Name);
        foreach (Node child in node.Children)
            include |= IncludeSkeletonNodes(child, weightedBones, included);
        if (include)
            included.Add(node.Name);
        return include;
    }

    private static void AddBones(
        Node node,
        int nearestParent,
        HashSet<string> included,
        SkeletalAsset asset,
        Dictionary<string, int> indices)
    {
        int parent = nearestParent;
        if (included.Contains(node.Name))
        {
            parent = asset.Bones.Count;
            indices[node.Name] = parent;
            asset.Bones.Add(new SkeletalBone
            {
                Name = node.Name,
                ParentIndex = nearestParent,
                LocalBindTransform = Matrix4x4.Transpose(node.Transform)
            });
        }
        foreach (Node child in node.Children)
            AddBones(child, parent, included, asset, indices);
    }

    private static void CollectMeshTransforms(
        Node node,
        Matrix4x4 parentTransform,
        Matrix4x4[] meshTransforms)
    {
        Matrix4x4 localTransform = Matrix4x4.Transpose(node.Transform);
        Matrix4x4 globalTransform = localTransform * parentTransform;
        foreach (int meshIndex in node.MeshIndices)
        {
            if (meshIndex >= 0 && meshIndex < meshTransforms.Length)
                meshTransforms[meshIndex] = globalTransform;
        }

        foreach (Node child in node.Children)
            CollectMeshTransforms(child, globalTransform, meshTransforms);
    }

    private static void AppendMesh(
        Assimp.Mesh mesh,
        Matrix4x4 meshTransform,
        SkeletalAsset asset,
        Dictionary<string, int> boneIndices)
    {
        int baseVertex = asset.Vertices.Count;
        List<(int Bone, float Weight)>[] influences =
            Enumerable.Range(0, mesh.VertexCount).Select(_ => new List<(int, float)>()).ToArray();
        foreach (Bone bone in mesh.Bones)
        {
            if (!boneIndices.TryGetValue(bone.Name, out int boneIndex))
                continue;
            foreach (VertexWeight weight in bone.VertexWeights)
                influences[weight.VertexID].Add((boneIndex, weight.Weight));
        }

        for (int index = 0; index < mesh.VertexCount; index++)
        {
            var weights = influences[index].OrderByDescending(item => item.Weight).Take(4).ToArray();
            float total = weights.Sum(item => item.Weight);
            Vector3 transformedNormal = mesh.HasNormals
                ? Vector3.TransformNormal(mesh.Normals[index], meshTransform)
                : Vector3.UnitY;
            if (transformedNormal.LengthSquared() > 0.000001f)
                transformedNormal = Vector3.Normalize(transformedNormal);

            SkinnedVertex vertex = new()
            {
                Position = Vector3.Transform(mesh.Vertices[index], meshTransform),
                Normal = transformedNormal,
                UV = mesh.TextureCoordinateChannelCount > 0
                    ? new Vector2(mesh.TextureCoordinateChannels[0][index].X, mesh.TextureCoordinateChannels[0][index].Y)
                    : Vector2.Zero
            };
            for (int influence = 0; influence < weights.Length; influence++)
            {
                vertex.BoneIndices[influence] = weights[influence].Bone;
                vertex.BoneWeights[influence] = total <= 0.00001f ? 0.0f : weights[influence].Weight / total;
            }
            asset.Vertices.Add(vertex);
        }

        foreach (Face face in mesh.Faces)
        foreach (int index in face.Indices)
            asset.Indices.Add((uint)(baseVertex + index));
    }

    private static SkeletalAnimationClip ConvertAnimation(Assimp.Animation animation)
    {
        double ticksPerSecond = animation.TicksPerSecond <= 0.0 ? 25.0 : animation.TicksPerSecond;
        SkeletalAnimationClip clip = new()
        {
            Name = string.IsNullOrWhiteSpace(animation.Name) ? "Animation" : animation.Name,
            Duration = (float)(animation.DurationInTicks / ticksPerSecond)
        };
        foreach (NodeAnimationChannel channel in animation.NodeAnimationChannels)
        {
            BoneAnimationTrack track = new() { BoneName = channel.NodeName };
            track.Positions.AddRange(channel.PositionKeys.Select(key => new VectorKeyframe
            {
                Time = (float)(key.Time / ticksPerSecond),
                Value = key.Value
            }));
            track.Rotations.AddRange(channel.RotationKeys.Select(key => new QuaternionKeyframe
            {
                Time = (float)(key.Time / ticksPerSecond),
                Value = key.Value
            }));
            track.Scales.AddRange(channel.ScalingKeys.Select(key => new VectorKeyframe
            {
                Time = (float)(key.Time / ticksPerSecond),
                Value = key.Value
            }));
            clip.Tracks.Add(track);
        }
        return clip;
    }

}
