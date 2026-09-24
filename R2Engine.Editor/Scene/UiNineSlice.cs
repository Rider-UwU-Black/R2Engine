using System.Numerics;
using StbImageSharp;

namespace R2Engine.Editor.Scene;

public static class UiNineSlice
{
    private static readonly Dictionary<string, (DateTime Stamp, Vector2 Size)> Sizes = new();

    // Borders are left, top, right, bottom, in original image pixels.
    public static Vector4 UvBorders(string? path, Vector4 borders, Vector4 sourceRect = default)
    {
        if (borders == Vector4.Zero || string.IsNullOrWhiteSpace(path)) return Vector4.Zero;
        string absolute = AssetDatabase.ToAbsolutePath(path);
        if (!File.Exists(absolute)) return Vector4.Zero;
        DateTime stamp = File.GetLastWriteTimeUtc(absolute);
        if (!Sizes.TryGetValue(absolute, out var entry) || entry.Stamp != stamp)
        {
            var image = ImageResult.FromMemory(File.ReadAllBytes(absolute), ColorComponents.RedGreenBlueAlpha);
            entry = (stamp, new Vector2(image.Width, image.Height));
            Sizes[absolute] = entry;
        }
        Vector4 region = Region(entry.Size, sourceRect);
        Vector2 regionSize = entry.Size * new Vector2(region.Z - region.X, region.W - region.Y);
        Vector4 uv = Vector4.Max(borders, Vector4.Zero) / new Vector4(regionSize.X, regionSize.Y, regionSize.X, regionSize.Y);
        float x = MathF.Max(1, uv.X + uv.Z), y = MathF.Max(1, uv.Y + uv.W);
        return uv / new Vector4(x, y, x, y);
    }

    public static Vector4 Region(Vector2 textureSize, Vector4 rect)
    {
        if (rect.Z <= 0 || rect.W <= 0 || !float.IsFinite(rect.X + rect.Y + rect.Z + rect.W)) return new(0, 0, 1, 1);
        textureSize = Vector2.Max(textureSize, Vector2.One);
        float x = Math.Clamp(rect.X, 0, textureSize.X - 1), y = Math.Clamp(rect.Y, 0, textureSize.Y - 1);
        return new(x / textureSize.X, y / textureSize.Y,
            Math.Clamp(x + rect.Z, x + 1, textureSize.X) / textureSize.X,
            Math.Clamp(y + rect.W, y + 1, textureSize.Y) / textureSize.Y);
    }

    public static Vector4 UvRegion(string? path, Vector4 rect)
    {
        if (rect.Z <= 0 || rect.W <= 0 || string.IsNullOrWhiteSpace(path)) return new(0, 0, 1, 1);
        // Reuse the dimension cache, including its timestamp-based invalidation.
        UvBorders(path, Vector4.One);
        return Sizes.TryGetValue(AssetDatabase.ToAbsolutePath(path), out var entry)
            ? Region(entry.Size, rect) : new(0, 0, 1, 1);
    }

    public static Vector2 MapUv(Vector2 uv, Vector4 region) =>
        new Vector2(region.X, region.Y) + uv * new Vector2(region.Z - region.X, region.W - region.Y);

    public static IEnumerable<(Vector2 Min, Vector2 Max, Vector2 UvMin, Vector2 UvMax)> Quads(
        Vector2 min, Vector2 max, Vector4 borders, Vector4 uv, Vector2 scale)
    {
        if (uv == Vector4.Zero)
        { yield return (min, max, Vector2.Zero, Vector2.One); yield break; }
        borders = Vector4.Max(borders, Vector4.Zero) * new Vector4(scale.X, scale.Y, scale.X, scale.Y);
        Vector2 size = Vector2.Max(max - min, Vector2.Zero);
        float sx = MathF.Min(1, size.X / MathF.Max(.0001f, borders.X + borders.Z));
        float sy = MathF.Min(1, size.Y / MathF.Max(.0001f, borders.Y + borders.W));
        float[] x = [min.X, min.X + borders.X * sx, max.X - borders.Z * sx, max.X];
        float[] y = [min.Y, min.Y + borders.Y * sy, max.Y - borders.W * sy, max.Y];
        float[] u = [0, uv.X, 1 - uv.Z, 1], v = [0, uv.Y, 1 - uv.W, 1];
        for (int row = 0; row < 3; ++row)
        for (int col = 0; col < 3; ++col)
            if (x[col + 1] > x[col] && y[row + 1] > y[row])
                yield return (new(x[col], y[row]), new(x[col + 1], y[row + 1]),
                    new(u[col], v[row]), new(u[col + 1], v[row + 1]));
    }
}
