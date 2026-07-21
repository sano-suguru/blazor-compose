using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace BlazorCompose.Compiler.Tests;

/// <summary>
/// Validates incremental generator caching behavior: when only one of two components
/// changes across driver re-runs, the unchanged component model must be Cached/Unchanged
/// while the changed component must be recomputed.
/// </summary>
public sealed class IncrementalGeneratorTests
{
    private const string ComponentASource = """
        using BlazorCompose;
        using static BlazorCompose.UI;

        namespace TestNs;

        public partial class ComponentA : ComposeComponentBase
        {
            protected override View Body => Text("Hello A");
        }
        """;

    private const string ComponentBSource = """
        using BlazorCompose;
        using static BlazorCompose.UI;

        namespace TestNs;

        public partial class ComponentB : ComposeComponentBase
        {
            protected override View Body => Text("Hello B");
        }
        """;

    private const string ComponentBModifiedSource = """
        using BlazorCompose;
        using static BlazorCompose.UI;

        namespace TestNs;

        public partial class ComponentB : ComposeComponentBase
        {
            protected override View Body => Text("Modified B");
        }
        """;

    /// <summary>
    /// Proves that when the same driver is reused and only one syntax tree changes,
    /// the pipeline caches the unchanged component model and recomputes the changed one.
    /// </summary>
    [Fact]
    public void UnchangedComponentIsCachedWhenOnlyOtherTreeChanges()
    {
        // Arrange: two components in separate syntax trees
        var treeA = CSharpSyntaxTree.ParseText(
            ComponentASource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
            path: "ComponentA.cs");

        var treeB = CSharpSyntaxTree.ParseText(
            ComponentBSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
            path: "ComponentB.cs");

        var compilation = CreateCompilation(treeA, treeB);

        var driverOptions = new GeneratorDriverOptions(
            disabledOutputs: default,
            trackIncrementalGeneratorSteps: true);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new BlazorComposeGenerator().AsSourceGenerator()],
            driverOptions: driverOptions);

        // Run 1: initial full run
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var run1 = driver.GetRunResult();
        Assert.Equal(2, run1.Results[0].GeneratedSources.Length);

        // Act: replace only tree B with a modified version
        var treeBModified = CSharpSyntaxTree.ParseText(
            ComponentBModifiedSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
            path: "ComponentB.cs");

        var compilation2 = compilation.ReplaceSyntaxTree(treeB, treeBModified);

        // Run 2: reuse the same driver returned from Run 1
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation2, out _, out _);
        var run2 = driver.GetRunResult();

        // Assert: the pipeline must have tracked steps
        var trackedSteps = run2.Results[0].TrackedSteps;
        Assert.True(trackedSteps.ContainsKey("ComponentModeling"),
            "Expected tracked step 'ComponentModeling' but found: " +
            string.Join(", ", trackedSteps.Keys));

        // The ComponentModeling step should have two outputs (one per component)
        var modelingSteps = trackedSteps["ComponentModeling"];
        var allOutputs = modelingSteps.SelectMany(s => s.Outputs).ToImmutableArray();
        Assert.Equal(2, allOutputs.Length);

        // ComponentA is unchanged → its model should be Cached or Unchanged
        // ComponentB was modified → its model should be Modified or New
        var cachedOrUnchanged = allOutputs
            .Where(o => o.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged)
            .ToImmutableArray();
        var modifiedOrNew = allOutputs
            .Where(o => o.Reason is IncrementalStepRunReason.Modified or IncrementalStepRunReason.New)
            .ToImmutableArray();

        Assert.True(cachedOrUnchanged.Length >= 1,
            $"Expected at least one Cached/Unchanged output for the unchanged component, " +
            $"but got reasons: [{string.Join(", ", allOutputs.Select(o => o.Reason))}]");
        Assert.True(modifiedOrNew.Length >= 1,
            $"Expected at least one Modified/New output for the changed component, " +
            $"but got reasons: [{string.Join(", ", allOutputs.Select(o => o.Reason))}]");
    }

    /// <summary>
    /// Proves that when nothing changes between runs, all component models are cached.
    /// </summary>
    [Fact]
    public void AllComponentsCachedWhenNothingChanges()
    {
        var treeA = CSharpSyntaxTree.ParseText(
            ComponentASource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
            path: "ComponentA.cs");

        var treeB = CSharpSyntaxTree.ParseText(
            ComponentBSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
            path: "ComponentB.cs");

        var compilation = CreateCompilation(treeA, treeB);

        var driverOptions = new GeneratorDriverOptions(
            disabledOutputs: default,
            trackIncrementalGeneratorSteps: true);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new BlazorComposeGenerator().AsSourceGenerator()],
            driverOptions: driverOptions);

        // Run 1
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        // Run 2 with the same compilation (no changes)
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var run2 = driver.GetRunResult();

        var trackedSteps = run2.Results[0].TrackedSteps;
        Assert.True(trackedSteps.ContainsKey("ComponentModeling"),
            "Expected tracked step 'ComponentModeling'");

        var modelingSteps = trackedSteps["ComponentModeling"];
        var allOutputs = modelingSteps.SelectMany(s => s.Outputs).ToImmutableArray();

        // Both should be cached/unchanged when nothing changed
        Assert.All(allOutputs, output =>
            Assert.True(
                output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                $"Expected Cached or Unchanged but got {output.Reason}"));
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static CSharpCompilation CreateCompilation(params SyntaxTree[] trees)
    {
        var references = BuildMetadataReferences();
        return CSharpCompilation.Create(
            assemblyName: "IncrementalTestAssembly",
            syntaxTrees: trees,
            references: references,
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

        foreach (var path in ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
                     .Split(Path.PathSeparator))
        {
            Add(path);
        }

        Add(typeof(BlazorCompose.ComposeComponentBase).Assembly.Location);
        Add(typeof(Microsoft.AspNetCore.Components.ComponentBase).Assembly.Location);

        return references.ToImmutableArray();
    }
}
