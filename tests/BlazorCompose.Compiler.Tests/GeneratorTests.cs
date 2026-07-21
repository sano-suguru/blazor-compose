using System.Globalization;
using System.Linq;
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

    private const string BlockBodySource = """
        using BlazorCompose;
        using static BlazorCompose.UI;

        public partial class Counter : ComposeComponentBase
        {
            protected override View Body
            {
                get { return Text("Count"); }
            }
        }
        """;

    private const string UnrecognizedChildSource = """
        using BlazorCompose;
        using static BlazorCompose.UI;

        public partial class Counter : ComposeComponentBase
        {
            private View GetView() => Text("foo");

            protected override View Body =>
                VStack(GetView());
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
        Assert.Contains("__builder.OpenElement(0, \"span\")", source);
        Assert.Contains("__builder.AddContent(1, \"Count\")", source);
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

    [Fact]
    public void BlockBodyDoesNotGenerateSourceAndProducesCS0534()
    {
        var result = CompilationTestHost.RunGenerator(BlockBodySource);

        Assert.Empty(result.GeneratedSources);
        Assert.Single(result.OutputCompilation.GetDiagnostics()
            .Where(static d => d.Id == "CS0534"));
    }

    [Fact]
    public void UnrecognizedChildDoesNotGenerateSourceAndProducesCS0534()
    {
        var result = CompilationTestHost.RunGenerator(UnrecognizedChildSource);

        Assert.Empty(result.GeneratedSources);
        Assert.Single(result.OutputCompilation.GetDiagnostics()
            .Where(static d => d.Id == "CS0534"));
    }

    // -----------------------------------------------------------------------
    // If emission
    // -----------------------------------------------------------------------

    private const string IfCounterSource = """
        using BlazorCompose;
        using static BlazorCompose.UI;

        public partial class IfCounter : ComposeComponentBase
        {
            private bool _visible = true;

            protected override View Body =>
                VStack(
                    If(_visible, () => Text("Yes"), () => Text("No")),
                    Text("Always"));
        }
        """;

    [Fact]
    public void IfWithBothBranchesGeneratesDisjointSequenceRangesAndStableFollowingSequence()
    {
        var result = CompilationTestHost.RunGenerator(IfCounterSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // VStack outer element at 0
        Assert.Contains("__builder.OpenElement(0, \"div\")", generated);

        // If region boundary at 1 (one literal sequence for the conditional region)
        Assert.Contains("__builder.OpenRegion(1)", generated);

        // then branch: Text("Yes") uses [2, 3]
        Assert.Contains("__builder.OpenElement(2, \"span\")", generated);
        Assert.Contains("__builder.AddContent(3, \"Yes\")", generated);

        // else branch: Text("No") uses [4, 5] — disjoint from then range [2, 3]
        Assert.Contains("__builder.OpenElement(4, \"span\")", generated);
        Assert.Contains("__builder.AddContent(5, \"No\")", generated);

        // Region close (no sequence argument)
        Assert.Contains("__builder.CloseRegion()", generated);

        // Text("Always") starts at 6 — same sequence regardless of which branch ran
        Assert.Contains("__builder.OpenElement(6, \"span\")", generated);
        Assert.Contains("__builder.AddContent(7, \"Always\")", generated);

        // All sequence numbers are compile-time literals; no runtime ++ variable
        Assert.DoesNotContain("__seq", generated);
        Assert.DoesNotContain("seqVar", generated);
    }

    private const string IfOnlyThenSource = """
        using BlazorCompose;
        using static BlazorCompose.UI;

        public partial class IfThen : ComposeComponentBase
        {
            private bool _show = true;

            protected override View Body =>
                If(_show, () => Text("Visible"), null);
        }
        """;

    [Fact]
    public void IfWithNullOtherwiseGeneratesThenBranchOnly()
    {
        var result = CompilationTestHost.RunGenerator(IfOnlyThenSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("__builder.OpenRegion(0)", generated);
        Assert.Contains("__builder.OpenElement(1, \"span\")", generated);
        Assert.Contains("__builder.AddContent(2, \"Visible\")", generated);
        Assert.Contains("__builder.CloseRegion()", generated);

        // No else branch
        Assert.DoesNotContain("else", generated);
    }
}
