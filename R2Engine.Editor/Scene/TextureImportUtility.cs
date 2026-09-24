using StbImageSharp;

namespace R2Engine.Editor.Scene;

public readonly record struct TextureImportInfo(
    int SourceWidth,
    int SourceHeight,
    int ImportedWidth,
    int ImportedHeight,
    long EstimatedVramBytes);

public static class TextureImportUtility
{
    public static TextureImportInfo Analyze(string path, TextureImportSettings settings)
    {
        // Import settings only need the image header. Fully decoding a 4K RGBA
        // source here allocates roughly 64 MB; this method is called while the
        // inspector is drawn, so the old implementation repeated that allocation
        // every frame and could make the editor appear to crash.
        using FileStream stream = File.OpenRead(path);
        ImageInfo image = ImageInfo.FromStream(stream)
            ?? throw new InvalidDataException($"Could not read image header for '{path}'.");
        (int width, int height) = CalculateImportedSize(image.Width, image.Height, settings.MaxSize);
        return new TextureImportInfo(
            image.Width,
            image.Height,
            width,
            height,
            EstimateVramBytes(width, height, settings.Format, settings.GenerateMipmaps));
    }

    public static (int Width, int Height) CalculateImportedSize(int width, int height, int maxSize)
    {
        if (maxSize <= 0)
            return (Math.Max(1, width), Math.Max(1, height));
        if (width <= maxSize && height <= maxSize)
            return (Math.Max(1, width), Math.Max(1, height));

        float scale = MathF.Min((float)maxSize / width, (float)maxSize / height);
        return (Math.Max(1, (int)MathF.Round(width * scale)), Math.Max(1, (int)MathF.Round(height * scale)));
    }

    // GS normalized ST coordinates address 2^TW by 2^TH texels. World textures
    // must fill that rectangle, including when UVs repeat. Resample, do not pad:
    // padding would expose an unused strip on every repeat. UI-only textures use
    // pixel UVs instead and retain their existing aspect-preserving cook sizes.
    public static (int Width, int Height) CalculatePs2WorldSize(int width, int height, int maxSize)
    {
        int limit = maxSize <= 0 ? 512 : Math.Min(maxSize, 512);
        int powerOfTwoLimit = 1;
        while (powerOfTwoLimit <= limit / 2) powerOfTwoLimit *= 2;
        (width, height) = CalculateImportedSize(width, height, powerOfTwoLimit);
        static int RoundUp(int value)
        {
            int result = 1;
            while (result < value) result *= 2;
            return result;
        }
        return (RoundUp(width), RoundUp(height));
    }

    public static long EstimateVramBytes(
        int width,
        int height,
        Ps2TextureFormat format,
        bool mipmaps)
    {
        long texelBytes = 0;
        int levelWidth = Math.Max(1, width);
        int levelHeight = Math.Max(1, height);
        while (true)
        {
            long pixels = (long)levelWidth * levelHeight;
            texelBytes += format switch
            {
                Ps2TextureFormat.Rgba32 => pixels * 4,
                Ps2TextureFormat.Rgba16 => pixels * 2,
                Ps2TextureFormat.Indexed8 => pixels,
                Ps2TextureFormat.Indexed4 => (pixels + 1) / 2,
                _ => pixels * 4
            };

            if (!mipmaps || (levelWidth == 1 && levelHeight == 1))
                break;
            levelWidth = Math.Max(1, levelWidth / 2);
            levelHeight = Math.Max(1, levelHeight / 2);
        }

        texelBytes += format switch
        {
            Ps2TextureFormat.Indexed8 => 256 * 4,
            Ps2TextureFormat.Indexed4 => 16 * 4,
            _ => 0
        };

        return (texelBytes + 255) / 256 * 256;
    }
}
