using System.Collections.Immutable;
using System.Threading;
using BlazorCompose.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace BlazorCompose.Compiler.Analysis;

/// <summary>
/// Carries the shared state required to normalize the expressions inside a single composable
/// definition body: the semantic model, the containing type, the parameter-to-ordinal map, the
/// resolved runtime symbols, and the accumulating access-requirement and diagnostic builders.
/// </summary>
internal sealed class ComposableBodyContext
{
    private readonly ImmutableDictionary<ISymbol, int> _parameterOrdinals;

    public ComposableBodyContext(
        SemanticModel semanticModel,
        INamedTypeSymbol containingType,
        string methodDisplayName,
        KnownSymbols knownSymbols,
        ImmutableDictionary<ISymbol, int> parameterOrdinals,
        CancellationToken cancellationToken)
    {
        SemanticModel = semanticModel;
        ContainingType = containingType;
        MethodDisplayName = methodDisplayName;
        KnownSymbols = knownSymbols;
        _parameterOrdinals = parameterOrdinals;
        CancellationToken = cancellationToken;
        AccessRequirements = ImmutableArray.CreateBuilder<ComposableAccessRequirement>();
        Diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
    }

    public SemanticModel SemanticModel { get; }

    public INamedTypeSymbol ContainingType { get; }

    public string MethodDisplayName { get; }

    public KnownSymbols KnownSymbols { get; }

    public CancellationToken CancellationToken { get; }

    public ImmutableArray<ComposableAccessRequirement>.Builder AccessRequirements { get; }

    public ImmutableArray<DiagnosticInfo>.Builder Diagnostics { get; }

    public bool TryGetParameterOrdinal(ISymbol symbol, out int ordinal) =>
        _parameterOrdinals.TryGetValue(symbol, out ordinal);

    /// <summary>
    /// Records a distinct accessibility requirement for a referenced member/type so expansion can
    /// reject inlining into a site that cannot legally name it.
    /// </summary>
    public void AddAccessRequirement(ComposableAccessRequirement requirement)
    {
        foreach (var existing in AccessRequirements)
        {
            if (existing == requirement)
                return;
        }

        AccessRequirements.Add(requirement);
    }

    /// <summary>
    /// Records a single declaration-time BC1002 for a body that references a symbol which cannot exist
    /// in generated component code (for example a local function or a local declared in an enclosing
    /// scope).  Only the first such reference is reported so a body yields exactly one declaration
    /// diagnostic regardless of how many unsupported references it contains.
    /// </summary>
    public void ReportUnsupportedReference(Location location, string reason)
    {
        if (Diagnostics.Count > 0)
            return;

        Diagnostics.Add(DiagnosticInfo.Create(
            DiagnosticDescriptors.BC1002.Id,
            location,
            [MethodDisplayName, reason]));
    }
}
