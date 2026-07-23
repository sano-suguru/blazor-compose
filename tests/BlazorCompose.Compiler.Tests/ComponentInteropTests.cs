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
            [Parameter] public string Title { get; set; } = "";
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

    [Fact]
    public void Component_WithParameter_EmitsOpenComponentAndAddComponentParameter()
    {
        const string host = """
            using BlazorCompose;
            using static BlazorCompose.UI;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Component<Child>().Param(c => c.Label, "hi");
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Child.cs", ChildSource), ("Host.cs", host));

        CompilationTestHost.AssertOutputCompiles(result);
        var generated = result.GeneratedSources.Single(s => s.HintName.Contains("Host")).SourceText.ToString();
        Assert.Contains("OpenComponent<global::T.Child>", generated);
        Assert.Contains("AddComponentParameter(1, \"Label\", \"hi\")", generated);
        Assert.Contains("CloseComponent();", generated);
    }

    [Fact]
    public void ForEach_WithComponentContent_EmitsSetKeyOnComponentAndNoBC3003()
    {
        const string host = """
            using System.Collections.Generic;
            using BlazorCompose;
            using static BlazorCompose.UI;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                private readonly List<Item> _items = new();
                protected override View Body =>
                    ForEach(_items, key: i => i.Id, content: i => Component<Child>().Param(c => c.Label, i.Name));
                public sealed record Item(int Id, string Name);
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Child.cs", ChildSource), ("Host.cs", host));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BC3003");
        CompilationTestHost.AssertOutputCompiles(result);

        var generated = result.GeneratedSources.Single(s => s.HintName.Contains("Host")).SourceText.ToString();
        int openIdx = generated.IndexOf("OpenComponent<global::T.Child>", System.StringComparison.Ordinal);
        int keyIdx = generated.IndexOf("SetKey(", System.StringComparison.Ordinal);
        Assert.True(openIdx >= 0, "component should be opened");
        Assert.True(keyIdx > openIdx, "SetKey must be emitted after OpenComponent");
    }

    [Fact]
    public void Component_MultipleParams_EmitsAddComponentParameterInSourceOrder()
    {
        const string host = """
            using BlazorCompose;
            using static BlazorCompose.UI;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body =>
                    Component<Child>().Param(c => c.Label, "hi").Param(c => c.Title, "there");
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Child.cs", ChildSource), ("Host.cs", host));

        CompilationTestHost.AssertOutputCompiles(result);
        var generated = result.GeneratedSources.Single(s => s.HintName.Contains("Host")).SourceText.ToString();

        int firstIdx = generated.IndexOf(
            "AddComponentParameter(1, \"Label\", \"hi\")", System.StringComparison.Ordinal);
        int secondIdx = generated.IndexOf(
            "AddComponentParameter(2, \"Title\", \"there\")", System.StringComparison.Ordinal);
        Assert.True(firstIdx >= 0, "first parameter should be emitted");
        Assert.True(secondIdx > firstIdx, "AddComponentParameter calls must appear in source order");
    }

    [Fact]
    public void Component_DuplicateParamOnSameProperty_ReportsBC3007AndNoSource()
    {
        const string host = """
            using BlazorCompose;
            using static BlazorCompose.UI;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body =>
                    Component<Child>().Param(c => c.Label, "a").Param(c => c.Label, "b");
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Child.cs", ChildSource), ("Host.cs", host));

        Assert.Contains(result.Diagnostics, d => d.Id == "BC3007" && d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(result.GeneratedSources, s => s.HintName.Contains("Host"));
    }

    [Fact]
    public void Component_DistinctParams_DoNotReportBC3007()
    {
        const string host = """
            using BlazorCompose;
            using static BlazorCompose.UI;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body =>
                    Component<Child>().Param(c => c.Label, "a").Param(c => c.Title, "b");
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Child.cs", ChildSource), ("Host.cs", host));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BC3007");
    }
}
