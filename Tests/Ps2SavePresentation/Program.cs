using System.Text;
using R2Engine.Editor;
using R2Engine.Editor.Scene;

static void Require(bool condition,string message) { if(!condition)throw new Exception(message); }
static void Reject(Action action) { try { action(); } catch(InvalidDataException) { return; } throw new Exception("Expected invalid data rejection"); }
byte[] system=Ps2SavePresentation.CreateIconSys("Mimi Save");
Require(system.Length==964 && Encoding.ASCII.GetString(system,0,4)=="PS2D","icon.sys layout");
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Require(Encoding.GetEncoding(932).GetString(system,192,18)=="Ｍｉｍｉ　Ｓａｖｅ","Shift-JIS title");
foreach(int offset in new[]{260,324,388})Require(Encoding.ASCII.GetString(system,offset,9)=="save.icn\0","browser icon references");
Ps2SavePresentation.CreateIconSys("ミミの冒険");
Reject(()=>Ps2SavePresentation.CreateIconSys(new string('a',33)));
Reject(()=>Ps2SavePresentation.CreateIconSys("bad\ntitle"));
Reject(()=>Ps2SavePresentation.CreateIconSys("🙂"));
byte[] icon=Ps2SavePresentation.CreateDefaultIcon();
Ps2SavePresentation.ValidateIcon(icon);
Require(BitConverter.ToUInt32(icon,16)==24,"fallback has eight triangles");
Reject(()=>Ps2SavePresentation.ValidateIcon(icon[..^1]));
Reject(()=>Ps2SavePresentation.ValidateIcon(new byte[40]));
byte[] corrupt=(byte[])icon.Clone();corrupt[4]=0;Reject(()=>Ps2SavePresentation.ValidateIcon(corrupt));
corrupt=(byte[])icon.Clone();corrupt[^4]=0;corrupt[^3]=0;Reject(()=>Ps2SavePresentation.ValidateIcon(corrupt));
string root=Path.Combine(Path.GetTempPath(),"r2-save-presentation-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var settings=new ProjectSettings { ProductName="Fallback Game", Ps2SaveId="GAME_A" };
Ps2SavePresentation.Export(settings,root,Path.Combine(root,"Default"));
Require(File.Exists(Path.Combine(root,"Default/R2Data/Save/icon.sys")),"default export");
byte[] identity=File.ReadAllBytes(Path.Combine(root,"Default/R2Data/Save/identity.bin"));
Require(identity.Length==32 && identity[4]==0 && Encoding.ASCII.GetString(identity,8,6)=="GAME_A","stable identity export, migration off");
Require(!identity.SequenceEqual(Ps2SavePresentation.CreateIdentity("GAME_B",false)),"distinct game identity");
Require(Ps2SavePresentation.CreateIdentity("GAME_A",true)[4]==1,"explicit migration flag");
foreach(string bad in new[]{"","../BAD","lowercase",new string('A',21),"HAS SPACE"})
    Reject(()=>Ps2SavePresentation.CreateIdentity(bad,false));
File.WriteAllBytes(Path.Combine(root,"custom.icn"),icon);
settings.Ps2MemoryCardTitle="Custom Save";settings.Ps2MemoryCardIcon="custom.icn";
settings.Save(Path.Combine(root,"settings.json"));
var reloaded=ProjectSettings.Load(Path.Combine(root,"settings.json"));
Require(reloaded.Ps2MemoryCardTitle==settings.Ps2MemoryCardTitle && reloaded.Ps2MemoryCardIcon=="custom.icn","settings round trip");
Require(reloaded.Ps2SaveId=="GAME_A" && !reloaded.Ps2ImportLegacySaves,"save identity round trip");
Ps2SavePresentation.Export(reloaded,root,Path.Combine(root,"Custom"));
Require(File.ReadAllBytes(Path.Combine(root,"Custom/R2Data/Save/save.icn")).SequenceEqual(icon),"custom icon copied verbatim");
Console.WriteLine("PASS: title encoding/limits, metadata offsets, default/custom icons, malformed input rejection, settings persistence and package export");
Console.WriteLine("Fixture: "+root);
