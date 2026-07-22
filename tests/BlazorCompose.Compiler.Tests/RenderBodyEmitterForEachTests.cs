using BlazorCompose.Compiler;

namespace BlazorCompose.Compiler.Tests;

public sealed class RenderBodyEmitterForEachTests
{
    [Fact]
    public void Emit_ForEachRoot_EmitsKeyedForeachRegionWithContentAtSeqPlusOne()
    {
        var node = new ForEachNode(
            Source: ExpressionTemplate.Literal("_items"),
            Key: ExpressionTemplate.Literal("__bc_item_0.Id"),
            Content: new TextNode(ExpressionTemplate.Literal("__bc_item_0.Title")),
            LoopVariableName: "__bc_item_0");

        var model = new ComponentModel(
            HintName: "T.g.cs",
            ClassName: "T",
            Namespace: null,
            RootNode: node);

        var generated = RenderBodyEmitter.Emit(model).ToString();

        Assert.Contains("__builder.OpenRegion(0);", generated);
        Assert.Contains("foreach (var __bc_item_0 in _items)", generated);
        Assert.Contains("__builder.SetKey(__bc_item_0.Id);", generated);
        Assert.Contains("__builder.OpenElement(1, \"span\");", generated);
        Assert.Contains("__builder.AddContent(2, __bc_item_0.Title);", generated);
        Assert.Contains("__builder.CloseRegion();", generated);
    }
}
