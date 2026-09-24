using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace R2Engine.Editor.Scene;

public static class FontAssetImporter
{
    public static string Import(string sourcePath, string destinationDirectory)
    {
        string source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source)) throw new FileNotFoundException("Font file does not exist.", source);
        if (Path.GetExtension(source).ToLowerInvariant() is not (".ttf" or ".otf"))
            throw new NotSupportedException("R2Engine font import supports TTF and OTF files.");
        Directory.CreateDirectory(destinationDirectory);
        string name = Path.GetFileNameWithoutExtension(source);
        string assetPath = Unique(Path.Combine(destinationDirectory, name + ".r2font"));
        Bake(source,assetPath);
        return AssetDatabase.ToProjectRelativePath(assetPath);
    }

    public static void Reimport(string assetPath,string sourcePath) => Bake(Path.GetFullPath(sourcePath),Path.GetFullPath(assetPath));

    private static void Bake(string source,string assetPath)
    {
        string name=Path.GetFileNameWithoutExtension(assetPath);
        string atlasPath = Path.ChangeExtension(assetPath, ".font.png");
        const int columns=16, rows=6, cellWidth=32, cellHeight=40, bakeSize=28;
        FontCollection collection = new();
        FontFamily family = collection.Add(source);
        Font font = family.CreateFont(bakeSize, FontStyle.Regular);
        using Image<Rgba32> atlas = new(columns*cellWidth, rows*cellHeight, new Rgba32(0,0,0,0));
        atlas.Mutate(context =>
        {
            for(int code=32;code<=126;code++)
            {
                int index=code-32, column=index%columns, row=index/columns;
                context.DrawText(((char)code).ToString(),font,Color.White,
                    new PointF(column*cellWidth+2,row*cellHeight+3));
            }
        });
        atlas.SaveAsPng(atlasPath);
        new FontAsset
        {
            Name=name, SourcePath=source,
            AtlasPath=AssetDatabase.ToProjectRelativePath(atlasPath),
            Columns=columns, Rows=rows, CellWidth=cellWidth, CellHeight=cellHeight, BakeSize=bakeSize
        }.Save(assetPath);
        AssetDatabase.SaveFileImportMetadata(assetPath,source);
        AssetDatabase.SaveTextureImportSettings(atlasPath,new TextureImportSettings
        { Format=Ps2TextureFormat.Indexed4, Filtering=TextureFilterOverride.Bilinear, GenerateMipmaps=false, MaxSize=512 });
    }

    private static string Unique(string path)
    {
        if(!File.Exists(path)) return path;
        string directory=Path.GetDirectoryName(path)!, name=Path.GetFileNameWithoutExtension(path), extension=Path.GetExtension(path);
        for(int i=1;;i++) { string candidate=Path.Combine(directory,$"{name} ({i}){extension}"); if(!File.Exists(candidate)) return candidate; }
    }
}
