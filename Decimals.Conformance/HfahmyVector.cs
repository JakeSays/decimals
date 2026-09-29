// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Conformance;

/// <summary>
/// One line of the Sayed-Ahmed and Fahmy test vectors.
/// </summary>
/// <remarks>
/// <para>
/// Example: <c>d64*+ =0 +12E-3 -4E5 +6E0 -> -48E2 x</c>. The fields are: the format and
/// operation as one token, the rounding mode, the operands, an arrow, the result, and the
/// IEEE 754 flags raised, if any. The syntax comes from IBM's FPGen vectors.
/// </para>
/// <para>
/// A finite value is a sign, an integer coefficient, <c>E</c>, and the quantum exponent.
/// For example, <c>+1E-398</c> is the smallest decimal64 subnormal. An infinity is
/// <c>+inf</c> or <c>-inf</c>, in any letter case. A NaN is <c>S</c> or <c>Q</c>, with no
/// sign or payload. The flags are <c>x</c> inexact, <c>u</c> underflow, <c>o</c> overflow,
/// <c>z</c> division by zero, and <c>i</c> invalid operation.
/// </para>
/// </remarks>
public sealed class HfahmyVector
{
    private HfahmyVector(string format, HfahmyVectorOperation operation, DecTestRounding rounding,
        string[] operands, string result, DecTestStatus flags)
    {
        Format = format;
        Operation = operation;
        Rounding = rounding;
        Operands = operands;
        Result = result;
        Flags = flags;
    }

    /// <summary><c>d64</c> or <c>d128</c>.</summary>
    public string Format { get; }

    public HfahmyVectorOperation Operation { get; }

    public DecTestRounding Rounding { get; }

    /// <summary>The operands as written on the line.</summary>
    public IReadOnlyList<string> Operands { get; }

    /// <summary>The result as written on the line.</summary>
    public string Result { get; }

    /// <summary>The flags the operation raises, as the runner's status bits.</summary>
    public DecTestStatus Flags { get; }

    public static bool TryParse(string line, out HfahmyVector? vector, out string error)
    {
        vector = null;
        error = string.Empty;

        var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0 || !TrySplitFormat(tokens[0], out var format, out var operationSymbol))
        {
            error = "the line does not start with a format and an operation";
            return false;
        }

        if (!TryParseOperation(operationSymbol, out var operation))
        {
            error = $"unknown operation '{operationSymbol}'";
            return false;
        }

        var operandCount = OperandCount(operation);
        var arrow = 2 + operandCount;

        if (tokens.Length < arrow + 2 || tokens.Length > arrow + 3 || tokens[arrow] != "->")
        {
            error = $"expected {operandCount} operands, an arrow, a result, and optional flags";
            return false;
        }

        if (!TryParseRounding(tokens[1], out var rounding))
        {
            error = $"unknown rounding mode '{tokens[1]}'";
            return false;
        }

        var flagText = tokens.Length == arrow + 3
            ? tokens[arrow + 2]
            : string.Empty;

        if (!TryParseFlags(flagText, out var flags))
        {
            error = $"unknown flags '{flagText}'";
            return false;
        }

        vector = new HfahmyVector(format, operation, rounding, tokens[2..arrow], tokens[arrow + 1], flags);
        return true;
    }

    /// <summary>
    /// Converts a value to the specification's syntax. Only the two NaN forms need
    /// converting; every other value already uses that syntax.
    /// </summary>
    public static string ToNumericString(string value)
    {
        return value switch
        {
            "S" => "sNaN",
            "Q" => "NaN",
            _ => value
        };
    }

    private static bool TrySplitFormat(string token, out string format, out string operationSymbol)
    {
        var end = 1;
        while (end < token.Length && char.IsAsciiDigit(token[end]))
        {
            end++;
        }

        format = token[..end];
        operationSymbol = token[end..];
        return token.StartsWith('d') && end > 1 && operationSymbol.Length > 0;
    }

    private static bool TryParseOperation(string symbol, out HfahmyVectorOperation operation)
    {
        operation = HfahmyVectorOperation.Add;
        switch (symbol)
        {
            case "+":
                operation = HfahmyVectorOperation.Add;
                return true;
            case "-":
                operation = HfahmyVectorOperation.Subtract;
                return true;
            case "*":
                operation = HfahmyVectorOperation.Multiply;
                return true;
            case "/":
                operation = HfahmyVectorOperation.Divide;
                return true;
            case "*+":
                operation = HfahmyVectorOperation.FusedMultiplyAdd;
                return true;
            case "*-":
                operation = HfahmyVectorOperation.FusedMultiplySubtract;
                return true;
            case "V":
                operation = HfahmyVectorOperation.SquareRoot;
                return true;
            default:
                return false;
        }
    }

    private static int OperandCount(HfahmyVectorOperation operation)
    {
        return operation switch
        {
            HfahmyVectorOperation.SquareRoot => 1,
            HfahmyVectorOperation.FusedMultiplyAdd or HfahmyVectorOperation.FusedMultiplySubtract => 3,
            _ => 2
        };
    }

    private static bool TryParseRounding(string token, out DecTestRounding rounding)
    {
        rounding = DecTestRounding.HalfEven;
        switch (token)
        {
            case ">":
                rounding = DecTestRounding.Ceiling;
                return true;
            case "<":
                rounding = DecTestRounding.Floor;
                return true;
            case "0":
                rounding = DecTestRounding.Down;
                return true;
            case "=0":
                rounding = DecTestRounding.HalfEven;
                return true;
            case "h>":
                rounding = DecTestRounding.HalfUp;
                return true;
            default:
                return false;
        }
    }

    private static bool TryParseFlags(string text, out DecTestStatus flags)
    {
        flags = DecTestStatus.None;
        foreach (var letter in text)
        {
            switch (letter)
            {
                case 'x':
                    flags |= DecTestStatus.Inexact;
                    break;
                case 'u':
                    flags |= DecTestStatus.Underflow;
                    break;
                case 'o':
                    flags |= DecTestStatus.Overflow;
                    break;
                case 'z':
                    flags |= DecTestStatus.DivisionByZero;
                    break;
                case 'i':
                    flags |= DecTestStatus.InvalidOperation;
                    break;
                default:
                    return false;
            }
        }

        return true;
    }
}
