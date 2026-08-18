// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// Operations that read a value's parts but do not depend on which format holds them.
/// </summary>
internal static class DecimalOperations
{
    public static DecimalClass Classify<TFormat>(UnpackedDecimal<UInt128> value)
        where TFormat : IDecimalFormat
    {
        switch (value.Kind)
        {
            case DecimalKind.SignalingNaN:
                return DecimalClass.SignalingNaN;
            case DecimalKind.QuietNaN:
                return DecimalClass.QuietNaN;
            case DecimalKind.Infinity:
                return value.IsNegative ? DecimalClass.NegativeInfinity : DecimalClass.PositiveInfinity;
        }

        if (value.Coefficient == UInt128.Zero)
        {
            return value.IsNegative ? DecimalClass.NegativeZero : DecimalClass.PositiveZero;
        }

        if (IsSubnormal<TFormat>(value))
        {
            return value.IsNegative ? DecimalClass.NegativeSubnormal : DecimalClass.PositiveSubnormal;
        }

        return value.IsNegative ? DecimalClass.NegativeNormal : DecimalClass.PositiveNormal;
    }

    public static bool IsSubnormal<TFormat>(UnpackedDecimal<UInt128> value)
        where TFormat : IDecimalFormat
    {
        if (!value.IsFinite || value.Coefficient == UInt128.Zero)
        {
            return false;
        }

        var adjusted = value.Exponent + DecimalRounder.CountDigits(value.Coefficient) - 1;
        return adjusted < TFormat.MinExponent;
    }

    /// <summary>
    /// IEEE 754's total order, which the specification calls compare-total. Every value is
    /// ordered against every other, negative zero sits below positive zero, NaNs sort
    /// outside the infinities by payload, and two members of one cohort are separated by
    /// their exponents.
    /// </summary>
    public static int CompareTotal(UnpackedDecimal<UInt128> left, UnpackedDecimal<UInt128> right)
    {
        if (left.IsNegative != right.IsNegative)
        {
            return left.IsNegative ? -1 : 1;
        }

        var magnitude = CompareMagnitudeTotal(left, right);
        return left.IsNegative ? -magnitude : magnitude;
    }

    public static int CompareTotalMagnitude(UnpackedDecimal<UInt128> left, UnpackedDecimal<UInt128> right)
    {
        return CompareMagnitudeTotal(
            new UnpackedDecimal<UInt128>(left.Kind, false, left.Exponent, left.Coefficient),
            new UnpackedDecimal<UInt128>(right.Kind, false, right.Exponent, right.Coefficient));
    }

    private static int CompareMagnitudeTotal(UnpackedDecimal<UInt128> left, UnpackedDecimal<UInt128> right)
    {
        var leftRank = Rank(left);
        var rightRank = Rank(right);
        if (leftRank != rightRank)
        {
            return leftRank < rightRank ? -1 : 1;
        }

        if (leftRank == 2)
        {
            // Both infinite, and infinities of the same sign are indistinguishable.
            return 0;
        }

        if (leftRank > 2)
        {
            // Both the same kind of NaN; the payload breaks the tie.
            return left.Coefficient.CompareTo(right.Coefficient);
        }

        var value = CompareFiniteValue(left, right);
        if (value != 0)
        {
            return value;
        }

        // Same value, so this is one cohort: the member with the larger exponent has the
        // shorter coefficient, and counts as the larger.
        return left.Exponent.CompareTo(right.Exponent);
    }

    private static int Rank(UnpackedDecimal<UInt128> value)
    {
        return value.Kind switch
        {
            DecimalKind.Finite => 1,
            DecimalKind.Infinity => 2,
            DecimalKind.SignalingNaN => 3,
            _ => 4
        };
    }

    /// <summary>
    /// Compares two finite magnitudes without regard to sign, lining their exponents up
    /// rather than widening either coefficient to the other's exponent.
    /// </summary>
    public static int CompareFiniteValue(UnpackedDecimal<UInt128> left, UnpackedDecimal<UInt128> right)
    {
        var leftIsZero = left.Coefficient == UInt128.Zero;
        var rightIsZero = right.Coefficient == UInt128.Zero;
        if (leftIsZero || rightIsZero)
        {
            return leftIsZero && rightIsZero ? 0 : (leftIsZero ? -1 : 1);
        }

        var leftAdjusted = left.Exponent + DecimalRounder.CountDigits(left.Coefficient) - 1;
        var rightAdjusted = right.Exponent + DecimalRounder.CountDigits(right.Coefficient) - 1;
        if (leftAdjusted != rightAdjusted)
        {
            return leftAdjusted < rightAdjusted ? -1 : 1;
        }

        // The same magnitude range, so the coefficients can be compared once the shorter
        // one has been scaled to the other's exponent. Both fit a format, so the shift is
        // bounded by the precision and cannot overflow.
        var leftCoefficient = left.Coefficient;
        var rightCoefficient = right.Coefficient;
        var shift = left.Exponent - right.Exponent;
        if (shift > 0)
        {
            leftCoefficient *= PowersOfTen.UInt128(shift);
        }
        else if (shift < 0)
        {
            rightCoefficient *= PowersOfTen.UInt128(-shift);
        }

        return leftCoefficient.CompareTo(rightCoefficient);
    }
}
