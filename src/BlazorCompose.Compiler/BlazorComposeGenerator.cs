using BlazorCompose.Compiler.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCompose.Compiler;

/// <summary>
/// Incremental source generator entry point.  Discovers <c>ComposeComponentBase</c> subclasses and
/// emits a <c>RenderBody</c> override into the same partial class.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class BlazorComposeGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Resolve the factory-method symbols once per compilation so incremental rebuilds that
        // change only component syntax do not re-walk the BlazorCompose.UI type members.
        var knownSymbols = context.CompilationProvider
            .Select(static (compilation, _) => KnownSymbols.TryCreate(compilation));

        var syntaxCandidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                static (ctx, _) => ctx);

        var components = syntaxCandidates
            .Combine(knownSymbols)
            .Select(static (pair, cancellationToken) =>
                ComponentModelFactory.TryCreate(pair.Left, pair.Right, cancellationToken))
            .Where(static model => model is not null);

        context.RegisterSourceOutput(
            components,
            static (productionContext, model) =>
                productionContext.AddSource(model!.HintName, RenderBodyEmitter.Emit(model)));
    }
}
