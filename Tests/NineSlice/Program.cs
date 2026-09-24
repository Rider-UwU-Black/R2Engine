using System.Numerics;
using R2Engine.Editor.Scene;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
var plain = UiNineSlice.Quads(Vector2.Zero, new(100, 60), Vector4.Zero, Vector4.Zero, Vector2.One).ToArray();
Check(plain.Length == 1 && plain[0].UvMax == Vector2.One, "Zero borders must preserve single-quad stretching");
var sliced = UiNineSlice.Quads(Vector2.Zero, new(200, 100), new(10, 20, 30, 15), new(.1f, .2f, .3f, .15f), Vector2.One).ToArray();
Check(sliced.Length == 9, "Expected nine patches");
Check(sliced[0].Max == new Vector2(10, 20), "Top-left corner dimensions");
Check(sliced[8].Min == new Vector2(170, 85), "Bottom-right corner dimensions");
Check(sliced[4].UvMin == new Vector2(.1f, .2f), "Center UV begins after margins");
foreach (var size in new[] { new Vector2(5, 3), new Vector2(100, 60), new Vector2(200, 100) })
{
    var quads = UiNineSlice.Quads(Vector2.Zero, size, new(10,20,30,15), new(.1f,.2f,.3f,.15f), new(2,3)).ToArray();
    float area = 0;
    foreach (var q in quads)
    {
        Check(q.Min.X >= -.001f && q.Min.Y >= -.001f && q.Max.X <= size.X + .001f && q.Max.Y <= size.Y + .001f, "Patch outside destination");
        area += (q.Max.X-q.Min.X)*(q.Max.Y-q.Min.Y);
    }
    Check(MathF.Abs(area-size.X*size.Y) < .1f, "Patches must cover destination exactly");
}
Console.WriteLine("PASS: zero borders, nine patches, asymmetric corners, UV boundaries, small targets and nonuniform scaling.");
Vector4 region = UiNineSlice.Region(new(64, 64), new(16, 8, 24, 32));
Check(region == new Vector4(.25f, .125f, .625f, .625f), "Top-left pixel crop to normalized UV bounds");
Check(UiNineSlice.Region(new(64,64), Vector4.Zero) == new Vector4(0,0,1,1), "Old images default to full sheet");
Check(UiNineSlice.Region(new(64,64), new(60,60,32,32)) == new Vector4(.9375f,.9375f,1,1), "Crop clamps to image bounds");
Check(UiNineSlice.MapUv(Vector2.Zero, region) == new Vector2(.25f,.125f), "Crop origin");
Check(UiNineSlice.MapUv(Vector2.One, region) == new Vector2(.625f,.625f), "Crop end");
Scene scene = new("Atlas");
var img = scene.CreateGameObject("Icon").AddComponent<UIImage>();
img.SourceRect = new(16,8,24,32);
Scene copy = SceneSerializer.Deserialize(SceneSerializer.Serialize(scene));
Check(copy.GameObjects[0].GetComponent<UIImage>()!.SourceRect == img.SourceRect, "Crop survives scene/prefab serialization");
foreach (var q in UiNineSlice.Quads(Vector2.Zero, new(100,100), new(4), new(.1f), Vector2.One))
{
    var min = UiNineSlice.MapUv(q.UvMin, region); var max = UiNineSlice.MapUv(q.UvMax, region);
    Check(min.X >= region.X && min.Y >= region.Y && max.X <= region.Z && max.Y <= region.W, "Nine-slice UVs stay inside crop");
}
Console.WriteLine("PASS: atlas coordinates, full-image default, clamping, serialization and cropped nine-slice UVs.");
R2Engine.Runtime.Platform.RuntimePlatform.Configure(new DesktopPlatformServices("Atlas test", () => { }));
string output = Path.GetFullPath(Path.Combine("Builds", "AtlasTest-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(output);
string texture = Path.GetFullPath("R2Engine.Editor/Assets/Textures/test.png");
var source = StbImageSharp.ImageResult.FromMemory(File.ReadAllBytes(texture), StbImageSharp.ColorComponents.RedGreenBlueAlpha);
scene.GameObjects[0].AddComponent<Canvas>();
img.TexturePath = texture;
img.SourceRect = new(source.Width / 4f, source.Height / 4f, source.Width / 2f, source.Height / 2f);
var second = scene.CreateGameObject("Second icon");
second.AddComponent<Canvas>();
second.AddComponent<UIImage>().TexturePath = texture;
string path = Path.Combine(output, "Atlas.r2scene");
File.WriteAllText(path, SceneSerializer.Serialize(scene));
ScenePackageExporter.Export(new[] { path }, output, new R2Engine.Editor.ProjectSettings());
byte[] cooked = File.ReadAllBytes(Path.Combine(output, "R2Data/Scenes/Atlas.r2scene"));
Check(BitConverter.ToUInt32(cooked, 4) == 24, "Atlas package version");
Check(BitConverter.ToUInt32(cooked, 12) == 1, "Two atlas images reuse one texture slot");
int records = cooked.Length - 2 * 648;
Check(BitConverter.ToSingle(cooked, records + 504) == .25f && BitConverter.ToSingle(cooked, records + 508) == .25f &&
    BitConverter.ToSingle(cooked, records + 512) == .75f && BitConverter.ToSingle(cooked, records + 516) == .75f,
    "Pixel crop exported as normalized source region");
Check(BitConverter.ToSingle(cooked, records + 648 + 512) == 1f && BitConverter.ToSingle(cooked, records + 648 + 516) == 1f,
    "Uncropped image exports whole texture bounds");
Console.WriteLine("PASS: PS2 crop export, full-image export and shared atlas texture slot.");
var go = second.AddComponent<UIButton>();
go.Action = UIButtonAction.GoToScene;
go.TargetScene = "Atlas";
var restored = SceneSerializer.Deserialize(SceneSerializer.Serialize(scene)).FindGameObject("Second icon")!.GetComponent<UIButton>()!;
Check(restored.Action == UIButtonAction.GoToScene && restored.TargetScene == "Atlas", "Scene button serialization");
SceneManager.ClearRuntimeState();
Canvas.ApplyCanvasAction(scene, go);
Check(SceneManager.TakePendingRequest()?.SceneName == "Atlas", "Click queues existing scene loading flow");
second.RemoveComponent<UIImage>();
File.WriteAllText(path, SceneSerializer.Serialize(scene));
ScenePackageExporter.Export(new[] { path }, output, new R2Engine.Editor.ProjectSettings());
cooked = File.ReadAllBytes(Path.Combine(output, "R2Data/Scenes/Atlas.r2scene"));
Check(BitConverter.ToUInt32(cooked, cooked.Length - 648 + 144) % 65536 == 9, "PS2 scene action code");
Check(System.Text.Encoding.UTF8.GetString(cooked, cooked.Length - 128, 128).TrimEnd('\0') == "Atlas.r2scene", "PS2 target filename survives export");
go.TargetScene = "NotInBuild";
File.WriteAllText(path, SceneSerializer.Serialize(scene));
try { ScenePackageExporter.Export(new[] { path }, output, new R2Engine.Editor.ProjectSettings()); throw new Exception("Missing target accepted"); }
catch (InvalidOperationException e) { Check(e.Message.Contains("not included"), "Useful missing target error"); }
Console.WriteLine("PASS: scene button serialization, deferred loading, PS2 action/target export and missing-target validation.");
