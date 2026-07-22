using System.Threading.Tasks;
using BlazorCompose.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace BlazorCompose.Compiler.Tests;

/// <summary>
/// Tests for <see cref="RenderMutationAnalyzer"/> (BC3001).
/// Verifies that direct state mutations in the Body rendering path are diagnosed,
/// while mutations inside recognized deferred event handler lambdas are not.
/// </summary>
public sealed class RenderMutationAnalyzerTests
{
    // -----------------------------------------------------------------------
    // Sources that should report BC3001
    // -----------------------------------------------------------------------

    private const string IncrementInTextSource = """
        using BlazorCompose;
        using static BlazorCompose.UI;

        public partial class Counter : ComposeComponentBase
        {
            private int _count;
            protected override View Body => Text($"{_count++}");
        }
        """;

    private const string AssignmentInTextSource = """
        using BlazorCompose;
        using static BlazorCompose.UI;

        public partial class Counter : ComposeComponentBase
        {
            private int _count;
            protected override View Body => Text($"{_count = 4}");
        }
        """;

    private const string CompoundAssignmentInTextSource = """
        using BlazorCompose;
        using static BlazorCompose.UI;

        public partial class Counter : ComposeComponentBase
        {
            private int _count;
            protected override View Body => Text($"{_count += 4}");
        }
        """;

    private const string DecrementInTextSource = """
        using BlazorCompose;
        using static BlazorCompose.UI;

        public partial class Counter : ComposeComponentBase
        {
            private int _count;
            protected override View Body => Text($"{_count--}");
        }
        """;

    private const string PropertyAssignmentInTextSource = """
        using BlazorCompose;
        using static BlazorCompose.UI;

        public partial class Counter : ComposeComponentBase
        {
            private int Count { get; set; }
            protected override View Body => Text($"{Count = 4}");
        }
        """;

    private const string PropertyIncrementInTextSource = """
        using BlazorCompose;
        using static BlazorCompose.UI;

        public partial class Counter : ComposeComponentBase
        {
            private int Count { get; set; }
            protected override View Body => Text($"{Count++}");
        }
        """;

    // -----------------------------------------------------------------------
    // Sources that must NOT report BC3001
    // -----------------------------------------------------------------------

    private const string IncrementInButtonHandlerSource = """
        using BlazorCompose;
        using static BlazorCompose.UI;

        public partial class Counter : ComposeComponentBase
        {
            private int _count;
            protected override View Body => Button("Increment", () => _count++);
        }
        """;

    private const string PropertyIncrementInButtonHandlerSource = """
        using BlazorCompose;
        using static BlazorCompose.UI;

        public partial class Counter : ComposeComponentBase
        {
            private int Count { get; set; }
            protected override View Body => Button("Increment", () => Count++);
        }
        """;

    private const string HelperMutationSource = """
        using BlazorCompose;
        using static BlazorCompose.UI;

        public partial class Counter : ComposeComponentBase
        {
            private int _count;

            protected override View Body => Text(MutateAndReturnText());

            private string MutateAndReturnText()
            {
                _count++;
                return _count.ToString();
            }
        }
        """;

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RenderMutationAnalyzer_IncrementInsideBodyText_ReportsBC3001()
    {
        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(
            IncrementInTextSource);

        Assert.Contains(diagnostics, static d => d.Id == "BC3001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task RenderMutationAnalyzer_AssignmentInsideBodyText_ReportsBC3001()
    {
        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(
            AssignmentInTextSource);

        Assert.Contains(diagnostics, static d => d.Id == "BC3001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task RenderMutationAnalyzer_CompoundAssignmentInsideBodyText_ReportsBC3001()
    {
        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(
            CompoundAssignmentInTextSource);

        Assert.Contains(diagnostics, static d => d.Id == "BC3001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task RenderMutationAnalyzer_DecrementInsideBodyText_ReportsBC3001()
    {
        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(
            DecrementInTextSource);

        Assert.Contains(diagnostics, static d => d.Id == "BC3001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task RenderMutationAnalyzer_PropertyAssignmentInsideBodyText_ReportsBC3001()
    {
        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(
            PropertyAssignmentInTextSource);

        Assert.Contains(diagnostics, static d => d.Id == "BC3001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task RenderMutationAnalyzer_PropertyIncrementInsideBodyText_ReportsBC3001()
    {
        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(
            PropertyIncrementInTextSource);

        Assert.Contains(diagnostics, static d => d.Id == "BC3001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task RenderMutationAnalyzer_IncrementInsideButtonHandler_DoesNotReportBC3001()
    {
        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(
            IncrementInButtonHandlerSource);

        Assert.DoesNotContain(diagnostics, static d => d.Id == "BC3001");
    }

    [Fact]
    public async Task RenderMutationAnalyzer_PropertyIncrementInsideButtonHandler_DoesNotReportBC3001()
    {
        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(
            PropertyIncrementInButtonHandlerSource);

        Assert.DoesNotContain(diagnostics, static d => d.Id == "BC3001");
    }

    [Fact]
    public async Task RenderMutationAnalyzer_HelperMutationReferencedByBody_DoesNotReportBC3001()
    {
        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(
            HelperMutationSource);

        Assert.DoesNotContain(diagnostics, static d => d.Id == "BC3001");
    }
}
