using Microsoft.CodeAnalysis;

namespace BlazorCompose.Compiler;

/// <summary>Shared symbol predicates for <c>ComposeComponentBase</c> subclasses.</summary>
internal static class ComposeComponentBaseFacts
{
    internal static bool InheritsFromComposeComponentBase(INamedTypeSymbol symbol)
    {
        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (current.Name == "ComposeComponentBase" &&
                current.ContainingNamespace is { IsGlobalNamespace: false, Name: "BlazorCompose" } ns &&
                ns.ContainingNamespace.IsGlobalNamespace)
            {
                return true;
            }
        }

        return false;
    }
}
