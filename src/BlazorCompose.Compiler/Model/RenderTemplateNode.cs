using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace BlazorCompose.Compiler;

/// <summary>
/// Symbol-free, value-equal capture of a source location for a template node.  Stores only the
/// primitive coordinates required to reconstruct a <see cref="Location"/> at diagnostic-report time,
/// following the same discipline as <see cref="Diagnostics.DiagnosticInfo"/>.
/// </summary>
internal readonly record struct TemplateLocation(
    string FilePath,
    TextSpan Span,
    LinePositionSpan LineSpan)
{
    public static TemplateLocation From(Location location)
    {
        var lineSpan = location.GetLineSpan();
        return new TemplateLocation(lineSpan.Path ?? string.Empty, location.SourceSpan, lineSpan.Span);
    }

    public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);
}

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
    string DisplayName,
    ImmutableArray<ComposableInvocationArgument> Arguments,
    TemplateLocation Location) : RenderTemplateNode
{
    public bool Equals(ComposableCallTemplateNode? other) =>
        other is not null
        && MethodKey == other.MethodKey
        && DisplayName == other.DisplayName
        && Location.Equals(other.Location)
        && StructuralEquals(Arguments, other.Arguments);

    public override int GetHashCode()
    {
        var hash = unchecked(17 * 31 + MethodKey.GetHashCode());
        hash = unchecked(hash * 31 + DisplayName.GetHashCode());
        hash = unchecked(hash * 31 + Location.GetHashCode());
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
