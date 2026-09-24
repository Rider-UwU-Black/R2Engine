using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

using R2Engine.Runtime.Platform;

namespace R2Engine.Editor.Scene;

public static class MaterialSerializer
{
    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented =
                true,

            DefaultIgnoreCondition =
                JsonIgnoreCondition.WhenWritingNull
        };

    public static void Save(
        Material material,
        string filePath)
    {
        string? directory =
            Path.GetDirectoryName(
                filePath);

        if (!string.IsNullOrWhiteSpace(
                directory))
        {
            Directory.CreateDirectory(
                directory);
        }

        MaterialFileData data =
            new()
            {
                Name =
                    material.Name,

                BaseColor =
                    new Vector4Data
                    {
                        X =
                            material.BaseColor.X,

                        Y =
                            material.BaseColor.Y,

                        Z =
                            material.BaseColor.Z,

                        W =
                            material.BaseColor.W
                    },

                TexturePath =
                    material.TexturePath,

                TextureTiling = Vector2Data.FromVector2(material.TextureTiling),
                TextureOffset = Vector2Data.FromVector2(material.TextureOffset),

                LightingMode = material.LightingMode.ToString(),
                SurfaceMode = material.SurfaceMode.ToString(),
                AlphaCutoff = material.AlphaCutoff,
                DoubleSided = material.DoubleSided,
                ReceiveFog = material.ReceiveFog
            };

        string json =
            JsonSerializer.Serialize(
                data,
                JsonOptions);

        File.WriteAllText(
            filePath,
            json);
    }

    public static Material Load(
        string filePath)
    {
        string json =
            RuntimePlatform.Files.ReadAllText(
                filePath);

        MaterialFileData? data =
            JsonSerializer.Deserialize<MaterialFileData>(
                json,
                JsonOptions);

        if (data ==
            null)
        {
            throw new InvalidDataException(
                "Failed to deserialize material.");
        }

        Material material =
            new Material(
                string.IsNullOrWhiteSpace(
                    data.Name)
                    ? Path.GetFileNameWithoutExtension(
                        filePath)
                    : data.Name);

        material.BaseColor =
            new Vector4(
                data.BaseColor.X,
                data.BaseColor.Y,
                data.BaseColor.Z,
                data.BaseColor.W);

        material.TexturePath =
            string.IsNullOrWhiteSpace(
                data.TexturePath)
                ? null
                : data.TexturePath;

        material.TextureTiling = data.TextureTiling?.ToVector2() ?? Vector2.One;
        material.TextureOffset = data.TextureOffset?.ToVector2() ?? Vector2.Zero;

        if (Enum.TryParse(data.LightingMode, true, out MaterialLightingMode lightingMode))
            material.LightingMode = lightingMode;

        if (Enum.TryParse(data.SurfaceMode, true, out MaterialSurfaceMode surfaceMode))
            material.SurfaceMode = surfaceMode;

        material.AlphaCutoff = Math.Clamp(data.AlphaCutoff, 0.0f, 1.0f);
        material.DoubleSided = data.DoubleSided;
        material.ReceiveFog = data.ReceiveFog;

        return material;
    }

    private sealed class MaterialFileData
    {
        public string Name { get; set; } =
            "Material";

        public Vector4Data BaseColor { get; set; } =
            new();

        public string? TexturePath { get; set; }

        public Vector2Data? TextureTiling { get; set; }
        public Vector2Data? TextureOffset { get; set; }

        public string LightingMode { get; set; } = "Lit";
        public string SurfaceMode { get; set; } = "Opaque";
        public float AlphaCutoff { get; set; } = 0.5f;
        public bool DoubleSided { get; set; }
        public bool ReceiveFog { get; set; } = true;
    }

    private sealed class Vector2Data
    {
        public float X { get; set; }
        public float Y { get; set; }

        public static Vector2Data FromVector2(Vector2 value) =>
            new() { X = value.X, Y = value.Y };

        public Vector2 ToVector2() => new(X, Y);
    }

    private sealed class Vector4Data
    {
        public float X { get; set; } =
            0.72f;

        public float Y { get; set; } =
            0.74f;

        public float Z { get; set; } =
            0.78f;

        public float W { get; set; } =
            1.0f;
    }
}
