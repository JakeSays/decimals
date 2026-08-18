// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace Decimals.Conformance;

/// <summary>
/// Drives <see cref="Decimal128"/> from the corpus, which reaches it through the
/// <c>decQuad</c> group.
/// </summary>
public readonly struct Decimal128Target : IDecTestTarget<Decimal128Target, Decimal128>
{
    public static string Name => "Decimal128";

    public static int Precision => 34;

    public static int MaxExponent => 6144;

    public static int MinExponent => -6143;

    public static int HexDigitCount => 32;

    public static Decimal128 FromString(ReadOnlySpan<char> text, ref DecimalContext context)
    {
        return Decimal128.FromString(text, ref context);
    }

    public static Decimal128 FromDpdHex(ReadOnlySpan<char> hex)
    {
        return Decimal128.FromDpdBits(UInt128.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    public static string ToDpdHex(Decimal128 value)
    {
        return value.ToDpdBits().ToString("x32", CultureInfo.InvariantCulture);
    }

    public static string ToScientificString(Decimal128 value) => value.ToString();

    public static string ToEngineeringString(Decimal128 value) => value.ToEngineeringString();

    public static DecimalClass Classify(Decimal128 value) => Decimal128.Class(value);

    public static bool TryApply(DecTestOperation operation, ReadOnlySpan<Decimal128> operands,
        ref DecimalContext context, out Decimal128 result)
    {
        switch (operation)
        {
            case DecTestOperation.Apply:
            case DecTestOperation.Canonical:
            case DecTestOperation.Copy:
                result = operands[0];
                return true;
            case DecTestOperation.CopyAbs:
                result = Decimal128.CopyAbs(operands[0]);
                return true;
            case DecTestOperation.CopyNegate:
                result = Decimal128.CopyNegate(operands[0]);
                return true;
            case DecTestOperation.Add:
                result = Decimal128.Add(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Subtract:
                result = Decimal128.Subtract(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Multiply:
                result = Decimal128.Multiply(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Divide:
                result = Decimal128.Divide(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.DivideInt:
                result = Decimal128.DivideInteger(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Remainder:
                result = Decimal128.Remainder(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.RemainderNear:
                result = Decimal128.RemainderNear(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Fma:
                result = Decimal128.FusedMultiplyAdd(operands[0], operands[1], operands[2], ref context);
                return true;
            case DecTestOperation.Abs:
                result = Decimal128.Abs(operands[0], ref context);
                return true;
            case DecTestOperation.Plus:
                result = Decimal128.Plus(operands[0], ref context);
                return true;
            case DecTestOperation.Minus:
                result = Decimal128.Minus(operands[0], ref context);
                return true;
            case DecTestOperation.Compare:
                result = Decimal128.Compare(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.CompareSig:
                result = Decimal128.CompareSignal(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.CopySign:
                result = Decimal128.CopySign(operands[0], operands[1]);
                return true;
            case DecTestOperation.CompareTotal:
                result = FromComparison(Decimal128.CompareTotal(operands[0], operands[1]));
                return true;
            case DecTestOperation.CompareTotalMag:
                result = FromComparison(Decimal128.CompareTotalMagnitude(operands[0], operands[1]));
                return true;
            case DecTestOperation.SameQuantum:
                result = Decimal128.SameQuantum(operands[0], operands[1]) ? Decimal128.One : Decimal128.Zero;
                return true;
            case DecTestOperation.And:
                result = Decimal128.And(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Or:
                result = Decimal128.Or(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Xor:
                result = Decimal128.Xor(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Invert:
                result = Decimal128.Invert(operands[0], ref context);
                return true;
            case DecTestOperation.Max:
                result = Decimal128.Max(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Min:
                result = Decimal128.Min(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.MaxMag:
                result = Decimal128.MaxMagnitude(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.MinMag:
                result = Decimal128.MinMagnitude(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.LogB:
                result = Decimal128.LogB(operands[0], ref context);
                return true;
            case DecTestOperation.ScaleB:
                result = Decimal128.ScaleB(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Reduce:
                result = Decimal128.Reduce(operands[0], ref context);
                return true;
            case DecTestOperation.Trim:
                result = Decimal128.Trim(operands[0]);
                return true;
            case DecTestOperation.ToIntegral:
                result = Decimal128.RoundToIntegral(operands[0], ref context);
                return true;
            case DecTestOperation.ToIntegralX:
                result = Decimal128.RoundToIntegralExact(operands[0], ref context);
                return true;
            case DecTestOperation.Quantize:
                result = Decimal128.Quantize(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Rotate:
                result = Decimal128.Rotate(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Shift:
                result = Decimal128.Shift(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.NextPlus:
                result = Decimal128.NextPlus(operands[0], ref context);
                return true;
            case DecTestOperation.NextMinus:
                result = Decimal128.NextMinus(operands[0], ref context);
                return true;
            case DecTestOperation.NextToward:
                result = Decimal128.NextToward(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Exp:
                result = Decimal128.Exp(operands[0], ref context);
                return true;
            case DecTestOperation.Ln:
                result = Decimal128.Log(operands[0], ref context);
                return true;
            case DecTestOperation.Log10:
                result = Decimal128.Log10(operands[0], ref context);
                return true;
            case DecTestOperation.Power:
                result = Decimal128.Pow(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.SquareRoot:
                result = Decimal128.Sqrt(operands[0], ref context);
                return true;
            default:
                result = Decimal128.Zero;
                return false;
        }
    }

    private static Decimal128 FromComparison(int comparison)
    {
        if (comparison < 0)
        {
            return Decimal128.NegativeOne;
        }

        return comparison > 0 ? Decimal128.One : Decimal128.Zero;
    }
}
