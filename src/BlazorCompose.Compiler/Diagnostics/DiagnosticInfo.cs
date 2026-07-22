using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace BlazorCompose.Compiler.Diagnostics;

/// <summary>
/// Value-equal, symbol-free representation of a diagnostic captured while source syntax was
/// available.  Incremental generator values must never carry <see cref="Diagnostic"/>,
/// <see cref="Location"/>, <see cref="SyntaxTree"/>, <see cref="ISymbol"/>, or
/// <see cref="SemanticModel"/>; this record captures only the primitive coordinates required to
/// reconstruct a location in <c>RegisterSourceOutput</c>.
/// </summary>
internal sealed record DiagnosticInfo(
    string Id,
    string FilePath,
    TextSpan Span,
    LinePositionSpan LineSpan,
    EquatableArray<string> MessageArguments)
{
    /// <summary>Captures a <see cref="DiagnosticInfo"/> from a live <see cref="Location"/>.</summary>
    public static DiagnosticInfo Create(
        string id,
        Location location,
        ImmutableArray<string> messageArguments)
    {
        var lineSpan = location.GetLineSpan();
        return new DiagnosticInfo(
            id,
            lineSpan.Path ?? string.Empty,
            location.SourceSpan,
            lineSpan.Span,
            messageArguments);
    }

    /// <summary>
    /// Reconstructs a <see cref="Diagnostic"/> for <c>RegisterSourceOutput</c> using the captured
    /// coordinates and the supplied descriptor (matched by <see cref="Id"/> at the call site).
    /// </summary>
    public Diagnostic ToDiagnostic(DiagnosticDescriptor descriptor)
    {
        var location = Location.Create(FilePath, Span, LineSpan);
        return Diagnostic.Create(descriptor, location, MessageArguments.ToArray<object?>());
    }
}
