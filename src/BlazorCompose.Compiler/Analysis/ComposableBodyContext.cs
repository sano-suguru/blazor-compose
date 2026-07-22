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
    private readonly System.Collections.Generic.Dictionary<ISymbol, int> _iterationOverlay =
        new(SymbolEqualityComparer.Default);
    private int _iterationDepth;

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
        Warnings = ImmutableArray.CreateBuilder<DiagnosticInfo>();
    }

    public SemanticModel SemanticModel { get; }

    public INamedTypeSymbol ContainingType { get; }

    public string MethodDisplayName { get; }

    public KnownSymbols KnownSymbols { get; }

    public CancellationToken CancellationToken { get; }

    public ImmutableArray<ComposableAccessRequirement>.Builder AccessRequirements { get; }

    public ImmutableArray<DiagnosticInfo>.Builder Diagnostics { get; }

    /// <summary>Non-fatal warnings (for example BC3002). Never gates definition building.</summary>
    public ImmutableArray<DiagnosticInfo>.Builder Warnings { get; }

    public bool TryGetParameterOrdinal(ISymbol symbol, out int ordinal)
    {
        if (_parameterOrdinals.TryGetValue(symbol, out ordinal))
            return true;
        return _iterationOverlay.TryGetValue(symbol, out ordinal);
    }

    /// <summary>
    /// Registers a ForEach iteration variable (its content and key lambda parameters denote the same
    /// loop variable) at the next free ordinal — base parameter count plus current nesting depth — so a
    /// reference to it becomes a parameter hole at that ordinal. Returns that ordinal.
    /// </summary>
    public int PushIterationVariable(ISymbol contentParameter, ISymbol keyParameter)
    {
        var ordinal = _parameterOrdinals.Count + _iterationDepth;
        _iterationOverlay[contentParameter] = ordinal;
        _iterationOverlay[keyParameter] = ordinal;
        _iterationDepth++;
        return ordinal;
    }

    /// <summary>Removes an iteration variable registered by <see cref="PushIterationVariable"/>.</summary>
    public void PopIterationVariable(ISymbol contentParameter, ISymbol keyParameter)
    {
        _iterationOverlay.Remove(contentParameter);
        _iterationOverlay.Remove(keyParameter);
        _iterationDepth--;
    }

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
