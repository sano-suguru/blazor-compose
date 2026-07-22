using Microsoft.CodeAnalysis;

namespace BlazorCompose.Compiler.Diagnostics;

internal static class DiagnosticDescriptors
{
    /// <summary>
    /// BC1001: A class deriving from <c>ComposeComponentBase</c> must be declared <c>partial</c>
    /// so the source generator can emit the <c>RenderBody</c> override.
    /// </summary>
    public static readonly DiagnosticDescriptor BC1001 = new(
        id: "BC1001",
        title: "ComposeComponentBase subclass must be partial",
        messageFormat: "'{0}' derives from ComposeComponentBase but is not declared partial; add the partial modifier",
        category: "BlazorCompose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Classes that derive from ComposeComponentBase must be declared partial so the source generator can emit the RenderBody override.");

    /// <summary>
    /// BC1002: A <c>[Composable]</c> method does not satisfy the source generator's supported
    /// static-expansion contract.
    /// </summary>
    public static readonly DiagnosticDescriptor BC1002 = new(
        id: "BC1002",
        title: "Composable method shape is unsupported",
        messageFormat: "Composable method '{0}' is unsupported: {1}",
        category: "BlazorCompose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A method marked Composable must satisfy the compiler's supported static expansion contract.");

    /// <summary>
    /// BC3001: A <c>Body</c> getter must not mutate component state (single-direction
    /// data-flow violation).
    /// The initial detectable boundary covers statically identifiable direct writes (field assignments,
    /// property assignments, and increment/decrement operators) whose target is an instance member of
    /// the containing component.  Recognized deferred event handler lambdas (the <c>Button</c> onClick
    /// argument) are excluded.  Arbitrary interprocedural side effects are not guaranteed to be detected.
    /// </summary>
    public static readonly DiagnosticDescriptor BC3001 = new(
        id: "BC3001",
        title: "State mutation inside Body violates single-direction data flow",
        messageFormat: "'{0}' is mutated inside Body; move state changes to event handlers to preserve single-direction data flow",
        category: "BlazorCompose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "The Body getter must be a pure projection of state to UI. " +
            "Mutating component state inside Body causes render-time side effects that can corrupt " +
            "the rendering pipeline. Move the mutation to an event handler.");

    /// <summary>
    /// BC3002: A <c>ForEach</c> key selector does not reference its item and therefore cannot express
    /// per-item identity, defeating keyed diffing. Heuristic and intentionally conservative: it does not
    /// detect an item-derived-but-index-like key.
    /// </summary>
    public static readonly DiagnosticDescriptor BC3002 = new(
        id: "BC3002",
        title: "ForEach key selector may not identify items",
        messageFormat: "ForEach key selector does not reference the item; a key must identify each item so list state is preserved across reorders",
        category: "BlazorCompose",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "A ForEach key selector should return a value derived from the item so Blazor can preserve " +
            "per-row state across insertion, removal, and reordering. A key that ignores the item " +
            "(a constant, an external index, or another list's item) forces full re-rendering.");

    /// <summary>Resolves a captured diagnostic <paramref name="id"/> to its descriptor.</summary>
    public static DiagnosticDescriptor ById(string id) => id switch
    {
        "BC1001" => BC1001,
        "BC1002" => BC1002,
        "BC3001" => BC3001,
        "BC3002" => BC3002,
        _ => throw new System.ArgumentOutOfRangeException(nameof(id), id, "Unknown BlazorCompose diagnostic id."),
    };
}
