using System.Collections.Immutable;
using BlazorCompose.Compiler.Diagnostics;

namespace BlazorCompose.Compiler.Analysis;

/// <summary>
/// The value-equal output of discovering one <c>[Composable]</c> declaration: the registry
/// <see cref="Entry"/> (always present, even for invalid declarations) and the declaration-time
/// <see cref="Diagnostics"/> captured as symbol-free data.
/// </summary>
internal sealed record ComposableDiscoveryResult
{
    public ComposableDiscoveryResult(
        ComposableDefinitionEntry entry,
        ImmutableArray<DiagnosticInfo> diagnostics)
    {
        Entry = entry;
        Diagnostics = diagnostics;
    }

    public ComposableDefinitionEntry Entry { get; }

    public EquatableArray<DiagnosticInfo> Diagnostics { get; }
}
