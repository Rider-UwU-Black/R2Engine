using R2Engine.Editor;
using R2Engine.Editor.Scene;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

string suffix = Guid.NewGuid().ToString("N");
string scenePath = Path.Combine(Path.GetTempPath(), $"collider-only-{suffix}.json");
string build = Path.Combine(Path.GetTempPath(), $"r2-collider-only-{suffix}");

try
{
    Scene scene = new("Collider Only");
    scene.CreateGameObject("Visible").AddComponent<MeshRenderer>();
    GameObject floor = scene.CreateGameObject("Floor Collision");
    floor.Transform.Position = new System.Numerics.Vector3(0, -0.25f, 0);
    floor.AddComponent<BoxCollider>().Size = new System.Numerics.Vector3(10, 0.5f, 10);
    SceneSerializer.Save(scene, scenePath);

    ScenePackageExportResult result = ScenePackageExporter.Export(
        new[] { scenePath }, build, new ProjectSettings());
    byte[] package = File.ReadAllBytes(Path.Combine(build, "R2Data", "Scenes",
        Path.ChangeExtension(Path.GetFileName(scenePath), ".r2scene")));

    Check(result.InstanceCount == 2, "Collider-only box is counted as a native instance");
    Check(BitConverter.ToUInt32(package, 16) == 2, "R2SC header includes collider-only box");

    int cursor = 140;
    uint meshCount = BitConverter.ToUInt32(package, 8);
    uint textureCount = BitConverter.ToUInt32(package, 12);
    for (uint i = 0; i < meshCount + textureCount; ++i)
    {
        int length = BitConverter.ToInt32(package, cursor);
        cursor += 4 + length;
    }
    const int instanceRecordSize = 64;
    int colliderOnlyRecord = cursor + instanceRecordSize;
    Check(BitConverter.ToUInt32(package, colliderOnlyRecord) == uint.MaxValue,
        "Collider-only instance has no render mesh");
}
finally
{
    if (File.Exists(scenePath)) File.Delete(scenePath);
    if (Directory.Exists(build)) Directory.Delete(build, true);
}

Console.WriteLine("PASS: collider-only solid boxes are cooked without render meshes.");
