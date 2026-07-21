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
        var components = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                static (syntaxContext, cancellationToken) =>
                    ComponentModelFactory.TryCreate(syntaxContext, cancellationToken))
            .Where(static model => model is not null);

        context.RegisterSourceOutput(
            components,
            static (productionContext, model) =>
                productionContext.AddSource(model!.HintName, RenderBodyEmitter.Emit(model)));
    }
}
