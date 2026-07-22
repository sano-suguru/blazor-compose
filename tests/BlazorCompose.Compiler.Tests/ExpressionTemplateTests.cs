using System.Collections.Immutable;
using BlazorCompose.Compiler;

namespace BlazorCompose.Compiler.Tests;

public sealed class ExpressionTemplateTests
{
    [Fact]
    public void SubstituteReplacesOnlyParameterHoles()
    {
        var template = ExpressionTemplate.Create(
            ImmutableArray.Create<ExpressionSegment>(
                new LiteralExpressionSegment("$\""),
                new ParameterHoleExpressionSegment(0),
                new LiteralExpressionSegment(" value\"")));

        var result = template.Substitute(ImmutableArray.Create("__bc_arg_1_0"));

        Assert.Equal("$\"__bc_arg_1_0 value\"", result.ToCode());
    }

    [Fact]
    public void StructurallyEqualTemplatesCompareEqual()
    {
        var left = ExpressionTemplate.Create(
            ImmutableArray.Create<ExpressionSegment>(
                new LiteralExpressionSegment("prefix "),
                new ParameterHoleExpressionSegment(1)));
        var right = ExpressionTemplate.Create(
            ImmutableArray.Create<ExpressionSegment>(
                new LiteralExpressionSegment("prefix "),
                new ParameterHoleExpressionSegment(1)));

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }
}
