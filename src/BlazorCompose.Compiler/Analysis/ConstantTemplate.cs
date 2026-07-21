using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace BlazorCompose.Compiler.Analysis;

/// <summary>
/// Builds normalized constant <see cref="ExpressionTemplate"/>s for optional-parameter defaults so that
/// a missing argument at an expansion site produces the same value the compiler would have supplied.
/// </summary>
internal static class ConstantTemplate
{
    public static ExpressionTemplate ForParameterDefault(IParameterSymbol parameter, string typeName)
    {
        if (!parameter.HasExplicitDefaultValue)
            return ExpressionTemplate.Literal($"default({typeName})");

        var value = parameter.ExplicitDefaultValue;
        if (value is null)
        {
            var isNonNullableValueType =
                parameter.Type.IsValueType
                && parameter.Type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T;

            return ExpressionTemplate.Literal(
                isNonNullableValueType ? $"default({typeName})" : "null");
        }

        if (parameter.Type.TypeKind == TypeKind.Enum)
        {
            return ExpressionTemplate.Literal(
                $"({typeName}){SymbolDisplay.FormatPrimitive(value, quoteStrings: false, useHexadecimalNumbers: false)}");
        }

        return ExpressionTemplate.Literal(
            SymbolDisplay.FormatPrimitive(value, quoteStrings: true, useHexadecimalNumbers: false)
                ?? $"default({typeName})");
    }
}
