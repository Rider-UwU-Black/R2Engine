using System.Numerics;
using R2Engine.Editor;
using R2Engine.Editor.Scene;

string repositoryRoot = Directory.GetCurrentDirectory();
if (File.Exists(Path.Combine(repositoryRoot, "R2Engine.Editor", "R2Engine.Editor.csproj")))
    Directory.SetCurrentDirectory(Path.Combine(repositoryRoot, "R2Engine.Editor"));
RuntimeAssetServices.MeshLoader = MeshAssetCache.Get;
RuntimeAssetServices.MaterialLoader = MaterialAssetCache.Get;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

string suffix = Guid.NewGuid().ToString("N");
string modelRelative = $"Assets/Models/multi-{suffix}.obj";
string firstMaterialRelative = $"Assets/Materials/multi-a-{suffix}.r2mat";
string secondMaterialRelative = $"Assets/Materials/multi-b-{suffix}.r2mat";
string scenePath = Path.Combine(AssetDatabase.ProjectRoot, "Scenes", $"multi-{suffix}.r2scene");
string build = Path.Combine(Path.GetTempPath(), $"r2-multi-{suffix}");
string modelPath = AssetDatabase.ToAbsolutePath(modelRelative);
string firstMaterialPath = AssetDatabase.ToAbsolutePath(firstMaterialRelative);
string secondMaterialPath = AssetDatabase.ToAbsolutePath(secondMaterialRelative);

try
{
    Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
    Directory.CreateDirectory(Path.GetDirectoryName(firstMaterialPath)!);
    File.WriteAllText(modelPath,
        "v 0 0 0\nv 1 0 0\nv 0 1 0\nvt 0 0\nvt 1 0\nvt 0 1\nvn 0 0 1\n" +
        "usemtl Walls\nf 1/1/1 2/2/1 3/3/1\nusemtl Floor\nf 3/3/1 2/2/1 1/1/1\n");
    Material first = new("Walls") { BaseColor = new Vector4(1, 0, 0, 1),
        TextureTiling = new Vector2(3, 2), TextureOffset = new Vector2(.25f, -.5f) };
    Material second = new("Floor") { BaseColor = new Vector4(0, 1, 0, 1), DoubleSided = true };
    MaterialSerializer.Save(first, firstMaterialPath);
    MaterialSerializer.Save(second, secondMaterialPath);

    Scene scene = new("Multi Material");
    MeshRenderer renderer = scene.CreateGameObject("Room").AddComponent<MeshRenderer>();
    renderer.MeshPath = modelRelative;
    renderer.SetMaterialPath(0, firstMaterialRelative);
    renderer.SetMaterialPath(1, secondMaterialRelative);
    SceneSerializer.Save(scene, scenePath);

    ScenePackageExporter.Export(new[] { scenePath }, build, new ProjectSettings());
    byte[] package = File.ReadAllBytes(Path.Combine(build, "R2Data", "Scenes", Path.GetFileName(scenePath)));
    Check(BitConverter.ToUInt32(package, 4) == 46, "Tiled multi-material scene uses R2SC v46");
    const int recordSize = 72;
    const int inheritedPlaybackBytes = 92;
    int drawTable = package.Length - inheritedPlaybackBytes - 4 - recordSize * 2;
    Check(BitConverter.ToUInt32(package, drawTable - 4) == 0,
        "V45 retains the inherited zero-door table");
    Check(BitConverter.ToUInt32(package, drawTable) == 2, "Two submesh draw records");
    int firstDraw = drawTable + 4;
    int secondDraw = firstDraw + recordSize;
    Check(BitConverter.ToUInt32(package, firstDraw) == 0 && BitConverter.ToUInt32(package, secondDraw) == 0,
        "Both draws reference one gameplay object");
    Check(BitConverter.ToUInt32(package, firstDraw + 12) == 0 && BitConverter.ToUInt32(package, firstDraw + 16) == 3,
        "First submesh index range");
    Check(BitConverter.ToUInt32(package, secondDraw + 12) == 3 && BitConverter.ToUInt32(package, secondDraw + 16) == 3,
        "Second submesh index range");
    Check(BitConverter.ToUInt32(package, secondDraw + 28) == 1, "Second material keeps double-sided state");
    Check(Math.Abs(BitConverter.ToSingle(package, firstDraw + 40) - 1.0f) < 0.0001f,
        "First material color is serialized");
    Check(Math.Abs(BitConverter.ToSingle(package, secondDraw + 44) - 1.0f) < 0.0001f,
        "Second material color is serialized");
    Check(new Vector2(BitConverter.ToSingle(package, firstDraw + 56), BitConverter.ToSingle(package, firstDraw + 60)) == new Vector2(3, 2),
        "Texture tiling is serialized");
    Check(new Vector2(BitConverter.ToSingle(package, firstDraw + 64), BitConverter.ToSingle(package, firstDraw + 68)) == new Vector2(.25f, -.5f),
        "Texture offset is serialized");
}
finally
{
    foreach (string path in new[] { modelPath, firstMaterialPath, secondMaterialPath, scenePath })
        if (File.Exists(path)) File.Delete(path);
    if (Directory.Exists(build)) Directory.Delete(build, true);
    MeshAssetCache.Clear();
    MaterialAssetCache.Clear();
}

Console.WriteLine("PASS: PS2 v46 keeps material draws, tiling, and offset attached to one scene object.");
