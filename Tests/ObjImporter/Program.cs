using System.Numerics;
using R2Engine.Editor.Scene;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void CheckUV(Vector2 actual, Vector2 expected, string message)
{
    const float tolerance = 0.00001f;
    Check(Vector2.Distance(actual, expected) <= tolerance,
        $"{message}: expected {expected}, got {actual}");
}

string objPath = Path.Combine(Path.GetTempPath(), $"r2-obj-uv-{Guid.NewGuid():N}.obj");

try
{
    File.WriteAllText(objPath,
        "v 0 0 0\n" +
        "v 1 0 0\n" +
        "v 0 1 0\n" +
        "vt 0.25 0.125\n" +
        "vt 1.25 -0.5\n" +
        "vt 0 1\n" +
        "vn 0 0 1\n" +
        "usemtl Room Walls\n" +
        "f 1/1/1 2/2/1 3/3/1\n" +
        "usemtl Floor\n" +
        "f 3/3/1 2/2/1 1/1/1\n");

    Mesh mesh = ObjImporter.Load(objPath);

    Check(mesh.VertexCount == 3, "Shared vertex count");
    Check(mesh.TriangleCount == 2, "Both material sections retain their triangles");
    Check(mesh.Submeshes.Count == 2, "Authored material sections remain separate submeshes");
    Check(mesh.Submeshes[0] == new MeshSubmesh("Room Walls", 0, 3), "First material slot and range");
    Check(mesh.Submeshes[1] == new MeshSubmesh("Floor", 3, 3), "Second material slot and range");
    CheckUV(mesh.GetUV(0), new Vector2(0.25f, 0.875f), "Standard OBJ V is inverted");
    CheckUV(mesh.GetUV(1), new Vector2(1.25f, 1.5f), "Tiled UVs remain unclamped");
    CheckUV(mesh.GetUV(2), new Vector2(0.0f, 0.0f), "Top edge maps to zero");

    File.WriteAllText(objPath,
        "v 0 0 0\nv 1 0 0\nv 0 1 0\n" +
        "o North Wall\nusemtl Plaster\nf 1 2 3\n" +
        "g Door\nusemtl Wood\nf 3 2 1\n");
    Mesh grouped = ObjImporter.Load(objPath);
    Check(grouped.Submeshes.Count == 2, "OBJ object/group sections remain separate");
    Check(grouped.Submeshes[0].ObjectName == "North Wall", "Object name retained");
    Check(grouped.Submeshes[1].ObjectName == "Door", "Group name retained");
}
finally
{
    File.Delete(objPath);
}

Console.WriteLine("PASS: OBJ UV conversion plus separate, named material submeshes.");
