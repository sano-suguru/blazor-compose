using System;
using System.Linq;

namespace BlazorCompose.Compiler.Generation;

/// <summary>
/// Computes preorder sequence widths for the SSC render-node tree.
///
/// <para>
/// <em>Width</em> is defined as the count of <c>RenderTreeBuilder</c> calls that consume a sequence
/// argument in the subtree rooted at the given node.  <c>CloseElement</c> and <c>CloseRegion</c>
/// do <em>not</em> consume sequence numbers and are therefore excluded from the width calculation.
/// </para>
/// </summary>
internal static class SequenceAllocator
{
    /// <summary>
    /// Returns the number of sequence-consuming <c>RenderTreeBuilder</c> calls produced by
    /// <paramref name="node"/> and its entire subtree.
    /// </summary>
    public static int Width(RenderNode node) => node switch
    {
        // OpenElement("span") + AddContent = 2 calls
        TextNode => 2,

        // OpenElement("button") + AddAttribute("onclick") + AddContent = 3 calls
        ButtonNode => 3,

        // OpenElement("div") = 1 call, plus the sum of all children
        VStackNode { Children: var children } => 1 + children.Sum(Width),

        // IfNode allocation is implemented in Task 5.
        IfNode => throw new NotSupportedException(
            "IfNode width allocation is not yet implemented (Task 5)."),

        _ => throw new NotSupportedException(
            $"Unknown RenderNode type '{node.GetType().Name}'; add a Width case for it."),
    };
}
