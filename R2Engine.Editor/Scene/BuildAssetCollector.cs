using System.Text.Json;
using System.Text.Json.Serialization;

namespace R2Engine.Editor.Scene;

public sealed record BuildAssetEntry(string Path, string Category, long Bytes);

public sealed class BuildAssetCollection
{
    public List<BuildAssetEntry> Assets { get; } = new();
    public List<string> MissingReferences { get; } = new();
    public long TotalBytes => Assets.Sum(asset => asset.Bytes);
}

public static class BuildAssetCollector
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static BuildAssetCollection Collect(IEnumerable<string> scenePaths)
    {
        BuildAssetCollection result = new();
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        string loadingPrefab = LoadingScreen.ConfiguredPath();
        if (!string.IsNullOrWhiteSpace(loadingPrefab))
        {
            AddReference(loadingPrefab, "Prefabs", result, visited);
            scenePaths = scenePaths.Append(AssetDatabase.ToAbsolutePath(loadingPrefab));
        }
        foreach (string scenePath in scenePaths)
        {
            try
            {
                Scene scene = SceneSerializer.Deserialize(File.ReadAllText(scenePath));
                CollectScene(scene, result, visited);
            }
            catch (Exception exception)
            {
                result.MissingReferences.Add(
                    $"Could not inspect scene {Path.GetFileName(scenePath)}: {exception.GetBaseException().Message}");
            }
        }
        result.Assets.Sort((left, right) => string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    public static void CopyToBuild(BuildAssetCollection collection, string buildDirectory)
    {
        string destinationAssets = Path.Combine(buildDirectory, "Assets");
        if (Directory.Exists(destinationAssets))
            Directory.Delete(destinationAssets, recursive: true);

        foreach (BuildAssetEntry entry in collection.Assets)
        {
            string source = AssetDatabase.ToAbsolutePath(entry.Path);
            string destination = Path.Combine(buildDirectory, entry.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);

            string metadata = source + ".r2import";
            if (File.Exists(metadata))
                File.Copy(metadata, destination + ".r2import", overwrite: true);
        }
    }

    public static string WriteManifest(BuildAssetCollection collection, string buildDirectory)
    {
        var categoryTotals = collection.Assets
            .GroupBy(asset => asset.Category, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new { Count = group.Count(), Bytes = group.Sum(asset => asset.Bytes) },
                StringComparer.OrdinalIgnoreCase);
        var manifest = new
        {
            Version = 1,
            TotalBytes = collection.TotalBytes,
            Categories = categoryTotals,
            Assets = collection.Assets
        };
        string dataRoot = Path.Combine(buildDirectory, "R2Data");
        Directory.CreateDirectory(dataRoot);
        string path = Path.Combine(dataRoot, "asset-manifest.json");
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOptions));
        return path;
    }

    private static void CollectScene(Scene scene, BuildAssetCollection result, HashSet<string> visited)
    {
        foreach (GameObject gameObject in scene.GameObjects)
        {
            AddReference(gameObject.PrefabPath, "Prefabs", result, visited);

            MeshRenderer? renderer = gameObject.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                AddReference(renderer.MeshPath, "Models", result, visited);
                bool hasMaterialAsset = false;
                for (int slot = 0; slot < renderer.MaterialSlotCount; slot++)
                {
                    string? materialPath = renderer.GetMaterialPath(slot);
                    if (string.IsNullOrWhiteSpace(materialPath))
                        continue;
                    hasMaterialAsset = true;
                    AddReference(materialPath, "Materials", result, visited);
                }
                if (!hasMaterialAsset)
                    AddReference(renderer.LocalMaterial.TexturePath, "Textures", result, visited);
            }

            AudioSource? audio = gameObject.GetComponent<AudioSource>();
            if (audio != null) AddReference(audio.ClipPath, "Audio", result, visited);
            if (gameObject.GetComponent<Canvas>() is { } canvas)
            {
                AddReference(canvas.NavigateSound, "Audio", result, visited);
                AddReference(canvas.SubmitSound, "Audio", result, visited);
                AddReference(canvas.CancelSound, "Audio", result, visited);
                AddReference(canvas.ErrorSound, "Audio", result, visited);
                AddReference(canvas.OpenSound, "Audio", result, visited);
            }
            if (gameObject.GetComponent<UIImage>() is { } image) AddReference(image.TexturePath, "Textures", result, visited);
            if (gameObject.GetComponent<UIText>() is { } text) AddReference(text.FontPath, "Fonts", result, visited);
            if (gameObject.GetComponent<UIButton>() is { } fontButton) AddReference(fontButton.FontPath, "Fonts", result, visited);
            if (gameObject.GetComponent<Interactable>() is { } interaction) AddReference(interaction.PromptFontPath, "Fonts", result, visited);
            if (gameObject.GetComponent<UIButton>() is { } button) { AddReference(button.NormalSprite, "Textures", result, visited); AddReference(button.HoverSprite, "Textures", result, visited); AddReference(button.PressedSprite, "Textures", result, visited); }

            foreach (ScriptComponent script in gameObject.Components.OfType<ScriptComponent>())
                AddReference(script.ScriptPath, "Scripts", result, visited);

            Animator? animator = gameObject.GetComponent<Animator>();
            if (animator != null)
            {
                AddReference(animator.ClipPath, "Animations", result, visited);
                AddReference(animator.SkeletalAnimationPath, "Animations", result, visited);
                AddReference(animator.ControllerPath, "Animator Controllers", result, visited);
            }
        }
    }

    private static void AddReference(
        string? assetPath,
        string category,
        BuildAssetCollection result,
        HashSet<string> visited)
    {
        if (string.IsNullOrWhiteSpace(assetPath)) return;
        string absolute;
        try
        {
            absolute = AssetDatabase.ToAbsolutePath(assetPath);
        }
        catch
        {
            result.MissingReferences.Add($"Invalid {category.ToLowerInvariant()} reference: {assetPath}");
            return;
        }

        if (!visited.Add(absolute)) return;
        if (!File.Exists(absolute))
        {
            result.MissingReferences.Add($"Missing {category.ToLowerInvariant()}: {assetPath}");
            return;
        }

        string relative = AssetDatabase.ToProjectRelativePath(absolute);
        if (relative.StartsWith("..", StringComparison.Ordinal))
        {
            result.MissingReferences.Add($"External asset is not buildable: {assetPath}");
            return;
        }
        result.Assets.Add(new BuildAssetEntry(relative.Replace('\\', '/'), category, new FileInfo(absolute).Length));

        string extension = Path.GetExtension(absolute).ToLowerInvariant();
        try
        {
            if (extension == ".r2mat")
            {
                Material material = MaterialSerializer.Load(absolute);
                AddReference(material.TexturePath, "Textures", result, visited);
            }
            else if (extension == ".r2controller")
            {
                AnimatorController controller = AnimatorController.Load(absolute);
                AddReference(controller.SkeletonPath, "Models", result, visited);
                foreach (AnimatorState state in controller.States)
                    AddReference(state.AnimationPath, "Animations", result, visited);
            }
            else if (extension == ".r2prefab")
            {
                Scene prefab = SceneSerializer.Deserialize(File.ReadAllText(absolute));
                CollectScene(prefab, result, visited);
            }
            else if (extension == ".r2font")
                AddReference(FontAsset.Load(absolute).AtlasPath, "Font Atlases", result, visited);
        }
        catch (Exception exception)
        {
            result.MissingReferences.Add(
                $"Could not inspect {relative}: {exception.GetBaseException().Message}");
        }
    }
}
