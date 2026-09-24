using System.Reflection;
using System.Security.Cryptography;
using R2Engine.Editor;

var render = typeof(AssetThumbnailCache).Assembly.GetType("R2Engine.Editor.ModelThumbnail")!
    .GetMethod("Render", BindingFlags.Public | BindingFlags.Static)!;
int count = 0;
foreach (string path in Directory.EnumerateFiles("Assets", "*", SearchOption.AllDirectories)
    .Where(p => Path.GetExtension(p).ToLowerInvariant() is ".obj" or ".r2skel" or ".r2mat"))
{
    var before = SHA256.HashData(File.ReadAllBytes(path));
    var pixels = (byte[])render.Invoke(null, new object[] { Path.GetFullPath(path), 96 })!;
    if (pixels.Length != 96*96*4 || !pixels.Where((b,i) => i%4==3).Any(b => b>0))
        throw new Exception($"Empty preview: {path}");
    if (!before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))))
        throw new Exception($"Preview modified source: {path}");
    Console.WriteLine($"PASS {path}"); count++;
}
if (count == 0) throw new Exception("No fixtures found; run from R2Engine.Editor.");
Console.WriteLine($"PASS: {count} previews; sources unchanged.");
