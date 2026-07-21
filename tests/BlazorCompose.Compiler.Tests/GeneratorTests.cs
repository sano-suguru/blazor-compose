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

    // -----------------------------------------------------------------------
    // Static composable call-site expansion
    // -----------------------------------------------------------------------

    [Fact]
    public void StaticComposableExpandsWithoutRuntimeMethodCall()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.UI;

            public partial class Counter : ComposeComponentBase
            {
                [Composable]
                private static View Label(string value) => Text(value);

                private string Compute() => "Count";

                protected override View Body =>
                    VStack(Label(Compute()), Text("After"));
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("string __bc_arg_1_0 = Compute();", generated);
        Assert.Contains("__builder.OpenElement(1, \"span\")", generated);
        Assert.Contains("__builder.AddContent(2, __bc_arg_1_0)", generated);
        Assert.Contains("__builder.OpenElement(3, \"span\")", generated);
        Assert.DoesNotContain("Label(", generated);
    }

    [Fact]
    public void SameComposableCalledTwiceGetsDistinctLocalsAndSequences()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.UI;

            public partial class Counter : ComposeComponentBase
            {
                [Composable]
                private static View Label(string value) => Text(value);

                protected override View Body =>
                    VStack(Label("A"), Label("B"));
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // Distinct locals from distinct logical preorder ordinals (Label calls are 1 and 3).
        Assert.Contains("string __bc_arg_1_0 = \"A\";", generated);
        Assert.Contains("string __bc_arg_3_0 = \"B\";", generated);

        // Disjoint runtime sequence ranges for the two expanded Text nodes.
        Assert.Contains("__builder.AddContent(2, __bc_arg_1_0)", generated);
        Assert.Contains("__builder.AddContent(4, __bc_arg_3_0)", generated);

        Assert.DoesNotContain("Label(", generated);
    }

    [Fact]
    public void NamedArgumentsEmittedInSourceOrderMappedToParameterOrdinals()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.UI;

            public partial class Counter : ComposeComponentBase
            {
                [Composable]
                private static View Pair(string first, string second) => Text(first + second);

                private string A() => "a";
                private string B() => "b";

                protected override View Body =>
                    Pair(second: B(), first: A());
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // Locals are named by parameter ordinal (first=0, second=1) ...
        var idxSecond = generated.IndexOf("__bc_arg_0_1 = B();", System.StringComparison.Ordinal);
        var idxFirst = generated.IndexOf("__bc_arg_0_0 = A();", System.StringComparison.Ordinal);
        Assert.True(idxSecond >= 0, "second local declaration missing");
        Assert.True(idxFirst >= 0, "first local declaration missing");

        // ... but emitted in source evaluation order (second is written first in source).
        Assert.True(idxSecond < idxFirst, "supplied arguments must evaluate in source order");

        // The body maps holes back to their parameter ordinals.
        Assert.Contains("__builder.AddContent(1, __bc_arg_0_0 + __bc_arg_0_1)", generated);
    }

    [Fact]
    public void UnusedSuppliedArgumentStillReceivesLocal()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.UI;

            public partial class Counter : ComposeComponentBase
            {
                [Composable]
                private static View Ignore(string used, string unused) => Text(used);

                protected override View Body => Ignore("keep", "drop");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("string __bc_arg_0_0 = \"keep\";", generated);
        Assert.Contains("string __bc_arg_0_1 = \"drop\";", generated);
        Assert.Contains("__builder.AddContent(1, __bc_arg_0_0)", generated);
    }

    [Fact]
    public void LambdaAndMethodGroupReceiveDelegateParameterType()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.UI;

            public partial class Counter : ComposeComponentBase
            {
                [Composable]
                private static View Clickable(string label, System.Action onClick) => Button(label, onClick);

                private void Handle() { }

                protected override View Body =>
                    VStack(
                        Clickable("lambda", () => Handle()),
                        Clickable("group", Handle));
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // The typed local gives the lambda and method group their exact delegate target type.
        Assert.Contains("global::System.Action __bc_arg_1_1 = () => Handle();", generated);
        Assert.Contains("global::System.Action __bc_arg_3_1 = Handle;", generated);
    }

    [Fact]
    public void NumericImplicitConversionUsesDeclaredParameterType()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.UI;

            public partial class Counter : ComposeComponentBase
            {
                [Composable]
                private static View Sized(double width) => Text(width.ToString());

                protected override View Body => Sized(5);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // The declared parameter type (double) drives the implicit int-to-double conversion.
        Assert.Contains("double __bc_arg_0_0 = 5;", generated);
        Assert.Contains("__builder.AddContent(1, __bc_arg_0_0.ToString())", generated);
    }

    [Fact]
    public void ComposableCallInIfBranchDeclaresLocalInsideBranchBraces()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.UI;

            public partial class Counter : ComposeComponentBase
            {
                private bool _show = true;

                [Composable]
                private static View Label(string value) => Text(value);

                protected override View Body =>
                    If(_show, () => Label("in-branch"), () => Text("no"));
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        var ifIdx = generated.IndexOf("if (_show)", System.StringComparison.Ordinal);
        var localIdx = generated.IndexOf("__bc_arg_1_0 = \"in-branch\";", System.StringComparison.Ordinal);
        var elseIdx = generated.IndexOf("else", System.StringComparison.Ordinal);

        Assert.True(ifIdx >= 0, "if branch missing");
        Assert.True(localIdx > ifIdx, "expansion local must be declared inside the then branch");
        Assert.True(elseIdx > localIdx, "expansion local must stay before the else branch");
        Assert.DoesNotContain("Label(", generated);
    }

    [Fact]
    public void RecursiveComposableCycleReportsBC1002AndGeneratesNoSource()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.UI;

            public partial class Counter : ComposeComponentBase
            {
                [Composable]
                private static View Ping() => Pong();

                [Composable]
                private static View Pong() => Ping();

                protected override View Body => Ping();
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        var diagnostic = Assert.Single(result.Diagnostics.Where(static d => d.Id == "BC1002"));
        Assert.Contains("cycle", diagnostic.GetMessage(CultureInfo.InvariantCulture));
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void CallToInvalidComposableReportsOnlyDeclarationDiagnostic()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.UI;

            public partial class Counter : ComposeComponentBase
            {
                // Invalid declaration: a composable must be static.  The call site must not add a
                // duplicate BC1002.
                [Composable]
                private View Helper() => Text("x");

                protected override View Body => Helper();
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        var diagnostic = Assert.Single(result.Diagnostics.Where(static d => d.Id == "BC1002"));
        Assert.Contains("must be static", diagnostic.GetMessage(CultureInfo.InvariantCulture));
        Assert.Empty(result.GeneratedSources);
    }
}
