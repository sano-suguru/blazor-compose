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

        var components = syntaxCandidates
            .Combine(knownSymbols)
            .Select(static (pair, cancellationToken) =>
                ComponentModelFactory.TryCreate(pair.Left, pair.Right, cancellationToken))
            .Where(static model => model is not null)
            .WithTrackingName("ComponentModeling");

        context.RegisterSourceOutput(
            components,
            static (productionContext, model) =>
                productionContext.AddSource(model!.HintName, RenderBodyEmitter.Emit(model)));

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
        // deterministic value-equal registry for the expansion pass introduced in a later task.
        var registry = discoveryResults
            .Select(static (result, _) => result.Entry)
            .Collect()
            .Select(static (entries, _) => ComposableRegistry.Create(entries))
            .WithTrackingName("ComposableRegistry");

        // The registry is not yet consumed by expansion.  Register a no-op output so the provider
        // participates in the pipeline (keeping tracking-name coverage and value-equal caching alive)
        // without emitting sources; expansion will consume it in a later task.
        context.RegisterSourceOutput(registry, static (_, _) => { });
    }
}
