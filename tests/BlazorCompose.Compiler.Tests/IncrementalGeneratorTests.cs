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
    /// Identifies each component by its <see cref="ComponentModel"/> value.
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

        // Identify each output by its ComponentModel value
        var componentA = allOutputs.SingleOrDefault(o =>
            o.Value is ComponentModel m && m.ClassName == "ComponentA");
        var componentB = allOutputs.SingleOrDefault(o =>
            o.Value is ComponentModel m && m.ClassName == "ComponentB");

        Assert.True(componentA.Value is not null,
            "Expected ComponentA model in tracked outputs");
        Assert.True(componentB.Value is not null,
            "Expected ComponentB model in tracked outputs");

        // ComponentA is unchanged → Cached or Unchanged
        Assert.True(
            componentA.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
            $"Expected ComponentA to be Cached/Unchanged but got {componentA.Reason}");

        // ComponentB was modified → Modified or New
        Assert.True(
            componentB.Reason is IncrementalStepRunReason.Modified or IncrementalStepRunReason.New,
            $"Expected ComponentB to be Modified/New but got {componentB.Reason}");
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

    /// <summary>
    /// Regression test: changing the <c>BlazorCompose.UI</c> API signature between reused-driver
    /// runs must invalidate all component models (not incorrectly cache them).  This validates
    /// that <c>KnownSymbols</c> equality is based on a semantic signature fingerprint, not mere
    /// symbol presence.
    /// </summary>
    [Fact]
    public void ChangedUIApiSignatureInvalidatesAllComponentModels()
    {
        // Source-defined UI type so we can mutate its signature between runs.
        const string runtimeSourceV1 = """
            namespace BlazorCompose
            {
                public struct View { }
                public abstract class ComposeComponentBase : Microsoft.AspNetCore.Components.ComponentBase
                {
                    protected abstract View Body { get; }
                }
                public static class UI
                {
                    public static View Text(string content) => default;
                }
            }
            """;

        const string runtimeSourceV2 = """
            namespace BlazorCompose
            {
                public struct View { }
                public abstract class ComposeComponentBase : Microsoft.AspNetCore.Components.ComponentBase
                {
                    protected abstract View Body { get; }
                }
                public static class UI
                {
                    public static View Text(string content, string style) => default;
                }
            }
            """;

        const string componentSource = """
            using BlazorCompose;
            using static BlazorCompose.UI;

            namespace TestNs;

            public partial class MyComponent : ComposeComponentBase
            {
                protected override View Body => Text("Hello");
            }
            """;

        var runtimeTreeV1 = CSharpSyntaxTree.ParseText(
            runtimeSourceV1,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
            path: "Runtime.cs");

        var componentTree = CSharpSyntaxTree.ParseText(
            componentSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
            path: "MyComponent.cs");

        // Build compilation WITHOUT the real BlazorCompose.Runtime assembly reference—
        // use our in-source definitions instead.
        var compilation1 = CreateCompilationWithoutRuntime(runtimeTreeV1, componentTree);

        var driverOptions = new GeneratorDriverOptions(
            disabledOutputs: default,
            trackIncrementalGeneratorSteps: true);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new BlazorComposeGenerator().AsSourceGenerator()],
            driverOptions: driverOptions);

        // Run 1: Text(string) matches — component is generated
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation1, out _, out _);
        var run1 = driver.GetRunResult();
        Assert.Single(run1.Results[0].GeneratedSources);

        // Act: replace the runtime tree with V2 (Text now takes two parameters)
        var runtimeTreeV2 = CSharpSyntaxTree.ParseText(
            runtimeSourceV2,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
            path: "Runtime.cs");

        var compilation2 = compilation1.ReplaceSyntaxTree(runtimeTreeV1, runtimeTreeV2);

        // Run 2: reuse driver with changed UI API
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation2, out _, out _);
        var run2 = driver.GetRunResult();

        // The KnownSymbols fingerprint must have changed, so the downstream pipeline must
        // NOT incorrectly cache the old component model.
        var trackedSteps = run2.Results[0].TrackedSteps;

        // Verify KnownSymbols was recomputed (Modified or New)
        Assert.True(trackedSteps.ContainsKey("KnownSymbols"),
            "Expected tracked step 'KnownSymbols'");
        var knownSymbolsOutputs = trackedSteps["KnownSymbols"]
            .SelectMany(s => s.Outputs).ToImmutableArray();
        Assert.All(knownSymbolsOutputs, output =>
            Assert.True(
                output.Reason is IncrementalStepRunReason.Modified or IncrementalStepRunReason.New,
                $"Expected KnownSymbols Modified/New but got {output.Reason}"));

        // The component model must NOT be generated in Run 2 because Text(string) no longer
        // matches (it now requires 2 params). If ComponentModeling still has outputs, they
        // must all be Modified/New (recomputed), not Cached/Unchanged.
        if (trackedSteps.TryGetValue("ComponentModeling", out var modelingSteps))
        {
            var modelOutputs = modelingSteps.SelectMany(s => s.Outputs).ToImmutableArray();
            Assert.All(modelOutputs, output =>
                Assert.True(
                    output.Reason is not (IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged),
                    $"ComponentModel was incorrectly cached after UI API signature change (reason: {output.Reason})"));
        }

        // The second run should produce NO generated sources because Text(string) no longer
        // resolves to a known single-param method.
        Assert.Empty(run2.Results[0].GeneratedSources);
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

    private static CSharpCompilation CreateCompilationWithoutRuntime(params SyntaxTree[] trees)
    {
        var references = BuildMetadataReferencesWithoutRuntime();
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

    private static ImmutableArray<MetadataReference> BuildMetadataReferencesWithoutRuntime()
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

        // Only ComponentBase — NOT the BlazorCompose.Runtime assembly since we define it in-source.
        Add(typeof(Microsoft.AspNetCore.Components.ComponentBase).Assembly.Location);

        return references.ToImmutableArray();
    }
}
