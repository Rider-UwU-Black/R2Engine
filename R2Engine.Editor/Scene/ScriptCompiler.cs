using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using R2Engine.Runtime.Platform;

namespace R2Engine.Editor.Scene;

public sealed class ScriptCompilationResult
{
    public Dictionary<string, Type> Types { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<string> Messages { get; } = new();
    public bool Success { get; internal set; }
}

public static class ScriptCompiler
{
    public static ScriptCompilationResult Compile(string scriptsRoot)
    {
        ScriptCompilationResult result = new();

        string[] files = Directory.Exists(scriptsRoot)
            ? Directory.GetFiles(scriptsRoot, "*.cs", SearchOption.AllDirectories)
            : Array.Empty<string>();

        if (files.Length == 0)
        {
            result.Success = true;
            result.Messages.Add("No C# scripts found in Assets/Scripts.");
            return result;
        }

        List<SyntaxTree> syntaxTrees = files
            .Select(file => CSharpSyntaxTree.ParseText(RuntimePlatform.Files.ReadAllText(file), path: file))
            .ToList();

        string[] trustedAssemblies =
            ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        List<MetadataReference> references = trustedAssemblies
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();

        references.Add(
            MetadataReference.CreateFromFile(
                typeof(ScriptBehaviour).Assembly.Location));

        // Scene/runtime types now live in R2Engine.Runtime, while a few engine
        // components are still migrating from the editor assembly. Keep both
        // available to user scripts throughout that transition.
        string editorAssemblyPath = typeof(ScriptCompiler).Assembly.Location;
        if (!string.Equals(editorAssemblyPath, typeof(ScriptBehaviour).Assembly.Location, StringComparison.OrdinalIgnoreCase))
            references.Add(MetadataReference.CreateFromFile(editorAssemblyPath));

        CSharpCompilation compilation = CSharpCompilation.Create(
            $"R2Engine.GameScripts.{Guid.NewGuid():N}",
            syntaxTrees,
            references.DistinctBy(reference => reference.Display),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        using MemoryStream assemblyStream = new();
        var emitResult = compilation.Emit(assemblyStream);

        foreach (Diagnostic diagnostic in emitResult.Diagnostics
                     .Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning))
        {
            result.Messages.Add(diagnostic.ToString());
        }

        if (!emitResult.Success)
            return result;

        assemblyStream.Position = 0;
        Assembly assembly = Assembly.Load(assemblyStream.ToArray());

        foreach (Type type in assembly.GetTypes()
                     .Where(type => !type.IsAbstract && typeof(ScriptBehaviour).IsAssignableFrom(type)))
        {
            result.Types[type.Name] = type;
            result.Types[type.FullName ?? type.Name] = type;
        }

        result.Success = true;
        result.Messages.Insert(0, $"Compiled {files.Length} script file(s) successfully.");
        return result;
    }
}
