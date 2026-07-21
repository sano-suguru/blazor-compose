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
