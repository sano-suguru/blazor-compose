using BlazorCompose.Compiler.Analysis;
using BlazorCompose.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCompose.Compiler;

/// <summary>
/// Incremental source generator entry point.  Discovers <c>ComposeComponentBase</c> subclasses and
/// emits a <c>RenderBody</c> override into the same partial class, and discovers <c>[Composable]</c>
/// definitions into a value-equal registry while reporting declaration-time BC1002 diagnostics.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class BlazorComposeGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Resolve the factory-method symbols once per compilation so incremental rebuilds that
        // change only component syntax do not re-walk the BlazorCompose.UI type members.
        var knownSymbols = context.CompilationProvider
            .Select(static (compilation, _) => KnownSymbols.TryCreate(compilation))
            .WithTrackingName("KnownSymbols");

        var syntaxCandidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                static (ctx, _) => ctx)
            .WithTrackingName("CandidateDiscovery");

        // Discover [Composable] definitions.  Definition-side SSC analysis resolves KnownSymbols
        // transiently from the definition's compilation so the transform output stays value-equal and
        // free of Roslyn symbols.
        var discoveryResults = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "BlazorCompose.ComposableAttribute",
                static (node, _) => node is MethodDeclarationSyntax,
                static (attributeContext, cancellationToken) =>
                {
                    var symbols = KnownSymbols.TryCreate(attributeContext.SemanticModel.Compilation);
                    return ComposableDefinitionFactory.Create(attributeContext, symbols, cancellationToken);
                })
            .WithTrackingName("ComposableDiscovery");

        // Report declaration-time diagnostics per definition so unchanged declarations are not
        // re-reported, reconstructing each location from the captured symbol-free coordinates.
        context.RegisterSourceOutput(
            discoveryResults,
            static (productionContext, result) =>
            {
                foreach (var diagnostic in result.Diagnostics)
                    productionContext.ReportDiagnostic(diagnostic.ToDiagnostic(DiagnosticDescriptors.BC1002));
            });

        // Collect every source composable entry — including invalid declarations — into a
        // deterministic value-equal registry consumed by call-site expansion.
        var registry = discoveryResults
            .Select(static (result, _) => result.Entry)
            .Collect()
            .Select(static (entries, _) => ComposableRegistry.Create(entries))
            .WithTrackingName("ComposableRegistry");

        // Model each candidate component against the registry so composable calls expand statically.
        // The tracking name observes the value-equal ComponentModelResult stream so incremental caching
        // tests can identify each component (or diagnostic-only result) by value, and so an unchanged
        // rerun is Cached/Unchanged even on the diagnostic branch.
        var modelResults = syntaxCandidates
            .Combine(knownSymbols)
            .Combine(registry)
            .Select(static (input, cancellationToken) =>
                ComponentModelFactory.TryCreate(
                    input.Left.Left,
                    input.Left.Right,
                    input.Right,
                    cancellationToken))
            .WithTrackingName("ComponentModeling");

        // Report model (call-site expansion) diagnostics separately, reconstructing Roslyn diagnostics
        // only inside the output callback.
        context.RegisterSourceOutput(
            modelResults,
            static (productionContext, result) =>
            {
                foreach (var diagnostic in result.Diagnostics)
                    productionContext.ReportDiagnostic(diagnostic.ToDiagnostic(DiagnosticDescriptors.BC1002));
            });

        // Add source only when a final model exists.
        var components = modelResults
            .Select(static (result, _) => result.Model)
            .Where(static model => model is not null);

        context.RegisterSourceOutput(
            components,
            static (productionContext, model) =>
                productionContext.AddSource(model!.HintName, RenderBodyEmitter.Emit(model)));
    }
}
