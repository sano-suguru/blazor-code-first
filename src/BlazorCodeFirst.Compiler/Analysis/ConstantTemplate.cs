using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace BlazorCodeFirst.Compiler.Analysis;

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

        // Floating-point and decimal literals need an explicit type suffix (and NaN/Infinity need a
        // named constant) so the emitted default round-trips into the declared parameter type instead of
        // decaying to a bare double literal that fails CS0664 when assigned to a float or decimal local.
        var floating = FormatFloatingPointLiteral(value);
        if (floating is not null)
            return ExpressionTemplate.Literal(floating);

        return ExpressionTemplate.Literal(
            SymbolDisplay.FormatPrimitive(value, quoteStrings: true, useHexadecimalNumbers: false)
                ?? $"default({typeName})");
    }

    private static string? FormatFloatingPointLiteral(object value) => value switch
    {
        float singleValue => FormatSingle(singleValue),
        double doubleValue => FormatDouble(doubleValue),
        decimal decimalValue => decimalValue.ToString(CultureInfo.InvariantCulture) + "M",
        _ => null,
    };

    private static string FormatSingle(float value)
    {
        if (float.IsNaN(value))
            return "float.NaN";
        if (float.IsPositiveInfinity(value))
            return "float.PositiveInfinity";
        if (float.IsNegativeInfinity(value))
            return "float.NegativeInfinity";

        return value.ToString("R", CultureInfo.InvariantCulture) + "F";
    }

    private static string FormatDouble(double value)
    {
        if (double.IsNaN(value))
            return "double.NaN";
        if (double.IsPositiveInfinity(value))
            return "double.PositiveInfinity";
        if (double.IsNegativeInfinity(value))
            return "double.NegativeInfinity";

        return value.ToString("R", CultureInfo.InvariantCulture);
    }
}
