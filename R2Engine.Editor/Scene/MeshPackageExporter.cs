using System.Numerics;
using System.Text;
using System.Text.Json;

namespace R2Engine.Editor.Scene;

public sealed class MeshPackageExportResult
{
    public int MeshCount { get; init; }
    public long TotalBytes { get; init; }
    public string ManifestPath { get; init; } = "";
}

public static class MeshPackageExporter
{
    private const int Version = 2;
    private const int MeshletMaxVertices = 64;
    // One PS2 VU meshlet maps to one XGKICK. Keeping this at 24 lets both
    // alternating input/output banks fit in VU1's 16 KB data memory without
    // recycling an output region while GIF is still consuming it.
    private const int MeshletMaxTriangles = 24;
    private const int VertexStrideBytes = Mesh.FloatsPerVertex * sizeof(float);

    public static MeshPackageExportResult Export(
        BuildAssetCollection assets,
        string buildDirectory)
    {
        string packageRoot = Path.Combine(buildDirectory, "R2Data", "Meshes");
        if (Directory.Exists(packageRoot))
            Directory.Delete(packageRoot, recursive: true);
        Directory.CreateDirectory(packageRoot);

        List<object> entries = new();
        long totalBytes = 0;

        foreach (PrimitiveMesh primitive in Enum.GetValues<PrimitiveMesh>())
        {
            Mesh mesh = PrimitiveMeshes.Get(primitive);
            string source = $"BuiltIn/{primitive}";
            string packageRelative = Path.Combine("R2Data", "Meshes", "BuiltIn", primitive + ".r2mesh");
            string packagePath = Path.Combine(buildDirectory, packageRelative);
            Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
            WritePackage(packagePath, mesh);
            long bytes = new FileInfo(packagePath).Length;
            totalBytes += bytes;
            entries.Add(new
            {
                Source = source,
                Package = packageRelative.Replace('\\', '/'),
                mesh.VertexCount,
                mesh.TriangleCount,
                Bytes = bytes
            });
        }

        foreach (BuildAssetEntry entry in assets.Assets.Where(asset =>
                     string.Equals(asset.Category, "Models", StringComparison.OrdinalIgnoreCase)))
        {
            Mesh mesh = MeshAssetCache.Get(entry.Path);
            if (mesh.VertexCount > ushort.MaxValue)
                throw new InvalidOperationException(
                    $"PS2 mesh '{entry.Path}' has {mesh.VertexCount:N0} vertices; the R2MS v1 limit is {ushort.MaxValue:N0}.");
            if (mesh.Indices.Length == 0 || mesh.Indices.Length % 3 != 0)
                throw new InvalidOperationException($"PS2 mesh '{entry.Path}' has invalid triangle indices.");

            string relativeWithoutExtension = Path.ChangeExtension(entry.Path, null) ?? entry.Path;
            string packageRelative = Path.Combine("R2Data", "Meshes", relativeWithoutExtension + ".r2mesh");
            string packagePath = Path.Combine(buildDirectory, packageRelative);
            Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
            WritePackage(packagePath, mesh);
            long bytes = new FileInfo(packagePath).Length;
            totalBytes += bytes;
            entries.Add(new
            {
                Source = entry.Path.Replace('\\', '/'),
                Package = packageRelative.Replace('\\', '/'),
                mesh.VertexCount,
                mesh.TriangleCount,
                Bytes = bytes
            });
        }

        string manifestPath = Path.Combine(packageRoot, "mesh-manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new
        {
            Version,
            MeshCount = entries.Count,
            TotalBytes = totalBytes,
            Meshes = entries
        }, new JsonSerializerOptions { WriteIndented = true }));
        return new MeshPackageExportResult
        {
            MeshCount = entries.Count,
            TotalBytes = totalBytes,
            ManifestPath = manifestPath
        };
    }

    private static void WritePackage(string path, Mesh mesh)
    {
        using BinaryWriter writer = new(File.Create(path), Encoding.UTF8, leaveOpen: false);
        writer.Write(Encoding.ASCII.GetBytes("R2MS"));
        writer.Write(Version);
        writer.Write(mesh.VertexCount);
        writer.Write(mesh.Indices.Length);
        writer.Write(VertexStrideBytes);
        writer.Write(sizeof(ushort));
        writer.Write(mesh.BoundsMin.X);
        writer.Write(mesh.BoundsMin.Y);
        writer.Write(mesh.BoundsMin.Z);
        writer.Write(mesh.BoundsMax.X);
        writer.Write(mesh.BoundsMax.Y);
        writer.Write(mesh.BoundsMax.Z);
        foreach (float value in mesh.VertexData)
            writer.Write(value);
        ushort[] cookedIndices = new ushort[mesh.Indices.Length];
        // Normalize PS2 triangle winding against the mesh's authored normals.
        // Some imported/skinned assets contain sections whose index winding is
        // opposite their vertex normals. The desktop renderer tolerates those,
        // but native back-face culling cannot. Repair each triangle while
        // cooking instead of guessing a global front-face convention at run time.
        for (int triangle = 0; triangle < mesh.Indices.Length; triangle += 3)
        {
            uint a = mesh.Indices[triangle];
            uint b = mesh.Indices[triangle + 1];
            uint c = mesh.Indices[triangle + 2];
            Vector3 edgeAb = mesh.GetPosition((int)b) - mesh.GetPosition((int)a);
            Vector3 edgeAc = mesh.GetPosition((int)c) - mesh.GetPosition((int)a);
            Vector3 faceNormal = Vector3.Cross(edgeAb, edgeAc);
            Vector3 authoredNormal = mesh.GetNormal((int)a) +
                mesh.GetNormal((int)b) + mesh.GetNormal((int)c);
            if (faceNormal.LengthSquared() > 0.0000001f &&
                authoredNormal.LengthSquared() > 0.0000001f &&
                Vector3.Dot(faceNormal, authoredNormal) < 0.0f)
                (b, c) = (c, b);
            cookedIndices[triangle] = checked((ushort)a);
            cookedIndices[triangle + 1] = checked((ushort)b);
            cookedIndices[triangle + 2] = checked((ushort)c);
        }
        foreach (ushort index in cookedIndices)
            writer.Write(index);

        List<Meshlet> meshlets = BuildMeshlets(cookedIndices);
        writer.Write(Encoding.ASCII.GetBytes("R2ML"));
        writer.Write(1);
        writer.Write(meshlets.Count);
        writer.Write(0);
        foreach (Meshlet meshlet in meshlets)
        {
            int dataBytes = meshlet.Vertices.Count * sizeof(ushort) + meshlet.Indices.Count;
            writer.Write(checked((ushort)meshlet.Vertices.Count));
            writer.Write(checked((ushort)(meshlet.Indices.Count / 3)));
            writer.Write(dataBytes);
            writer.Write(0);
            writer.Write(0);
            foreach (ushort vertex in meshlet.Vertices)
                writer.Write(vertex);
            foreach (byte index in meshlet.Indices)
                writer.Write(index);
            while ((writer.BaseStream.Position & 15) != 0)
                writer.Write((byte)0);
        }
    }

    private sealed class Meshlet
    {
        public List<ushort> Vertices { get; } = new(MeshletMaxVertices);
        public List<byte> Indices { get; } = new(MeshletMaxTriangles * 3);
    }

    private static List<Meshlet> BuildMeshlets(IReadOnlyList<ushort> indices)
    {
        List<Meshlet> result = new();
        Meshlet current = new();
        Dictionary<ushort, byte> local = new(MeshletMaxVertices);
        for (int triangle = 0; triangle < indices.Count; triangle += 3)
        {
            int additions = 0;
            for (int corner = 0; corner < 3; ++corner)
                if (!local.ContainsKey(indices[triangle + corner]))
                    ++additions;
            if (current.Indices.Count / 3 >= MeshletMaxTriangles ||
                current.Vertices.Count + additions > MeshletMaxVertices)
            {
                result.Add(current);
                current = new Meshlet();
                local.Clear();
            }
            for (int corner = 0; corner < 3; ++corner)
            {
                ushort globalIndex = indices[triangle + corner];
                if (!local.TryGetValue(globalIndex, out byte localIndex))
                {
                    localIndex = checked((byte)current.Vertices.Count);
                    local.Add(globalIndex, localIndex);
                    current.Vertices.Add(globalIndex);
                }
                current.Indices.Add(localIndex);
            }
        }
        if (current.Indices.Count != 0)
            result.Add(current);
        return result;
    }
}
