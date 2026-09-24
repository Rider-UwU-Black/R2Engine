namespace R2Engine.Editor.Scene;

public enum PrimitiveMesh
{
    Cube,
    Sphere,
    Plane
}

public class MeshRenderer : Component
{
    // =========================================================
    // Built-In Primitive
    // =========================================================

    public PrimitiveMesh Mesh =
        PrimitiveMesh.Cube;

    // =========================================================
    // Imported Mesh Asset
    // =========================================================

    public string? MeshPath { get; set; }

    // -1 renders every section. OBJ hierarchy instances select one authored
    // section so multiple child objects can share the source mesh asset.
    public int SubmeshIndex { get; set; } = -1;

    public Mesh MeshData
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(
                    MeshPath))
            {
                return RuntimeAssetServices.LoadMesh(
                    MeshPath);
            }

            return PrimitiveMeshes.Get(
                Mesh);
        }
    }

    // =========================================================
    // Material
    // =========================================================

    private readonly Material _localMaterial =
        new Material(
            "Default Material");

    public string? MaterialPath { get; set; }

    public Material LocalMaterial =>
        _localMaterial;

    public bool UsesMaterialAsset =>
        !string.IsNullOrWhiteSpace(
            MaterialPath);

    public Material Material
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(
                    MaterialPath))
            {
                try
                {
                    return RuntimeAssetServices.LoadMaterial(MaterialPath);
                }
                catch
                {
                    return _localMaterial;
                }
            }

            return _localMaterial;
        }
    }

    private readonly List<string?> _additionalMaterialPaths =
        new();

    public int MaterialSlotCount =>
        SubmeshIndex >= 0 ? 1 : MeshData.Submeshes.Count;

    public MeshSubmesh GetSubmesh(int slot)
    {
        Mesh mesh = MeshData;
        int sourceSlot = SubmeshIndex >= 0 ? SubmeshIndex : slot;
        if (slot < 0 || slot >= MaterialSlotCount ||
            sourceSlot < 0 || sourceSlot >= mesh.Submeshes.Count)
            throw new ArgumentOutOfRangeException(nameof(slot));
        return mesh.Submeshes[sourceSlot];
    }

    public string? GetMaterialPath(int slot)
    {
        if (slot < 0)
            throw new ArgumentOutOfRangeException(nameof(slot));
        return slot == 0
            ? MaterialPath
            : slot - 1 < _additionalMaterialPaths.Count
                ? _additionalMaterialPaths[slot - 1]
                : null;
    }

    public void SetMaterialPath(int slot, string? path)
    {
        if (slot < 0)
            throw new ArgumentOutOfRangeException(nameof(slot));
        if (slot == 0)
        {
            MaterialPath = path;
            return;
        }
        while (_additionalMaterialPaths.Count < slot)
            _additionalMaterialPaths.Add(null);
        _additionalMaterialPaths[slot - 1] = path;
    }

    public Material GetMaterial(int slot)
    {
        string? path = GetMaterialPath(slot);
        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                return RuntimeAssetServices.LoadMaterial(path);
            }
            catch
            {
            }
        }
        return _localMaterial;
    }

    public void ClearMaterialAsset(
        bool preserveAppearance = true)
    {
        if (preserveAppearance &&
            !string.IsNullOrWhiteSpace(
                MaterialPath))
        {
            _localMaterial.CopyFrom(
                Material);
        }

        MaterialPath =
            null;
    }
}
