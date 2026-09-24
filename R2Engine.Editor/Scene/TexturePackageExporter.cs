using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using StbImageSharp;

namespace R2Engine.Editor.Scene;

public sealed class TexturePackageExportResult
{
    public int TextureCount { get; init; }
    public long TotalVramBytes { get; init; }
    public string ManifestPath { get; init; } = "";
}

public static class TexturePackageExporter
{
    private sealed class Manifest
    {
        public int Version { get; set; } = 1;
        public long TotalEstimatedVramBytes { get; set; }
        public List<ManifestEntry> Textures { get; set; } = new();
    }

    private sealed class ManifestEntry
    {
        public string Source { get; set; } = "";
        public string Package { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public Ps2TextureFormat Format { get; set; }
        public int MipLevels { get; set; }
        public long EstimatedVramBytes { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static TexturePackageExportResult Export(IEnumerable<string> scenePaths, string buildDirectory)
    {
        string loadingPrefab = LoadingScreen.ConfiguredPath();
        if (!string.IsNullOrWhiteSpace(loadingPrefab)) scenePaths = scenePaths.Append(AssetDatabase.ToAbsolutePath(loadingPrefab));
        string[] includedScenes = scenePaths.ToArray();
        HashSet<string> texturePaths = CollectSceneTextures(includedScenes);
        HashSet<string> worldTexturePaths = CollectSceneTextures(includedScenes, includeUi: false);
        HashSet<string> uiTexturePaths = CollectUiTextures(includedScenes);
        string dataRoot = Path.Combine(buildDirectory, "R2Data");
        string packageRoot = Path.Combine(dataRoot, "Textures");
        if (Directory.Exists(packageRoot))
            Directory.Delete(packageRoot, recursive: true);
        Directory.CreateDirectory(packageRoot);

        Manifest manifest = new();
        foreach (string texturePath in texturePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            TextureImportSettings settings = AssetDatabase.GetTextureImportSettings(texturePath);
            // At 640x448 the PS2 color/depth buffers leave well under 1 MB of GS
            // memory for scene textures. Preserve source assets and desktop quality,
            // but cook default RGBA32 imports as native RGBA16 so a normal scene plus
            // UI can coexist without framebuffer overlap.
            bool fontAtlas=texturePath.EndsWith(".font.png",StringComparison.OrdinalIgnoreCase);
            bool uiTexture=uiTexturePaths.Contains(texturePath);
            // UI artwork tends to cover much more screen area than world textures.
            // Packing it to an 8-bit palette cuts its GS allocation in half while
            // retaining enough color precision for gradients and decorated panels.
            // Font atlases use 4-bit because their glyph coverage needs very few colors.
            Ps2TextureFormat ps2Format = fontAtlas ? Ps2TextureFormat.Indexed4 :
                uiTexture ? Ps2TextureFormat.Indexed8 :
                settings.Format == Ps2TextureFormat.Rgba32 ? Ps2TextureFormat.Rgba16 : settings.Format;
            ImageResult source = ImageResult.FromMemory(File.ReadAllBytes(texturePath), ColorComponents.RedGreenBlueAlpha);
            // Although the GS fields can represent 1024, the console has only 4 MB of
            // shared framebuffer/texture VRAM. A practical 512 ceiling prevents a pair
            // of RGBA UI sprites from wrapping into the active framebuffer.
            int ps2MaxSize = settings.MaxSize <= 0 ? 512 : Math.Min(settings.MaxSize, 512);
            // Full-screen UI images compete with both framebuffers and every
            // world texture in the GS's 4 MB. A 384px UI ceiling keeps complex
            // menus/loading screens viable while fonts retain their exact 512px grid.
            if (uiTexture && !fontAtlas) ps2MaxSize = Math.Min(ps2MaxSize, 384);
            (int width, int height) = TextureImportUtility.CalculateImportedSize(
                source.Width, source.Height, ps2MaxSize);
            // Shared world/UI textures also need a fully populated GS sampling
            // rectangle; the UI pixel-UV path uses these cooked dimensions.
            if (worldTexturePaths.Contains(texturePath))
                (width, height) = TextureImportUtility.CalculatePs2WorldSize(
                    source.Width, source.Height, ps2MaxSize);
            byte[] basePixels = ResizeRgba(source.Data, source.Width, source.Height, width, height);
            List<(int Width, int Height, byte[] Pixels)> levels = BuildMipChain(
                width, height, basePixels, !uiTexture && settings.GenerateMipmaps);

            string sourceRelative = AssetDatabase.ToProjectRelativePath(texturePath);
            string relativeWithoutExtension = Path.ChangeExtension(sourceRelative, null) ?? sourceRelative;
            string packageRelative = Path.Combine("R2Data", "Textures", relativeWithoutExtension + ".r2tex");
            string packagePath = Path.Combine(buildDirectory, packageRelative);
            Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
            WritePackage(packagePath, ps2Format, settings.Filtering, levels);

            long vram = TextureImportUtility.EstimateVramBytes(width, height, ps2Format, !uiTexture && settings.GenerateMipmaps);
            manifest.TotalEstimatedVramBytes += vram;
            manifest.Textures.Add(new ManifestEntry
            {
                Source = sourceRelative.Replace('\\', '/'),
                Package = packageRelative.Replace('\\', '/'),
                Width = width,
                Height = height,
                Format = ps2Format,
                MipLevels = levels.Count,
                EstimatedVramBytes = vram
            });
        }

        Directory.CreateDirectory(dataRoot);
        string manifestPath = Path.Combine(dataRoot, "texture-manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
        return new TexturePackageExportResult
        {
            TextureCount = manifest.Textures.Count,
            TotalVramBytes = manifest.TotalEstimatedVramBytes,
            ManifestPath = manifestPath
        };
    }

    private static HashSet<string> CollectSceneTextures(IEnumerable<string> scenePaths, bool includeUi = true)
    {
        HashSet<string> textures = new(StringComparer.OrdinalIgnoreCase);
        foreach (string scenePath in scenePaths)
        {
            Scene scene = SceneSerializer.Deserialize(File.ReadAllText(scenePath));
            foreach (MeshRenderer renderer in scene.GameObjects.SelectMany(gameObject =>
                         gameObject.Components.OfType<MeshRenderer>()))
            {
                for (int slot = 0; slot < renderer.MaterialSlotCount; slot++)
                {
                    string? texture = renderer.GetMaterial(slot).TexturePath;
                    if (string.IsNullOrWhiteSpace(texture)) continue;
                    string absolute = AssetDatabase.ToAbsolutePath(texture);
                    if (File.Exists(absolute) && AssetThumbnailCache.IsTextureFile(absolute))
                        textures.Add(absolute);
                }
            }
            if (!includeUi) continue;
            IEnumerable<string> uiTextures = scene.GameObjects.SelectMany(gameObject =>
            {
                List<string> paths = new();
                if (gameObject.GetComponent<UIImage>() is { } image) paths.Add(image.TexturePath);
                if (gameObject.GetComponent<UIButton>() is { } button) { paths.Add(button.NormalSprite); paths.Add(button.HoverSprite); paths.Add(button.PressedSprite); }
                if (gameObject.GetComponent<UIButton>() is { FontPath.Length: > 0 } fontButton)
                    try { paths.Add(FontAsset.Load(AssetDatabase.ToAbsolutePath(fontButton.FontPath)).AtlasPath); } catch { }
                if (gameObject.GetComponent<Interactable>() is { PromptFontPath.Length: > 0 } interaction)
                    try { paths.Add(FontAsset.Load(AssetDatabase.ToAbsolutePath(interaction.PromptFontPath)).AtlasPath); } catch { }
                if (gameObject.GetComponent<UIText>() is { FontPath.Length: >0 } text)
                    try { paths.Add(FontAsset.Load(AssetDatabase.ToAbsolutePath(text.FontPath)).AtlasPath); } catch { }
                return paths;
            });
            foreach (string texture in uiTextures.Where(path => !string.IsNullOrWhiteSpace(path)))
            { string absolute = AssetDatabase.ToAbsolutePath(texture); if (File.Exists(absolute) && AssetThumbnailCache.IsTextureFile(absolute)) textures.Add(absolute); }
        }
        return textures;
    }

    private static HashSet<string> CollectUiTextures(IEnumerable<string> scenePaths)
    {
        HashSet<string> textures = new(StringComparer.OrdinalIgnoreCase);
        foreach (string scenePath in scenePaths)
        {
            Scene scene = SceneSerializer.Deserialize(File.ReadAllText(scenePath));
            IEnumerable<string> paths = scene.GameObjects.SelectMany(gameObject =>
            {
                List<string> found = new();
                if (gameObject.GetComponent<UIImage>() is { } image) found.Add(image.TexturePath);
                if (gameObject.GetComponent<UIButton>() is { } button)
                { found.Add(button.NormalSprite); found.Add(button.HoverSprite); found.Add(button.PressedSprite); }
                if (gameObject.GetComponent<UIButton>() is { FontPath.Length: > 0 } fontButton)
                    try { found.Add(FontAsset.Load(AssetDatabase.ToAbsolutePath(fontButton.FontPath)).AtlasPath); } catch { }
                if (gameObject.GetComponent<Interactable>() is { PromptFontPath.Length: > 0 } interaction)
                    try { found.Add(FontAsset.Load(AssetDatabase.ToAbsolutePath(interaction.PromptFontPath)).AtlasPath); } catch { }
                if (gameObject.GetComponent<UIText>() is { FontPath.Length: > 0 } text)
                    try { found.Add(FontAsset.Load(AssetDatabase.ToAbsolutePath(text.FontPath)).AtlasPath); } catch { }
                return found;
            });
            foreach (string path in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                string absolute = AssetDatabase.ToAbsolutePath(path);
                if (File.Exists(absolute) && AssetThumbnailCache.IsTextureFile(absolute)) textures.Add(absolute);
            }
        }
        return textures;
    }

    private static List<(int Width, int Height, byte[] Pixels)> BuildMipChain(
        int width, int height, byte[] pixels, bool mipmaps)
    {
        List<(int Width, int Height, byte[] Pixels)> levels = new() { (width, height, pixels) };
        while (mipmaps && (width > 1 || height > 1))
        {
            int nextWidth = Math.Max(1, width / 2);
            int nextHeight = Math.Max(1, height / 2);
            pixels = ResizeRgba(pixels, width, height, nextWidth, nextHeight);
            width = nextWidth;
            height = nextHeight;
            levels.Add((width, height, pixels));
        }
        return levels;
    }

    private static byte[] ResizeRgba(byte[] source, int sourceWidth, int sourceHeight, int width, int height)
    {
        if (sourceWidth == width && sourceHeight == height)
            return source.ToArray();
        byte[] result = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int x0 = x * sourceWidth / width;
            int y0 = y * sourceHeight / height;
            int sourceOffset = (y0 * sourceWidth + x0) * 4;
            int targetOffset = (y * width + x) * 4;
            System.Buffer.BlockCopy(source, sourceOffset, result, targetOffset, 4);
        }
        return result;
    }

    private static void WritePackage(
        string path,
        Ps2TextureFormat format,
        TextureFilterOverride filtering,
        List<(int Width, int Height, byte[] Pixels)> levels)
    {
        using BinaryWriter writer = new(File.Create(path), Encoding.UTF8, leaveOpen: false);
        writer.Write(Encoding.ASCII.GetBytes("R2TX"));
        writer.Write(2);
        writer.Write((int)format);
        writer.Write(levels[0].Width);
        writer.Write(levels[0].Height);
        writer.Write(levels.Count);

        byte[] palette = BuildPalette(format, levels[0].Pixels);
        writer.Write(palette.Length / 4);
        writer.Write(filtering == TextureFilterOverride.Nearest ? 0 : 1);
        writer.Write(palette);
        foreach ((int width, int height, byte[] pixels) in levels)
        {
            byte[] encoded = EncodePixels(pixels, format, palette);
            writer.Write(width);
            writer.Write(height);
            writer.Write(encoded.Length);
            writer.Write(encoded);
        }
    }

    private static byte[] BuildPalette(Ps2TextureFormat format, byte[] rgba)
    {
        int count = format == Ps2TextureFormat.Indexed8 ? 256 :
            format == Ps2TextureFormat.Indexed4 ? 16 : 0;
        if (count == 0) return Array.Empty<byte>();

        // Build an image-specific RGBA palette. Quantizing to four bits per
        // channel first keeps the histogram compact while retaining several
        // real alpha levels for translucent panels and antialiased glyphs.
        Dictionary<ushort, int> histogram = new();
        for (int offset = 0; offset < rgba.Length; offset += 4)
        {
            if (rgba[offset + 3] < 8) continue; // palette index zero is transparent
            ushort key = (ushort)((rgba[offset] >> 4) |
                                  (rgba[offset + 1] >> 4) << 4 |
                                  (rgba[offset + 2] >> 4) << 8 |
                                  (rgba[offset + 3] >> 4) << 12);
            histogram.TryGetValue(key, out int occurrences);
            histogram[key] = occurrences + 1;
        }
        ushort[] colors = histogram.OrderByDescending(pair => pair.Value)
            .Take(count - 1).Select(pair => pair.Key).ToArray();
        byte[] palette = new byte[count * 4];
        for (int index = 0; index < colors.Length; index++)
        {
            ushort color = colors[index];
            int destination = (index + 1) * 4;
            palette[destination] = (byte)((color & 15) * 17);
            palette[destination + 1] = (byte)(((color >> 4) & 15) * 17);
            palette[destination + 2] = (byte)(((color >> 8) & 15) * 17);
            palette[destination + 3] = (byte)(((color >> 12) & 15) * 17);
        }
        return palette;
    }

    private static byte[] EncodePixels(byte[] rgba, Ps2TextureFormat format, byte[] palette)
    {
        if (format == Ps2TextureFormat.Rgba32)
            return rgba.ToArray();
        int pixels = rgba.Length / 4;
        if (format == Ps2TextureFormat.Rgba16)
        {
            byte[] encoded = new byte[pixels * 2];
            for (int pixel = 0; pixel < pixels; pixel++)
            {
                int offset = pixel * 4;
                ushort value = (ushort)((rgba[offset] >> 3) |
                                        (rgba[offset + 1] >> 3) << 5 |
                                        (rgba[offset + 2] >> 3) << 10 |
                                        (rgba[offset + 3] >= 128 ? 1 << 15 : 0));
                encoded[pixel * 2] = (byte)value;
                encoded[pixel * 2 + 1] = (byte)(value >> 8);
            }
            return encoded;
        }
        if (format == Ps2TextureFormat.Indexed8)
        {
            byte[] encoded = new byte[pixels];
            for (int pixel = 0; pixel < pixels; pixel++)
            {
                int offset = pixel * 4;
                encoded[pixel] = FindPaletteIndex(rgba, offset, palette);
            }
            return encoded;
        }

        byte[] packed = new byte[(pixels + 1) / 2];
        for (int pixel = 0; pixel < pixels; pixel++)
        {
            int offset = pixel * 4;
            byte index = FindPaletteIndex(rgba, offset, palette);
            if ((pixel & 1) == 0) packed[pixel / 2] = index;
            else packed[pixel / 2] |= (byte)(index << 4);
        }
        return packed;
    }

    private static byte FindPaletteIndex(byte[] rgba, int offset, byte[] palette)
    {
        if (rgba[offset + 3] < 8) return 0;
        int best = 1;
        int bestDistance = int.MaxValue;
        for (int index = 1; index < palette.Length / 4; index++)
        {
            int paletteOffset = index * 4;
            // Empty trailing entries are never candidates.
            if (palette[paletteOffset + 3] == 0) continue;
            int red = rgba[offset] - palette[paletteOffset];
            int green = rgba[offset + 1] - palette[paletteOffset + 1];
            int blue = rgba[offset + 2] - palette[paletteOffset + 2];
            int alpha = rgba[offset + 3] - palette[paletteOffset + 3];
            int distance = red * red + green * green + blue * blue + alpha * alpha * 2;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = index;
            if (distance == 0) break;
        }
        return (byte)best;
    }
}
