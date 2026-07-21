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
    public async Task IncrementDirectlyInBodyTextReportsBC3001()
    {
        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(
            IncrementInTextSource);

        Assert.Contains(diagnostics, static d => d.Id == "BC3001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task DirectAssignmentInBodyTextReportsBC3001()
    {
        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(
            AssignmentInTextSource);

        Assert.Contains(diagnostics, static d => d.Id == "BC3001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task CompoundAssignmentDirectlyInBodyTextReportsBC3001()
    {
        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(
            CompoundAssignmentInTextSource);

        Assert.Contains(diagnostics, static d => d.Id == "BC3001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task DecrementDirectlyInBodyTextReportsBC3001()
    {
        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(
            DecrementInTextSource);

        Assert.Contains(diagnostics, static d => d.Id == "BC3001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task DirectPropertyAssignmentInBodyTextReportsBC3001()
    {
        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(
            PropertyAssignmentInTextSource);

        Assert.Contains(diagnostics, static d => d.Id == "BC3001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task PropertyIncrementDirectlyInBodyTextReportsBC3001()
    {
        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(
            PropertyIncrementInTextSource);

        Assert.Contains(diagnostics, static d => d.Id == "BC3001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task IncrementInsideButtonHandlerDoesNotReportBC3001()
    {
        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(
            IncrementInButtonHandlerSource);

        Assert.DoesNotContain(diagnostics, static d => d.Id == "BC3001");
    }

    [Fact]
    public async Task PropertyIncrementInsideButtonHandlerDoesNotReportBC3001()
    {
        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(
            PropertyIncrementInButtonHandlerSource);

        Assert.DoesNotContain(diagnostics, static d => d.Id == "BC3001");
    }

    [Fact]
    public async Task HelperMutationUsedByBodyDoesNotReportBC3001()
    {
        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(
            HelperMutationSource);

        Assert.DoesNotContain(diagnostics, static d => d.Id == "BC3001");
    }
}
