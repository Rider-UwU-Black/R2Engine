using System.Text.Json;

namespace R2Engine.Editor.Scene;

public static class Ps2ScriptExport
{
    public sealed record Entry(string Scene, string Object, string Script, string Status, string Detail);

    public static Dictionary<GameObject, Ps2ScriptMotion> CompileScene(Scene scene, string scenePath,
        List<Entry> report, List<string> warnings, IEnumerable<InputActionBinding>? actions = null)
    {
        var motions = new Dictionary<GameObject, Ps2ScriptMotion>();
        foreach (var obj in scene.GameObjects)
        foreach (var script in obj.Components.OfType<ScriptComponent>())
        {
            string status = "Unsupported", detail;
            if (!obj.IsActiveInHierarchy)
            { status = "Inactive"; detail = "Inactive object is not exported."; }
            else if ((Path.GetFileName(script.ScriptPath).Equals("TriggerSceneLoader.cs", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(script.ScriptPath).Equals("InteractScenePortal.cs", StringComparison.OrdinalIgnoreCase)) &&
                obj.GetComponent<BoxCollider>()?.IsTrigger == true &&
                ReferenceEquals(obj.GetComponent<ScriptComponent>(), script) &&
                script.FieldValues.TryGetValue("SceneName", out var target) && !string.IsNullOrWhiteSpace(target))
            {
                status = "Native adapter";
                detail = Path.GetFileName(script.ScriptPath).Equals("InteractScenePortal.cs", StringComparison.OrdinalIgnoreCase)
                    ? "Interact-to-travel portal cooked natively for PS2."
                    : "Existing automatic scene trigger cooked natively for PS2.";
            }
            else
            {
                try
                {
                    var motion = Ps2ScriptCompiler.Compile(File.ReadAllText(AssetDatabase.ToAbsolutePath(script.ScriptPath)), script.FieldValues, actions);
                    bool trigger = motion.Timer?.Comparison is 9 or 10 or 11;
                    if (obj.Parent != null || obj.Children.Any() || obj.GetComponent<MeshRenderer>() == null ||
                        obj.Components.Any(c => c is not Transform && c is not MeshRenderer && c is not ScriptComponent && c is not PersistentObject && !(trigger && c is BoxCollider { IsTrigger: true })) ||
                        obj.Components.OfType<ScriptComponent>().Count() != 1)
                        throw new InvalidOperationException("Scripts require a root, childless Mesh Renderer with one Script; only player-trigger scripts may additionally have one trigger Box Collider.");
                    PersistentObject? persistent = obj.GetComponent<PersistentObject>();
                    if (persistent?.SaveBoolean == true && motion.Timer?.Comparison != 12)
                        throw new InvalidOperationException("Save Boolean on a scripted object currently supports only a translated bool-toggle script.");
                    if (persistent?.SaveInteger == true && motion.Timer?.Comparison is not (1 or 2 or 3 or 4 or 5 or 6 or 13))
                        throw new InvalidOperationException("Save Integer on a scripted object supports translated one-shot/repeating timer phase only.");
                    if (trigger)
                    {
                        var boxes = obj.Components.OfType<BoxCollider>().ToArray();
                        var players = scene.GameObjects.Where(o => o.IsActiveInHierarchy && o.GetComponent<PlayerController>() != null).ToArray();
                        if (boxes.Length != 1 || players.Length != 1 || players[0].GetComponent<MeshRenderer>() == null)
                            throw new InvalidOperationException("Player-trigger scripts need one trigger Box Collider and exactly one active rendered Player Controller in the scene.");
                        var playerColliders = players[0].Components.OfType<Collider>().Where(c => !c.IsTrigger).ToArray();
                        if (playerColliders.Length != 1 || players[0].Components.OfType<Collider>().Count() != 1 ||
                            playerColliders[0] is not (CapsuleCollider or BoxCollider))
                            throw new InvalidOperationException("The player needs exactly one non-trigger Capsule or Box Collider for scripted entry detection.");
                        var box = boxes[0]; var playerCollider = playerColliders[0];
                        if (box.Center != System.Numerics.Vector3.Zero ||
                            !float.IsFinite(box.Size.X) || !float.IsFinite(box.Size.Y) || !float.IsFinite(box.Size.Z) ||
                            box.Size.X <= 0 || box.Size.Y <= 0 || box.Size.Z <= 0 ||
                            box.Layer < 0 || box.Layer > 31 || playerCollider.Layer < 0 || playerCollider.Layer > 31)
                            throw new InvalidOperationException("Use a centered trigger box with positive finite dimensions and collider layers 0-31.");
                        bool allowed = (box.CollisionMask & (1u << playerCollider.Layer)) != 0 &&
                            (playerCollider.CollisionMask & (1u << box.Layer)) != 0;
                        motion = motion with { Timer = motion.Timer! with { Threshold = allowed ? 1 : 0 } };
                    }
                    motions.Add(obj, motion);
                    status = "Translated";
                    detail = motion.Startup != null
                        ? "Start applies fixed transform increments once; Update retains independent timer/toggle state and motion branches."
                        : motion.Timer == null
                        ? "Update lowered to native position/rotation rates; Inspector float values baked into this instance."
                        : motion.Timer.Comparison == 19
                            ? "Start applies fixed transform increments once; held-action Update applies motion rates without repeating initialization."
                        : motion.Timer.Comparison == 18
                            ? "Start applies fixed transform increments once on scene load; optional unconditional Update motion follows."
                        : motion.Timer.Comparison == 17
                            ? "Two independent held actions apply deltaTime-scaled motion in source order."
                        : motion.Timer.Comparison == 16
                            ? "Two independent named press/release actions apply fixed motion steps in source order."
                        : motion.Timer.Comparison == 15
                            ? "Named button release applies one fixed motion step."
                        : motion.Timer.Comparison == 14
                            ? "Two independent named button presses apply fixed motion steps in source order."
                        : motion.Timer.Comparison == 13
                            ? "Update lowered to a repeating timer with two deltaTime-scaled motion branches."
                        : motion.Timer.Comparison == 12
                            ? "Update lowered to a persistent per-instance bool toggle and motion branches driven by named button presses."
                        : motion.Timer.Comparison is 9 or 10 or 11
                            ? "Trigger callback(s) lowered to player-only entry/exit rotation steps; mutual collider layer masks are respected."
                        : motion.Timer.Comparison == 8
                            ? "Update lowered to one-shot button-press transform steps using the project's named controller binding."
                        : motion.Timer.Comparison == 7
                            ? "Update lowered to held-button motion branches using the project's named controller binding."
                            : "Update lowered to a per-instance timer and conditional motion branches; Inspector float values baked into this instance.";
                }
                catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
                { detail = ex.Message; }
            }
            report.Add(new(Path.GetFileName(scenePath), obj.Name, script.ScriptPath, status, detail));
            if (status is "Unsupported")
            {
                string warning = $"PS2 script {status}: {Path.GetFileName(scenePath)} / {obj.Name} / {script.ScriptPath}: {detail}";
                warnings.Add(warning);
                Console.WriteLine("WARNING: " + warning);
            }
        }
        return motions;
    }

    public static void WriteReport(string buildDirectory, List<Entry> entries) =>
        File.WriteAllText(Path.Combine(buildDirectory, "R2Data", "ps2-script-report.json"),
            JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
}
