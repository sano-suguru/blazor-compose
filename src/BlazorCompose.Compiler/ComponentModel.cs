using System.Collections.Immutable;
using BlazorCompose.Compiler.Diagnostics;

namespace BlazorCompose.Compiler;

/// <summary>
/// Represents a discovered Compose component together with the statically analyzed
/// render-node tree extracted from its <c>Body</c> expression.
/// </summary>
/// <remarks>
/// All fields contain only strings, primitive values, and nested <see cref="RenderNode"/> instances
/// so that the record can participate in Roslyn's incremental generator equality checks without
/// holding references to syntax nodes, symbols, semantic models, or compilations.
/// <c>ComponentModel</c> instances are only created when the <c>Body</c> expression is fully
/// SSC-analyzable; a null <c>RootNode</c> is therefore not representable.
/// </remarks>
internal sealed record ComponentModel(
    string HintName,
    string ClassName,
    string? Namespace,
    RenderNode RootNode);

/// <summary>
/// The value-equal outcome of modeling a candidate component: either a final <see cref="Model"/>
/// (with no diagnostics) or a set of call-site <see cref="Diagnostics"/> (with a null model).
/// </summary>
/// <remarks>
/// Diagnostics are captured as symbol-free <see cref="DiagnosticInfo"/> so the result stays value-equal
/// across incremental runs; they are deliberately excluded from <see cref="ComponentModel"/> equality and
/// reconstructed into Roslyn diagnostics only inside the source-output callback.  The no-diagnostic case is
/// normalized to <see cref="ImmutableArray{T}.Empty"/> so equality never depends on a default array.
/// </remarks>
internal sealed record ComponentModelResult
{
    /// <summary>A shared result carrying neither a model nor diagnostics.</summary>
    public static ComponentModelResult None { get; } =
        new(null, []);

    public ComponentModelResult(ComponentModel? model, ImmutableArray<DiagnosticInfo> diagnostics)
    {
        Model = model;
        Diagnostics = diagnostics;
    }

    public ComponentModel? Model { get; }

    public EquatableArray<DiagnosticInfo> Diagnostics { get; }
}
