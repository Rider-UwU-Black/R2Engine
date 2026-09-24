using R2Engine.Editor.Scene;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
Check(TextureImportUtility.CalculateImportedSize(1058, 1178, 64) == (57, 64), "Reproduce old cube size");
Check(TextureImportUtility.CalculatePs2WorldSize(1058, 1178, 64) == (64, 64), "Cube must fill its GS sampling rectangle");
Check(TextureImportUtility.CalculatePs2WorldSize(1178, 1058, 64) == (64, 64), "Landscape equivalent");
Check(TextureImportUtility.CalculatePs2WorldSize(512, 256, 512) == (512, 256), "Existing POT texture unchanged");
Check(TextureImportUtility.CalculatePs2WorldSize(1024, 256, 128) == (128, 32), "Rectangular textures need not be square");
Check(TextureImportUtility.CalculatePs2WorldSize(1, 4096, 64) == (1, 64), "Thin texture");
Check(TextureImportUtility.CalculateImportedSize(1058, 1178, 384) == (345, 384), "UI-only sizing unchanged");
int cases = 0;
foreach (int limit in new[] { 1, 31, 64, 128, 384, 512, 1024, 0 })
for (int width = 1; width <= 1200; width += 17)
for (int height = 1; height <= 1200; height += 19)
{
    var size = TextureImportUtility.CalculatePs2WorldSize(width, height, limit);
    int cap = limit <= 0 ? 512 : Math.Min(limit, 512);
    Check(size.Width > 0 && size.Height > 0 && size.Width <= cap && size.Height <= cap, "Respect import/VRAM ceiling");
    Check((size.Width & (size.Width - 1)) == 0 && (size.Height & (size.Height - 1)) == 0, "Both axes POT");
    cases++;
}
Console.WriteLine($"PASS: cube regression, rectangular/thin/POT textures, UI size preservation, and {cases} size/cap combinations.");
