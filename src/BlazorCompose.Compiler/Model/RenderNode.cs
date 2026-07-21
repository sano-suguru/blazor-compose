using System.Collections.Generic;
using System.Collections.Immutable;

namespace BlazorCompose.Compiler;

/// <summary>
/// Discriminated union of statically sequenceable UI nodes extracted from a <c>Body</c> expression.
/// All fields contain only strings and primitive values so that instances are immutable and value-equal.
/// No syntax nodes, symbols, semantic models, or absolute TextSpan offsets are stored here.
/// </summary>
internal abstract record RenderNode;

/// <summary>Represents a <c>Text(expr)</c> call that emits an HTML <c>span</c>.</summary>
internal sealed record TextNode(string ContentExpression) : RenderNode;

/// <summary>Represents a <c>Button(label, handler)</c> call that emits an HTML <c>button</c>.</summary>
internal sealed record ButtonNode(string LabelExpression, string HandlerExpression) : RenderNode;

/// <summary>Represents a <c>VStack(children…)</c> call that emits an HTML <c>div</c> wrapper.</summary>
internal sealed record VStackNode(ImmutableArray<RenderNode> Children) : RenderNode
{
    // ImmutableArray<T> uses reference equality in the compiler-generated record Equals, so we
    // override to give structural equality that the incremental generator pipeline can use.
    // No 'virtual' here: sealed records do not allow new virtual members.
    public bool Equals(VStackNode? other) =>
        other is not null && StructuralEquals(Children, other.Children);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            foreach (var child in Children)
                hash = hash * 31 + (child?.GetHashCode() ?? 0);
            return hash;
        }
    }

    private static bool StructuralEquals(ImmutableArray<RenderNode> a, ImmutableArray<RenderNode> b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (!EqualityComparer<RenderNode>.Default.Equals(a[i], b[i]))
                return false;
        }
        return true;
    }
}

/// <summary>
/// Represents an <c>If(condition, then, otherwise)</c> call.
/// Allocation and emission are implemented in Task 5; the type is defined here so that the model
/// hierarchy is complete.
/// </summary>
internal sealed record IfNode(string ConditionExpression, RenderNode Then, RenderNode? Otherwise) : RenderNode;
