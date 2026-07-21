using System.Globalization;
using BlazorCompose.Compiler.Diagnostics;
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

    private const string VStackCounterSource = """
        using BlazorCompose;
        using static BlazorCompose.UI;

        public partial class Counter : ComposeComponentBase
        {
            private int _count = 0;

            protected override View Body =>
                VStack(
                    Text($"Count: {_count}"),
                    Button("Increment", () => _count++));
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
    public void VStackWithTextAndButtonGeneratesLinearSscRenderBody()
    {
        var result = CompilationTestHost.RunGenerator(VStackCounterSource);

        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // Sequence-literal builder calls in preorder
        Assert.Contains("__builder.OpenElement(0, \"div\")", generated);
        Assert.Contains("__builder.OpenElement(1, \"span\")", generated);
        Assert.Contains("__builder.AddContent(2, $\"Count: {_count}\")", generated);
        Assert.Contains("__builder.OpenElement(3, \"button\")", generated);
        Assert.Contains(
            "__builder.AddAttribute(4, \"onclick\", global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => _count++))",
            generated);
        Assert.Contains("__builder.AddContent(5, \"Increment\")", generated);

        // No runtime Body access, no runtime sequence variable
        Assert.DoesNotContain(".Body", generated);
        Assert.DoesNotContain("get_Body", generated);
    }

    [Fact]
    public async Task NestedPartialComponentDoesNotGenerateSourceAndProducesCS0534()
    {
        var result = CompilationTestHost.RunGenerator(NestedPartialCounterSource);

        Assert.Empty(result.GeneratedSources);

        var analyzerDiagnostics = await CompilationTestHost.RunAnalyzerAsync<PartialComponentAnalyzer>(NestedPartialCounterSource);
        Assert.DoesNotContain(analyzerDiagnostics, static diagnostic => diagnostic.Id == "BC1001");

        var diagnostic = Assert.Single(
            result.OutputCompilation.GetDiagnostics().Where(static diagnostic => diagnostic.Id == "CS0534"));

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Outer.Counter", diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }
}
