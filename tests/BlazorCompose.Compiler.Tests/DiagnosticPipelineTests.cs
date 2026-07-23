using System.Linq;
using Microsoft.CodeAnalysis;

namespace BlazorCompose.Compiler.Tests;

public sealed class DiagnosticPipelineTests
{
    [Fact]
    public void ComponentBody_WithBadKeyAndUnkeyableContent_ReportsBothBc3002AndBc3003()
    {
        // A constant-key ForEach (BC3002 warning) beside a region-rooted-content ForEach (BC3003 error)
        // in the same Body. The warning must not be dropped by the co-located error.
        const string source = """
            using System.Collections.Generic;
            using static BlazorCompose.UI;
            public partial class P : BlazorCompose.ComposeComponentBase
            {
                private readonly List<int> _xs = new();
                private readonly List<Group> _groups = new();
                protected override BlazorCompose.View Body =>
                    VStack(
                        ForEach(_xs, key: _ => 0, content: x => Text(x.ToString())),
                        ForEach(_groups, key: g => g.Id, content: g =>
                            ForEach(g.Items, key: i => i.Id, content: i => Text(i.Name))));
                private sealed record Item(int Id, string Name);
                private sealed record Group(int Id, List<Item> Items);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "BC3002" && d.Severity == DiagnosticSeverity.Warning);
        Assert.Contains(result.Diagnostics, d => d.Id == "BC3003" && d.Severity == DiagnosticSeverity.Error);
    }
}
