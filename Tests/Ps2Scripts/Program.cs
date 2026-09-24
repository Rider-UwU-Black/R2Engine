using System.Numerics;
using R2Engine.Editor;
using R2Engine.Editor.Scene;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
const string prefix = "using System.Numerics; using R2Engine.Editor.Scene; public class Motion : ScriptBehaviour { public float Speed = 45f; ";
static string Script(string body) => prefix + "public override void Update(float deltaTime) { " + body + " } }";
const string spin = "Transform.Rotation += Vector3.UnitY * Speed * deltaTime;";
var empty = new Dictionary<string,string>();
string startOnlySource=prefix+"public override void Start() { Transform.Rotation += Vector3.UnitZ * Speed; } }";
string startSource=startOnlySource.Replace(" } }", " } public override void Update(float deltaTime) { "+spin+" } }");
var startup=Ps2ScriptCompiler.Compile(startSource,empty);
Check(startup.Timer is {Comparison:18, Initial:0, Threshold:0} && startup.Timer.ElseRotation.Z==45 && startup.Rotation.Y==45,"Start increments and Update rates");
Check(Ps2ScriptCompiler.Compile(startOnlySource,empty).Rotation==Vector3.Zero,"Start-only has no ongoing rate");
string dualHeldSource = Script("if (RuntimeInput.IsActionDown(\"Sprint\")) Transform.Position += Vector3.UnitX * Speed * deltaTime; if (RuntimeInput.IsActionDown(\"Jump\")) Transform.Position += Vector3.UnitX * -45f * deltaTime;");
var dualHeld=Ps2ScriptCompiler.Compile(dualHeldSource,empty);
Check(dualHeld.Timer is { Comparison:17, Threshold:1 } && dualHeld.Position.X==45 && dualHeld.Timer.ElsePosition.X==-45,"Paired held rates");
Check(Ps2ScriptCompiler.Compile(dualHeldSource.Replace("\"Jump\"","\"Sprint\""),empty).Timer!.Threshold==17,"Same button paired held statements retained");
string releaseSource = Script("if (RuntimeInput.WasActionReleased(\"Sprint\")) Transform.Rotation += Vector3.UnitY * Speed;");
var release = Ps2ScriptCompiler.Compile(releaseSource, empty);
Check(release.Timer is { Initial: 0, Threshold: 1, Comparison: 15 } && release.Rotation.Y==45,"Release step compilation");
RuntimeInput.Clear(); RuntimeInput.Configure(InputActionBinding.CreateDefaults());
RuntimeInput.BeginFrame(); RuntimeInput.SetKey(RuntimeKey.LeftShift,true);
Check(!RuntimeInput.WasActionReleased("Sprint"),"Keyboard press is not release");
RuntimeInput.BeginFrame(); RuntimeInput.SetKey(RuntimeKey.LeftShift,false);
Check(RuntimeInput.WasActionReleased("Sprint"),"Keyboard action release");
RuntimeInput.SetInputEnabled(false); Check(!RuntimeInput.WasActionReleased("Sprint"),"Disabled input hides release");
RuntimeInput.SetInputEnabled(true); RuntimeInput.BeginFrame();
Check(!RuntimeInput.WasActionReleased("Sprint"),"Release lasts one frame");
RuntimeInput.SetControllerState("test",Array.Empty<float>(),new[]{false,true}); RuntimeInput.BeginFrame();
RuntimeInput.SetControllerState("test",Array.Empty<float>(),new[]{false,false});
Check(RuntimeInput.WasActionReleased("Sprint") && !RuntimeInput.WasActionReleased("Missing"),"Controller action release and missing action");
RuntimeInput.BeginFrame(); RuntimeInput.SetControllerState("test",Array.Empty<float>(),new[]{false,true});
RuntimeInput.BeginFrame(); RuntimeInput.ClearControllerState();
Check(!RuntimeInput.WasActionReleased("Sprint"),"Disconnect is not release");
RuntimeInput.SetControllerState("test",Array.Empty<float>(),new[]{false,false});
Check(!RuntimeInput.WasActionReleased("Sprint"),"Reconnect has no stale release"); RuntimeInput.Clear();
string dualSource = Script("if (RuntimeInput.WasActionPressed(\"Sprint\")) Transform.Rotation += Vector3.UnitY * Speed; if (RuntimeInput.WasActionPressed(\"Jump\")) Transform.Rotation += Vector3.UnitY * -45f;");
var dual = Ps2ScriptCompiler.Compile(dualSource, empty);
Check(dual.Timer is { Initial: 0, Threshold: 1, Comparison: 14 } && dual.Rotation.Y == 45 && dual.Timer.ElseRotation.Y == -45, "Two independent press steps");
Check(Ps2ScriptCompiler.Compile(dualSource.Replace("\"Jump\"", "\"Sprint\""), empty).Timer!.Threshold == 17, "Shared button retains both statements");
string toggleSource = prefix + "private bool _spinning; public override void Update(float deltaTime) { if (RuntimeInput.WasActionPressed(\"Sprint\")) _spinning = !_spinning; if (_spinning) { " + spin + " } } }";
var toggle = Ps2ScriptCompiler.Compile(toggleSource, empty);
Check(toggle.Timer is { Initial: 0, Threshold: 1, Comparison: 12 } && toggle.Rotation.Y == 45, "Toggle defaults and button binding");
Check(Ps2ScriptCompiler.Compile(toggleSource.Replace("bool _spinning;", "bool _spinning = true;"), empty).Timer!.Initial == 1, "Toggle initial true");
Check(Ps2ScriptCompiler.Compile(toggleSource, new Dictionary<string,string>{{"Speed","90"}}).Rotation.Y == 90, "Toggle speed override");
var result = Ps2ScriptCompiler.Compile(Script(spin), empty);
Check(result.Rotation == new Vector3(0,45,0) && result.Position == Vector3.Zero, "Spin default");
result = Ps2ScriptCompiler.Compile(Script(spin), new Dictionary<string,string>{{"Speed","-90"}});
Check(result.Rotation.Y == -90, "Inspector per-instance override");
result = Ps2ScriptCompiler.Compile(Script("Transform.Position += new Vector3(1f, -2f, Speed) * deltaTime;" + spin), empty);
Check(result.Position == new Vector3(1,-2,45), "Move and rotate");
void Reject(string source, Dictionary<string,string>? values = null)
{
    try { Ps2ScriptCompiler.Compile(source, values ?? empty); }
    catch (InvalidOperationException) { return; }
    throw new Exception("Accepted unsupported script: " + source);
}
Reject(Script("while(true) {}"));
Reject(startOnlySource.Replace("public override void Start()", "private float timer; public override void Start()"));
string startHeldSource=startSource.Replace(spin,"if (RuntimeInput.IsActionDown(\"Sprint\")) "+spin);
var startHeld=Ps2ScriptCompiler.Compile(startHeldSource,empty);
Check(startHeld.Timer is {Comparison:19,Threshold:1} && startHeld.Timer.ElseRotation.Z==45 && startHeld.Rotation.Y==45,"Start plus held motion");
Reject(startHeldSource.Replace(spin,spin+" else "+spin));
Reject(startHeldSource.Replace("IsActionDown","WasActionPressed"));
Reject(startOnlySource.Replace("Transform.Rotation += Vector3.UnitZ * Speed;", "Transform.Scale += Vector3.One;"));
Reject(dualHeldSource.Replace("* deltaTime", ""));
Reject(dualHeldSource.Replace("IsActionDown(\"Jump\")", "WasActionPressed(\"Jump\")").Replace("-45f * deltaTime", "-45f"));
Reject(dualHeldSource.Replace("; if (", "; else if ("));
Reject(releaseSource.Replace("* Speed;", "* Speed * deltaTime;"));
Reject(releaseSource.Replace("* Speed;", "* Speed; else Transform.Rotation += Vector3.UnitY * Speed;"));
Reject(toggleSource.Replace("WasActionPressed", "WasActionReleased"));
string mixedSource = dualSource.Replace("WasActionPressed(\"Jump\")", "WasActionReleased(\"Sprint\")");
var mixed = Ps2ScriptCompiler.Compile(mixedSource,empty);
Check(mixed.Timer is { Comparison:16, Threshold:529 } && mixed.Timer.ElseRotation.Y==-45,"Press/release same-button pair");
Check(Ps2ScriptCompiler.Compile(dualSource.Replace("WasActionPressed", "WasActionReleased"),empty).Timer!.Threshold==769,"Two release statements");
Check(Ps2ScriptCompiler.Compile(dualSource.Replace("WasActionPressed(\"Sprint\")", "WasActionReleased(\"Sprint\")"),empty).Timer!.Threshold==257,"Release first, press second");
Reject(dualSource.Replace("WasActionPressed", "IsActionDown"));
Reject(dualSource.Replace("; if (", "; else if ("));
Reject(dualSource.Replace("* Speed;", "* Speed * deltaTime;"));
Reject(dualSource.Replace("\"Jump\"", "\"Missing\""));
Reject(toggleSource.Replace("WasActionPressed", "IsActionDown"));
Reject(toggleSource.Replace("_spinning = !_spinning", "_spinning = true"));
Reject(toggleSource.Replace("private bool", "public bool"));
Reject(toggleSource.Replace("private bool", "private float _elapsed; private bool"));
Reject(toggleSource.Replace("private bool", "private bool _other; private bool"));
Reject(toggleSource, new(){{"_spinning","true"}});
Reject(toggleSource.Replace(" * deltaTime", ""));
Reject(Script(spin + "Speed += deltaTime;"));
Reject(Script("Transform.Rotation += Vector3.UnitY * Speed;"));
Reject(Script("Transform.Rotation += Vector3.UnitY * deltaTime * deltaTime;"));
Reject(Script(spin + spin));
Reject(Script("Transform.Scale += Vector3.One * deltaTime;"));
Reject(Script("if (Speed > 0) { " + spin + " }"));
Reject(prefix + "public override void Start() {} public override void Update(float deltaTime) {" + spin + "} }");
Reject(Script(spin), new(){{"Speed","NaN"}});
Reject(Script(spin), new(){{"Speed","Infinity"}});
Reject(Script(spin), new(){{"Speed","invalid"}});
Reject(Script(spin), new(){{"StaleField","1"}});
Reject(Script(spin).Replace("Speed = 45f", "Speed = System.MathF.Sin(1f)"));
Reject(Script(spin).Replace("Speed", "deltaTime"));
Reject("#define HIDE\n" + Script(spin));
Reject(Script(spin).Replace("class Motion", "class Motion(float x)"));

static string Timed(string body) => prefix + "public float Delay = 2f; private float _elapsed; " +
    "public override void Update(float deltaTime) { " + body + " } }";
string delayed = Timed("_elapsed += deltaTime; if (_elapsed >= Delay) { " + spin + " }");
string AddStart(string source) => source.Replace("public override void Update", "public override void Start() { Transform.Rotation += Vector3.UnitZ * Speed; } public override void Update");
string startTimerSource=AddStart(delayed);
string startToggleSource=AddStart(toggleSource);
var startToggle=Ps2ScriptCompiler.Compile(startToggleSource,empty);
Check(startToggle.Startup?.Rotation.Z==45 && startToggle.Timer is { Comparison:12,Initial:0,Threshold:1 },"Startup separate from bool state");
Check(Ps2ScriptCompiler.Compile(startToggleSource.Replace("bool _spinning;","bool _spinning = true;"),empty).Timer!.Initial==1,"Startup preserves bool initializer");
Reject(startToggleSource.Replace("Transform.Rotation += Vector3.UnitZ * Speed;", "_spinning = true;"));
var startTimer=Ps2ScriptCompiler.Compile(startTimerSource,empty);
Check(startTimer.Startup?.Rotation.Z==45 && startTimer.Timer is {Comparison:4,Initial:0,Threshold:2} && startTimer.Rotation.Y==45,"Separate startup and timer state");
Reject(startTimerSource.Replace("Transform.Rotation += Vector3.UnitZ * Speed;", "_elapsed = 0f;"));
Reject(startTimerSource.Replace("Vector3.UnitZ * Speed", "Vector3.UnitZ * _elapsed"));
string loopSource = Timed("_elapsed += deltaTime; _elapsed %= Delay * 2f; if (_elapsed < Delay) { " + spin + " } else { Transform.Position += Vector3.UnitX * Speed * deltaTime; }");
var loop = Ps2ScriptCompiler.Compile(loopSource, empty);
Check(Ps2ScriptCompiler.Compile(AddStart(loopSource),empty).Startup != null && Ps2ScriptCompiler.Compile(AddStart(loopSource),empty).Timer!.Comparison==13,"Startup with repeating timer preserves operation");
Check(loop.Timer is { Initial: 0, Threshold: 2, Comparison: 13 } && loop.Timer.ElsePosition.X == 45, "Repeating timer branches");
Reject(loopSource.Replace("Delay * 2f", "Delay * 3f"));
Reject(loopSource.Replace("%=", "="));
Reject(loopSource.Replace("< Delay", ">= Delay"));
Reject(loopSource, new(){{"Delay","0"}});
Reject(loopSource, new(){{"Delay","-1"}});
Reject(loopSource, new(){{"Delay","3e38"}});
Reject(loopSource.Replace("float _elapsed;", "float _elapsed = -1f;"));
result = Ps2ScriptCompiler.Compile(delayed, new Dictionary<string,string>{{"Delay","3.5"},{"Speed","90"}});
Check(result.Timer is { Initial: 0, Threshold: 3.5f, Comparison: 4 } && result.Rotation.Y == 90,
    "Timer initial state, comparison and per-instance overrides");
Check(result.Timer!.ElsePosition == Vector3.Zero && result.Timer.ElseRotation == Vector3.Zero,
    "Missing else is no motion");
string alternate = Timed("_elapsed += deltaTime; if (_elapsed < Delay) { " + spin +
    " } else { Transform.Position += Vector3.UnitX * Speed * deltaTime; }");
result = Ps2ScriptCompiler.Compile(alternate, empty);
Check(result.Rotation.Y == 45 && result.Timer!.ElsePosition.X == 45 && result.Timer.Comparison == 1,
    "If/else retain independent motion targets");
foreach (var (op, code) in new[] {("<",1u),("<=",2u),(">",3u),(">=",4u),("==",5u),("!=",6u)})
    Check(Ps2ScriptCompiler.Compile(Timed("_elapsed += deltaTime; if (_elapsed " + op + " Delay) " + spin),empty).Timer!.Comparison==code,
        "Comparison opcode " + op);
Check(Ps2ScriptCompiler.Compile(delayed.Replace("float _elapsed;","float _elapsed = -1f;"),empty).Timer!.Initial==-1,
    "Explicit initial timer value");
Reject(Timed("if (_elapsed >= Delay) {" + spin + "} _elapsed += deltaTime;"));
Reject(Timed("_elapsed += deltaTime; if (_elapsed >= Delay) {" + spin + "} _elapsed = 0f;"));
Reject(Timed("_elapsed += deltaTime; if (_elapsed >= Delay && Speed > 0f) {" + spin + "}"));
Reject(Timed("_elapsed += deltaTime; if (_elapsed >= Delay) { if (Speed > 0f) {" + spin + "} }"));
Reject(Timed("_elapsed += deltaTime; if (_elapsed >= Delay) { return; }"));
Reject(delayed.Replace("private float _elapsed;","private float _elapsed; private float _other;"));
Reject(delayed.Replace("* Speed *", "* _elapsed *"));
Reject(delayed, new(){{"_elapsed","1"}});
Reject(delayed, new(){{"Delay","NaN"}});
Reject(delayed.Replace("private float _elapsed;","private float _elapsed = float.PositiveInfinity;"));

// Export real scene packages to a unique test directory. Do not touch user scenes.
string buttonSource = Script("if (RuntimeInput.IsActionDown(\"Sprint\")) { " + spin +
    " } else { Transform.Position += Vector3.UnitX * Speed * deltaTime; }");
result = Ps2ScriptCompiler.Compile(buttonSource, empty);
Check(result.Timer is { Comparison: 7, Threshold: 1, Initial: 0 } && result.Timer.ElsePosition.X == 45,
    "Held Sprint uses default controller index with independent else motion");
var remapped = new[] { new InputActionBinding { Name="SPRINT",Type=InputActionType.Button,ControllerButton=4 } };
Check(Ps2ScriptCompiler.Compile(buttonSource,empty,remapped).Timer!.Threshold==4,"Project override and case-insensitive lookup");
Check(Ps2ScriptCompiler.Compile(buttonSource.Replace("Sprint","Interact"),empty,Array.Empty<InputActionBinding>()).Timer!.Threshold==0,
    "Implicit Interact matches desktop default");
Reject(buttonSource.Replace("Sprint","MissingAction"));
Reject(buttonSource.Replace("IsActionDown","WasActionPressed"));
Reject(buttonSource.Replace("IsActionDown(\"Sprint\")","IsActionDown(\"Sprint\") && Speed > 0f"));
Reject(buttonSource.Replace("if (RuntimeInput", "if (!RuntimeInput"));
foreach (var binding in new[] {
    new InputActionBinding{Name="Sprint",Type=InputActionType.Axis1D,ControllerButton=1},
    new InputActionBinding{Name="Sprint",Type=InputActionType.Button,ControllerButton=-1},
    new InputActionBinding{Name="Sprint",Type=InputActionType.Button,ControllerButton=16}})
{
    bool rejected=false;
    try { Ps2ScriptCompiler.Compile(buttonSource,empty,new[]{binding}); } catch(InvalidOperationException) { rejected=true; }
    Check(rejected,"Unsupported binding must be rejected");
}

string scratch = Path.Combine(Path.GetTempPath(), "r2-ps2-script-test-" + Guid.NewGuid().ToString("N"));
string triggerSource=prefix + "public override void OnTriggerEnter(GameObject other) { " +
    "if (other.GetComponent<PlayerController>() == null) return; Transform.Rotation += Vector3.UnitY * Speed; } }";
result=Ps2ScriptCompiler.Compile(triggerSource,empty);
Check(result.Timer is { Comparison:9,Threshold:1 } && result.Rotation.Y==45,"Player entry compiles fixed rotation");
string exitSource=triggerSource.Replace("OnTriggerEnter","OnTriggerExit");
result=Ps2ScriptCompiler.Compile(exitSource,empty);
Check(result.Timer is { Comparison:10,Threshold:1 } && result.Rotation.Y==45,"Exit-only fixed rotation");
string pairedSource=triggerSource[..^1] + "public override void OnTriggerExit(GameObject other) { " +
    "if (other.GetComponent<PlayerController>() == null) return; Transform.Rotation += Vector3.UnitY * -45f; } }";
result=Ps2ScriptCompiler.Compile(pairedSource,empty);
Check(result.Timer is { Comparison:11 } && result.Rotation.Y==45 && result.Timer.ElseRotation.Y==-45,"Paired callbacks retain independent rotations");
Reject(pairedSource.Replace("Vector3.UnitY * -45f", "Vector3.UnitY * other.Transform.Position.X"));
Reject(triggerSource[..^1] + "public override void Update(float deltaTime) {} }");
Reject(pairedSource.Replace("return; Transform.Rotation += Vector3.UnitY * -45f;", "Transform.Rotation += Vector3.UnitY * -45f;"));
Reject(triggerSource.Replace("if (other.GetComponent<PlayerController>() == null) return;", ""));
Reject(triggerSource.Replace("== null", "!= null"));
Reject(triggerSource.Replace("GetComponent<PlayerController>","GetComponent<MeshRenderer>"));
Reject(triggerSource.Replace("Transform.Rotation", "Transform.Position"));
Reject(triggerSource.Replace("OnTriggerEnter","OnTriggerStay"));
string pressSource=Script("if (RuntimeInput.WasActionPressed(\"Sprint\")) { Transform.Rotation += Vector3.UnitY * Speed; }");
result=Ps2ScriptCompiler.Compile(pressSource,empty);
Check(result.Timer is { Comparison:8,Threshold:1 } && result.Rotation.Y==45,"Press compiles fixed rotation, not a rate");
Reject(pressSource.Replace("* Speed;","* Speed * deltaTime;"));
Reject(pressSource.Replace("WasActionPressed","IsActionDown"));
Directory.CreateDirectory(scratch);
string sourcePath = Path.Combine(scratch,"Motion.cs");
File.WriteAllText(sourcePath, Script(spin));
var scene = new Scene("ScriptTest");
var obj = scene.CreateGameObject("SpinCube"); obj.AddComponent<MeshRenderer>();
var script = obj.AddComponent<ScriptComponent>(); script.ScriptPath = sourcePath; script.FieldValues["Speed"] = "90";
var second = scene.CreateGameObject("StaticCube"); second.AddComponent<MeshRenderer>();
string scenePath = Path.Combine(scratch,"ScriptTest.r2scene");
File.WriteAllText(scenePath, SceneSerializer.Serialize(scene));
var settings = new ProjectSettings { LoadingScreenPrefab = "" };
var export = ScenePackageExporter.Export(new[]{scenePath}, Path.Combine(scratch,"v29"), settings);
Check(export.ScriptWarnings.Count == 0,"Supported script should not warn");
byte[] package = File.ReadAllBytes(Path.Combine(scratch,"v29/R2Data/Scenes/ScriptTest.r2scene"));
Check(BitConverter.ToInt32(package,4)==29,"Motion emits R2SC v29");
Check(BitConverter.ToSingle(package,package.Length-48+16)==90,"First instance rotation Y rate in tail");
Check(package.AsSpan(package.Length-24).ToArray().All(b=>b==0),"Static instance receives zero rates");
Check(BitConverter.ToUInt32(package,package.Length-64)==0,"Playback flags precede motion block");
obj.Components.Remove(script);
File.WriteAllText(scenePath, SceneSerializer.Serialize(scene));
ScenePackageExporter.Export(new[]{scenePath},Path.Combine(scratch,"v24"),settings);
var old = File.ReadAllBytes(Path.Combine(scratch,"v24/R2Data/Scenes/ScriptTest.r2scene"));
Check(BitConverter.ToInt32(old,4)==24,"No scripts keeps old version");
Check(package.Length==old.Length+8+48,"v29 adds empty zone/interaction counts plus six floats per instance");
obj.Components.Add(script);
File.WriteAllText(sourcePath, delayed);
script.FieldValues["Delay"]="3.5";
File.WriteAllText(scenePath, SceneSerializer.Serialize(scene));
ScenePackageExporter.Export(new[]{scenePath},Path.Combine(scratch,"v30"),settings);
byte[] timed = File.ReadAllBytes(Path.Combine(scratch,"v30/R2Data/Scenes/ScriptTest.r2scene"));
Check(BitConverter.ToInt32(timed,4)==30 && timed.Length==package.Length+72,"v30 appends 36-byte timer record per instance");
Check(BitConverter.ToSingle(timed,timed.Length-72)==0 && BitConverter.ToSingle(timed,timed.Length-68)==3.5f &&
    BitConverter.ToUInt32(timed,timed.Length-64)==4,"v30 timer initial/threshold/opcode order");
Check(BitConverter.ToSingle(timed,timed.Length-120+16)==90,"v29 motion block preserved before timers");
Check(timed.AsSpan(timed.Length-36).ToArray().All(b=>b==0),"Static object has disabled timer record");
Check(BitConverter.ToUInt32(timed,timed.Length-136)==0,"Playback still precedes both script blocks");
// Mixed v30/v29 export must keep the maximum manifest version independent of order.
string oldScriptPath=Path.Combine(scratch,"Other.cs"); File.WriteAllText(oldScriptPath, Script(spin));
var spinScene=new Scene("SpinOnly"); var spinObj=spinScene.CreateGameObject("Spin"); spinObj.AddComponent<MeshRenderer>();
spinObj.AddComponent<ScriptComponent>().ScriptPath=oldScriptPath;
string spinScenePath=Path.Combine(scratch,"SpinOnly.r2scene"); File.WriteAllText(spinScenePath,SceneSerializer.Serialize(spinScene));
ScenePackageExporter.Export(new[]{scenePath,spinScenePath},Path.Combine(scratch,"mixed"),settings);
Check(File.ReadAllText(Path.Combine(scratch,"mixed/R2Data/Scenes/scene-manifest.json")).Contains("\"Version\": 30"),"Mixed manifest retains v30");
File.WriteAllText(sourcePath,buttonSource); script.FieldValues.Remove("Delay");
File.WriteAllText(scenePath,SceneSerializer.Serialize(scene));
settings.InputActions = remapped.ToList();
var inputExport=ScenePackageExporter.Export(new[]{scenePath},Path.Combine(scratch,"v31"),settings);
Check(inputExport.ScriptWarnings.Count==0,"Input script export succeeds");
byte[] inputPackage=File.ReadAllBytes(Path.Combine(scratch,"v31/R2Data/Scenes/ScriptTest.r2scene"));
Check(BitConverter.ToInt32(inputPackage,4)==31 && inputPackage.Length==timed.Length,"v31 reuses v30 condition layout");
Check(BitConverter.ToSingle(inputPackage,inputPackage.Length-68)==4 && BitConverter.ToUInt32(inputPackage,inputPackage.Length-64)==7,
    "Project input mapping passed through exporter into opcode 7");
Check(BitConverter.ToSingle(inputPackage,inputPackage.Length-60)==90,"Else motion serialized");
File.WriteAllText(sourcePath,pressSource);
File.WriteAllText(sourcePath,loopSource);
ScenePackageExporter.Export(new[]{scenePath},Path.Combine(scratch,"v36"),settings);
byte[] loopPackage=File.ReadAllBytes(Path.Combine(scratch,"v36/R2Data/Scenes/ScriptTest.r2scene"));
Check(BitConverter.ToInt32(loopPackage,4)==36 && loopPackage.Length==inputPackage.Length,"v36 reuses timer layout");
Check(BitConverter.ToUInt32(loopPackage,loopPackage.Length-64)==13 && BitConverter.ToSingle(loopPackage,loopPackage.Length-68)==2,"Repeating timer opcode and period");
File.WriteAllText(sourcePath,pressSource);
ScenePackageExporter.Export(new[]{scenePath},Path.Combine(scratch,"v32"),settings);
byte[] pressPackage=File.ReadAllBytes(Path.Combine(scratch,"v32/R2Data/Scenes/ScriptTest.r2scene"));
Check(BitConverter.ToInt32(pressPackage,4)==32 && pressPackage.Length==inputPackage.Length,"v32 layout compatible with v31");
Check(BitConverter.ToUInt32(pressPackage,pressPackage.Length-64)==8 && BitConverter.ToSingle(pressPackage,pressPackage.Length-68)==4,
    "Press opcode and remapped button exported");
Check(BitConverter.ToSingle(pressPackage,pressPackage.Length-120+16)==90,"Fixed degree increment exported");
obj.AddComponent<BoxCollider>();
// Toggle uses the same two-instance record layout, without a trigger collider.
File.WriteAllText(sourcePath,toggleSource.Replace("bool _spinning;", "bool _spinning = true;"));
ScenePackageExporter.Export(new[]{scenePath},Path.Combine(scratch,"v35"),settings);
byte[] togglePackage=File.ReadAllBytes(Path.Combine(scratch,"v35/R2Data/Scenes/ScriptTest.r2scene"));
Check(BitConverter.ToInt32(togglePackage,4)==35 && togglePackage.Length==inputPackage.Length,"v35 reuses condition layout");
Check(BitConverter.ToSingle(togglePackage,togglePackage.Length-72)==1 && BitConverter.ToSingle(togglePackage,togglePackage.Length-68)==4 && BitConverter.ToUInt32(togglePackage,togglePackage.Length-64)==12,"Toggle initial bool and remapped button serialized");
File.WriteAllText(sourcePath,dualSource);
settings.InputActions.Add(new InputActionBinding { Name="Jump", Type=InputActionType.Button, ControllerButton=5 });
ScenePackageExporter.Export(new[]{scenePath},Path.Combine(scratch,"v37"),settings);
byte[] dualPackage=File.ReadAllBytes(Path.Combine(scratch,"v37/R2Data/Scenes/ScriptTest.r2scene"));
Check(BitConverter.ToInt32(dualPackage,4)==37 && dualPackage.Length==inputPackage.Length,"v37 preserves record layout");
Check(BitConverter.ToUInt32(dualPackage,dualPackage.Length-64)==14 && BitConverter.ToSingle(dualPackage,dualPackage.Length-68)==84 && BitConverter.ToSingle(dualPackage,dualPackage.Length-44)==-45,"Two remapped buttons and second step exported");
File.WriteAllText(sourcePath,releaseSource);
ScenePackageExporter.Export(new[]{scenePath},Path.Combine(scratch,"v38"),settings);
byte[] releasePackage=File.ReadAllBytes(Path.Combine(scratch,"v38/R2Data/Scenes/ScriptTest.r2scene"));
Check(BitConverter.ToInt32(releasePackage,4)==38 && releasePackage.Length==inputPackage.Length,"v38 preserves record layout");
Check(BitConverter.ToUInt32(releasePackage,releasePackage.Length-64)==15 && BitConverter.ToSingle(releasePackage,releasePackage.Length-68)==4,"Release opcode and remapped binding exported");
File.WriteAllText(sourcePath,mixedSource);
ScenePackageExporter.Export(new[]{scenePath},Path.Combine(scratch,"v39"),settings);
byte[] mixedPackage=File.ReadAllBytes(Path.Combine(scratch,"v39/R2Data/Scenes/ScriptTest.r2scene"));
Check(BitConverter.ToInt32(mixedPackage,4)==39 && mixedPackage.Length==inputPackage.Length,"v39 preserves layout");
Check(BitConverter.ToUInt32(mixedPackage,mixedPackage.Length-64)==16 && BitConverter.ToSingle(mixedPackage,mixedPackage.Length-68)==580,"Paired edge flags and remapped button indices exported");
File.WriteAllText(sourcePath,dualHeldSource);
ScenePackageExporter.Export(new[]{scenePath},Path.Combine(scratch,"v40"),settings);
byte[] dualHeldPackage=File.ReadAllBytes(Path.Combine(scratch,"v40/R2Data/Scenes/ScriptTest.r2scene"));
Check(BitConverter.ToInt32(dualHeldPackage,4)==40 && dualHeldPackage.Length==inputPackage.Length,"v40 retains layout");
Check(BitConverter.ToUInt32(dualHeldPackage,dualHeldPackage.Length-64)==17 && BitConverter.ToSingle(dualHeldPackage,dualHeldPackage.Length-68)==84 && BitConverter.ToSingle(dualHeldPackage,dualHeldPackage.Length-60)==-45,"Held pair remapping and second rate exported");
File.WriteAllText(sourcePath,startSource);
ScenePackageExporter.Export(new[]{scenePath},Path.Combine(scratch,"v41"),settings);
byte[] startPackage=File.ReadAllBytes(Path.Combine(scratch,"v41/R2Data/Scenes/ScriptTest.r2scene"));
Check(BitConverter.ToInt32(startPackage,4)==41 && startPackage.Length==inputPackage.Length,"v41 preserves layout");
Check(BitConverter.ToUInt32(startPackage,startPackage.Length-64)==18 && BitConverter.ToSingle(startPackage,startPackage.Length-40)==90 && BitConverter.ToSingle(startPackage,startPackage.Length-120+16)==90,"Start increment and Update rate retain Inspector override");
File.WriteAllText(sourcePath,startHeldSource);
ScenePackageExporter.Export(new[]{scenePath},Path.Combine(scratch,"v42"),settings);
byte[] startHeldPackage=File.ReadAllBytes(Path.Combine(scratch,"v42/R2Data/Scenes/ScriptTest.r2scene"));
Check(BitConverter.ToInt32(startHeldPackage,4)==42 && startHeldPackage.Length==inputPackage.Length,"v42 retains layout");
Check(BitConverter.ToUInt32(startHeldPackage,startHeldPackage.Length-64)==19 && BitConverter.ToSingle(startHeldPackage,startHeldPackage.Length-68)==4 && BitConverter.ToSingle(startHeldPackage,startHeldPackage.Length-40)==90,"Startup held opcode, binding and initial tilt exported");
File.WriteAllText(sourcePath,startTimerSource);
ScenePackageExporter.Export(new[]{scenePath},Path.Combine(scratch,"v43"),settings);
byte[] startTimerPackage=File.ReadAllBytes(Path.Combine(scratch,"v43/R2Data/Scenes/ScriptTest.r2scene"));
Check(BitConverter.ToInt32(startTimerPackage,4)==43 && startTimerPackage.Length==inputPackage.Length+48,"v43 adds 24-byte startup per instance");
Check(BitConverter.ToUInt32(startTimerPackage,startTimerPackage.Length-120+8)==4 && BitConverter.ToSingle(startTimerPackage,startTimerPackage.Length-120+4)==2,"Timer record remains independent of startup");
Check(BitConverter.ToSingle(startTimerPackage,startTimerPackage.Length-48+20)==90 && BitConverter.ToSingle(startTimerPackage,startTimerPackage.Length-168+16)==90,"Startup and Update retain overrides");
Check(startTimerPackage.AsSpan(startTimerPackage.Length-24).ToArray().All(b=>b==0),"Static cube has zero startup record");
File.WriteAllText(sourcePath,startToggleSource);
ScenePackageExporter.Export(new[]{scenePath},Path.Combine(scratch,"v43-toggle"),settings);
byte[] startTogglePackage=File.ReadAllBytes(Path.Combine(scratch,"v43-toggle/R2Data/Scenes/ScriptTest.r2scene"));
Check(BitConverter.ToInt32(startTogglePackage,4)==43 && startTogglePackage.Length==startTimerPackage.Length,"Startup toggle reuses v43 layout");
Check(BitConverter.ToUInt32(startTogglePackage,startTogglePackage.Length-112)==12 && BitConverter.ToSingle(startTogglePackage,startTogglePackage.Length-116)==4 && BitConverter.ToSingle(startTogglePackage,startTogglePackage.Length-120)==0,"Startup toggle state and remapped input");
Check(BitConverter.ToSingle(startTogglePackage,startTogglePackage.Length-28)==90,"Startup toggle tilt exported separately");
File.WriteAllText(sourcePath,pressSource);
var entries = new List<Ps2ScriptExport.Entry>(); var warnings = new List<string>();
Check(Ps2ScriptExport.CompileScene(scene,scenePath,entries,warnings).Count==0 && warnings.Count==1,
    "Physics object must warn and not translate");
Check(entries[0].Status=="Unsupported","Explicit unsupported status");
File.WriteAllText(sourcePath,triggerSource);
var box=obj.GetComponent<BoxCollider>()!; box.IsTrigger=true; box.Size=new Vector3(3,3,3);
var player=scene.CreateGameObject("Player"); player.AddComponent<MeshRenderer>();
player.AddComponent<PlayerController>(); player.AddComponent<CapsuleCollider>();
entries.Clear(); warnings.Clear();
Check(Ps2ScriptExport.CompileScene(scene,scenePath,entries,warnings).Count==1 && warnings.Count==0,"Trigger target exports with unique player");
File.WriteAllText(scenePath,SceneSerializer.Serialize(scene));
ScenePackageExporter.Export(new[]{scenePath},Path.Combine(scratch,"v33"),settings);
byte[] triggerPackage=File.ReadAllBytes(Path.Combine(scratch,"v33/R2Data/Scenes/ScriptTest.r2scene"));
Check(BitConverter.ToInt32(triggerPackage,4)==33 && BitConverter.ToInt32(triggerPackage,16)==3,"v33 includes trigger, static cube and player");
Check(BitConverter.ToUInt32(triggerPackage,triggerPackage.Length-108+8)==9 &&
    BitConverter.ToSingle(triggerPackage,triggerPackage.Length-108+4)==1,"Trigger opcode and mutual-mask permission");
File.WriteAllText(sourcePath,pairedSource);
ScenePackageExporter.Export(new[]{scenePath},Path.Combine(scratch,"v34"),settings);
byte[] pairedPackage=File.ReadAllBytes(Path.Combine(scratch,"v34/R2Data/Scenes/ScriptTest.r2scene"));
Check(BitConverter.ToInt32(pairedPackage,4)==34 && pairedPackage.Length==triggerPackage.Length,"v34 retains v33 record layout");
Check(BitConverter.ToUInt32(pairedPackage,pairedPackage.Length-108+8)==11 &&
    BitConverter.ToSingle(pairedPackage,pairedPackage.Length-108+28)==-45,"Paired opcode and exit rotation serialized");
Check(BitConverter.ToSingle(pairedPackage,pairedPackage.Length-180+16)==90,"Entry preserves Inspector override independently");
box.CollisionMask=0; entries.Clear(); warnings.Clear();
Check(Ps2ScriptExport.CompileScene(scene,scenePath,entries,warnings)[obj].Timer!.Threshold==0,"Trigger layer mask respected");
box.CollisionMask=uint.MaxValue; player.GetComponent<CapsuleCollider>()!.CollisionMask=0;
Check(Ps2ScriptExport.CompileScene(scene,scenePath,new(),new())[obj].Timer!.Threshold==0,"Player layer mask respected");
player.GetComponent<CapsuleCollider>()!.CollisionMask=uint.MaxValue;
box.Center=Vector3.One;
Check(Ps2ScriptExport.CompileScene(scene,scenePath,new(),new()).Count==0,"Offset trigger rejected explicitly");
box.Center=Vector3.Zero; box.IsTrigger=false;
Check(Ps2ScriptExport.CompileScene(scene,scenePath,new(),new()).Count==0,"Solid collider rejected for trigger callback");
box.IsTrigger=true; scene.GameObjects.Remove(player);
Check(Ps2ScriptExport.CompileScene(scene,scenePath,new(),new()).Count==0,"Missing player rejected");
var persistentScene=new Scene("PersistentScripts");
var persistentObject=persistentScene.CreateGameObject("Toggle");persistentObject.AddComponent<MeshRenderer>();
var persistentScript=persistentObject.AddComponent<ScriptComponent>();persistentScript.ScriptPath=sourcePath;
var persistent=persistentObject.AddComponent<PersistentObject>();persistent.SaveId="TOGGLE";persistent.SaveBoolean=true;
File.WriteAllText(sourcePath,toggleSource);
Check(Ps2ScriptExport.CompileScene(persistentScene,scenePath,new(),new()).Count==1,"PersistentObject is supported on translated scripts");
persistent.SaveInteger=true;
Check(Ps2ScriptExport.CompileScene(persistentScene,scenePath,new(),new()).Count==0,"Toggle rejects incompatible timer-phase persistence");
persistent.SaveInteger=false;persistent.SaveBoolean=false;File.WriteAllText(sourcePath,delayed);
Check(Ps2ScriptExport.CompileScene(persistentScene,scenePath,new(),new()).Count==1,"Timer accepts integer phase persistence");
persistent.SaveInteger=true;Check(Ps2ScriptExport.CompileScene(persistentScene,scenePath,new(),new()).Count==1,"Timer phase persistence supported");
persistent.SaveBoolean=true;Check(Ps2ScriptExport.CompileScene(persistentScene,scenePath,new(),new()).Count==0,"Timer rejects incompatible toggle persistence");
Console.WriteLine("PASS: motion/timer/input/entry/exit/toggle/loop/dual-press/release/mixed-edge/dual-held/Start/Start-held/Start-timer translation, desktop release input, bindings, masks, overrides, rejection cases, v24-v43 package layout, isolation, unsupported-target report.");
Console.WriteLine("Test artifacts: " + scratch);
