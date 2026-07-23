using System.Collections.Generic;
using System.Collections.Immutable;
using BlazorCompose.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace BlazorCompose.Compiler.Analysis;

/// <summary>
/// Classifies a composable definition body expression into the statically sequenceable
/// <see cref="RenderTemplateNode"/> hierarchy.  Dynamic argument text is normalized through
/// <see cref="ExpressionTemplateFactory"/> so parameter references become holes and imports/containing
/// type context are preserved.  Nested <c>[Composable]</c> calls become <see cref="ComposableCallTemplateNode"/>.
/// Returns <see langword="null"/> when the expression cannot be statically analyzed.
/// </summary>
internal static class RenderExpressionAnalyzer
{
    public static RenderTemplateNode? Analyze(ExpressionSyntax expression, ComposableBodyContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();

        if (expression is not InvocationExpressionSyntax invocation)
            return null;

        if (context.SemanticModel
            .GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method)
            return null;

        var symbols = context.KnownSymbols;

        if (SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, symbols.TextMethod))
        {
            var content = invocation.ArgumentList.Arguments[0].Expression;
            return new TextTemplateNode(ExpressionTemplateFactory.Create(content, context));
        }

        if (SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, symbols.ButtonMethod))
        {
            var label = invocation.ArgumentList.Arguments[0].Expression;
            var handler = invocation.ArgumentList.Arguments[1].Expression;
            return new ButtonTemplateNode(
                ExpressionTemplateFactory.Create(label, context),
                ExpressionTemplateFactory.Create(handler, context));
        }

        if (SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, symbols.VStackMethod))
        {
            var children = ImmutableArray.CreateBuilder<RenderTemplateNode>(
                invocation.ArgumentList.Arguments.Count);
            foreach (var argument in invocation.ArgumentList.Arguments)
            {
                var child = Analyze(argument.Expression, context);
                if (child is null)
                    return null;
                children.Add(child);
            }

            return new VStackTemplateNode(children.ToImmutable());
        }

        if (SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, symbols.IfMethod))
        {
            var condition = invocation.ArgumentList.Arguments[0].Expression;
            var thenExpr = ExtractLambdaBody(invocation.ArgumentList.Arguments[1].Expression);
            if (thenExpr is null)
                return null;

            var thenNode = Analyze(thenExpr, context);
            if (thenNode is null)
                return null;

            RenderTemplateNode? otherwiseNode = null;
            if (invocation.ArgumentList.Arguments.Count >= 3)
            {
                var otherwiseArg = invocation.ArgumentList.Arguments[2].Expression;
                if (otherwiseArg is not LiteralExpressionSyntax
                    { Token.RawKind: (int)SyntaxKind.NullKeyword })
                {
                    var otherwiseExpr = ExtractLambdaBody(otherwiseArg);
                    if (otherwiseExpr is null)
                        return null;

                    otherwiseNode = Analyze(otherwiseExpr, context);
                    if (otherwiseNode is null)
                        return null;
                }
            }

            return new IfTemplateNode(
                ExpressionTemplateFactory.Create(condition, context),
                thenNode,
                otherwiseNode);
        }

        if (SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, symbols.ForEachMethod))
        {
            var sourceExpression = invocation.ArgumentList.Arguments[0].Expression;
            if (!TryExtractSingleParameterLambda(
                    invocation.ArgumentList.Arguments[1].Expression, out var keyParameter, out var keyBody)
                || !TryExtractSingleParameterLambda(
                    invocation.ArgumentList.Arguments[2].Expression, out var contentParameter, out var contentBody))
            {
                return null;
            }

            if (context.SemanticModel.GetDeclaredSymbol(keyParameter, context.CancellationToken) is not { } keyParamSymbol
                || context.SemanticModel.GetDeclaredSymbol(contentParameter, context.CancellationToken) is not { } contentParamSymbol)
            {
                return null;
            }

            // Source references the enclosing scope (fields, composable params, outer items) — never this
            // item — so it is normalized before the iteration variable is registered.
            var source = ExpressionTemplateFactory.Create(sourceExpression, context);

            var itemOrdinal = context.PushIterationVariable(contentParamSymbol, keyParamSymbol);
            try
            {
                var key = ExpressionTemplateFactory.Create(keyBody, context);
                var content = Analyze(contentBody, context);
                if (content is null)
                    return null;

                if (!KeyReferencesItemOrdinal(key, itemOrdinal))
                {
                    context.Diagnostics.Add(DiagnosticInfo.Create(
                        DiagnosticDescriptors.BC3002,
                        invocation.ArgumentList.Arguments[1].GetLocation(),
                        []));
                }

                return new ForEachTemplateNode(
                    source,
                    key,
                    content,
                    TemplateLocation.From(invocation.GetLocation()));
            }
            finally
            {
                context.PopIterationVariable(contentParamSymbol, keyParamSymbol);
            }
        }

        if (IsComposable(method, context))
        {
            var arguments = CreateInvocationArguments(invocation, method, context);
            if (arguments is null)
                return null;

            return new ComposableCallTemplateNode(
                MethodKey.Create(method),
                method.Name,
                arguments.Value,
                TemplateLocation.From(invocation.GetLocation()));
        }

        return null;
    }

    private static bool IsComposable(IMethodSymbol method, ComposableBodyContext context)
    {
        var attributeType = context.KnownSymbols.ComposableAttributeType;
        if (attributeType is null)
            return false;

        foreach (var attribute in method.OriginalDefinition.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType))
                return true;
        }

        return false;
    }

    private static ImmutableArray<ComposableInvocationArgument>? CreateInvocationArguments(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        ComposableBodyContext context)
    {
        if (context.SemanticModel.GetOperation(invocation, context.CancellationToken)
            is not IInvocationOperation operation)
        {
            return null;
        }

        // Explicitly supplied arguments sort by their source position; implicit/default arguments sort
        // after every supplied argument.  Operation arguments are parameter-ordered, so the enumeration
        // index cannot be used as source order.
        var builder = ImmutableArray.CreateBuilder<ComposableInvocationArgument>(operation.Arguments.Length);
        foreach (var argument in operation.Arguments)
        {
            var parameter = argument.Parameter;
            if (parameter is null)
                return null;

            var isImplicitDefault = argument.ArgumentKind == ArgumentKind.DefaultValue;
            var parameterTypeName = parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            ExpressionTemplate value;
            int sourceOrder;
            if (isImplicitDefault)
            {
                value = ConstantTemplate.ForParameterDefault(parameter, parameterTypeName);

                // Strictly increasing in parameter ordinal and always greater than any source
                // position (a small non-negative span start), so implicit defaults sort after every
                // supplied argument while staying in parameter order.  Subtracting the parameter count
                // before adding the ordinal keeps the value below int.MaxValue and cannot overflow,
                // unlike a formula that could add to int.MaxValue when trailing optionals are omitted.
                sourceOrder = int.MaxValue - method.Parameters.Length + parameter.Ordinal;
            }
            else
            {
                var argumentExpression = (argument.Syntax as ArgumentSyntax)?.Expression;
                if (argumentExpression is null)
                    return null;

                value = ExpressionTemplateFactory.Create(argumentExpression, context);
                sourceOrder = argument.Syntax.SpanStart;
            }

            builder.Add(new ComposableInvocationArgument(
                parameter.Ordinal,
                sourceOrder,
                parameterTypeName,
                isImplicitDefault,
                value));
        }

        return builder.ToImmutable();
    }

    private static ExpressionSyntax? ExtractLambdaBody(ExpressionSyntax expression) => expression switch
    {
        ParenthesizedLambdaExpressionSyntax { Body: ExpressionSyntax body } => body,
        SimpleLambdaExpressionSyntax { Body: ExpressionSyntax body } => body,
        _ => null,
    };

    private static bool TryExtractSingleParameterLambda(
        ExpressionSyntax expression,
        out ParameterSyntax parameter,
        out ExpressionSyntax body)
    {
        switch (expression)
        {
            case SimpleLambdaExpressionSyntax { Body: ExpressionSyntax simpleBody } simple:
                parameter = simple.Parameter;
                body = simpleBody;
                return true;
            // A list pattern ([var single]) on a SeparatedSyntaxList requires System.Index.GetOffset,
            // which is unavailable on netstandard2.0 (CS0656); match the single-parameter shape with an
            // explicit count check instead.
            case ParenthesizedLambdaExpressionSyntax { Body: ExpressionSyntax parenBody } paren
                when paren.ParameterList.Parameters.Count == 1:
                parameter = paren.ParameterList.Parameters[0];
                body = parenBody;
                return true;
            default:
                parameter = null!;
                body = null!;
                return false;
        }
    }

    private static bool KeyReferencesItemOrdinal(ExpressionTemplate key, int itemOrdinal)
    {
        foreach (var segment in key.Segments)
        {
            if (segment is ParameterHoleExpressionSegment hole && hole.ParameterOrdinal == itemOrdinal)
                return true;
        }

        return false;
    }
}
