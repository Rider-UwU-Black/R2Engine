using System.Numerics;

namespace R2Engine.Editor.Scene;

public static class PrimitiveMeshes
{
    // =========================================================
    // Built-In Meshes
    // =========================================================

    public static Mesh Cube { get; } =
        CreateCube();

    public static Mesh Sphere { get; } =
        CreateSphere();

    public static Mesh Plane { get; } =
        CreatePlane();

    // =========================================================
    // Lookup
    // =========================================================

    public static Mesh Get(
        PrimitiveMesh primitive)
    {
        return primitive switch
        {
            PrimitiveMesh.Cube =>
                Cube,

            PrimitiveMesh.Sphere =>
                Sphere,

            PrimitiveMesh.Plane =>
                Plane,

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(primitive),
                    primitive,
                    "Unknown primitive mesh.")
        };
    }

    // =========================================================
    // Cube
    // =========================================================

    private static Mesh CreateCube()
    {
        // -----------------------------------------------------
        // Cube uses 24 vertices instead of 8.
        //
        // Each face needs its own vertices so each face can
        // have its own normal and full 0-1 UV coordinates.
        // -----------------------------------------------------

        List<float> vertices =
            new();

        List<uint> indices =
            new();

        // Back
        AddQuad(
            vertices,
            indices,

            new Vector3(
                -0.5f,
                -0.5f,
                -0.5f),

            new Vector3(
                 0.5f,
                -0.5f,
                -0.5f),

            new Vector3(
                 0.5f,
                 0.5f,
                -0.5f),

            new Vector3(
                -0.5f,
                 0.5f,
                -0.5f),

            new Vector3(
                0.0f,
                0.0f,
                -1.0f));

        // Front
        AddQuad(
            vertices,
            indices,

            new Vector3(
                 0.5f,
                -0.5f,
                 0.5f),

            new Vector3(
                -0.5f,
                -0.5f,
                 0.5f),

            new Vector3(
                -0.5f,
                 0.5f,
                 0.5f),

            new Vector3(
                 0.5f,
                 0.5f,
                 0.5f),

            new Vector3(
                0.0f,
                0.0f,
                1.0f));

        // Left
        AddQuad(
            vertices,
            indices,

            new Vector3(
                -0.5f,
                -0.5f,
                 0.5f),

            new Vector3(
                -0.5f,
                -0.5f,
                -0.5f),

            new Vector3(
                -0.5f,
                 0.5f,
                -0.5f),

            new Vector3(
                -0.5f,
                 0.5f,
                 0.5f),

            new Vector3(
                -1.0f,
                0.0f,
                0.0f));

        // Right
        AddQuad(
            vertices,
            indices,

            new Vector3(
                0.5f,
               -0.5f,
               -0.5f),

            new Vector3(
                0.5f,
               -0.5f,
                0.5f),

            new Vector3(
                0.5f,
                0.5f,
                0.5f),

            new Vector3(
                0.5f,
                0.5f,
               -0.5f),

            new Vector3(
                1.0f,
                0.0f,
                0.0f));

        // Bottom
        AddQuad(
            vertices,
            indices,

            new Vector3(
                -0.5f,
                -0.5f,
                 0.5f),

            new Vector3(
                 0.5f,
                -0.5f,
                 0.5f),

            new Vector3(
                 0.5f,
                -0.5f,
                -0.5f),

            new Vector3(
                -0.5f,
                -0.5f,
                -0.5f),

            new Vector3(
                0.0f,
                -1.0f,
                0.0f));

        // Top
        AddQuad(
            vertices,
            indices,

            new Vector3(
                -0.5f,
                 0.5f,
                -0.5f),

            new Vector3(
                 0.5f,
                 0.5f,
                -0.5f),

            new Vector3(
                 0.5f,
                 0.5f,
                 0.5f),

            new Vector3(
                -0.5f,
                 0.5f,
                 0.5f),

            new Vector3(
                0.0f,
                1.0f,
                0.0f));

        return new Mesh(
            "Cube",
            vertices.ToArray(),
            indices.ToArray());
    }

    // =========================================================
    // Sphere
    // =========================================================

    private static Mesh CreateSphere()
    {
        const int latitudeSegments =
            12;

        const int longitudeSegments =
            16;

        const float radius =
            0.5f;

        List<float> vertices =
            new();

        List<uint> indices =
            new();

        // -----------------------------------------------------
        // Vertices
        // -----------------------------------------------------

        for (
            int latitude = 0;
            latitude <= latitudeSegments;
            latitude++)
        {
            float v =
                latitude /
                (float)latitudeSegments;

            float theta =
                v *
                MathF.PI;

            float sinTheta =
                MathF.Sin(
                    theta);

            float cosTheta =
                MathF.Cos(
                    theta);

            for (
                int longitude = 0;
                longitude <= longitudeSegments;
                longitude++)
            {
                float u =
                    longitude /
                    (float)longitudeSegments;

                float phi =
                    u *
                    MathF.PI *
                    2.0f;

                float sinPhi =
                    MathF.Sin(
                        phi);

                float cosPhi =
                    MathF.Cos(
                        phi);

                Vector3 normal =
                    new Vector3(
                        sinTheta *
                        cosPhi,

                        cosTheta,

                        sinTheta *
                        sinPhi);

                Vector3 position =
                    normal *
                    radius;

                AddVertex(
                    vertices,
                    position,
                    normal,
                    new Vector2(
                        u,
                        1.0f - v));
            }
        }

        // -----------------------------------------------------
        // Indices
        // -----------------------------------------------------

        int rowLength =
            longitudeSegments +
            1;

        for (
            int latitude = 0;
            latitude < latitudeSegments;
            latitude++)
        {
            for (
                int longitude = 0;
                longitude < longitudeSegments;
                longitude++)
            {
                uint current =
                    (uint)(
                        latitude *
                        rowLength +
                        longitude);

                uint next =
                    current +
                    (uint)rowLength;

                indices.Add(
                    current);

                indices.Add(
                    next);

                indices.Add(
                    current + 1);

                indices.Add(
                    current + 1);

                indices.Add(
                    next);

                indices.Add(
                    next + 1);
            }
        }

        return new Mesh(
            "Sphere",
            vertices.ToArray(),
            indices.ToArray());
    }

    // =========================================================
    // Plane
    // =========================================================

    private static Mesh CreatePlane()
    {
        // Plane lies on X/Z with +Y facing upward.
        //
        // Size:
        // 1 x 1 world unit.

        List<float> vertices =
            new();

        Vector3 normal =
            Vector3.UnitY;

        AddVertex(
            vertices,
            new Vector3(
                -0.5f,
                 0.0f,
                -0.5f),
            normal,
            new Vector2(
                0.0f,
                0.0f));

        AddVertex(
            vertices,
            new Vector3(
                 0.5f,
                 0.0f,
                -0.5f),
            normal,
            new Vector2(
                1.0f,
                0.0f));

        AddVertex(
            vertices,
            new Vector3(
                 0.5f,
                 0.0f,
                 0.5f),
            normal,
            new Vector2(
                1.0f,
                1.0f));

        AddVertex(
            vertices,
            new Vector3(
                -0.5f,
                 0.0f,
                 0.5f),
            normal,
            new Vector2(
                0.0f,
                1.0f));

        uint[] indices =
        {
            0, 2, 1,
            0, 3, 2
        };

        return new Mesh(
            "Plane",
            vertices.ToArray(),
            indices);
    }

    // =========================================================
    // Geometry Helpers
    // =========================================================

    private static void AddQuad(
        List<float> vertices,
        List<uint> indices,
        Vector3 bottomLeft,
        Vector3 bottomRight,
        Vector3 topRight,
        Vector3 topLeft,
        Vector3 normal)
    {
        uint startIndex =
            (uint)(
                vertices.Count /
                Mesh.FloatsPerVertex);

        AddVertex(
            vertices,
            bottomLeft,
            normal,
            new Vector2(
                0.0f,
                0.0f));

        AddVertex(
            vertices,
            bottomRight,
            normal,
            new Vector2(
                1.0f,
                0.0f));

        AddVertex(
            vertices,
            topRight,
            normal,
            new Vector2(
                1.0f,
                1.0f));

        AddVertex(
            vertices,
            topLeft,
            normal,
            new Vector2(
                0.0f,
                1.0f));

        indices.Add(
            startIndex);

        indices.Add(
            startIndex + 2);

        indices.Add(
            startIndex + 1);

        indices.Add(
            startIndex);

        indices.Add(
            startIndex + 3);

        indices.Add(
            startIndex + 2);
    }

    private static void AddVertex(
        List<float> vertices,
        Vector3 position,
        Vector3 normal,
        Vector2 uv)
    {
        // Position

        vertices.Add(
            position.X);

        vertices.Add(
            position.Y);

        vertices.Add(
            position.Z);

        // Normal

        vertices.Add(
            normal.X);

        vertices.Add(
            normal.Y);

        vertices.Add(
            normal.Z);

        // UV

        vertices.Add(
            uv.X);

        vertices.Add(
            uv.Y);
    }
}
