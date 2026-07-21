using System.Collections.Immutable;
using System.Linq;
using BlazorCompose.Compiler;
using BlazorCompose.Compiler.Generation;

namespace BlazorCompose.Compiler.Tests;

public sealed class SequenceAllocatorTests
{
    [Fact]
    public void TextNodeWidthIsTwo()
    {
        Assert.Equal(2, SequenceAllocator.Width(new TextNode("\"hello\"")));
    }

    [Fact]
    public void ButtonNodeWidthIsThree()
    {
        Assert.Equal(3, SequenceAllocator.Width(new ButtonNode("\"label\"", "() => { }")));
    }

    [Fact]
    public void VStackNodeWidthIsOnePlusChildrenWidthSum()
    {
        var children = ImmutableArray.Create<RenderNode>(
            new TextNode("\"a\""),
            new ButtonNode("\"b\"", "() => { }"));

        Assert.Equal(
            1 + children.Sum(SequenceAllocator.Width),
            SequenceAllocator.Width(new VStackNode(children)));
    }

    [Fact]
    public void EmptyVStackWidthIsOne()
    {
        Assert.Equal(1, SequenceAllocator.Width(new VStackNode(ImmutableArray<RenderNode>.Empty)));
    }

    // -----------------------------------------------------------------------
    // IfNode
    // -----------------------------------------------------------------------

    [Fact]
    public void IfNodeWithNullOtherwiseWidthIsOnePlusThenWidth()
    {
        // OpenRegion(k) + then contents — else branch absent
        var then = new TextNode("\"yes\"");
        Assert.Equal(
            1 + SequenceAllocator.Width(then),
            SequenceAllocator.Width(new IfNode("_visible", then, null)));
    }

    [Fact]
    public void IfNodeWithBothBranchesWidthIsOnePlusBothWidths()
    {
        // OpenRegion(k) + then contents + else contents
        var then = new TextNode("\"yes\"");
        var otherwise = new ButtonNode("\"no\"", "() => { }");
        var ifNode = new IfNode("_visible", then, otherwise);
        Assert.Equal(
            1 + SequenceAllocator.Width(then) + SequenceAllocator.Width(otherwise),
            SequenceAllocator.Width(ifNode));
    }

    [Fact]
    public void IfNodeBranchRangesAreDisjoint()
    {
        // then  range: [k+1, k+1+W(then))
        // else  range: [k+1+W(then), k+1+W(then)+W(else))
        // Total width accounts for both so the next node always starts at k+Width(if),
        // regardless of which branch executed.
        var then = new TextNode("\"yes\"");
        var otherwise = new TextNode("\"no\"");
        int thenW = SequenceAllocator.Width(then);
        int elseW = SequenceAllocator.Width(otherwise);
        Assert.Equal(1 + thenW + elseW, SequenceAllocator.Width(new IfNode("_v", then, otherwise)));
    }

    [Fact]
    public void NodeAfterIfReceivesSequenceStableAcrossBranches()
    {
        // VStack(If(...), Text("Always"))
        // Text starts at 1 + Width(If) regardless of which branch If took.
        var then = new TextNode("\"yes\"");
        var otherwise = new ButtonNode("\"no\"", "() => { }");
        var ifNode = new IfNode("_visible", then, otherwise);
        var textNode = new TextNode("\"always\"");
        var vstack = new VStackNode(ImmutableArray.Create<RenderNode>(ifNode, textNode));

        int expectedVStackWidth = 1 + SequenceAllocator.Width(ifNode) + SequenceAllocator.Width(textNode);
        Assert.Equal(expectedVStackWidth, SequenceAllocator.Width(vstack));
    }
}
