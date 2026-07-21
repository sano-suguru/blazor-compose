using System;
using Microsoft.CodeAnalysis;

namespace BlazorCompose.Compiler.Analysis;

/// <summary>
/// Caches resolved <see cref="IMethodSymbol"/> references for the <c>BlazorCompose.UI</c> factory
/// methods so that expression analysis can compare symbols by identity rather than by name.
/// </summary>
/// <remarks>
/// Implements value equality based on a stable signature fingerprint of each recognized method.
/// The fingerprint captures the fully qualified return type and parameter signatures so that any
/// change to the <c>BlazorCompose.UI</c> API surface (e.g., adding/removing/retyping a parameter)
/// invalidates the downstream pipeline.  Symbol instances are retained for current-run identity
/// checks in <see cref="ComponentModelFactory"/> but are never compared across compilations.
/// </remarks>
internal sealed class KnownSymbols : IEquatable<KnownSymbols>
{
    /// <summary>Resolved symbol for <c>BlazorCompose.UI.Text(string)</c>, or <see langword="null"/> if unavailable.</summary>
    public IMethodSymbol? TextMethod { get; }

    /// <summary>Resolved symbol for <c>BlazorCompose.UI.Button(string, Action)</c>, or <see langword="null"/> if unavailable.</summary>
    public IMethodSymbol? ButtonMethod { get; }

    /// <summary>Resolved symbol for <c>BlazorCompose.UI.VStack(params View[])</c>, or <see langword="null"/> if unavailable.</summary>
    public IMethodSymbol? VStackMethod { get; }

    /// <summary>Resolved symbol for <c>BlazorCompose.UI.If(bool, Func&lt;View&gt;, Func&lt;View&gt;?)</c>, or <see langword="null"/> if unavailable.</summary>
    public IMethodSymbol? IfMethod { get; }

    // Stable value fingerprint computed once at construction, used for equality/hashing.
    private readonly string _fingerprint;

    private KnownSymbols(INamedTypeSymbol uiType)
    {
        foreach (var member in uiType.GetMembers())
        {
            if (member is not IMethodSymbol method)
                continue;

            switch (method.Name)
            {
                case "Text" when method.Parameters.Length == 1:
                    TextMethod = method;
                    break;
                case "Button" when method.Parameters.Length == 2:
                    ButtonMethod = method;
                    break;
                case "VStack" when method.Parameters.Length == 1 && method.Parameters[0].IsParams:
                    VStackMethod = method;
                    break;
                case "If" when method.Parameters.Length == 3:
                    IfMethod = method;
                    break;
            }
        }

        _fingerprint = BuildFingerprint(TextMethod, ButtonMethod, VStackMethod, IfMethod);
    }

    /// <summary>
    /// Resolves <c>BlazorCompose.UI</c> from the given compilation and returns a populated instance,
    /// or <see langword="null"/> when the type cannot be found (e.g., the runtime assembly is not referenced).
    /// </summary>
    public static KnownSymbols? TryCreate(Compilation compilation)
    {
        var uiType = compilation.GetTypeByMetadataName("BlazorCompose.UI");
        return uiType is not null ? new KnownSymbols(uiType) : null;
    }

    // ---------------------------------------------------------------------------
    // Value equality — based on stable signature fingerprint
    // ---------------------------------------------------------------------------

    public bool Equals(KnownSymbols? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(_fingerprint, other._fingerprint, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj) => Equals(obj as KnownSymbols);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_fingerprint);

    // ---------------------------------------------------------------------------
    // Fingerprint construction
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Produces a deterministic string that changes whenever the semantic signature of any
    /// recognized UI method changes.  Format per method:
    /// <c>ContainingType.Name(ParamType1,ParamType2):ReturnType</c>
    /// A null method contributes the literal <c>-</c>.
    /// </summary>
    private static string BuildFingerprint(params IMethodSymbol?[] methods)
    {
        var builder = new System.Text.StringBuilder(256);
        for (int i = 0; i < methods.Length; i++)
        {
            if (i > 0) builder.Append('|');

            var m = methods[i];
            if (m is null)
            {
                builder.Append('-');
                continue;
            }

            builder.Append(m.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            builder.Append('.');
            builder.Append(m.Name);
            builder.Append('(');
            for (int p = 0; p < m.Parameters.Length; p++)
            {
                if (p > 0) builder.Append(',');
                if (m.Parameters[p].IsParams) builder.Append("params ");
                builder.Append(m.Parameters[p].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }
            builder.Append(')');
            builder.Append(':');
            builder.Append(m.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        return builder.ToString();
    }
}
