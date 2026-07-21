using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace BlazorCompose.Compiler.Diagnostics;

/// <summary>
/// Reports BC1001 when a class that inherits from <c>ComposeComponentBase</c> is not declared <c>partial</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PartialComponentAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.BC1001);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var symbol = (INamedTypeSymbol)context.Symbol;

        if (symbol.TypeKind != TypeKind.Class)
            return;

        if (!InheritsFromComposeComponentBase(symbol))
            return;

        var isPartial = symbol.DeclaringSyntaxReferences
            .Any(syntaxRef =>
                syntaxRef.GetSyntax() is ClassDeclarationSyntax classDecl &&
                classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)));

        if (isPartial)
            return;

        var firstDeclaration = symbol.DeclaringSyntaxReferences
            .Select(static r => r.GetSyntax())
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault();

        if (firstDeclaration is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.BC1001,
                firstDeclaration.Identifier.GetLocation(),
                symbol.Name));
        }
    }

    private static bool InheritsFromComposeComponentBase(INamedTypeSymbol symbol)
    {
        var current = symbol.BaseType;
        while (current is not null)
        {
            if (current.Name == "ComposeComponentBase" &&
                current.ContainingNamespace is { IsGlobalNamespace: false, Name: "BlazorCompose" } ns &&
                ns.ContainingNamespace.IsGlobalNamespace)
            {
                return true;
            }
            current = current.BaseType;
        }
        return false;
    }
}
