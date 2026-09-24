using R2Engine.Editor.Scene;

static void Check(bool value, string message) { if (!value) throw new Exception(message); }
var scene = new Scene("Assignment");
var obj = scene.CreateGameObject("Cube");
var script = obj.AddComponent<ScriptComponent>();
script.AssignScriptAsset("Assets/Scripts/Ps2TriggerTurn.cs");
script.FieldValues["Degrees"] = "45";
string before = SceneSerializer.Serialize(scene); // same snapshots used by editor undo
script.AssignScriptAsset("Assets\\Scripts\\Ps2TriggerTurn.cs");
Check(script.FieldValues["Degrees"] == "45", "Same asset, alternate separators keeps overrides");
script.AssignScriptAsset("assets/scripts/PS2TRIGGERTURN.cs");
Check(script.FieldValues.Count == 1, "Case-insensitive same asset preserves overrides");
script.AssignScriptAsset("Assets/Scripts/Ps2TriggerEnterExit.cs");
Check(script.FieldValues.Count == 0, "Switching scripts removes old Degrees override");
Check(script.ScriptPath.EndsWith("Ps2TriggerEnterExit.cs"), "New asset assigned");
string after = SceneSerializer.Serialize(scene);
var restored = SceneSerializer.Deserialize(before).GameObjects.Single().GetComponent<ScriptComponent>()!;
Check(restored.ScriptPath.EndsWith("Ps2TriggerTurn.cs") && restored.FieldValues["Degrees"] == "45", "Undo snapshot restores path and values together");
var redone = SceneSerializer.Deserialize(after).GameObjects.Single().GetComponent<ScriptComponent>()!;
Check(redone.ScriptPath.EndsWith("Ps2TriggerEnterExit.cs") && redone.FieldValues.Count == 0, "Redo/save-load preserves clean reassignment");
script.FieldValues["EnterDegrees"] = "30";
script.ScriptPath = "Assets/Scripts/Renamed.cs"; // editor rename path deliberately bypasses reassignment
Check(script.FieldValues["EnterDegrees"] == "30", "Asset rename preserves values");
script.AssignScriptAsset("");
Check(script.FieldValues.Count == 0, "Unassign clears values");
Console.WriteLine("PASS: different-script reset, same-asset preservation, separator/case handling, snapshot undo/redo, rename preservation, unassignment");
