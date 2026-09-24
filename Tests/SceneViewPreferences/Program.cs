using R2Engine.Editor;

string path = Path.Combine(Path.GetTempPath(), "R2SceneView-" + Guid.NewGuid() + ".json");
try
{
    if (!SceneViewPreferences.Load(path).ShowHelperGizmos) throw new Exception("Missing-file default");
    new SceneViewPreferences { ShowHelperGizmos = false }.Save(path);
    if (SceneViewPreferences.Load(path).ShowHelperGizmos) throw new Exception("Off did not persist");
    new SceneViewPreferences { ShowHelperGizmos = true }.Save(path);
    if (!SceneViewPreferences.Load(path).ShowHelperGizmos) throw new Exception("On did not persist");
    File.WriteAllText(path, "invalid json");
    if (!SceneViewPreferences.Load(path).ShowHelperGizmos) throw new Exception("Invalid-file fallback");
    Console.WriteLine("PASS: helper gizmo preferences persist both states and handle missing/corrupt files.");
}
finally { File.Delete(path); }
