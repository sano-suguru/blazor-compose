using System;
using System.Collections.Generic;
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
    ImmutableArray<string> MessageArguments)
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
            messageArguments.IsDefault ? [] : messageArguments);
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

    public bool Equals(DiagnosticInfo? other) =>
        other is not null
        && Id == other.Id
        && FilePath == other.FilePath
        && Span.Equals(other.Span)
        && LineSpan.Equals(other.LineSpan)
        && SequenceEquals(MessageArguments, other.MessageArguments);

    public override int GetHashCode()
    {
        var hash = 17;
        hash = unchecked(hash * 31 + Id.GetHashCode());
        hash = unchecked(hash * 31 + FilePath.GetHashCode());
        hash = unchecked(hash * 31 + Span.GetHashCode());
        hash = unchecked(hash * 31 + LineSpan.GetHashCode());
        foreach (var argument in MessageArguments)
            hash = unchecked(hash * 31 + (argument?.GetHashCode() ?? 0));
        return hash;
    }

    private static bool SequenceEquals(ImmutableArray<string> left, ImmutableArray<string> right)
    {
        if (left.IsDefaultOrEmpty && right.IsDefaultOrEmpty)
            return true;
        if (left.IsDefault || right.IsDefault || left.Length != right.Length)
            return false;

        for (var index = 0; index < left.Length; index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}
