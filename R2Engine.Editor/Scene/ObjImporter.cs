using System.Globalization;
using System.Numerics;

namespace R2Engine.Editor.Scene;

public static class ObjImporter
{
    private sealed class Section
    {
        public string? ObjectName;
        public string MaterialName = "Material";
        public readonly List<uint> Indices = new();
    }

    private readonly record struct VertexKey(
        int PositionIndex,
        int TextureIndex,
        int NormalIndex);

    public static Mesh Load(
        string filePath)
    {
        if (!File.Exists(
                filePath))
        {
            throw new FileNotFoundException(
                "OBJ file was not found.",
                filePath);
        }

        List<Vector3> positions =
            new();

        List<Vector2> textureCoordinates =
            new();

        List<Vector3> normals =
            new();

        List<float> vertexData =
            new();

        List<uint> indices =
            new();

        List<Section> sections = new();

        string activeMaterial =
            "Material";
        string? activeObject = null;
        Section? activeSection = null;

        Dictionary<VertexKey, uint> vertexLookup =
            new();

        foreach (
            string rawLine
            in File.ReadLines(
                filePath))
        {
            string line =
                rawLine.Trim();

            if (string.IsNullOrWhiteSpace(
                    line) ||
                line.StartsWith(
                    '#'))
            {
                continue;
            }

            string[] parts =
                line.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length ==
                0)
            {
                continue;
            }

            switch (parts[0])
            {
                case "v":

                    ParsePosition(
                        parts,
                        positions);

                    break;

                case "vt":

                    ParseTextureCoordinate(
                        parts,
                        textureCoordinates);

                    break;

                case "vn":

                    ParseNormal(
                        parts,
                        normals);

                    break;

                case "f":
                    if (activeSection == null ||
                        !string.Equals(activeSection.ObjectName, activeObject, StringComparison.Ordinal) ||
                        !string.Equals(activeSection.MaterialName, activeMaterial, StringComparison.Ordinal))
                    {
                        activeSection = new Section
                        {
                            ObjectName = activeObject,
                            MaterialName = activeMaterial
                        };
                        sections.Add(activeSection);
                    }

                    ParseFace(
                        parts,
                        positions,
                        textureCoordinates,
                        normals,
                        vertexLookup,
                        vertexData,
                        activeSection.Indices);

                    break;

                case "usemtl":

                    activeMaterial =
                        parts.Length > 1
                            ? string.Join(' ', parts.Skip(1))
                            : "Material";

                    activeSection = null;

                    break;

                case "o":
                case "g":
                    activeObject = parts.Length > 1
                        ? string.Join(' ', parts.Skip(1))
                        : "Object";
                    activeSection = null;

                    break;
            }
        }

        List<MeshSubmesh> submeshes =
            new();

        foreach (Section section in sections)
        {
            List<uint> sectionIndices = section.Indices;
            if (sectionIndices.Count == 0)
                continue;

            int indexStart = indices.Count;
            indices.AddRange(sectionIndices);
            submeshes.Add(new MeshSubmesh(section.MaterialName, indexStart,
                sectionIndices.Count, section.ObjectName));
        }

        if (vertexData.Count ==
                0 ||
            indices.Count ==
                0)
        {
            throw new InvalidDataException(
                "OBJ did not contain any renderable faces.");
        }

        Mesh mesh =
            new Mesh(
                Path.GetFileNameWithoutExtension(
                    filePath),
                vertexData.ToArray(),
                indices.ToArray(),
                submeshes);

        return mesh;
    }

    private static void ParsePosition(
        string[] parts,
        List<Vector3> positions)
    {
        if (parts.Length <
            4)
        {
            return;
        }

        positions.Add(
            new Vector3(
                ParseFloat(
                    parts[1]),
                ParseFloat(
                    parts[2]),
                ParseFloat(
                    parts[3])));
    }

    private static void ParseTextureCoordinate(
        string[] parts,
        List<Vector2> textureCoordinates)
    {
        if (parts.Length <
            3)
        {
            return;
        }

        float u =
            ParseFloat(
                parts[1]);

        float v =
            ParseFloat(
                parts[2]);

        // Wavefront OBJ uses a bottom-left texture origin, while decoded image
        // rows in the renderer use a top-left origin. Convert at import time so
        // editor rendering and exported meshes share the same convention.
        // Do not clamp: tiled UVs outside 0..1 are valid OBJ data.
        textureCoordinates.Add(
            new Vector2(
                u,
                1.0f - v));
    }

    private static void ParseNormal(
        string[] parts,
        List<Vector3> normals)
    {
        if (parts.Length <
            4)
        {
            return;
        }

        Vector3 normal =
            new Vector3(
                ParseFloat(
                    parts[1]),
                ParseFloat(
                    parts[2]),
                ParseFloat(
                    parts[3]));

        if (normal.LengthSquared() >
            0.000001f)
        {
            normal =
                Vector3.Normalize(
                    normal);
        }

        normals.Add(
            normal);
    }

    private static void ParseFace(
        string[] parts,
        List<Vector3> positions,
        List<Vector2> textureCoordinates,
        List<Vector3> normals,
        Dictionary<VertexKey, uint> vertexLookup,
        List<float> vertexData,
        List<uint> indices)
    {
        if (parts.Length <
            4)
        {
            return;
        }

        List<uint> faceIndices =
            new();

        for (
            int i = 1;
            i < parts.Length;
            i++)
        {
            VertexKey key =
                ParseVertexKey(
                    parts[i],
                    positions.Count,
                    textureCoordinates.Count,
                    normals.Count);

            if (!vertexLookup.TryGetValue(
                    key,
                    out uint vertexIndex))
            {
                vertexIndex =
                    AddVertex(
                        key,
                        positions,
                        textureCoordinates,
                        normals,
                        vertexData);

                vertexLookup.Add(
                    key,
                    vertexIndex);
            }

            faceIndices.Add(
                vertexIndex);
        }

        for (
            int i = 1;
            i <
            faceIndices.Count - 1;
            i++)
        {
            indices.Add(
                faceIndices[0]);

            indices.Add(
                faceIndices[i]);

            indices.Add(
                faceIndices[i + 1]);
        }
    }

    private static VertexKey ParseVertexKey(
        string token,
        int positionCount,
        int textureCount,
        int normalCount)
    {
        string[] values =
            token.Split('/');

        int positionIndex =
            ResolveIndex(
                ParseIndex(
                    values,
                    0),
                positionCount);

        int textureIndex =
            -1;

        int normalIndex =
            -1;

        if (values.Length >
                1 &&
            !string.IsNullOrWhiteSpace(
                values[1]))
        {
            textureIndex =
                ResolveIndex(
                    ParseIndex(
                        values,
                        1),
                    textureCount);
        }

        if (values.Length >
                2 &&
            !string.IsNullOrWhiteSpace(
                values[2]))
        {
            normalIndex =
                ResolveIndex(
                    ParseIndex(
                        values,
                        2),
                    normalCount);
        }

        return new VertexKey(
            positionIndex,
            textureIndex,
            normalIndex);
    }

    private static uint AddVertex(
        VertexKey key,
        List<Vector3> positions,
        List<Vector2> textureCoordinates,
        List<Vector3> normals,
        List<float> vertexData)
    {
        if (key.PositionIndex <
                0 ||
            key.PositionIndex >=
                positions.Count)
        {
            throw new InvalidDataException(
                "OBJ face references an invalid position.");
        }

        Vector3 position =
            positions[
                key.PositionIndex];

        Vector2 uv =
            key.TextureIndex >=
                    0 &&
                key.TextureIndex <
                    textureCoordinates.Count
                ? textureCoordinates[
                    key.TextureIndex]
                : Vector2.Zero;

        Vector3 normal =
            key.NormalIndex >=
                    0 &&
                key.NormalIndex <
                    normals.Count
                ? normals[
                    key.NormalIndex]
                : Vector3.UnitY;

        uint vertexIndex =
            (uint)(
                vertexData.Count /
                Mesh.FloatsPerVertex);

        vertexData.Add(
            position.X);

        vertexData.Add(
            position.Y);

        vertexData.Add(
            position.Z);

        vertexData.Add(
            normal.X);

        vertexData.Add(
            normal.Y);

        vertexData.Add(
            normal.Z);

        vertexData.Add(
            uv.X);

        vertexData.Add(
            uv.Y);

        return vertexIndex;
    }

    private static int ParseIndex(
        string[] values,
        int index)
    {
        if (index >=
                values.Length ||
            !int.TryParse(
                values[index],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value))
        {
            throw new InvalidDataException(
                "OBJ contains an invalid face index.");
        }

        return value;
    }

    private static int ResolveIndex(
        int objIndex,
        int count)
    {
        if (objIndex >
            0)
        {
            return objIndex -
                1;
        }

        if (objIndex <
            0)
        {
            return count +
                objIndex;
        }

        throw new InvalidDataException(
            "OBJ indices may not be zero.");
    }

    private static float ParseFloat(
        string value)
    {
        if (!float.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float result))
        {
            throw new InvalidDataException(
                $"Invalid OBJ number: {value}");
        }

        return result;
    }
}
