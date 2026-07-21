using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BlazorCompose.Compiler.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace BlazorCompose.Compiler.Tests;

/// <summary>Encapsulates the output of a single generator run for assertion in tests.</summary>
public sealed record GeneratorRunResult(
    GeneratorDriver Driver,
    Compilation OutputCompilation,
    ImmutableArray<GeneratedSourceResult> GeneratedSources,
    IReadOnlyDictionary<string, ImmutableArray<IncrementalGeneratorRunStep>> TrackedSteps,
    ImmutableArray<Diagnostic> Diagnostics);

/// <summary>
/// Hosts in-memory Roslyn compilations for generator and analyzer tests.
/// The driver is created with <see cref="GeneratorDriverOptions.TrackIncrementalGeneratorSteps"/>
/// enabled and the updated driver is returned so that callers can reuse it for incremental runs.
/// </summary>
public static class CompilationTestHost
{
    /// <summary>
    /// Parses <paramref name="source"/>, creates a test compilation, runs
    /// <see cref="BlazorComposeGenerator"/>, and returns the updated driver together with all
    /// generated sources, tracked incremental steps, the updated compilation, and generator diagnostics.
    /// </summary>
    public static GeneratorRunResult RunGenerator(string source)
    {
        var compilation = CreateCompilation(source);

        var driverOptions = new GeneratorDriverOptions(
            disabledOutputs: default,
            trackIncrementalGeneratorSteps: true);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new BlazorComposeGenerator().AsSourceGenerator() },
            driverOptions: driverOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var runResult = driver.GetRunResult();

        var generatedSources = runResult.Results
            .SelectMany(static r => r.GeneratedSources)
            .ToImmutableArray();

        IReadOnlyDictionary<string, ImmutableArray<IncrementalGeneratorRunStep>> trackedSteps =
            runResult.Results.Length > 0
                ? runResult.Results[0].TrackedSteps
                : ImmutableDictionary<string, ImmutableArray<IncrementalGeneratorRunStep>>.Empty;

        return new GeneratorRunResult(driver, outputCompilation, generatedSources, trackedSteps, diagnostics);
    }

    /// <summary>
    /// Parses <paramref name="source"/>, creates a test compilation, runs the analyzer
    /// <typeparamref name="T"/>, and returns the analyzer-owned diagnostics only.
    /// </summary>
    public static async Task<ImmutableArray<Diagnostic>> RunAnalyzerAsync<T>(string source)
        where T : DiagnosticAnalyzer, new()
    {
        var compilation = CreateCompilation(source);
        var analyzer = new T();
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14));

        return CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: new[] { syntaxTree },
            references: BuildMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static ImmutableArray<MetadataReference> BuildMetadataReferences()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var references = new List<MetadataReference>();

        void Add(string path)
        {
            if (!string.IsNullOrEmpty(path) && seen.Add(path) && File.Exists(path))
                references.Add(MetadataReference.CreateFromFile(path));
        }

        // BCL and shared-framework assemblies available to the host process
        foreach (var path in ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
                     .Split(Path.PathSeparator))
        {
            Add(path);
        }

        // BlazorCompose.Runtime (provides ComposeComponentBase, View, UI)
        Add(typeof(BlazorCompose.ComposeComponentBase).Assembly.Location);

        // Microsoft.AspNetCore.Components (provides ComponentBase, RenderTreeBuilder)
        Add(typeof(ComponentBase).Assembly.Location);

        return references.ToImmutableArray();
    }
}
