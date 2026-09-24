using System.Numerics;

namespace R2Engine.Editor.Scene;

public readonly record struct MeshSubmesh(
    string MaterialSlotName,
    int IndexStart,
    int IndexCount,
    string? ObjectName = null);

public sealed class Mesh
{
    // =========================================================
    // Vertex Layout
    // =========================================================
    //
    // Each vertex contains 8 floats:
    //
    // Position:
    // X Y Z
    //
    // Normal:
    // X Y Z
    //
    // UV:
    // U V
    //
    // Total:
    // 8 floats = 32 bytes
    // =========================================================

    public const int FloatsPerVertex =
        8;

    public string Name { get; }

    public float[] VertexData { get; }

    public uint[] Indices { get; }

    public IReadOnlyList<MeshSubmesh> Submeshes { get; }

    // =========================================================
    // Bounds
    // =========================================================

    public Vector3 BoundsMin { get; }

    public Vector3 BoundsMax { get; }

    public Vector3 BoundsCenter =>
        (BoundsMin + BoundsMax) *
        0.5f;

    public Vector3 BoundsSize =>
        BoundsMax -
        BoundsMin;

    // =========================================================
    // Statistics
    // =========================================================

    public int VertexCount =>
        VertexData.Length /
        FloatsPerVertex;

    public int TriangleCount =>
        Indices.Length /
        3;

    public uint IndexCount =>
        (uint)Indices.Length;

    // =========================================================
    // Constructor
    // =========================================================

    public Mesh(
        string name,
        float[] vertexData,
        uint[] indices,
        IReadOnlyList<MeshSubmesh>? submeshes = null)
    {
        if (string.IsNullOrWhiteSpace(
                name))
        {
            throw new ArgumentException(
                "Mesh name cannot be empty.",
                nameof(name));
        }

        if (vertexData == null)
        {
            throw new ArgumentNullException(
                nameof(vertexData));
        }

        if (indices == null)
        {
            throw new ArgumentNullException(
                nameof(indices));
        }

        if (vertexData.Length == 0)
        {
            throw new ArgumentException(
                "Mesh must contain at least one vertex.",
                nameof(vertexData));
        }

        if (vertexData.Length %
            FloatsPerVertex !=
            0)
        {
            throw new ArgumentException(
                $"Vertex data must contain {FloatsPerVertex} floats per vertex.",
                nameof(vertexData));
        }

        if (indices.Length %
            3 !=
            0)
        {
            throw new ArgumentException(
                "Index data must contain complete triangles.",
                nameof(indices));
        }

        int vertexCount =
            vertexData.Length /
            FloatsPerVertex;

        foreach (
            uint index
            in indices)
        {
            if (index >=
                vertexCount)
            {
                throw new ArgumentException(
                    $"Mesh index {index} references a vertex that does not exist.",
                    nameof(indices));
            }
        }

        Name =
            name;

        VertexData =
            vertexData;

        Indices =
            indices;

        MeshSubmesh[] sections = submeshes?.ToArray() ??
            new[] { new MeshSubmesh("Material", 0, indices.Length) };

        if (sections.Length == 0)
        {
            throw new ArgumentException(
                "Mesh must contain at least one submesh.",
                nameof(submeshes));
        }

        int expectedIndexStart = 0;
        foreach (MeshSubmesh section in sections)
        {
            if (string.IsNullOrWhiteSpace(section.MaterialSlotName) ||
                section.IndexStart != expectedIndexStart ||
                section.IndexCount <= 0 ||
                section.IndexCount % 3 != 0 ||
                section.IndexStart + section.IndexCount > indices.Length)
            {
                throw new ArgumentException(
                    "Submeshes must be named, contiguous, non-empty triangle ranges covering the index buffer.",
                    nameof(submeshes));
            }

            expectedIndexStart += section.IndexCount;
        }

        if (expectedIndexStart != indices.Length)
        {
            throw new ArgumentException(
                "Submeshes must cover the complete index buffer.",
                nameof(submeshes));
        }

        Submeshes = sections;

        CalculateBounds(
            out Vector3 minimum,
            out Vector3 maximum);

        BoundsMin =
            minimum;

        BoundsMax =
            maximum;
    }

    // =========================================================
    // Vertex Access
    // =========================================================

    public Vector3 GetPosition(
        int vertexIndex)
    {
        int offset =
            vertexIndex *
            FloatsPerVertex;

        return new Vector3(
            VertexData[offset],
            VertexData[offset + 1],
            VertexData[offset + 2]);
    }

    public Vector3 GetNormal(
        int vertexIndex)
    {
        int offset =
            vertexIndex *
            FloatsPerVertex;

        return new Vector3(
            VertexData[offset + 3],
            VertexData[offset + 4],
            VertexData[offset + 5]);
    }

    public Vector2 GetUV(
        int vertexIndex)
    {
        int offset =
            vertexIndex *
            FloatsPerVertex;

        return new Vector2(
            VertexData[offset + 6],
            VertexData[offset + 7]);
    }

    // =========================================================
    // Bounds
    // =========================================================

    private void CalculateBounds(
        out Vector3 minimum,
        out Vector3 maximum)
    {
        Vector3 firstVertex =
            GetPosition(
                0);

        minimum =
            firstVertex;

        maximum =
            firstVertex;

        for (
            int vertexIndex = 1;
            vertexIndex < VertexCount;
            vertexIndex++)
        {
            Vector3 position =
                GetPosition(
                    vertexIndex);

            minimum =
                Vector3.Min(
                    minimum,
                    position);

            maximum =
                Vector3.Max(
                    maximum,
                    position);
        }
    }
}
