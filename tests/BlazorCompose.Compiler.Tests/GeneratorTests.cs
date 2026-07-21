using System.Globalization;
using Microsoft.CodeAnalysis;

namespace BlazorCompose.Compiler.Tests;

public sealed class GeneratorTests
{
    // The canonical counter component used to verify generator output.
    private const string PartialCounterSource = """
        using BlazorCompose;
        using static BlazorCompose.UI;

        public partial class Counter : ComposeComponentBase
        {
            protected override View Body => Text("Count");
        }
        """;

    private const string NestedPartialCounterSource = """
        using BlazorCompose;
        using static BlazorCompose.UI;

        public partial class Outer
        {
            public partial class Counter : ComposeComponentBase
            {
                protected override View Body => Text("Count");
            }
        }
        """;

    [Fact]
    public void PartialComponentGeneratesRenderBody()
    {
        var result = CompilationTestHost.RunGenerator(PartialCounterSource);

        var source = Assert.Single(result.GeneratedSources).SourceText.ToString();
        Assert.Contains(
            "protected override void RenderBody(global::Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder __builder)",
            source);
    }

    [Fact]
    public void NestedPartialComponentDoesNotGenerateSourceAndProducesCS0534()
    {
        var result = CompilationTestHost.RunGenerator(NestedPartialCounterSource);

        Assert.Empty(result.GeneratedSources);

        var diagnostic = Assert.Single(
            result.OutputCompilation.GetDiagnostics().Where(static diagnostic => diagnostic.Id == "CS0534"));

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Outer.Counter", diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }
}
