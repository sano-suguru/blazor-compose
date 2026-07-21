using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using BlazorCompose.Compiler.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCompose.Compiler.Tests;

public sealed class ComposableDefinitionTests
{
    [Theory]
    [InlineData("[Composable] private View Helper() => Text(\"x\");", "must be static")]
    [InlineData("[Composable] private static View Helper<T>() => Text(\"x\");", "must be non-generic")]
    [InlineData("[Composable] private static View Helper() { return Text(\"x\"); }", "must be expression-bodied")]
    [InlineData("[Composable] private static string Helper() => \"x\";", "must return BlazorCompose.View")]
    [InlineData("[Composable] private static View Helper(params string[] values) => Text(values[0]);", "params parameters are unsupported")]
    [InlineData("[Composable] private static View Helper(View content) => content;", "View parameters are unsupported")]
    public void UnsupportedDeclarationReportsBC1002(string declaration, string message)
    {
        var source = $$"""
            using BlazorCompose;
            using static BlazorCompose.UI;

            public partial class Counter : ComposeComponentBase
            {
                {{declaration}}
                protected override View Body => Text("Body");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var diagnostic = Assert.Single(
            result.Diagnostics.Where(static d => d.Id == "BC1002"));

        Assert.Contains(message, diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ValidDefinitionReportsNoBC1002()
    {
        var source = """
            using BlazorCompose;
            using static BlazorCompose.UI;

            public partial class Counter : ComposeComponentBase
            {
                [Composable]
                private static View Greeting(string name) => Text(name);

                protected override View Body => Text("Body");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(static d => d.Id == "BC1002"));
    }

    [Fact]
    public void RegistryIsOrderIndependentValueEqual()
    {
        var high = new ComposableDefinitionEntry("K:b", "Beta", Definition: null, DeclarationDiagnosticReported: true);
        var low = new ComposableDefinitionEntry("K:a", "Alpha", Definition: null, DeclarationDiagnosticReported: true);

        var registry = ComposableRegistry.Create(ImmutableArray.Create(high, low));
        var reordered = ComposableRegistry.Create(ImmutableArray.Create(low, high));

        Assert.Equal(registry, reordered);
        Assert.Equal(registry.GetHashCode(), reordered.GetHashCode());

        // Entries are sorted by method key so equality is discovery-order independent.
        Assert.Equal("K:a", registry.Entries[0].MethodKey);
        Assert.Equal("K:b", registry.Entries[1].MethodKey);

        Assert.True(registry.TryGet("K:a", out var found));
        Assert.Equal("Alpha", found.DisplayName);
        Assert.False(registry.TryGet("missing", out _));
    }

    [Fact]
    public void RegistryDeduplicatesByMethodKey()
    {
        var first = new ComposableDefinitionEntry("K", "First", Definition: null, DeclarationDiagnosticReported: true);
        var duplicate = new ComposableDefinitionEntry("K", "Second", Definition: null, DeclarationDiagnosticReported: true);

        var registry = ComposableRegistry.Create(ImmutableArray.Create(first, duplicate));

        var entry = Assert.Single(registry.Entries);
        Assert.Equal("First", entry.DisplayName);
    }

    [Fact]
    public void TemplateReplacesParametersWithHolesAndPreservesNameof()
    {
        var source = """
            using BlazorCompose;
            using static BlazorCompose.UI;

            public partial class Counter : ComposeComponentBase
            {
                [Composable]
                private static View Greeting(string name) => Text(nameof(name) + name);

                protected override View Body => Text("Body");
            }
            """;

        var compilation = CompilationTestHost.CreateCompilation(source);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);

        var method = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(static m => m.Identifier.Text == "Greeting");
        var methodSymbol = model.GetDeclaredSymbol(method)!;

        var textInvocation = (InvocationExpressionSyntax)method.ExpressionBody!.Expression;
        var argument = textInvocation.ArgumentList.Arguments[0].Expression;

        var knownSymbols = KnownSymbols.TryCreate(compilation)!;
        var ordinals = methodSymbol.Parameters.ToImmutableDictionary(
            static p => (ISymbol)p,
            static p => p.Ordinal,
            SymbolEqualityComparer.Default);

        var context = new ComposableBodyContext(
            model,
            methodSymbol.ContainingType,
            knownSymbols,
            ordinals,
            default);

        var template = ExpressionTemplateFactory.Create(argument, context);
        var code = template.Substitute(ImmutableArray.Create("__p0")).ToCode();

        // nameof(name) keeps the original parameter name; the bare 'name' becomes a substituted hole.
        Assert.Equal("nameof(name) + __p0", code);
    }
}
