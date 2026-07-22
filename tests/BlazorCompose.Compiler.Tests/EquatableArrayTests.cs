using System.Collections.Immutable;
using System.Linq;
using BlazorCompose.Compiler;

namespace BlazorCompose.Compiler.Tests;

public sealed class EquatableArrayTests
{
    [Fact]
    public void Default_EqualsEmpty_AndSharesHashCode()
    {
        EquatableArray<string> fromDefault = default;
        EquatableArray<string> fromEmpty = ImmutableArray<string>.Empty;

        Assert.True(fromDefault.Equals(fromEmpty));
        Assert.True(fromDefault == fromEmpty);
        Assert.Equal(fromEmpty.GetHashCode(), fromDefault.GetHashCode());
    }

    [Fact]
    public void Default_LengthIndexerAndEnumeration_DoNotThrow()
    {
        EquatableArray<string> value = default;

        Assert.Equal(0, value.Length);
        Assert.True(value.IsDefaultOrEmpty);

        var enumerated = 0;
        foreach (var _ in value)
            enumerated++;
        Assert.Equal(0, enumerated);

        Assert.Throws<System.IndexOutOfRangeException>(() => { _ = value[0]; });
    }

    [Fact]
    public void EqualValues_FromDistinctArrays_AreEqualAndShareHashCode()
    {
        EquatableArray<string> left = ImmutableArray.Create("a", "b", "c");
        EquatableArray<string> right = ImmutableArray.Create("a", "b", "c");

        Assert.True(left.Equals(right));
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void DifferentValues_AreNotEqual()
    {
        EquatableArray<string> left = ImmutableArray.Create("a", "b");
        EquatableArray<string> right = ImmutableArray.Create("a", "c");

        Assert.False(left.Equals(right));
        Assert.True(left != right);
    }

    [Fact]
    public void DifferentLengths_AreNotEqual()
    {
        EquatableArray<string> left = ImmutableArray.Create("a");
        EquatableArray<string> right = ImmutableArray.Create("a", "b");

        Assert.False(left == right);
    }

    [Fact]
    public void NestedEquality_ThroughRecord_IsStructural()
    {
        var childA = new TextNode(ExpressionTemplate.Literal("x"));
        var childB = new TextNode(ExpressionTemplate.Literal("x"));

        var left = new VStackNode(ImmutableArray.Create<RenderNode>(childA));
        var right = new VStackNode(ImmutableArray.Create<RenderNode>(childB));

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void Enumeration_YieldsElementsInOrder()
    {
        EquatableArray<string> value = ImmutableArray.Create("a", "b", "c");

        string[] expected = ["a", "b", "c"];
        Assert.Equal(expected, value.ToArray());
    }
}
