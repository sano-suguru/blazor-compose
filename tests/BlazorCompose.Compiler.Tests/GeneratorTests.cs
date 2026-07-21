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
    public void GeneratedSourceDisablesCS0219AfterNullableDirective()
    {
        var result = CompilationTestHost.RunGenerator(PartialCounterSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        var nullableIdx = generated.IndexOf("#nullable enable", System.StringComparison.Ordinal);
        var pragmaIdx = generated.IndexOf(
            "#pragma warning disable CS0219",
            System.StringComparison.Ordinal);
        var classIdx = generated.IndexOf("partial class Counter", System.StringComparison.Ordinal);

        Assert.True(nullableIdx >= 0, "nullable directive missing");
        Assert.True(pragmaIdx > nullableIdx, "CS0219 pragma must follow nullable directive");
        Assert.True(classIdx > pragmaIdx, "CS0219 pragma must be emitted before generated declarations");
    }

    [Fact]
    public void UnusedConstantComposableArgumentKeepsLocalWithoutCS0219Diagnostic()
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
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            static diagnostic => diagnostic.Id == "CS0219");
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

    // -----------------------------------------------------------------------
    // Cross-file and definition-context normalization
    // -----------------------------------------------------------------------

    [Fact]
    public void CrossFileComposableUsesDefinitionSemanticModel()
    {
        var result = CompilationTestHost.RunGenerator(
            ("Widgets.cs", """
                using BlazorCompose;
                using static BlazorCompose.UI;

                public static class Widgets
                {
                    internal const string Prefix = "Value: ";

                    [Composable]
                    public static View Label(string value) =>
                        Text(Prefix + value);
                }
                """),
            ("Counter.cs", """
                using BlazorCompose;
                using static BlazorCompose.UI;

                public partial class Counter : ComposeComponentBase
                {
                    protected override View Body => Widgets.Label("1");
                }
                """));

        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();
        Assert.Contains("global::Widgets.Prefix", generated);
        Assert.DoesNotContain("Widgets.Label(", generated);
    }

    [Fact]
    public void PrivateCrossTypeMemberReferenceReportsBC1002AtCall()
    {
        var result = CompilationTestHost.RunGenerator(
            ("Widgets.cs", """
                using BlazorCompose;
                using static BlazorCompose.UI;

                public static class Widgets
                {
                    private static string Secret() => "s";

                    [Composable]
                    public static View Label(string value) => Text(Secret() + value);
                }
                """),
            ("Counter.cs", """
                using BlazorCompose;
                using static BlazorCompose.UI;

                public partial class Counter : ComposeComponentBase
                {
                    protected override View Body => Widgets.Label("x");
                }
                """));

        var diagnostic = Assert.Single(result.Diagnostics.Where(static d => d.Id == "BC1002"));
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("Secret", message);
        Assert.Contains("not accessible", message);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void ProtectedCrossTypeReferenceIsValidatedAgainstInheritance()
    {
        var result = CompilationTestHost.RunGenerator(
            ("WidgetBase.cs", """
                using BlazorCompose;
                using static BlazorCompose.UI;

                public abstract partial class WidgetBase : ComposeComponentBase
                {
                    protected static string Prefix() => "P:";

                    [Composable]
                    protected static View Label(string value) => Text(Prefix() + value);
                }
                """),
            ("Counter.cs", """
                using BlazorCompose;
                using static BlazorCompose.UI;

                public partial class Counter : WidgetBase
                {
                    protected override View Body => Label("x");
                }
                """));

        Assert.Empty(result.Diagnostics.Where(static d => d.Id == "BC1002"));
        var counter = Assert.Single(
            result.GeneratedSources.Where(static s => s.HintName.Contains("Counter")));
        var generated = counter.SourceText.ToString();
        Assert.Contains("global::WidgetBase.Prefix", generated);
        Assert.DoesNotContain("Label(", generated);
    }

    [Fact]
    public void ProtectedCrossTypeReferenceFromUnrelatedComponentReportsBC1002AtCall()
    {
        var result = CompilationTestHost.RunGenerator(
            ("WidgetBase.cs", """
                using BlazorCompose;
                using static BlazorCompose.UI;

                public abstract class WidgetBase
                {
                    protected static string Prefix() => "P:";

                    [Composable]
                    public static View Label(string value) => Text(Prefix() + value);
                }
                """),
            ("Counter.cs", """
                using BlazorCompose;
                using static BlazorCompose.UI;

                public partial class Counter : ComposeComponentBase
                {
                    protected override View Body => WidgetBase.Label("x");
                }
                """));

        var diagnostic = Assert.Single(result.Diagnostics.Where(static d => d.Id == "BC1002"));
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("Prefix", message);
        Assert.Contains("not accessible", message);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void ProtectedInternalCrossTypeReferenceIsAccessibleThroughInternalHalf()
    {
        var result = CompilationTestHost.RunGenerator(
            ("WidgetBase.cs", """
                using BlazorCompose;
                using static BlazorCompose.UI;

                public abstract class WidgetBase
                {
                    protected internal static string Prefix() => "P:";

                    [Composable]
                    public static View Label(string value) => Text(Prefix() + value);
                }
                """),
            ("Counter.cs", """
                using BlazorCompose;
                using static BlazorCompose.UI;

                public partial class Counter : ComposeComponentBase
                {
                    protected override View Body => WidgetBase.Label("x");
                }
                """));

        Assert.Empty(result.Diagnostics.Where(static d => d.Id == "BC1002"));
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();
        Assert.Contains("global::WidgetBase.Prefix", generated);
        Assert.DoesNotContain("Label(", generated);
    }

    [Fact]
    public void PrivateProtectedCrossTypeReferenceFromUnrelatedComponentReportsBC1002AtCall()
    {
        var result = CompilationTestHost.RunGenerator(
            ("WidgetBase.cs", """
                using BlazorCompose;
                using static BlazorCompose.UI;

                public abstract class WidgetBase
                {
                    private protected static string Prefix() => "P:";

                    [Composable]
                    public static View Label(string value) => Text(Prefix() + value);
                }
                """),
            ("Counter.cs", """
                using BlazorCompose;
                using static BlazorCompose.UI;

                public partial class Counter : ComposeComponentBase
                {
                    protected override View Body => WidgetBase.Label("x");
                }
                """));

        var diagnostic = Assert.Single(result.Diagnostics.Where(static d => d.Id == "BC1002"));
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("Prefix", message);
        Assert.Contains("not accessible", message);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void MetadataOnlyComposableReportsBC1002AtCall()
    {
        var libraryReference = CompilationTestHost.CompileToMetadataReference("""
            using BlazorCompose;
            using static BlazorCompose.UI;

            public static class ExternalWidgets
            {
                [Composable]
                public static View Badge(string text) => Text(text);
            }
            """,
            "ExternalWidgets");

        var compilation = CompilationTestHost.CreateCompilation(
            new[]
            {
                ("Counter.cs", """
                    using BlazorCompose;
                    using static BlazorCompose.UI;

                    public partial class Counter : ComposeComponentBase
                    {
                        protected override View Body => ExternalWidgets.Badge("hi");
                    }
                    """),
            },
            libraryReference);

        var result = CompilationTestHost.RunGenerator(compilation);

        var diagnostic = Assert.Single(result.Diagnostics.Where(static d => d.Id == "BC1002"));
        Assert.Contains("no source declaration", diagnostic.GetMessage(CultureInfo.InvariantCulture));
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void UnnameableParameterTypeReportsBC1002AndGeneratesNoSource()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.UI;

            file class Hidden { }

            public partial class Counter : ComposeComponentBase
            {
                [Composable]
                private static View Show(Hidden h) => Text("x");

                protected override View Body => Show(new Hidden());
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        var diagnostic = Assert.Single(result.Diagnostics.Where(static d => d.Id == "BC1002"));
        Assert.Contains("cannot be named", diagnostic.GetMessage(CultureInfo.InvariantCulture));
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void NestedComposableCallsExpandTransitively()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.UI;

            public partial class Counter : ComposeComponentBase
            {
                [Composable]
                private static View Inner(string value) => Text(value);

                [Composable]
                private static View Outer(string value) => VStack(Inner(value), Text("tail"));

                protected override View Body => Outer("hi");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain("Outer(", generated);
        Assert.DoesNotContain("Inner(", generated);
        Assert.Contains("\"tail\"", generated);
    }

    [Fact]
    public void DirectRecursionReportsSingleBC1002()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.UI;

            public partial class Counter : ComposeComponentBase
            {
                [Composable]
                private static View Loop() => Loop();

                protected override View Body => Loop();
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        var diagnostic = Assert.Single(result.Diagnostics.Where(static d => d.Id == "BC1002"));
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("cycle", message);
        Assert.Contains("Loop -> Loop", message);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void IndirectRecursionReportsSingleBC1002WithCallChain()
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
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("cycle", message);
        Assert.Contains("Ping -> Pong -> Ping", message);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void ParameterShadowingInsideNestedLambdaIsNotReplaced()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.UI;

            public partial class Counter : ComposeComponentBase
            {
                [Composable]
                private static View Echo(string value) =>
                    Text(((System.Func<string, string>)(value => value))(value));

                protected override View Body => Echo("hi");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // The shadowing lambda parameter keeps its source name ...
        Assert.Contains("value => value", generated);
        // ... while the composable parameter reference becomes the typed local.
        Assert.Contains("__bc_arg_0_0", generated);
        Assert.DoesNotContain("Echo(", generated);
    }

    [Fact]
    public void NameofOfParameterEmitsCompileTimeConstantString()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.UI;

            public partial class Counter : ComposeComponentBase
            {
                [Composable]
                private static View Named(string value) => Text(nameof(value));

                protected override View Body => Named("x");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // The parameter 'value' does not exist after expansion, so nameof(value) must collapse to its
        // compile-time constant string rather than reference an out-of-scope name.
        Assert.Contains("__builder.AddContent(1, \"value\")", generated);
        Assert.DoesNotContain("nameof(", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void NameofOfParameterMemberEmitsMemberNameConstant()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.UI;

            public partial class Counter : ComposeComponentBase
            {
                [Composable]
                private static View NamedMember(string value) => Text(nameof(value.Length));

                protected override View Body => NamedMember("x");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // nameof(value.Length) evaluates to "Length"; the vanished parameter must not survive as text.
        Assert.Contains("__builder.AddContent(1, \"Length\")", generated);
        Assert.DoesNotContain("value", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void NameofOfNonParameterRemainsUnchangedAndCompiles()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.UI;

            public partial class Counter : ComposeComponentBase
            {
                [Composable]
                private static View NamedType(string value) => Text(nameof(System.String) + value);

                protected override View Body => NamedType("x");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // A nameof that does not depend on a composable parameter is left verbatim and stays valid.
        Assert.Contains("nameof(System.String)", generated);
        Assert.Contains("__bc_arg_0_0", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void InterpolatedParameterReferenceIsReplaced()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.UI;

            public partial class Counter : ComposeComponentBase
            {
                [Composable]
                private static View Greet(string value) => Text($"Hi {value}!");

                protected override View Body => Greet("x");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("$\"Hi {__bc_arg_0_0}!\"", generated);
    }

    [Fact]
    public void OptionalEnumDefaultIsFullyQualified()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.UI;

            public enum Color { Red, Green }

            public partial class Counter : ComposeComponentBase
            {
                [Composable]
                private static View Swatch(Color c = Color.Green) => Text(c.ToString());

                protected override View Body => Swatch();
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("global::Color __bc_arg_0_0 = (global::Color)1;", generated);
    }

    [Fact]
    public void OptionalValueTypeDefaultEmitsDefaultOfFullyQualifiedType()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.UI;

            public struct Box { public int V; }

            public partial class Counter : ComposeComponentBase
            {
                [Composable]
                private static View Show(Box b = default) => Text(b.V.ToString());

                protected override View Body => Show();
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("global::Box __bc_arg_0_0 = default(global::Box);", generated);
    }

    // -----------------------------------------------------------------------
    // Fractional and special floating-point / decimal optional defaults
    // -----------------------------------------------------------------------

    [Fact]
    public void OptionalSingleDefaultEmitsFloatSuffixedLiteral()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.UI;

            public partial class Counter : ComposeComponentBase
            {
                [Composable]
                private static View Scaled(float scale = 1.5f) => Text(scale.ToString());

                protected override View Body => Scaled();
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // A bare 1.5 is a double literal and cannot initialize a float local (CS0664); the 'F' suffix
        // makes the constant round-trip into the declared parameter type.
        Assert.Contains("float __bc_arg_0_0 = 1.5F;", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void OptionalDecimalDefaultEmitsDecimalSuffixedLiteral()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.UI;

            public partial class Counter : ComposeComponentBase
            {
                [Composable]
                private static View Priced(decimal amount = 1.5m) => Text(amount.ToString());

                protected override View Body => Priced();
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // A bare 1.5 is a double literal and cannot initialize a decimal local (CS0664); the 'M' suffix
        // makes the constant round-trip into the declared parameter type.
        Assert.Contains("decimal __bc_arg_0_0 = 1.5M;", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void OptionalSpecialFloatingPointDefaultsEmitTypedConstants()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.UI;

            public partial class Counter : ComposeComponentBase
            {
                [Composable]
                private static View Special(
                    float f = float.NaN,
                    double d = double.PositiveInfinity,
                    double n = double.NegativeInfinity) =>
                    Text(f.ToString() + d.ToString() + n.ToString());

                protected override View Body => Special();
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("float __bc_arg_0_0 = float.NaN;", generated);
        Assert.Contains("double __bc_arg_0_1 = double.PositiveInfinity;", generated);
        Assert.Contains("double __bc_arg_0_2 = double.NegativeInfinity;", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    // -----------------------------------------------------------------------
    // Accessibility of member-access (qualified) non-public references
    // -----------------------------------------------------------------------

    [Fact]
    public void PrivateMemberAccessReferenceFromUnrelatedComponentReportsBC1002AtCall()
    {
        var result = CompilationTestHost.RunGenerator(
            ("Widgets.cs", """
                using BlazorCompose;
                using static BlazorCompose.UI;

                public static class Widgets
                {
                    private static string Secret() => "s";

                    // Qualified member access to a private member: legal here, but inaccessible once
                    // inlined into an unrelated component.
                    [Composable]
                    public static View Label(string value) => Text(Widgets.Secret() + value);
                }
                """),
            ("Counter.cs", """
                using BlazorCompose;
                using static BlazorCompose.UI;

                public partial class Counter : ComposeComponentBase
                {
                    protected override View Body => Widgets.Label("x");
                }
                """));

        var diagnostic = Assert.Single(result.Diagnostics.Where(static d => d.Id == "BC1002"));
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("Secret", message);
        Assert.Contains("not accessible", message);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void ProtectedMemberAccessReferenceFromUnrelatedComponentReportsBC1002AtCall()
    {
        var result = CompilationTestHost.RunGenerator(
            ("WidgetBase.cs", """
                using BlazorCompose;
                using static BlazorCompose.UI;

                public abstract class WidgetBase
                {
                    protected static string Prefix() => "P:";

                    [Composable]
                    public static View Label(string value) => Text(WidgetBase.Prefix() + value);
                }
                """),
            ("Counter.cs", """
                using BlazorCompose;
                using static BlazorCompose.UI;

                public partial class Counter : ComposeComponentBase
                {
                    protected override View Body => WidgetBase.Label("x");
                }
                """));

        var diagnostic = Assert.Single(result.Diagnostics.Where(static d => d.Id == "BC1002"));
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("Prefix", message);
        Assert.Contains("not accessible", message);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void PrivateMemberAccessReferenceFromSameTypeExpandsAndCompiles()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.UI;

            public partial class Widget : ComposeComponentBase
            {
                private static string Secret() => "s";

                [Composable]
                private static View Label(string value) => Text(Widget.Secret() + value);

                protected override View Body => Label("x");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(static d => d.Id == "BC1002"));
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();
        Assert.Contains("global::Widget.Secret()", generated);
        Assert.DoesNotContain("Label(", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ProtectedMemberAccessReferenceFromDerivedComponentExpandsAndCompiles()
    {
        var result = CompilationTestHost.RunGenerator(
            ("WidgetBase.cs", """
                using BlazorCompose;
                using static BlazorCompose.UI;

                public abstract partial class WidgetBase : ComposeComponentBase
                {
                    protected static string Prefix() => "P:";

                    [Composable]
                    protected static View Label(string value) => Text(WidgetBase.Prefix() + value);
                }
                """),
            ("Counter.cs", """
                using BlazorCompose;
                using static BlazorCompose.UI;

                public partial class Counter : WidgetBase
                {
                    protected override View Body => Label("x");
                }
                """));

        Assert.Empty(result.Diagnostics.Where(static d => d.Id == "BC1002"));
        var counter = Assert.Single(
            result.GeneratedSources.Where(static s => s.HintName.Contains("Counter")));
        var generated = counter.SourceText.ToString();
        Assert.Contains("global::WidgetBase.Prefix()", generated);
        Assert.DoesNotContain("Label(", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }
}
