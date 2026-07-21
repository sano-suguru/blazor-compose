using System.Collections.Generic;
using System.Collections.Immutable;

namespace BlazorCompose.Compiler;

internal sealed record ComposableInvocationArgument(
    int ParameterOrdinal,
    int SourceOrder,
    string ParameterTypeName,
    bool IsImplicitDefault,
    ExpressionTemplate Value);

internal abstract record RenderTemplateNode;

internal sealed record TextTemplateNode(ExpressionTemplate Content) : RenderTemplateNode;

internal sealed record ButtonTemplateNode(
    ExpressionTemplate Label,
    ExpressionTemplate Handler) : RenderTemplateNode;

internal sealed record VStackTemplateNode(
    ImmutableArray<RenderTemplateNode> Children) : RenderTemplateNode
{
    public bool Equals(VStackTemplateNode? other) =>
        other is not null && StructuralEquals(Children, other.Children);

    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var child in Children)
            hash = unchecked(hash * 31 + (child?.GetHashCode() ?? 0));
        return hash;
    }

    private static bool StructuralEquals(
        ImmutableArray<RenderTemplateNode> left,
        ImmutableArray<RenderTemplateNode> right)
    {
        if (left.Length != right.Length)
            return false;

        for (var index = 0; index < left.Length; index++)
        {
            if (!EqualityComparer<RenderTemplateNode>.Default.Equals(
                    left[index],
                    right[index]))
            {
                return false;
            }
        }

        return true;
    }
}

internal sealed record IfTemplateNode(
    ExpressionTemplate Condition,
    RenderTemplateNode Then,
    RenderTemplateNode? Otherwise) : RenderTemplateNode;

internal sealed record ComposableCallTemplateNode(
    string MethodKey,
    ImmutableArray<ComposableInvocationArgument> Arguments) : RenderTemplateNode
{
    public bool Equals(ComposableCallTemplateNode? other) =>
        other is not null
        && MethodKey == other.MethodKey
        && StructuralEquals(Arguments, other.Arguments);

    public override int GetHashCode()
    {
        var hash = unchecked(17 * 31 + MethodKey.GetHashCode());
        foreach (var argument in Arguments)
            hash = unchecked(hash * 31 + (argument?.GetHashCode() ?? 0));
        return hash;
    }

    private static bool StructuralEquals(
        ImmutableArray<ComposableInvocationArgument> left,
        ImmutableArray<ComposableInvocationArgument> right)
    {
        if (left.Length != right.Length)
            return false;

        for (var index = 0; index < left.Length; index++)
        {
            if (!EqualityComparer<ComposableInvocationArgument>.Default.Equals(
                    left[index],
                    right[index]))
            {
                return false;
            }
        }

        return true;
    }
}
