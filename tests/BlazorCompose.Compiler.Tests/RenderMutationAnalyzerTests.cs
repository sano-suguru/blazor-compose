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
    public async Task IncrementInsideButtonHandlerDoesNotReportBC3001()
    {
        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(
            IncrementInButtonHandlerSource);

        Assert.DoesNotContain(diagnostics, static d => d.Id == "BC3001");
    }
}
