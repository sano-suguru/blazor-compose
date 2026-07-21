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
}
