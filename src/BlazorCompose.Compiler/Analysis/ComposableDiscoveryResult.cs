using System.Collections.Immutable;
using BlazorCompose.Compiler.Diagnostics;

namespace BlazorCompose.Compiler.Analysis;

/// <summary>
/// The value-equal output of discovering one <c>[Composable]</c> declaration: the registry
/// <see cref="Entry"/> (always present, even for invalid declarations) and the declaration-time
/// <see cref="Diagnostics"/> captured as symbol-free data.
/// </summary>
internal sealed record ComposableDiscoveryResult(
    ComposableDefinitionEntry Entry,
    ImmutableArray<DiagnosticInfo> Diagnostics)
{
    public bool Equals(ComposableDiscoveryResult? other) =>
        other is not null
        && Entry == other.Entry
        && StructuralEquality.ArrayEquals(Diagnostics, other.Diagnostics);

    public override int GetHashCode()
    {
        var hash = 17;
        hash = unchecked(hash * 31 + Entry.GetHashCode());
        hash = unchecked(hash * 31 + StructuralEquality.ArrayHashCode(Diagnostics));
        return hash;
    }
}
