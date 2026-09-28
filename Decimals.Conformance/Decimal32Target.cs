// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace Decimals.Conformance;

/// <summary>
/// Drives <see cref="Decimal32"/> from the corpus, which reaches it through the
/// <c>decSingle</c> group and the generated <c>d32</c> groups.
/// </summary>
/// <remarks>
/// The type carries its own context, rounding, status, and class enumerations, laid out
/// with the same values as the runner's so that the two convert by a cast. Every
/// operation runs under a context built from the runner's and its conditions are copied
/// back.
/// </remarks>
public readonly struct Decimal32Target : IDecTestTarget<Decimal32Target, Decimal32>
{
    public static string Name => "Decimal32";

    public static int Precision => 7;

    public static int MaxExponent => 96;

    public static int MinExponent => -95;

    public static int HexDigitCount => 8;

    /// <summary>The binary-integer form canonicalizes on the way in.</summary>
    public static bool PreservesNonCanonicalEncodings => false;

    public static Decimal32 FromString(ReadOnlySpan<char> text, ref DecTestContext context)
    {
        var inner = ToInner(context);
        var value = Decimal32.FromString(text, ref inner);
        CopyBack(ref context, inner);
        return value;
    }

    public static Decimal32 FromDpdHex(ReadOnlySpan<char> hex)
    {
        return Decimal32.FromDpdBits(uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    public static string ToDpdHex(Decimal32 value)
    {
        return value.ToDpdBits().ToString("x8", CultureInfo.InvariantCulture);
    }

    public static string ToScientificString(Decimal32 value) => value.ToString();

    public static string ToEngineeringString(Decimal32 value) => value.ToEngineeringString();

    public static DecTestClass Classify(Decimal32 value) => (DecTestClass)(int)Decimal32.Class(value);

    public static bool TryApply(DecTestOperation operation, ReadOnlySpan<Decimal32> operands,
        ref DecTestContext context, out Decimal32 result)
    {
        var inner = ToInner(context);
        var applied = TryApply(operation, operands, ref inner, out result);
        CopyBack(ref context, inner);
        return applied;
    }

    private static bool TryApply(DecTestOperation operation, ReadOnlySpan<Decimal32> operands,
        ref Decimal32Context context, out Decimal32 result)
    {
        switch (operation)
        {
            case DecTestOperation.Apply:
            case DecTestOperation.Copy:
                result = operands[0];
                return true;
            case DecTestOperation.Canonical:
                result = Decimal32.Canonical(operands[0]);
                return true;
            case DecTestOperation.CopyAbs:
                result = Decimal32.CopyAbs(operands[0]);
                return true;
            case DecTestOperation.CopyNegate:
                result = Decimal32.CopyNegate(operands[0]);
                return true;
            case DecTestOperation.Add:
                result = Decimal32.Add(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Subtract:
                result = Decimal32.Subtract(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Multiply:
                result = Decimal32.Multiply(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Divide:
                result = Decimal32.Divide(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.DivideInt:
                result = Decimal32.DivideInteger(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Remainder:
                result = Decimal32.Remainder(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.RemainderNear:
                result = Decimal32.RemainderNear(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Fma:
                result = Decimal32.FusedMultiplyAdd(operands[0], operands[1], operands[2], ref context);
                return true;
            case DecTestOperation.Abs:
                result = Decimal32.Abs(operands[0], ref context);
                return true;
            case DecTestOperation.Plus:
                result = Decimal32.Plus(operands[0], ref context);
                return true;
            case DecTestOperation.Minus:
                result = Decimal32.Minus(operands[0], ref context);
                return true;
            case DecTestOperation.Compare:
                result = Decimal32.Compare(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.CompareSig:
                result = Decimal32.CompareSignal(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.CopySign:
                result = Decimal32.CopySign(operands[0], operands[1]);
                return true;
            case DecTestOperation.CompareTotal:
                result = FromComparison(Decimal32.CompareTotal(operands[0], operands[1]));
                return true;
            case DecTestOperation.CompareTotalMag:
                result = FromComparison(Decimal32.CompareTotalMagnitude(operands[0], operands[1]));
                return true;
            case DecTestOperation.SameQuantum:
                result = Decimal32.SameQuantum(operands[0], operands[1]) ? Decimal32.One : Decimal32.Zero;
                return true;
            case DecTestOperation.And:
                result = Decimal32.And(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Or:
                result = Decimal32.Or(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Xor:
                result = Decimal32.Xor(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Invert:
                result = Decimal32.Invert(operands[0], ref context);
                return true;
            case DecTestOperation.Max:
                result = Decimal32.Max(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Min:
                result = Decimal32.Min(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.MaxMag:
                result = Decimal32.MaxMagnitude(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.MinMag:
                result = Decimal32.MinMagnitude(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.LogB:
                result = Decimal32.LogB(operands[0], ref context);
                return true;
            case DecTestOperation.ScaleB:
                result = Decimal32.ScaleB(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Reduce:
                result = Decimal32.Reduce(operands[0], ref context);
                return true;
            case DecTestOperation.Trim:
                result = Decimal32.Trim(operands[0]);
                return true;
            case DecTestOperation.ToIntegral:
                result = Decimal32.RoundToIntegral(operands[0], ref context);
                return true;
            case DecTestOperation.ToIntegralX:
                result = Decimal32.RoundToIntegralExact(operands[0], ref context);
                return true;
            case DecTestOperation.Quantize:
                result = Decimal32.Quantize(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Rotate:
                result = Decimal32.Rotate(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Shift:
                result = Decimal32.Shift(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.NextPlus:
                result = Decimal32.NextPlus(operands[0], ref context);
                return true;
            case DecTestOperation.NextMinus:
                result = Decimal32.NextMinus(operands[0], ref context);
                return true;
            case DecTestOperation.NextToward:
                result = Decimal32.NextToward(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.Exp:
                result = Decimal32.Exp(operands[0], ref context);
                return true;
            case DecTestOperation.Ln:
                result = Decimal32.Log(operands[0], ref context);
                return true;
            case DecTestOperation.Log10:
                result = Decimal32.Log10(operands[0], ref context);
                return true;
            case DecTestOperation.Power:
                result = Decimal32.Pow(operands[0], operands[1], ref context);
                return true;
            case DecTestOperation.SquareRoot:
                result = Decimal32.Sqrt(operands[0], ref context);
                return true;
            default:
                result = Decimal32.Zero;
                return false;
        }
    }

    private static Decimal32Context ToInner(DecTestContext context)
    {
        var inner = new Decimal32Context((Decimal32Rounding)(int)context.Rounding);
        inner.Status = (Decimal32Status)(int)context.Status;
        return inner;
    }

    private static void CopyBack(ref DecTestContext context, Decimal32Context inner)
    {
        context.Status = (DecTestStatus)(int)inner.Status;
    }

    private static Decimal32 FromComparison(int comparison)
    {
        if (comparison < 0)
        {
            return Decimal32.NegativeOne;
        }

        return comparison > 0 ? Decimal32.One : Decimal32.Zero;
    }
}
