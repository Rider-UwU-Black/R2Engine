namespace R2Engine.Editor.Scene;

/// <summary>
/// Platform/project supplied access to asset-backed runtime data. The runtime owns
/// the component and physics behavior without knowing how an editor imports files.
/// </summary>
public static class RuntimeAssetServices
{
    public static Func<string, Mesh>? MeshLoader { get; set; }
    public static Func<string, Material>? MaterialLoader { get; set; }
    public static Func<string, SkeletalAsset>? SkeletalAssetLoader { get; set; }
    public static Func<string, string>? AssetPathResolver { get; set; }

    public static Mesh LoadMesh(string assetPath) =>
        MeshLoader?.Invoke(assetPath) ??
        throw new InvalidOperationException("No runtime mesh asset provider has been configured.");

    public static Material LoadMaterial(string assetPath) =>
        MaterialLoader?.Invoke(assetPath) ??
        throw new InvalidOperationException("No runtime material asset provider has been configured.");

    public static SkeletalAsset LoadSkeletalAsset(string assetPath) =>
        SkeletalAssetLoader?.Invoke(assetPath) ??
        SkeletalAsset.Load(ResolvePath(assetPath));

    public static string ResolvePath(string assetPath) =>
        AssetPathResolver?.Invoke(assetPath) ?? assetPath;
}
