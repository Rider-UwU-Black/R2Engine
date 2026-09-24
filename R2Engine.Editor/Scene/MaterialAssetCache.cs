namespace R2Engine.Editor.Scene;

public static class MaterialAssetCache
{
    private static readonly Dictionary<string, Material> _materials =
        new(
            StringComparer.OrdinalIgnoreCase);

    public static Material Get(
        string assetPath)
    {
        string absolutePath =
            AssetDatabase.ToAbsolutePath(
                assetPath);

        if (_materials.TryGetValue(
                absolutePath,
                out Material? existing))
        {
            return existing;
        }

        Material material =
            MaterialSerializer.Load(
                absolutePath);

        _materials.Add(
            absolutePath,
            material);

        return material;
    }

    public static void Save(
        string assetPath)
    {
        string absolutePath =
            AssetDatabase.ToAbsolutePath(
                assetPath);

        if (!_materials.TryGetValue(
                absolutePath,
                out Material? material))
        {
            material =
                MaterialSerializer.Load(
                    absolutePath);

            _materials[absolutePath] =
                material;
        }

        MaterialSerializer.Save(
            material,
            absolutePath);
    }

    public static void Save(
        string assetPath,
        Material material)
    {
        string absolutePath =
            AssetDatabase.ToAbsolutePath(
                assetPath);

        MaterialSerializer.Save(
            material,
            absolutePath);

        _materials[absolutePath] =
            material;
    }

    public static void Reload(
        string assetPath)
    {
        string absolutePath =
            AssetDatabase.ToAbsolutePath(
                assetPath);

        _materials.Remove(
            absolutePath);
    }

    public static void Clear()
    {
        _materials.Clear();
    }
}
