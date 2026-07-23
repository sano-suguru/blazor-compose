using System.Linq;
using Microsoft.CodeAnalysis;

namespace BlazorCompose.Compiler.Tests;

public sealed class ComponentInteropTests
{
    private const string ChildSource = """
        using Microsoft.AspNetCore.Components;
        namespace T;
        public class Child : ComponentBase
        {
            [Parameter] public string Label { get; set; } = "";
            public string NotAParam { get; set; } = "";
        }
        """;

    [Fact]
    public void Component_ParamSelectsCapturedVariableMember_ReportsBC3005AndNoSource()
    {
        const string host = """
            using BlazorCompose;
            using static BlazorCompose.UI;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                private static readonly Child _other = new();
                protected override View Body => Component<Child>().Param(c => _other.Label, "hi");
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Child.cs", ChildSource), ("Host.cs", host));

        Assert.Contains(result.Diagnostics, d => d.Id == "BC3005" && d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(result.GeneratedSources, s => s.HintName.Contains("Host"));
    }

    [Fact]
    public void Component_ParamTargetsNonParameterProperty_ReportsBC3006AndNoSource()
    {
        const string host = """
            using BlazorCompose;
            using static BlazorCompose.UI;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Component<Child>().Param(c => c.NotAParam, "hi");
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Child.cs", ChildSource), ("Host.cs", host));

        Assert.Contains(result.Diagnostics, d => d.Id == "BC3006" && d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(result.GeneratedSources, s => s.HintName.Contains("Host"));
    }
}
