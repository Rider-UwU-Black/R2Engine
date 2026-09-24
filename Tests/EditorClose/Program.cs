using System.Reflection;
using System.Runtime.CompilerServices;
using R2Engine.Editor;
using R2Engine.Editor.Scene;
using R2Engine.Runtime.Platform;

static void Check(bool value, string message) { if (!value) throw new Exception(message); }
RuntimePlatform.Configure(new DesktopPlatformServices("Close guard test", () => { }));
// Exercise the actual editor's dirty-check / request routing without a GL window.
var editor = (EditorUI)RuntimeHelpers.GetUninitializedObject(typeof(EditorUI));
var flags = BindingFlags.Instance | BindingFlags.NonPublic;
void Set(string field, object? value) => typeof(EditorUI).GetField(field, flags)!.SetValue(editor, value);
object? Get(string field) => typeof(EditorUI).GetField(field, flags)!.GetValue(editor);
var scene = new Scene("Close test");
Set("_scene", scene);
Set("_savedSceneSnapshot", SceneSerializer.Serialize(scene));
Check(editor.ConfirmWindowClose(), "Unchanged scene may close");
scene.CreateGameObject("Unsaved work");
Check(!editor.ConfirmWindowClose(), "Dirty untitled scene must veto close");
Check((bool)Get("_openUnsavedChangesPopup")!, "Prompt requested");
Check(Get("_pendingSceneAction")!.ToString() == "CloseEditor", "Pending action must be editor close");
Check(!editor.ConfirmWindowClose(), "Repeated X must not bypass confirmation");

string folder = Path.Combine(Path.GetTempPath(), "R2CloseTest-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
Set("_currentScenePath", folder); // a directory cannot be overwritten by a scene
bool saved = (bool)typeof(EditorUI).GetMethod("SaveScene", flags)!.Invoke(editor, null)!;
Check(!saved && !editor.ConfirmWindowClose(), "Failed save must not authorize closing");
Set("_currentScenePath", Path.Combine(folder, "Saved.r2scene"));
saved = (bool)typeof(EditorUI).GetMethod("SaveScene", flags)!.Invoke(editor, null)!;
Check(saved && editor.ConfirmWindowClose(), "Successful save clears dirty state");
Check(File.ReadAllText(Path.Combine(folder, "Saved.r2scene")).Contains("Unsaved work"), "Saved data contains changes");
Console.WriteLine("PASS: clean close, dirty untitled prompt, repeated close, failed save and successful save.");
var previewScene = new Scene("Preview");
var main = previewScene.CreateGameObject("Main");
var mainCanvas = main.AddComponent<Canvas>();
mainCanvas.StartsVisible = true;
var settings = previewScene.CreateGameObject("Settings");
var settingsCanvas = settings.AddComponent<Canvas>();
var child = previewScene.CreateGameObject("Settings button");
child.SetParent(settings, false);
child.AddComponent<UIButton>();
var preview = new CanvasPreviewState();
string previewBefore = SceneSerializer.Serialize(previewScene);
Check(preview.Resolve(previewScene, null) == mainCanvas, "Default preview uses initially visible canvas");
Check(preview.Resolve(previewScene, child) == settingsCanvas, "Child selection focuses its owner");
Check(preview.Resolve(previewScene, null) == settingsCanvas, "Preview retains canvas focus");
Check(preview.Resolve(previewScene, main) == mainCanvas, "Selecting another canvas switches focus");
Check(SceneSerializer.Serialize(previewScene) == previewBefore && !settingsCanvas.IsVisible,
    "Preview must not mutate saved or runtime visibility");
Check(preview.Resolve(new Scene("Other"), null) == null, "Focus does not leak across scenes");
Console.WriteLine("PASS: canvas focus, child selection, retained focus, non-mutating preview and scene isolation.");
string preferencesPath = Path.Combine(folder, "CanvasPreview.editor.json");
Check(!preview.ShowInScene, "Canvas overlay hidden on startup");
preview.Isolate = false; preview.ShowInScene = true;
preview.SavePreferences(preferencesPath);
var reopened = new CanvasPreviewState(); reopened.LoadPreferences(preferencesPath);
Check(!reopened.Isolate && !reopened.ShowInScene, "Focus preference survives restart while overlay starts hidden");
reopened.Isolate = true; reopened.SavePreferences(preferencesPath);
preview.LoadPreferences(preferencesPath);
Check(preview.Isolate && !preview.ShowInScene, "Enabled isolation also persists");
Console.WriteLine("PASS: canvas preview defaults off and focus preference persists both ways.");
Set("_projectSettings", new ProjectSettings { BuildScenes = new(), StartupScene = "" });
var resolve = typeof(EditorUI).GetMethod("ResolveRuntimeScenePath", flags)!;
string expectedScene = Directory.GetFiles(Path.Combine(AssetDatabase.ProjectRoot, "Scenes"), "*.r2scene").First();
Check(File.Exists(expectedScene), "Saved scene fixture exists");
foreach (string reference in new[] { Path.GetFileNameWithoutExtension(expectedScene), Path.GetFileName(expectedScene), Path.Combine("Scenes", Path.GetFileName(expectedScene)) })
    Check((string)resolve.Invoke(editor, new object[] { reference })! == expectedScene,
        "Editor resolves saved scene independently of build list: " + reference);
try { resolve.Invoke(editor, new object[] { "MissingScene-" + Guid.NewGuid() }); throw new Exception("Missing scene accepted"); }
catch (TargetInvocationException error) { Check(error.InnerException is FileNotFoundException, "Missing scenes still report an error"); }
Console.WriteLine("PASS: editor scene-name, filename and path lookup without build entries; missing-scene error.");
