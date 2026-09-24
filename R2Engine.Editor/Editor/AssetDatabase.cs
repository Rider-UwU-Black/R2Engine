using R2Engine.Editor.Scene;
using System.Text.Json;

namespace R2Engine.Editor;

public static class AssetDatabase
{
    // =========================================================
    // Project Root
    // =========================================================
    //
    // Resolve the actual R2Engine.Editor project folder instead
    // of trusting the process working directory.
    //
    // This keeps Assets pointing at the real project even if a
    // native file dialog or launch environment changes ".".
    // =========================================================

    private static string? _projectRoot;

    public static string ProjectRoot =>
        _projectRoot ??= FindProjectRoot();

    public static void ConfigureProjectRoot(string path)
    {
        string root = Path.GetFullPath(path);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Project folder does not exist: {root}");

        _projectRoot = root;
    }

    public static string AssetsRoot =>
        Path.Combine(
            ProjectRoot,
            "Assets");

    public static string ModelsDirectory =>
        Path.Combine(
            AssetsRoot,
            "Models");

    public static string HumanoidAnimationsDirectory =>
        Path.Combine(ModelsDirectory, "Humanoid Animations");

    public static string TexturesDirectory =>
        Path.Combine(
            AssetsRoot,
            "Textures");

    public static string MaterialsDirectory =>
        Path.Combine(
            AssetsRoot,
            "Materials");

    public static string PrefabsDirectory =>
        Path.Combine(
            AssetsRoot,
            "Prefabs");

    public static string AudioDirectory =>
        Path.Combine(AssetsRoot, "Audio");

    public static string AnimationsDirectory =>
        Path.Combine(AssetsRoot, "Animations");

    public static string FontsDirectory => Path.Combine(AssetsRoot, "Fonts");

    // =========================================================
    // Initialize
    // =========================================================

    public static void Initialize()
    {
        // Only Assets is structural. All folders beneath it belong to the user;
        // importers create a default destination only when one is actually needed.
        Directory.CreateDirectory(AssetsRoot);
    }

    // =========================================================
    // Import Texture
    // =========================================================

    public static string ImportTexture(
        string sourcePath, string? destinationDirectory = null)
    {
        return ImportFile(
            sourcePath,
            destinationDirectory ?? TexturesDirectory,
            trackSource: true);
    }

    // =========================================================
    // Import Model
    // =========================================================

    public static string ImportModel(
        string sourcePath, string? destinationDirectory = null)
    {
        string extension =
            Path.GetExtension(
                sourcePath)
            .ToLowerInvariant();

        if (extension == ".obj")
        {
            return ImportFile(
                sourcePath,
                destinationDirectory ?? ModelsDirectory,
                trackSource: true);
        }

        if (extension is ".fbx" or ".glb" or ".gltf")
        {
            Initialize();
            string sourceFullPath = Path.GetFullPath(sourcePath);
            if (!File.Exists(sourceFullPath))
                throw new FileNotFoundException("Model file does not exist.", sourceFullPath);
            string targetDirectory = destinationDirectory ?? ModelsDirectory;
            Directory.CreateDirectory(targetDirectory);
            string sourceAsset = GetUniqueDestinationPath(targetDirectory, Path.GetFileName(sourceFullPath));
            File.Copy(sourceFullPath, sourceAsset, false);
            return ApplyFbxImporter(sourceAsset);
        }

        throw new NotSupportedException(
            "R2Engine supports OBJ static models and FBX, GLB, or glTF skeletal models.");
    }

    public static string ImportAudio(string sourcePath, string? destinationDirectory = null)
    {
        Initialize();
        string source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source)) throw new FileNotFoundException("Audio source file does not exist.", source);
        string targetDirectory = destinationDirectory ?? AudioDirectory;
        Directory.CreateDirectory(targetDirectory);
        string destination = GetUniqueDestinationPath(targetDirectory,
            Path.GetFileNameWithoutExtension(source) + ".wav");
        AudioImportSettings settings = new();
        WavAssetImporter.ConvertToRuntimeWav(source, destination, settings.BitDepth, settings.MaximumSampleRate);
        SaveFileImportMetadata(destination, source);
        SaveAudioImportSettings(destination, settings);
        return ToProjectRelativePath(destination);
    }

    public static string ImportScript(string sourcePath, string? destinationDirectory = null) =>
        ImportFile(sourcePath, destinationDirectory ?? Path.Combine(AssetsRoot, "Scripts"));

    public static FbxImporterSettings LoadFbxImporterSettings(string sourceAssetPath)
    {
        string source = ToAbsolutePath(sourceAssetPath);
        string sidecar = source + ".r2fbximport";
        if (!File.Exists(sidecar)) return new FbxImporterSettings();
        return JsonSerializer.Deserialize<FbxImporterSettings>(File.ReadAllText(sidecar),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new FbxImporterSettings();
    }

    public static void SaveFbxImporterSettings(string sourceAssetPath, FbxImporterSettings settings)
    {
        string source = ToAbsolutePath(sourceAssetPath);
        File.WriteAllText(source + ".r2fbximport", JsonSerializer.Serialize(settings,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    public static string ApplyFbxImporter(string sourceAssetPath)
    {
        string source = ToAbsolutePath(sourceAssetPath);
        if (!File.Exists(source)) throw new FileNotFoundException("FBX source asset is missing.", source);
        string extension = Path.GetExtension(source).ToLowerInvariant();
        if (extension is not (".fbx" or ".glb" or ".gltf"))
            throw new InvalidDataException("The model importer requires FBX, GLB, or glTF source data.");

        FbxImporterSettings importer = LoadFbxImporterSettings(source);
        string directory = Path.GetDirectoryName(source)!;
        string baseName = Path.GetFileNameWithoutExtension(source);
        if (importer.ImportModel)
        {
            string modelPath = string.IsNullOrWhiteSpace(importer.GeneratedModelPath)
                ? GetUniqueDestinationPath(directory, baseName + ".r2skel")
                : ToAbsolutePath(importer.GeneratedModelPath);
            SkeletalAsset? previous = File.Exists(modelPath) ? SkeletalAsset.Load(modelPath) : null;
            ModelImportSettings modelSettings = new()
            {
                SourcePath = source,
                Scale = Math.Max(0.0001f, importer.Scale),
                Rotation = importer.Rotation,
                AutoCorrectOrientation = importer.AutoCorrectOrientation,
                SkeletonLimit = Math.Max(1, importer.SkeletonLimit)
            };
            SkeletalAsset model = SkeletalAssetImporter.Import(source, modelSettings);
            if (previous != null)
                foreach ((string bone, string role) in previous.BoneMappings)
                    if (model.Bones.Any(item => item.Name == bone)) model.BoneMappings[bone] = role;
            model.Save(modelPath);
            importer.GeneratedModelPath = ToProjectRelativePath(modelPath);
        }

        if (importer.ImportAnimations)
        {
            Directory.CreateDirectory(HumanoidAnimationsDirectory);
            string animationPath = string.IsNullOrWhiteSpace(importer.GeneratedAnimationPath)
                ? GetUniqueDestinationPath(HumanoidAnimationsDirectory, baseName + ".r2sanim")
                : ToAbsolutePath(importer.GeneratedAnimationPath);
            try
            {
                HumanoidAnimationAsset? previous = File.Exists(animationPath)
                    ? HumanoidAnimationAsset.Load(animationPath) : null;
                HumanoidAnimationAsset animation = SkeletalAssetImporter.ImportAnimation(source,
                    new AnimationImportSettings { SourcePath = source,
                        SpeedScale = Math.Max(0.01f, importer.AnimationSpeedScale) });
                if (previous != null)
                {
                    foreach ((string bone, string role) in previous.BoneMappings)
                        if (animation.BoneNames.Contains(bone)) animation.BoneMappings[bone] = role;
                    foreach (SkeletalAnimationClip clip in animation.Clips)
                    {
                        SkeletalAnimationClip? oldClip = previous.Clips.FirstOrDefault(item => item.Name == clip.Name);
                        if (oldClip != null)
                        {
                            clip.Events = oldClip.Events;
                            clip.ApplyRootMotion = oldClip.ApplyRootMotion;
                        }
                    }
                }
                animation.Save(animationPath);
                importer.GeneratedAnimationPath = ToProjectRelativePath(animationPath);
            }
            catch (InvalidDataException exception) when (exception.Message.Contains("no animation", StringComparison.OrdinalIgnoreCase))
            {
                importer.GeneratedAnimationPath = "";
            }
        }
        importer.SourceLastWriteUtcTicks = File.GetLastWriteTimeUtc(source).Ticks;
        SaveFbxImporterSettings(source, importer);
        MeshAssetCache.Clear();
        return importer.GeneratedModelPath;
    }

    public static string ImportHumanoidAnimation(string sourcePath, string? destinationDirectory = null)
    {
        Initialize();
        string sourceFullPath = Path.GetFullPath(sourcePath);
        AnimationImportSettings settings = new() { SourcePath = sourceFullPath };
        HumanoidAnimationAsset animation = SkeletalAssetImporter.ImportAnimation(sourceFullPath, settings);
        string targetDirectory = destinationDirectory ?? HumanoidAnimationsDirectory;
        Directory.CreateDirectory(targetDirectory);
        string destination = GetUniqueDestinationPath(
            targetDirectory,
            Path.GetFileNameWithoutExtension(sourcePath) + ".r2sanim");
        animation.Save(destination);
        return ToProjectRelativePath(destination);
    }

    public static void ReimportSkeletalModel(string assetPath)
    {
        string absoluteAssetPath = ToAbsolutePath(assetPath);
        SkeletalAsset previous = SkeletalAsset.Load(absoluteAssetPath);
        ModelImportSettings settings = previous.ImportSettings ?? new ModelImportSettings();
        if (string.IsNullOrWhiteSpace(settings.SourcePath) || !File.Exists(settings.SourcePath))
            throw new FileNotFoundException("The original model source file could not be found.", settings.SourcePath);
        SkeletalAsset imported = SkeletalAssetImporter.Import(settings.SourcePath, settings);
        foreach ((string bone, string role) in previous.BoneMappings)
            if (imported.Bones.Any(item => item.Name == bone)) imported.BoneMappings[bone] = role;
        imported.Save(absoluteAssetPath);
        MeshAssetCache.Clear();
    }

    public static void ReimportHumanoidAnimation(string assetPath)
    {
        string absoluteAssetPath = ToAbsolutePath(assetPath);
        HumanoidAnimationAsset previous = HumanoidAnimationAsset.Load(absoluteAssetPath);
        AnimationImportSettings settings = previous.ImportSettings ?? new AnimationImportSettings();
        if (string.IsNullOrWhiteSpace(settings.SourcePath) || !File.Exists(settings.SourcePath))
            throw new FileNotFoundException("The original animation source file could not be found.", settings.SourcePath);
        HumanoidAnimationAsset imported = SkeletalAssetImporter.ImportAnimation(settings.SourcePath, settings);
        foreach ((string bone, string role) in previous.BoneMappings)
            if (imported.BoneNames.Contains(bone)) imported.BoneMappings[bone] = role;
        foreach (SkeletalAnimationClip clip in imported.Clips)
        {
            SkeletalAnimationClip? oldClip = previous.Clips.FirstOrDefault(item => item.Name == clip.Name);
            if (oldClip != null)
            {
                clip.Events = oldClip.Events;
                clip.ApplyRootMotion = oldClip.ApplyRootMotion;
            }
        }
        imported.Save(absoluteAssetPath);
    }

    // =========================================================
    // Generic Import
    // =========================================================

    private static string ImportFile(
        string sourcePath,
        string destinationDirectory,
        bool trackSource = false)
    {
        if (string.IsNullOrWhiteSpace(
                sourcePath))
        {
            throw new ArgumentException(
                "Asset path cannot be empty.",
                nameof(sourcePath));
        }

        Initialize();

        string sourceFullPath =
            Path.GetFullPath(
                sourcePath);

        if (!File.Exists(
                sourceFullPath))
        {
            throw new FileNotFoundException(
                "Asset file does not exist.",
                sourceFullPath);
        }

        // -----------------------------------------------------
        // Asset is already inside this project's Assets folder.
        // -----------------------------------------------------

        if (IsInsideAssets(
                sourceFullPath))
        {
            return ToProjectRelativePath(
                sourceFullPath);
        }

        // -----------------------------------------------------
        // Copy external asset into the requested asset folder.
        // -----------------------------------------------------

        Directory.CreateDirectory(
            destinationDirectory);

        string fileName =
            Path.GetFileName(
                sourceFullPath);

        string destination =
            GetUniqueDestinationPath(
                destinationDirectory,
                fileName);

        File.Copy(
            sourceFullPath,
            destination,
            overwrite: false);

        // Do not silently continue if the import copy somehow
        // failed or landed somewhere unexpected.
        if (!File.Exists(
                destination))
        {
            throw new IOException(
                $"Asset import copy was not created: {destination}");
        }

        if (trackSource)
            SaveFileImportMetadata(destination, sourceFullPath);

        return ToProjectRelativePath(
            destination);
    }

    public static FileImportMetadata? GetFileImportMetadata(string assetPath)
    {
        string metadataPath = ToAbsolutePath(assetPath) + ".r2import";
        if (!File.Exists(metadataPath)) return null;
        return JsonSerializer.Deserialize<FileImportMetadata>(File.ReadAllText(metadataPath));
    }

    public static void SaveFileImportMetadata(string assetPath, string sourcePath)
    {
        string sourceFullPath = Path.GetFullPath(sourcePath);
        FileImportMetadata metadata = GetFileImportMetadata(assetPath) ?? new FileImportMetadata();
        metadata.SourcePath = sourceFullPath;
        metadata.SourceLastWriteUtcTicks = File.Exists(sourceFullPath)
            ? File.GetLastWriteTimeUtc(sourceFullPath).Ticks
            : 0;
        File.WriteAllText(ToAbsolutePath(assetPath) + ".r2import", JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static TextureImportSettings GetTextureImportSettings(string assetPath) =>
        GetFileImportMetadata(assetPath)?.Texture ?? new TextureImportSettings();

    public static void SaveTextureImportSettings(string assetPath, TextureImportSettings settings)
    {
        string absolutePath = ToAbsolutePath(assetPath);
        FileImportMetadata metadata = GetFileImportMetadata(absolutePath) ?? new FileImportMetadata();
        metadata.Texture = settings;
        File.WriteAllText(absolutePath + ".r2import",
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static AudioImportSettings GetAudioImportSettings(string assetPath) =>
        GetFileImportMetadata(assetPath)?.Audio ?? new AudioImportSettings();

    public static void SaveAudioImportSettings(string assetPath, AudioImportSettings settings)
    {
        string absolutePath = ToAbsolutePath(assetPath);
        FileImportMetadata metadata = GetFileImportMetadata(absolutePath) ?? new FileImportMetadata();
        metadata.Audio = settings;
        File.WriteAllText(absolutePath + ".r2import",
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static bool HasChangedSource(string assetPath)
    {
        FileImportMetadata? metadata = GetFileImportMetadata(assetPath);
        return metadata != null && File.Exists(metadata.SourcePath) &&
               File.GetLastWriteTimeUtc(metadata.SourcePath).Ticks > metadata.SourceLastWriteUtcTicks;
    }

    public static void ReimportFileAsset(string assetPath)
    {
        string absoluteAssetPath = ToAbsolutePath(assetPath);
        FileImportMetadata metadata = GetFileImportMetadata(absoluteAssetPath)
            ?? throw new InvalidOperationException("This asset is not linked to an original source file.");
        if (!File.Exists(metadata.SourcePath))
            throw new FileNotFoundException("The original source file could not be found.", metadata.SourcePath);
        if(Path.GetExtension(absoluteAssetPath).Equals(".r2font",StringComparison.OrdinalIgnoreCase))
        { FontAssetImporter.Reimport(absoluteAssetPath,metadata.SourcePath); return; }
        if (Path.GetExtension(absoluteAssetPath).Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            AudioImportSettings settings = metadata.Audio ?? new AudioImportSettings();
            WavAssetImporter.ConvertToRuntimeWav(
                metadata.SourcePath, absoluteAssetPath, settings.BitDepth, settings.MaximumSampleRate);
        }
        else
            File.Copy(metadata.SourcePath, absoluteAssetPath, overwrite: true);
        SaveFileImportMetadata(absoluteAssetPath, metadata.SourcePath);
    }

    public static List<string> ReimportChangedFileAssets()
    {
        List<string> results = new();
        if (!Directory.Exists(AssetsRoot)) return results;
        foreach (string metadataPath in Directory.GetFiles(AssetsRoot, "*.r2import", SearchOption.AllDirectories))
        {
            string assetPath = metadataPath[..^".r2import".Length];
            try
            {
                if (!File.Exists(assetPath))
                {
                    results.Add($"Import metadata has no asset: {ToProjectRelativePath(assetPath)}");
                    continue;
                }
                FileImportMetadata? metadata = GetFileImportMetadata(assetPath);
                if (metadata == null) continue;
                if (string.IsNullOrWhiteSpace(metadata.SourcePath)) continue;
                if (!File.Exists(metadata.SourcePath))
                {
                    results.Add($"Linked source is missing for {Path.GetFileName(assetPath)}: {metadata.SourcePath}");
                    continue;
                }
                if (!HasChangedSource(assetPath)) continue;
                ReimportFileAsset(assetPath);
                results.Add($"Auto-reimported {Path.GetFileName(assetPath)}.");
            }
            catch (Exception exception)
            {
                results.Add($"Auto-reimport failed for {Path.GetFileName(assetPath)}: {exception.Message}");
            }
        }

        foreach (string assetPath in Directory.GetFiles(AssetsRoot, "*.r2skel", SearchOption.AllDirectories))
        {
            try
            {
                SkeletalAsset asset = SkeletalAsset.Load(assetPath);
                ModelImportSettings settings = asset.ImportSettings;
                if (!string.IsNullOrWhiteSpace(settings.SourcePath) && !File.Exists(settings.SourcePath))
                    results.Add($"Linked source is missing for {Path.GetFileName(assetPath)}: {settings.SourcePath}");
                else if (!string.IsNullOrWhiteSpace(settings.SourcePath) &&
                         File.GetLastWriteTimeUtc(settings.SourcePath).Ticks > settings.SourceLastWriteUtcTicks)
                {
                    ReimportSkeletalModel(assetPath);
                    results.Add($"Auto-reimported {Path.GetFileName(assetPath)}.");
                }
            }
            catch (Exception exception)
            {
                results.Add($"Auto-reimport check failed for {Path.GetFileName(assetPath)}: {exception.Message}");
            }
        }

        foreach (string assetPath in Directory.GetFiles(AssetsRoot, "*.r2sanim", SearchOption.AllDirectories))
        {
            try
            {
                HumanoidAnimationAsset asset = HumanoidAnimationAsset.Load(assetPath);
                AnimationImportSettings settings = asset.ImportSettings;
                if (!string.IsNullOrWhiteSpace(settings.SourcePath) && !File.Exists(settings.SourcePath))
                    results.Add($"Linked source is missing for {Path.GetFileName(assetPath)}: {settings.SourcePath}");
                else if (!string.IsNullOrWhiteSpace(settings.SourcePath) &&
                         File.GetLastWriteTimeUtc(settings.SourcePath).Ticks > settings.SourceLastWriteUtcTicks)
                {
                    ReimportHumanoidAnimation(assetPath);
                    results.Add($"Auto-reimported {Path.GetFileName(assetPath)}.");
                }
            }
            catch (Exception exception)
            {
                results.Add($"Auto-reimport check failed for {Path.GetFileName(assetPath)}: {exception.Message}");
            }
        }
        return results;
    }

    public static List<string> ValidateSceneReferences(Scene.Scene scene)
    {
        HashSet<string> messages = new(StringComparer.OrdinalIgnoreCase);
        void Check(string owner, string kind, string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                if (!File.Exists(ToAbsolutePath(path)))
                    messages.Add($"Missing {kind}: {owner} -> {path}");
            }
            catch (Exception exception)
            {
                messages.Add($"Invalid {kind}: {owner} -> {path} ({exception.Message})");
            }
        }

        foreach (GameObject gameObject in scene.GameObjects)
        {
            Check(gameObject.Name, "prefab reference", gameObject.PrefabPath);
            MeshRenderer? renderer = gameObject.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                Check(gameObject.Name, "mesh", renderer.MeshPath);
                for (int slot = 0; slot < renderer.MaterialSlotCount; slot++)
                {
                    string? materialPath = renderer.GetMaterialPath(slot);
                    Check(gameObject.Name, $"material slot {slot}", materialPath);
                    if (string.IsNullOrWhiteSpace(materialPath) || !File.Exists(ToAbsolutePath(materialPath)))
                        continue;
                    try
                    {
                        Material material = MaterialSerializer.Load(ToAbsolutePath(materialPath));
                        Check(Path.GetFileName(materialPath), "texture", material.TexturePath);
                    }
                    catch (Exception exception)
                    {
                        messages.Add($"Unreadable material: {materialPath} ({exception.Message})");
                    }
                }
            }

            Check(gameObject.Name, "script", gameObject.GetComponent<ScriptComponent>()?.ScriptPath);
            Check(gameObject.Name, "audio clip", gameObject.GetComponent<AudioSource>()?.ClipPath);
            Animator? animator = gameObject.GetComponent<Animator>();
            if (animator != null)
            {
                Check(gameObject.Name, "animation clip", animator.ClipPath);
                Check(gameObject.Name, "animator controller", animator.ControllerPath);
                Check(gameObject.Name, "skeletal animation", animator.SkeletalAnimationPath);
            }
        }

        return messages.OrderBy(message => message, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // =========================================================
    // Project Root Discovery
    // =========================================================

    private static string FindProjectRoot()
    {
        // Packaged games place game.json and Assets beside the executable.
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "game.json")) &&
            Directory.Exists(Path.Combine(AppContext.BaseDirectory, "Assets")))
        {
            return Path.GetFullPath(AppContext.BaseDirectory);
        }

        // First try the directory the editor was launched from.
        string? fromWorkingDirectory =
            FindProjectRootFrom(
                Directory.GetCurrentDirectory());

        if (fromWorkingDirectory !=
            null)
        {
            return fromWorkingDirectory;
        }

        // dotnet run places the executable under
        // bin/Debug/<framework>. Walking upward from the app
        // directory will still find R2Engine.Editor.csproj.
        string? fromApplicationDirectory =
            FindProjectRootFrom(
                AppContext.BaseDirectory);

        if (fromApplicationDirectory !=
            null)
        {
            return fromApplicationDirectory;
        }

        throw new DirectoryNotFoundException(
            "Could not locate R2Engine.Editor.csproj. " +
            "R2Engine cannot determine the project root.");
    }

    private static string? FindProjectRootFrom(
        string startingPath)
    {
        DirectoryInfo? directory =
            new DirectoryInfo(
                Path.GetFullPath(
                    startingPath));

        while (directory !=
            null)
        {
            string projectFile =
                Path.Combine(
                    directory.FullName,
                    "R2Engine.Editor.csproj");

            if (File.Exists(
                    projectFile))
            {
                return directory.FullName;
            }

            directory =
                directory.Parent;
        }

        return null;
    }

    // =========================================================
    // Asset Path Helpers
    // =========================================================

    public static bool IsInsideAssets(
        string path)
    {
        string fullPath =
            Path.GetFullPath(
                path);

        string assetsPath =
            Path.GetFullPath(
                AssetsRoot);

        string relative =
            Path.GetRelativePath(
                assetsPath,
                fullPath);

        return
            relative != ".." &&
            !relative.StartsWith(
                ".." +
                Path.DirectorySeparatorChar,
                StringComparison.Ordinal) &&
            !Path.IsPathRooted(
                relative);
    }

    public static string ToProjectRelativePath(
        string path)
    {
        string fullPath =
            Path.GetFullPath(
                path);

        return Path.GetRelativePath(
            ProjectRoot,
            fullPath);
    }

    public static string ToAbsolutePath(
        string projectRelativePath)
    {
        if (Path.IsPathRooted(
                projectRelativePath))
        {
            return Path.GetFullPath(
                projectRelativePath);
        }

        return Path.GetFullPath(
            Path.Combine(
                ProjectRoot,
                projectRelativePath));
    }

    // =========================================================
    // Asset Browser
    // =========================================================

    public static IEnumerable<string> GetDirectories(
        string directory)
    {
        if (!Directory.Exists(
                directory))
        {
            return Array.Empty<string>();
        }

        return Directory
            .GetDirectories(
                directory)
            .OrderBy(
                path =>
                    Path.GetFileName(
                        path),
                StringComparer.OrdinalIgnoreCase);
    }

    public static IEnumerable<string> GetFiles(
        string directory)
    {
        if (!Directory.Exists(
                directory))
        {
            return Array.Empty<string>();
        }

        return Directory
            .GetFiles(
                directory)
            .Where(path => !path.EndsWith(".r2import", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".r2fbximport", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith(".font.png", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(path[..^".font.png".Length] + ".r2font"))
            .OrderBy(
                path =>
                    Path.GetFileName(
                        path),
                StringComparer.OrdinalIgnoreCase);
    }

    public sealed class FileImportMetadata
    {
        public string SourcePath { get; set; } = "";
        public long SourceLastWriteUtcTicks { get; set; }
        public TextureImportSettings? Texture { get; set; }
        public AudioImportSettings? Audio { get; set; }
    }

    public static string GetAssetTypeLabel(
        string path)
    {
        if (Directory.Exists(
                path))
        {
            return "Folder";
        }

        string extension =
            Path.GetExtension(
                path)
            .ToLowerInvariant();

        return extension switch
        {
            ".png" =>
                "Texture",

            ".jpg" =>
                "Texture",

            ".jpeg" =>
                "Texture",

            ".bmp" =>
                "Texture",

            ".tga" =>
                "Texture",

            ".r2scene" =>
                "Scene",

            ".r2mat" =>
                "Material",

            ".r2prefab" =>
                "Prefab",

            ".obj" =>
                "Model",

            ".cs" =>
                "Script",

            ".fbx" =>
                "Model",

            ".gltf" =>
                "Model",

            ".glb" =>
                "Model",

            ".r2skel" =>
                "Skeletal Model",

            ".r2controller" =>
                "Animator Controller",

            ".r2sanim" =>
                "Humanoid Animation",

            ".wav" =>
                "Audio",

            ".r2font" =>
                "Font",

            ".ttf" or ".otf" =>
                "Font Source",

            _ =>
                "File"
        };
    }

    // =========================================================
    // Material Asset Paths
    // =========================================================

    public static string GetUniqueMaterialAssetPath(
        string baseName)
    {
        Initialize();

        string safeName =
            string.IsNullOrWhiteSpace(
                baseName)
                ? "New Material"
                : baseName.Trim();

        foreach (
            char invalid
            in Path.GetInvalidFileNameChars())
        {
            safeName =
                safeName.Replace(
                    invalid,
                    '_');
        }

        Directory.CreateDirectory(MaterialsDirectory);
        string destination =
            GetUniqueDestinationPath(
                MaterialsDirectory,
                $"{safeName}.r2mat");

        return ToProjectRelativePath(
            destination);
    }

    // =========================================================
    // Unique Import Names
    // =========================================================

    private static string GetUniqueDestinationPath(
        string directory,
        string fileName)
    {
        string candidate =
            Path.Combine(
                directory,
                fileName);

        if (!File.Exists(
                candidate))
        {
            return candidate;
        }

        string name =
            Path.GetFileNameWithoutExtension(
                fileName);

        string extension =
            Path.GetExtension(
                fileName);

        int number =
            1;

        while (true)
        {
            candidate =
                Path.Combine(
                    directory,
                    $"{name} ({number}){extension}");

            if (!File.Exists(
                    candidate))
            {
                return candidate;
            }

            number++;
        }
    }
}
