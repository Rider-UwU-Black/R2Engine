using Silk.NET.OpenGL;
using StbImageSharp;
using R2Engine.Editor.Scene;
using R2Engine.Runtime.Platform;

namespace R2Engine.Editor;

public sealed class TextureLoader : IDisposable
{
    private readonly GL _gl;

    private readonly Dictionary<string, uint> _textures =
        new(
            StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, long> _estimatedVram =
        new(StringComparer.OrdinalIgnoreCase);

    public RenderTextureFiltering Filtering { get; set; } =
        RenderTextureFiltering.Bilinear;

    public TextureLoader(
        GL gl)
    {
        _gl =
            gl;
    }

    // =========================================================
    // Load Texture
    // =========================================================

    public uint? GetTexture(
        string? texturePath)
    {
        if (string.IsNullOrWhiteSpace(
                texturePath))
        {
            return null;
        }

        string fullPath;

        try
        {
            fullPath =
                Path.GetFullPath(
                    texturePath);
        }
        catch
        {
            return null;
        }

        if (!RuntimePlatform.Files.FileExists(
                fullPath))
        {
            return null;
        }

        if (_textures.TryGetValue(
                fullPath,
                out uint existingTexture))
        {
            ApplyFiltering(existingTexture, AssetDatabase.GetTextureImportSettings(fullPath));
            return existingTexture;
        }

        try
        {
            uint texture =
                LoadTexture(
                    fullPath);

            _textures.Add(
                fullPath,
                texture);

            return texture;
        }
        catch (
            Exception exception)
        {
            RuntimePlatform.Log.Error(
                $"Failed to load texture '{fullPath}': {exception.Message}");

            return null;
        }
    }

    // =========================================================
    // Decode + Upload
    // =========================================================

    private unsafe uint LoadTexture(
        string fullPath)
    {
        byte[] fileData =
            RuntimePlatform.Files.ReadAllBytes(
                fullPath);

        ImageResult image =
            ImageResult.FromMemory(
                fileData,
                ColorComponents.RedGreenBlueAlpha);

        TextureImportSettings settings = AssetDatabase.GetTextureImportSettings(fullPath);
        (int importedWidth, int importedHeight) = TextureImportUtility.CalculateImportedSize(
            image.Width, image.Height, settings.MaxSize);
        byte[] pixels = image.Data;
        if (importedWidth != image.Width || importedHeight != image.Height)
            pixels = ResizeRgba(pixels, image.Width, image.Height, importedWidth, importedHeight);
        QuantizeForPreview(pixels, settings.Format);

        uint texture =
            _gl.GenTexture();

        _gl.BindTexture(
            TextureTarget.Texture2D,
            texture);

        fixed (
            byte* pixelData =
                pixels)
        {
            _gl.TexImage2D(
                TextureTarget.Texture2D,
                0,
                InternalFormat.Rgba8,
                (uint)importedWidth,
                (uint)importedHeight,
                0,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                pixelData);
        }

        // -----------------------------------------------------
        // PS2-ish default behavior
        // -----------------------------------------------------
        //
        // We're using nearest-neighbor filtering for now.
        // That's useful for our target aesthetic and also makes
        // it extremely obvious whether the UVs are working.
        // -----------------------------------------------------

        int wrapMode =
            (int)
            TextureWrapMode.Repeat;

        _gl.TexParameterI(
            GLEnum.Texture2D,
            GLEnum.TextureWrapS,
            ref wrapMode);

        if (settings.GenerateMipmaps)
            _gl.GenerateMipmap(TextureTarget.Texture2D);

        ApplyFiltering(texture, settings);

        _gl.TexParameterI(
            GLEnum.Texture2D,
            GLEnum.TextureWrapT,
            ref wrapMode);

        _gl.BindTexture(
            TextureTarget.Texture2D,
            0);

        return texture;
    }

    private void ApplyFiltering(uint texture, TextureImportSettings settings)
    {
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        bool nearest = settings.Filtering == TextureFilterOverride.Nearest ||
                       settings.Filtering == TextureFilterOverride.ProjectDefault &&
                       Filtering == RenderTextureFiltering.Nearest;
        int minFilter = nearest
            ? settings.GenerateMipmaps
                ? (int)TextureMinFilter.NearestMipmapNearest
                : (int)TextureMinFilter.Nearest
            : settings.GenerateMipmaps
                ? (int)TextureMinFilter.LinearMipmapLinear
                : (int)TextureMinFilter.Linear;
        int magFilter = nearest
            ? (int)TextureMagFilter.Nearest
            : (int)TextureMagFilter.Linear;
        _gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureMinFilter, ref minFilter);
        _gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureMagFilter, ref magFilter);
    }

    public long GetEstimatedVramBytes(string texturePath)
    {
        try
        {
            string fullPath = Path.GetFullPath(texturePath);
            if (_estimatedVram.TryGetValue(fullPath, out long cached))
                return cached;
            TextureImportSettings settings = AssetDatabase.GetTextureImportSettings(fullPath);
            long estimate = TextureImportUtility.Analyze(fullPath, settings).EstimatedVramBytes;
            _estimatedVram[fullPath] = estimate;
            return estimate;
        }
        catch
        {
            return 0;
        }
    }

    public void Invalidate(string texturePath)
    {
        string fullPath = Path.GetFullPath(texturePath);
        if (_textures.Remove(fullPath, out uint texture) && texture != 0)
            _gl.DeleteTexture(texture);
        _estimatedVram.Remove(fullPath);
    }

    private static byte[] ResizeRgba(byte[] source, int sourceWidth, int sourceHeight, int width, int height)
    {
        byte[] result = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            int sourceY = Math.Min(sourceHeight - 1, y * sourceHeight / height);
            for (int x = 0; x < width; x++)
            {
                int sourceX = Math.Min(sourceWidth - 1, x * sourceWidth / width);
                int sourceOffset = (sourceY * sourceWidth + sourceX) * 4;
                int targetOffset = (y * width + x) * 4;
                System.Buffer.BlockCopy(source, sourceOffset, result, targetOffset, 4);
            }
        }
        return result;
    }

    private static void QuantizeForPreview(byte[] pixels, Ps2TextureFormat format)
    {
        if (format == Ps2TextureFormat.Rgba32)
            return;

        for (int index = 0; index + 3 < pixels.Length; index += 4)
        {
            if (format == Ps2TextureFormat.Rgba16)
            {
                pixels[index] = (byte)(pixels[index] & 0xF8);
                pixels[index + 1] = (byte)(pixels[index + 1] & 0xF8);
                pixels[index + 2] = (byte)(pixels[index + 2] & 0xF8);
                pixels[index + 3] = pixels[index + 3] >= 128 ? (byte)255 : (byte)0;
            }
            else if (format == Ps2TextureFormat.Indexed8)
            {
                pixels[index] = (byte)(pixels[index] & 0xE0);
                pixels[index + 1] = (byte)(pixels[index + 1] & 0xE0);
                pixels[index + 2] = (byte)(pixels[index + 2] & 0xC0);
            }
            else
            {
                byte luminance = (byte)((pixels[index] * 3 + pixels[index + 1] * 6 + pixels[index + 2]) / 10);
                byte level = (byte)(luminance / 17 * 17);
                pixels[index] = level;
                pixels[index + 1] = level;
                pixels[index + 2] = level;
            }
        }
    }

    // =========================================================
    // Cleanup
    // =========================================================

    public void Dispose()
    {
        Clear();
    }

    public void Clear()
    {
        foreach (
            uint texture
            in _textures.Values)
        {
            if (texture !=
                0)
            {
                _gl.DeleteTexture(
                    texture);
            }
        }

        _textures.Clear();
        _estimatedVram.Clear();
    }
}
