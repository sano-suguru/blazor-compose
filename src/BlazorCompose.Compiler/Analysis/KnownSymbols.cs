using System;
using Microsoft.CodeAnalysis;

namespace BlazorCompose.Compiler.Analysis;

/// <summary>
/// Caches resolved <see cref="IMethodSymbol"/> references for the <c>BlazorCompose.UI</c> factory
/// methods so that expression analysis can compare symbols by identity rather than by name.
/// </summary>
/// <remarks>
/// Implements value equality based on symbol availability (present/absent) rather than Roslyn
/// object identity.  This ensures that unrelated compilation changes (e.g., editing a different
/// syntax tree) do not invalidate every component model that was Combined with KnownSymbols.
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
    // Value equality — based on symbol presence, not Roslyn object identity
    // ---------------------------------------------------------------------------

    public bool Equals(KnownSymbols? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        // Two KnownSymbols instances are equal when all factory method slots have the same
        // presence (non-null vs. null).  Signature details cannot change without a recompile
        // of the runtime assembly which would already invalidate the CompilationProvider.
        return (TextMethod is not null) == (other.TextMethod is not null)
            && (ButtonMethod is not null) == (other.ButtonMethod is not null)
            && (VStackMethod is not null) == (other.VStackMethod is not null)
            && (IfMethod is not null) == (other.IfMethod is not null);
    }

    public override bool Equals(object? obj) => Equals(obj as KnownSymbols);

    public override int GetHashCode()
    {
        // Hash reflects presence/absence only — stable across compilations.
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + (TextMethod is not null ? 1 : 0);
            hash = hash * 31 + (ButtonMethod is not null ? 1 : 0);
            hash = hash * 31 + (VStackMethod is not null ? 1 : 0);
            hash = hash * 31 + (IfMethod is not null ? 1 : 0);
            return hash;
        }
    }
}
