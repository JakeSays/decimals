// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace Decimals.Conformance;

/// <summary>
/// Drives <see cref="Decimal64"/> from the corpus, which reaches it through the
/// <c>decDouble</c> group and the generated <c>d64</c> groups.
/// </summary>
/// <remarks>
/// The type carries its own context, rounding, status, and class enumerations, laid out
/// with the same values as the runner's so that the two convert by a cast. Every
/// operation runs under a context built from the runner's and its conditions are copied
/// back.
/// </remarks>
public readonly struct Decimal64Target : IDecTestTarget<Decimal64Target, Decimal64>
{
    public static string Name => "Decimal64";

    public static int Precision => 16;

    public static int MaxExponent => 384;

    public static int MinExponent => -383;

    public static int HexDigitCount => 16;

    /// <summary>The binary-integer form canonicalizes on the way in.</summary>
    public static bool PreservesNonCanonicalEncodings => false;

    public static Decimal64 FromString(ReadOnlySpan<char> text, ref DecTestContext context)
    {
        var inner = ToInner(context);
        var value = Decimal64.FromString(text, ref inner);
        CopyBack(ref context, inner);
        return value;
    }

    public static Decimal64 FromDpdHex(ReadOnlySpan<char> hex)
    {
        return Decimal64.FromDpdBits(ulong.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    public static string ToDpdHex(Decimal64 value)
    {
        return value.ToDpdBits().ToString("x16", CultureInfo.InvariantCulture);
    }

    public static string ToScientificString(Decimal64 value) => value.ToString();

    public static string ToEngineeringString(Decimal64 value) => value.ToEngineeringString();

    public static DecTestClass Classify(Decimal64 value) => (DecTestClass)(int)Decimal64.Class(value);

    public static bool TryApply(DecTestOperation operation, ReadOnlySpan<Decimal64> operands,
        ref DecTestContext context, out Decimal64 result)
    {
        var inner = ToInner(context);
        var applied = TryApply(operation, operands, ref inner, out result);
        CopyBack(ref context, inner);
        return applied;
    }

    private static bool TryApply(DecTestOperation operation, ReadOnlySpan<Decimal64> operands,
        ref Decimal64Context context, out Decimal64 result)
    {
        switch (operation)
        {
            case DecTestOperation.Apply:
            case DecTestOperation.Copy:
                result = operands[0];
                return true;
            case DecTestOperation.Canonical:
                result = Decimal64.Canonical(operands[0]);
                return true;
            case DecTestOperation.CopyAbs:
                result = Decimal64.CopyAbs(operands[0]);
                return true;
            case DecTestOperation.CopyNegate:
                result = Decimal64.CopyNegate(operands[0]);
                return true;
            case DecTestOperation.Add:
                result = Decimal64.Add(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Subtract:
                result = Decimal64.Subtract(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Multiply:
                result = Decimal64.Multiply(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Divide:
                result = Decimal64.Divide(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.DivideInt:
                result = Decimal64.DivideInteger(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Remainder:
                result = Decimal64.Remainder(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.RemainderNear:
                result = Decimal64.RemainderNear(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Fma:
                result = Decimal64.FusedMultiplyAdd(operands[0], operands[1], operands[2], ref context);
                return true;
            case DecTestOperation.Abs:
                result = Decimal64.Abs(operands[0], ref context);
                return true;
            case DecTestOperation.Plus:
                result = Decimal64.Plus(operands[0], ref context);
                return true;
            case DecTestOperation.Minus:
                result = Decimal64.Minus(operands[0], ref context);
                return true;
            case DecTestOperation.Compare:
                result = Decimal64.Compare(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.CompareSig:
                result = Decimal64.CompareSignal(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.CopySign:
                result = Decimal64.CopySign(operands[0], operands[1]);
                return true;
            case DecTestOperation.CompareTotal:
                result = FromComparison(Decimal64.CompareTotal(operands[0], operands[1]));
                return true;
            case DecTestOperation.CompareTotalMag:
                result = FromComparison(Decimal64.CompareTotalMagnitude(operands[0], operands[1]));
                return true;
            case DecTestOperation.SameQuantum:
                result = Decimal64.SameQuantum(operands[0], operands[1]) ? Decimal64.One : Decimal64.Zero;
                return true;
            case DecTestOperation.And:
                result = Decimal64.And(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Or:
                result = Decimal64.Or(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Xor:
                result = Decimal64.Xor(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Invert:
                result = Decimal64.Invert(operands[0], ref context);
                return true;
            case DecTestOperation.Max:
                result = Decimal64.Max(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Min:
                result = Decimal64.Min(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.MaxMag:
                result = Decimal64.MaxMagnitude(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.MinMag:
                result = Decimal64.MinMagnitude(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.LogB:
                result = Decimal64.LogB(operands[0], ref context);
                return true;
            case DecTestOperation.ScaleB:
                result = Decimal64.ScaleB(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Reduce:
                result = Decimal64.Reduce(operands[0], ref context);
                return true;
            case DecTestOperation.Trim:
                result = Decimal64.Trim(operands[0]);
                return true;
            case DecTestOperation.ToIntegral:
                result = Decimal64.RoundToIntegral(operands[0], ref context);
                return true;
            case DecTestOperation.ToIntegralX:
                result = Decimal64.RoundToIntegralExact(operands[0], ref context);
                return true;
            case DecTestOperation.Quantize:
                result = Decimal64.Quantize(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Rotate:
                result = Decimal64.Rotate(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Shift:
                result = Decimal64.Shift(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.NextPlus:
                result = Decimal64.NextPlus(operands[0], ref context);
                return true;
            case DecTestOperation.NextMinus:
                result = Decimal64.NextMinus(operands[0], ref context);
                return true;
            case DecTestOperation.NextToward:
                result = Decimal64.NextToward(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Exp:
                result = Decimal64.Exp(operands[0], ref context);
                return true;
            case DecTestOperation.Ln:
                result = Decimal64.Log(operands[0], ref context);
                return true;
            case DecTestOperation.Log10:
                result = Decimal64.Log10(operands[0], ref context);
                return true;
            case DecTestOperation.Power:
                result = Decimal64.Pow(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.SquareRoot:
                result = Decimal64.Sqrt(operands[0], ref context);
                return true;
            default:
                result = Decimal64.Zero;
                return false;
        }
    }

    private static Decimal64Context ToInner(DecTestContext context)
    {
        var inner = new Decimal64Context((Decimal64Rounding)(int)context.Rounding);
        inner.Status = (Decimal64Status)(int)context.Status;
        return inner;
    }

    private static void CopyBack(ref DecTestContext context, Decimal64Context inner)
    {
        context.Status = (DecTestStatus)(int)inner.Status;
    }

    private static Decimal64 FromComparison(int comparison)
    {
        if (comparison < 0)
        {
            return Decimal64.NegativeOne;
        }

        return comparison > 0 ? Decimal64.One : Decimal64.Zero;
    }
}
