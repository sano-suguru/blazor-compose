using System.Collections.Immutable;
using System.Linq;
using BlazorCompose.Compiler.Analysis;

namespace BlazorCompose.Compiler.Tests;

public sealed class KeyabilityResolverTests
{
    private static ExpressionTemplate Lit(string text) => ExpressionTemplate.Literal(text);

    [Fact]
    public void ResolveRootKind_VStack_IsElement()
    {
        var node = new VStackTemplateNode(ImmutableArray.Create<RenderTemplateNode>(
            new TextTemplateNode(Lit("\"x\""))));

        Assert.Equal(ContentRootKind.Element, KeyabilityResolver.ResolveRootKind(node, ComposableRegistry.Empty));
    }

    [Fact]
    public void ResolveRootKind_BareIf_IsRegion()
    {
        var node = new IfTemplateNode(Lit("true"), new TextTemplateNode(Lit("\"x\"")), null);

        Assert.Equal(ContentRootKind.Region, KeyabilityResolver.ResolveRootKind(node, ComposableRegistry.Empty));
    }

    [Fact]
    public void ResolveRootKind_Component_IsElement()
    {
        var node = new ComponentTemplateNode("global::X.C", EquatableArray<ComponentParameter>.Empty);

        Assert.Equal(ContentRootKind.Element, KeyabilityResolver.ResolveRootKind(node, ComposableRegistry.Empty));
    }

    [Fact]
    public void ResolveRootKind_ComposableCallToUnknown_IsUnresolved()
    {
        var node = new ComposableCallTemplateNode(
            "K:Missing", "Missing", default, new TemplateLocation("f", default, default));

        Assert.Equal(ContentRootKind.Unresolved, KeyabilityResolver.ResolveRootKind(node, ComposableRegistry.Empty));
    }

    [Fact]
    public void CollectForEachContentDiagnostics_RegionRootedContent_EmitsBc3003()
    {
        // ForEach whose content root is a bare If (region).
        var forEach = new ForEachTemplateNode(
            Lit("_xs"), Lit("__bc_item_0.Id"),
            new IfTemplateNode(Lit("true"), new TextTemplateNode(Lit("\"x\"")), null),
            new TemplateLocation("f", default, default));
        var sink = ImmutableArray.CreateBuilder<BlazorCompose.Compiler.Diagnostics.DiagnosticInfo>();

        KeyabilityResolver.CollectForEachContentDiagnostics(forEach, ComposableRegistry.Empty, sink);

        Assert.Single(sink, d => d.Id == "BC3003");
    }

    [Fact]
    public void CollectForEachContentDiagnostics_ElementRootedContent_EmitsNothing()
    {
        var forEach = new ForEachTemplateNode(
            Lit("_xs"), Lit("__bc_item_0.Id"),
            new TextTemplateNode(Lit("__bc_item_0.Name")),
            new TemplateLocation("f", default, default));
        var sink = ImmutableArray.CreateBuilder<BlazorCompose.Compiler.Diagnostics.DiagnosticInfo>();

        KeyabilityResolver.CollectForEachContentDiagnostics(forEach, ComposableRegistry.Empty, sink);

        Assert.Empty(sink);
    }
}
