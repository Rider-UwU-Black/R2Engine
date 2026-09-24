using Silk.NET.OpenGL;
using StbImageSharp;

namespace R2Engine.Editor;

public sealed class AssetThumbnailCache : IDisposable
{
    private sealed class Thumbnail
    {
        public uint Texture;
        public int Width;
        public int Height;
    }

    private readonly GL _gl;
    private readonly Dictionary<string, Task<byte[]>> _previews = new(StringComparer.OrdinalIgnoreCase);
    // Serialize preview work so opening a large folder cannot saturate the CPU.
    private static readonly SemaphoreSlim PreviewWorker = new(1);

    public static bool SupportsPreview(string path) => IsTextureFile(path) || Path.GetExtension(path).Equals(".r2font",StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).ToLowerInvariant() is ".obj" or ".r2skel" or ".r2mat";

    public void Invalidate(string path)
    {
        path = Path.GetFullPath(path);
        if (_cache.Remove(path, out var old)) _gl.DeleteTexture(old.Texture);
        if (_fullSizeCache.Remove(path, out var fullSize)) _gl.DeleteTexture(fullSize.Texture);
        _previews.Remove(path);
    }

    private readonly Dictionary<string, Thumbnail> _cache =
        new(
            StringComparer.OrdinalIgnoreCase);

    // Canvas rendering must never sample the downscaled Project thumbnails.
    private readonly Dictionary<string, Thumbnail> _fullSizeCache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly int _maxThumbnailSize;

    public AssetThumbnailCache(
        GL gl,
        int maxThumbnailSize = 96)
    {
        _gl =
            gl;

        _maxThumbnailSize = Math.Max(1, maxThumbnailSize);
    }

    // =========================================================
    // Public Lookup
    // =========================================================

    public bool TryGetThumbnail(
        string filePath,
        out uint texture,
        out int width,
        out int height)
    {
        texture =
            0;

        width =
            0;

        height =
            0;

        if (!SupportsPreview(
                filePath))
        {
            return false;
        }

        string fullPath;

        try
        {
            fullPath =
                Path.GetFullPath(
                    filePath);
        }
        catch
        {
            return false;
        }

        if (!File.Exists(
                fullPath))
        {
            return false;
        }

        if (_cache.TryGetValue(
                fullPath,
                out Thumbnail? cached))
        {
            texture =
                cached.Texture;

            width =
                cached.Width;

            height =
                cached.Height;

            return true;
        }

        try
        {
            Thumbnail thumbnail;
            if (IsTextureFile(fullPath)) thumbnail = CreateThumbnail(fullPath);
            else if(Path.GetExtension(fullPath).Equals(".r2font",StringComparison.OrdinalIgnoreCase))
                thumbnail=CreateThumbnail(AssetDatabase.ToAbsolutePath(Scene.FontAsset.Load(fullPath).AtlasPath));
            else
            {
                if (!_previews.TryGetValue(fullPath, out var pending))
                {
                    pending = Task.Run(async () =>
                    {
                        await PreviewWorker.WaitAsync();
                        try { return ModelThumbnail.Render(fullPath, _maxThumbnailSize); }
                        finally { PreviewWorker.Release(); }
                    });
                    _previews.Add(fullPath, pending);
                }
                // Failed previews keep the generic icon until explicitly refreshed.
                if (!pending.IsCompletedSuccessfully) { if (pending.IsFaulted) _ = pending.Exception; return false; }
                thumbnail = Upload(pending.Result, _maxThumbnailSize, _maxThumbnailSize);
            }

            _cache.Add(
                fullPath,
                thumbnail);

            texture =
                thumbnail.Texture;

            width =
                thumbnail.Width;

            height =
                thumbnail.Height;

            return true;
        }
        catch (
            Exception exception)
        {
            Console.WriteLine(
                $"Thumbnail failed for '{fullPath}': {exception.Message}");

            return false;
        }
    }

    // =========================================================
    // Texture Type
    // =========================================================

    public static bool IsTextureFile(
        string path)
    {
        string extension =
            Path.GetExtension(
                path)
            .ToLowerInvariant();

        return extension switch
        {
            ".png" =>
                true,

            ".jpg" =>
                true,

            ".jpeg" =>
                true,

            ".bmp" =>
                true,

            ".tga" =>
                true,

            _ =>
                false
        };
    }

    // =========================================================
    // Thumbnail Creation
    // =========================================================

    private unsafe Thumbnail CreateThumbnail(
        string filePath)
    {
        byte[] fileData =
            File.ReadAllBytes(
                filePath);

        ImageResult source =
            ImageResult.FromMemory(
                fileData,
                ColorComponents.RedGreenBlueAlpha);

        CalculateThumbnailSize(
            source.Width,
            source.Height,
            out int thumbnailWidth,
            out int thumbnailHeight);

        byte[] thumbnailPixels;

        if (source.Width ==
                thumbnailWidth &&
            source.Height ==
                thumbnailHeight)
        {
            thumbnailPixels =
                source.Data;
        }
        else
        {
            thumbnailPixels =
                ResizeNearest(
                    source.Data,
                    source.Width,
                    source.Height,
                    thumbnailWidth,
                    thumbnailHeight);
        }

        return Upload(thumbnailPixels, thumbnailWidth, thumbnailHeight);
    }

    public bool TryGetFullTexture(string filePath, out uint texture, out int width, out int height)
    {
        texture = 0; width = 0; height = 0;
        string fullPath;
        try { fullPath = Path.GetFullPath(filePath); }
        catch { return false; }
        if (!File.Exists(fullPath) || !IsTextureFile(fullPath)) return false;
        if (!_fullSizeCache.TryGetValue(fullPath, out Thumbnail? cached))
        {
            try
            {
                ImageResult source = ImageResult.FromMemory(File.ReadAllBytes(fullPath), ColorComponents.RedGreenBlueAlpha);
                cached = Upload(source.Data, source.Width, source.Height);
                _fullSizeCache.Add(fullPath, cached);
            }
            catch (Exception exception)
            {
                Console.WriteLine($"UI texture failed for '{fullPath}': {exception.Message}");
                return false;
            }
        }
        texture = cached.Texture; width = cached.Width; height = cached.Height;
        return true;
    }

    private unsafe Thumbnail Upload(byte[] thumbnailPixels, int thumbnailWidth, int thumbnailHeight)
    {
        int previousTexture = _gl.GetInteger(GetPName.TextureBinding2D);
        uint texture =
            _gl.GenTexture();

        _gl.BindTexture(
            TextureTarget.Texture2D,
            texture);

        fixed (
            byte* pixels =
                thumbnailPixels)
        {
            _gl.TexImage2D(
                TextureTarget.Texture2D,
                0,
                InternalFormat.Rgba8,
                (uint)thumbnailWidth,
                (uint)thumbnailHeight,
                0,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                pixels);
        }

        int minFilter =
            (int)
            TextureMinFilter.Linear;

        int magFilter =
            (int)
            TextureMagFilter.Linear;

        int wrap =
            (int)
            TextureWrapMode.ClampToEdge;

        _gl.TexParameterI(
            GLEnum.Texture2D,
            GLEnum.TextureMinFilter,
            ref minFilter);

        _gl.TexParameterI(
            GLEnum.Texture2D,
            GLEnum.TextureMagFilter,
            ref magFilter);

        _gl.TexParameterI(
            GLEnum.Texture2D,
            GLEnum.TextureWrapS,
            ref wrap);

        _gl.TexParameterI(
            GLEnum.Texture2D,
            GLEnum.TextureWrapT,
            ref wrap);

        _gl.BindTexture(
            TextureTarget.Texture2D,
            (uint)previousTexture);

        return new Thumbnail
        {
            Texture =
                texture,

            Width =
                thumbnailWidth,

            Height =
                thumbnailHeight
        };
    }

    // =========================================================
    // Thumbnail Sizing
    // =========================================================

    private void CalculateThumbnailSize(
        int sourceWidth,
        int sourceHeight,
        out int width,
        out int height)
    {
        if (sourceWidth <=
                _maxThumbnailSize &&
            sourceHeight <=
                _maxThumbnailSize)
        {
            width =
                Math.Max(
                    1,
                    sourceWidth);

            height =
                Math.Max(
                    1,
                    sourceHeight);

            return;
        }

        float scale =
            MathF.Min(
                _maxThumbnailSize /
                (float)sourceWidth,

                _maxThumbnailSize /
                (float)sourceHeight);

        width =
            Math.Max(
                1,
                (int)(
                    sourceWidth *
                    scale));

        height =
            Math.Max(
                1,
                (int)(
                    sourceHeight *
                    scale));
    }

    // =========================================================
    // Simple Nearest Resize
    // =========================================================

    private static byte[] ResizeNearest(
        byte[] source,
        int sourceWidth,
        int sourceHeight,
        int destinationWidth,
        int destinationHeight)
    {
        byte[] destination =
            new byte[
                destinationWidth *
                destinationHeight *
                4];

        for (
            int y = 0;
            y < destinationHeight;
            y++)
        {
            int sourceY =
                Math.Min(
                    sourceHeight - 1,
                    y *
                    sourceHeight /
                    destinationHeight);

            for (
                int x = 0;
                x < destinationWidth;
                x++)
            {
                int sourceX =
                    Math.Min(
                        sourceWidth - 1,
                        x *
                        sourceWidth /
                        destinationWidth);

                int sourceIndex =
                    (
                        sourceY *
                        sourceWidth +
                        sourceX
                    ) *
                    4;

                int destinationIndex =
                    (
                        y *
                        destinationWidth +
                        x
                    ) *
                    4;

                destination[destinationIndex] =
                    source[sourceIndex];

                destination[destinationIndex + 1] =
                    source[sourceIndex + 1];

                destination[destinationIndex + 2] =
                    source[sourceIndex + 2];

                destination[destinationIndex + 3] =
                    source[sourceIndex + 3];
            }
        }

        return destination;
    }

    // =========================================================
    // Refresh
    // =========================================================

    public void Clear()
    {
        foreach (
            Thumbnail thumbnail
            in _cache.Values)
        {
            if (thumbnail.Texture !=
                0)
            {
                _gl.DeleteTexture(
                    thumbnail.Texture);
            }
        }

        _cache.Clear();
        foreach (Thumbnail texture in _fullSizeCache.Values)
            if (texture.Texture != 0) _gl.DeleteTexture(texture.Texture);
        _fullSizeCache.Clear();
        _previews.Clear();
    }

    // =========================================================
    // Cleanup
    // =========================================================

    public void Dispose()
    {
        Clear();
    }
}
