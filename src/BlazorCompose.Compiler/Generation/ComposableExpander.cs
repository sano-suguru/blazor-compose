using System;
using System.Collections.Immutable;
using BlazorCompose.Compiler.Analysis;
using BlazorCompose.Compiler.Diagnostics;

namespace BlazorCompose.Compiler.Generation;

/// <summary>
/// The value-equal outcome of expanding a component <c>Body</c> template: the final
/// <see cref="RenderNode"/> tree (or <see langword="null"/> when expansion failed) plus the call-site
/// BC1002 <see cref="Diagnostics"/> captured as symbol-free data.
/// </summary>
internal readonly record struct ExpansionResult(
    RenderNode? Node,
    ImmutableArray<DiagnosticInfo> Diagnostics);

/// <summary>
/// Statically expands <c>[Composable]</c> call template nodes into the emittable <see cref="RenderNode"/>
/// tree.  Every template node — including composable call nodes that consume no render sequence — is
/// assigned a global logical preorder ordinal used only to name the typed argument locals, so repeated
/// helpers at different depths cannot collide.  Expansion is a pure function of the template tree, the
/// registry, and the generated component's containing-type key; it performs no rendering and evaluates no
/// runtime composable calls.
/// </summary>
internal static class ComposableExpander
{
    internal static ExpansionResult Expand(
        RenderTemplateNode root,
        ComposableRegistry registry,
        string generatedContainingTypeKey)
    {
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var nextLogicalPreorderOrdinal = 0;

        var node = ExpandNode(
            root,
            ImmutableArray<string>.Empty,
            ref nextLogicalPreorderOrdinal,
            ImmutableArray<string>.Empty,
            registry,
            generatedContainingTypeKey,
            diagnostics);

        return new ExpansionResult(node, diagnostics.ToImmutable());
    }

    /// <param name="substitution">
    /// The local names bound to the enclosing composable's parameter holes; empty at the component
    /// <c>Body</c> root, where no holes exist.
    /// </param>
    /// <param name="activeMethodStack">
    /// The method keys currently being expanded along this path, used for cycle detection.  Sibling calls
    /// to the same composable are not cycles because each branch receives an independent immutable stack.
    /// </param>
    private static RenderNode? ExpandNode(
        RenderTemplateNode node,
        ImmutableArray<string> substitution,
        ref int nextLogicalPreorderOrdinal,
        ImmutableArray<string> activeMethodStack,
        ComposableRegistry registry,
        string generatedContainingTypeKey,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        // Every node consumes one logical preorder ordinal, assigned before its subtree is visited.
        var ordinal = nextLogicalPreorderOrdinal++;

        switch (node)
        {
            case TextTemplateNode text:
                return new TextNode(text.Content.Substitute(substitution));

            case ButtonTemplateNode button:
                return new ButtonNode(
                    button.Label.Substitute(substitution),
                    button.Handler.Substitute(substitution));

            case VStackTemplateNode vstack:
            {
                var children = ImmutableArray.CreateBuilder<RenderNode>(vstack.Children.Length);
                foreach (var child in vstack.Children)
                {
                    var expanded = ExpandNode(
                        child,
                        substitution,
                        ref nextLogicalPreorderOrdinal,
                        activeMethodStack,
                        registry,
                        generatedContainingTypeKey,
                        diagnostics);
                    if (expanded is null)
                        return null;
                    children.Add(expanded);
                }

                return new VStackNode(children.ToImmutable());
            }

            case IfTemplateNode ifNode:
            {
                var thenNode = ExpandNode(
                    ifNode.Then,
                    substitution,
                    ref nextLogicalPreorderOrdinal,
                    activeMethodStack,
                    registry,
                    generatedContainingTypeKey,
                    diagnostics);
                if (thenNode is null)
                    return null;

                RenderNode? otherwiseNode = null;
                if (ifNode.Otherwise is not null)
                {
                    otherwiseNode = ExpandNode(
                        ifNode.Otherwise,
                        substitution,
                        ref nextLogicalPreorderOrdinal,
                        activeMethodStack,
                        registry,
                        generatedContainingTypeKey,
                        diagnostics);
                    if (otherwiseNode is null)
                        return null;
                }

                return new IfNode(ifNode.Condition.Substitute(substitution), thenNode, otherwiseNode);
            }

            case ComposableCallTemplateNode call:
                return ExpandCall(
                    call,
                    ordinal,
                    substitution,
                    ref nextLogicalPreorderOrdinal,
                    activeMethodStack,
                    registry,
                    generatedContainingTypeKey,
                    diagnostics);

            default:
                return null;
        }
    }

    private static ExpansionNode? ExpandCall(
        ComposableCallTemplateNode call,
        int callPreorderOrdinal,
        ImmutableArray<string> substitution,
        ref int nextLogicalPreorderOrdinal,
        ImmutableArray<string> activeMethodStack,
        ComposableRegistry registry,
        string generatedContainingTypeKey,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        var methodKey = call.MethodKey;

        if (!registry.TryGet(methodKey, out var entry))
        {
            // A [Composable] method with no source declaration in this compilation (metadata-only) cannot
            // be inlined; report a call-site BC1002.
            diagnostics.Add(CreateDiagnostic(
                call,
                "no source declaration is available to expand at the call site"));
            return null;
        }

        foreach (var active in activeMethodStack)
        {
            if (string.Equals(active, methodKey, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    call,
                    "recursive composable expansion forms a cycle"));
                return null;
            }
        }

        if (entry.Definition is null)
        {
            // The declaration is present but invalid; BC1002 was already reported at the declaration, so
            // the call site suppresses a duplicate diagnostic and simply fails to expand.
            return null;
        }

        var definition = entry.Definition;

        foreach (var requirement in definition.AccessRequirements)
        {
            if (!SatisfiesAccess(requirement, definition.ContainingTypeKey, generatedContainingTypeKey))
            {
                diagnostics.Add(CreateDiagnostic(
                    call,
                    $"references '{requirement.SymbolDisplayName}' which is not accessible from the expansion site"));
                return null;
            }
        }

        var parameters = definition.Parameters;

        // One typed local per parameter, named from the call's logical preorder ordinal and the
        // parameter ordinal so names are unique across the whole component.
        var innerNames = new string[parameters.Length];
        foreach (var parameter in parameters)
            innerNames[parameter.Ordinal] = CreateLocalName(callPreorderOrdinal, parameter.Ordinal);
        var innerSubstitution = ImmutableArray.Create(innerNames);

        // Emit the locals in source evaluation order (supplied arguments by source position, then implicit
        // defaults) while binding each to its parameter ordinal.  Argument initializers reference the
        // caller's scope, so they are substituted with the outer names.
        var ordered = call.Arguments.ToArray();
        Array.Sort(ordered, static (left, right) => left.SourceOrder.CompareTo(right.SourceOrder));

        var locals = ImmutableArray.CreateBuilder<LocalBinding>(ordered.Length);
        foreach (var argument in ordered)
        {
            var parameter = parameters[argument.ParameterOrdinal];
            locals.Add(new LocalBinding(
                parameter.TypeName,
                innerNames[argument.ParameterOrdinal],
                argument.Value.Substitute(substitution)));
        }

        var body = ExpandNode(
            definition.Body,
            innerSubstitution,
            ref nextLogicalPreorderOrdinal,
            activeMethodStack.Add(methodKey),
            registry,
            generatedContainingTypeKey,
            diagnostics);
        if (body is null)
            return null;

        return new ExpansionNode(locals.ToImmutable(), body);
    }

    /// <summary>
    /// Determines whether an inlined body may legally name the referenced non-public member from the
    /// generated component type.  Without Roslyn symbols the expander can only compare containing-type
    /// keys, which is exact for private (<see cref="ComposableAccessRequirementKind.SameContainingType"/>)
    /// members and conservative for protected members.
    /// </summary>
    private static bool SatisfiesAccess(
        ComposableAccessRequirement requirement,
        string definitionContainingTypeKey,
        string generatedContainingTypeKey) =>
        string.Equals(definitionContainingTypeKey, generatedContainingTypeKey, StringComparison.Ordinal);

    private static string CreateLocalName(int callPreorderOrdinal, int parameterOrdinal) =>
        $"__bc_arg_{callPreorderOrdinal}_{parameterOrdinal}";

    private static DiagnosticInfo CreateDiagnostic(ComposableCallTemplateNode call, string reason) =>
        DiagnosticInfo.Create(
            DiagnosticDescriptors.BC1002.Id,
            call.Location.ToLocation(),
            ImmutableArray.Create(call.DisplayName, reason));
}
