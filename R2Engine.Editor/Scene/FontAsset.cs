using System.Text.Json;

namespace R2Engine.Editor.Scene;

public sealed class FontAsset
{
    public string Name { get; set; } = "Font";
    public string SourcePath { get; set; } = "";
    public string AtlasPath { get; set; } = "";
    public int FirstCharacter { get; set; } = 32;
    public int CharacterCount { get; set; } = 95;
    public int Columns { get; set; } = 16;
    public int Rows { get; set; } = 6;
    public int CellWidth { get; set; } = 32;
    public int CellHeight { get; set; } = 40;
    public int BakeSize { get; set; } = 28;

    public static FontAsset Load(string path) => JsonSerializer.Deserialize<FontAsset>(File.ReadAllText(path))
        ?? throw new InvalidDataException($"Invalid font asset: {path}");
    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
}
