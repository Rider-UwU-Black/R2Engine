using R2Engine.Editor;
using R2Engine.Editor.Scene;
using R2Engine.Runtime.Platform;

static void Check(bool value, string message) { if (!value) throw new Exception(message); }
RuntimePlatform.Configure(new DesktopPlatformServices("Loading test", () => { }));
string output = Path.GetFullPath(Path.Combine("Builds", "LoadingTest-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(output);
string prefabPath = Path.Combine(output, "Loading.r2prefab");
File.WriteAllText(prefabPath, SceneSerializer.Serialize(LoadingScreen.CreateTemplate()));
Scene scene = new("Test");
scene.CreateGameObject("Cube").AddComponent<MeshRenderer>();
for (int i = 0; i < 2; i++)
{
    var root = scene.CreateGameObject("Menu" + i);
    var menu = root.AddComponent<Canvas>();
    menu.StartsVisible = menu.IsVisible = true;
    root.AddComponent<UIPanel>();
}
string original = SceneSerializer.Serialize(scene);
string scenePath = Path.Combine(output, "Test.r2scene");
File.WriteAllText(scenePath, original);
LoadingScreen.Attach(scene, prefabPath);
LoadingScreen.Attach(scene, prefabPath);
Check(scene.GameObjects.Count == 5, "Loading prefab must attach once without altering hierarchy");
Check(scene.GameObjects.Count(o => o.GetComponent<Canvas>()?.IsLoadingScreen == true) == 1, "One loading canvas");
var previous = LoadingScreen.Show(scene);
Check(previous != null && previous.Count == 3, "Preserve all canvas states");
Check(scene.GameObjects.Select(o => o.GetComponent<Canvas>()).OfType<Canvas>().All(c => c.IsVisible == c.IsLoadingScreen), "Only loading canvas visible");
LoadingScreen.Restore(previous);
Check(previous!.All(p => p.Key.IsVisible == p.Value), "Failed transition restores menu visibility");
Check(File.ReadAllText(scenePath) == original, "Authored scene unchanged");
Check(!File.ReadAllText(prefabPath).Contains("IsLoadingScreen"), "Runtime flag must not leak into authored prefab");
ProjectSettings settings = new() { LoadingScreenPrefab = prefabPath };
settings.Save(Path.Combine(output, "settings.json"));
Check(ProjectSettings.Load(Path.Combine(output, "settings.json")).LoadingScreenPrefab == prefabPath, "Prefab setting persists");
ScenePackageExporter.Export(new[] { scenePath }, output, settings);
byte[] bytes = File.ReadAllBytes(Path.Combine(output, "R2Data/Scenes/Test.r2scene"));
Check(BitConverter.ToUInt32(bytes, 4) == 24, "R2SC version");
int header = bytes.Length - 8 - 4 * 648 - 32; // two menu panels + loading panel and text
Check(BitConverter.ToUInt32(bytes, header + 24) == 3 && BitConverter.ToUInt32(bytes, header + 28) == 3, "Legacy menus retain input, loading canvas excludes it");
Check(BitConverter.ToUInt32(bytes, header + 8) == 4, "UI count");
Check(BitConverter.ToUInt32(bytes, header + 12) == 3, "Two initially visible menu bits");
Check(BitConverter.ToUInt32(bytes, header + 16) == 3, "Only menus pause gameplay");
Check(BitConverter.ToUInt32(bytes, header + 20) == 4, "Dedicated loading canvas bit");
ScenePackageExporter.Export(new[] { scenePath }, output, new ProjectSettings());
bytes = File.ReadAllBytes(Path.Combine(output, "R2Data/Scenes/Test.r2scene"));
header = bytes.Length - 8 - 2 * 648 - 32;
Check(BitConverter.ToUInt32(bytes, header + 20) == 0, "Disabled loading screen leaves scenes unchanged");
Scene menuOnly = SceneSerializer.Deserialize(original);
menuOnly.DeleteGameObject(menuOnly.FindGameObject("Cube")!);
foreach (var c in menuOnly.GameObjects.Select(o => o.GetComponent<Canvas>()).OfType<Canvas>())
    c.ToggleWithMenuInput = c.CloseWithCancelInput = false;
string menuPath = Path.Combine(output, "MenuOnly.r2scene");
File.WriteAllText(menuPath, SceneSerializer.Serialize(menuOnly));
ScenePackageExporter.Export(new[] { menuPath }, output, settings);
bytes = File.ReadAllBytes(Path.Combine(output, "R2Data/Scenes/MenuOnly.r2scene"));
Check(BitConverter.ToUInt32(bytes, 8) == 0 && BitConverter.ToUInt32(bytes, 16) == 0,
    "UI-only menus must not require dummy geometry");
header = bytes.Length - 4 * 648 - 32;
Check(BitConverter.ToUInt32(bytes, header + 24) == 0 && BitConverter.ToUInt32(bytes, header + 28) == 0, "Main menu cannot be toggled or cancelled after serialization and cooking");
Check(BitConverter.ToUInt32(bytes, header + 8) == 4 && BitConverter.ToUInt32(bytes, header + 12) == 3 &&
    BitConverter.ToUInt32(bytes, header + 20) == 4, "UI-only menu and loading canvas survive cooking");
string invalid = Path.Combine(output, "Invalid.r2prefab");
File.WriteAllText(invalid, original);
try { LoadingScreen.Attach(new Scene("Invalid"), invalid); throw new Exception("Invalid prefab accepted"); }
catch (InvalidDataException) { }
Console.WriteLine("PASS: template, hierarchy, idempotence, visibility/restore, settings, v24 input masks, UI-only menus, disabled setting and invalid prefab rejection.");
Console.WriteLine(output);
