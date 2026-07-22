using System.Diagnostics.CodeAnalysis;
using BlazorCompose.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace BlazorCompose.Compiler.Tests;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "xUnit tests use Subject_Scenario_ExpectedBehavior names.")]
public sealed class PartialComponentAnalyzerTests
{
    // Same component as GeneratorTests but without the partial modifier.
    private const string NonPartialCounterSource = """
        using BlazorCompose;
        using static BlazorCompose.UI;

        public class Counter : ComposeComponentBase
        {
            protected override View Body => Text("Count");
        }
        """;

    [Fact]
    public async Task PartialComponentAnalyzer_NonPartialComponent_ReportsBC1001AtClassIdentifier()
    {
        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<PartialComponentAnalyzer>(NonPartialCounterSource);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("BC1001", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);

        // Verify the squiggle lands on the class name token, not the whole declaration.
        var sourceText = diagnostic.Location.SourceTree!.GetText();
        var locationText = sourceText.ToString(diagnostic.Location.SourceSpan);
        Assert.Equal("Counter", locationText);
    }
}
