using Microsoft.CodeAnalysis;

namespace BlazorCompose.Compiler.Analysis;

/// <summary>
/// Caches resolved <see cref="IMethodSymbol"/> references for the <c>BlazorCompose.UI</c> factory
/// methods so that expression analysis can compare symbols by identity rather than by name.
/// </summary>
internal sealed class KnownSymbols
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
}
