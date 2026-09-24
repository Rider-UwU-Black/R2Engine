namespace R2Engine.Editor.Scene;

public static class MeshAssetCache
{
    private static readonly Dictionary<string, Mesh> _meshes =
        new(
            StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, SkeletalAsset> _skeletalAssets =
        new(StringComparer.OrdinalIgnoreCase);

    public static SkeletalAsset GetSkeletalAsset(string assetPath)
    {
        string absolutePath = AssetDatabase.ToAbsolutePath(assetPath);
        if (_skeletalAssets.TryGetValue(absolutePath, out SkeletalAsset? existing))
            return existing;

        SkeletalAsset asset = SkeletalAsset.Load(absolutePath);
        _skeletalAssets.Add(absolutePath, asset);
        return asset;
    }

    public static Mesh Get(
        string assetPath)
    {
        string absolutePath =
            AssetDatabase.ToAbsolutePath(
                assetPath);

        if (_meshes.TryGetValue(
                absolutePath,
                out Mesh? existing))
        {
            return existing;
        }

        string extension =
            Path.GetExtension(
                absolutePath)
            .ToLowerInvariant();

        Mesh mesh =
            extension switch
            {
                ".obj" =>
                    ObjImporter.Load(
                        absolutePath),

                ".r2skel" =>
                    LoadSkeletalBindPose(
                        absolutePath),

                _ =>
                    throw new NotSupportedException(
                        $"Unsupported mesh format: {extension}")
            };

        _meshes.Add(
            absolutePath,
            mesh);

        return mesh;
    }

    private static Mesh LoadSkeletalBindPose(string absolutePath)
    {
        SkeletalAsset asset = GetSkeletalAsset(absolutePath);
        float[] vertexData = new float[asset.Vertices.Count * Mesh.FloatsPerVertex];
        for (int index = 0; index < asset.Vertices.Count; index++)
        {
            SkinnedVertex vertex = asset.Vertices[index];
            int offset = index * Mesh.FloatsPerVertex;
            vertexData[offset] = vertex.Position.X;
            vertexData[offset + 1] = vertex.Position.Y;
            vertexData[offset + 2] = vertex.Position.Z;
            vertexData[offset + 3] = vertex.Normal.X;
            vertexData[offset + 4] = vertex.Normal.Y;
            vertexData[offset + 5] = vertex.Normal.Z;
            vertexData[offset + 6] = vertex.UV.X;
            vertexData[offset + 7] = vertex.UV.Y;
        }

        return new Mesh(asset.Name, vertexData, asset.Indices.ToArray());
    }

    public static void Clear()
    {
        _meshes.Clear();
        _skeletalAssets.Clear();
    }
}
