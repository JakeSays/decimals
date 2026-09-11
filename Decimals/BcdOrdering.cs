// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// Reading a value's standing rather than computing with it: what kind it is, whether it is
/// subnormal, and where it falls in the total order. This is
/// <see cref="DecimalOperations"/>'s work on the digit form.
/// </summary>
internal static unsafe class BcdOrdering
{
    public static DecimalClass Classify<TFormat>(BcdNumber value)
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

        if (value.IsZero)
        {
            return value.IsNegative ? DecimalClass.NegativeZero : DecimalClass.PositiveZero;
        }

        if (IsSubnormal<TFormat>(value))
        {
            return value.IsNegative ? DecimalClass.NegativeSubnormal : DecimalClass.PositiveSubnormal;
        }

        return value.IsNegative ? DecimalClass.NegativeNormal : DecimalClass.PositiveNormal;
    }

    /// <summary>
    /// Whether the value's leading digit sits below where the format's normal range starts.
    /// On digits the adjusted exponent is the exponent plus the digit count, so this asks
    /// nothing of the coefficient beyond how long it is.
    /// </summary>
    public static bool IsSubnormal<TFormat>(BcdNumber value)
        where TFormat : IDecimalFormat
    {
        if (!value.IsFinite || value.IsZero)
        {
            return false;
        }

        return value.AdjustedExponent < TFormat.MinExponent;
    }

    /// <summary>
    /// IEEE 754's total order, which the specification calls compare-total. Every value is
    /// ordered against every other, negative zero sits below positive zero, NaNs sort
    /// outside the infinities by payload, and two members of one cohort are separated by
    /// their exponents.
    /// </summary>
    public static int CompareTotal(BcdNumber left, BcdNumber right)
    {
        if (left.IsNegative != right.IsNegative)
        {
            return left.IsNegative ? -1 : 1;
        }

        var magnitude = CompareMagnitudeTotal(left, right);
        return left.IsNegative ? -magnitude : magnitude;
    }

    public static int CompareTotalMagnitude(BcdNumber left, BcdNumber right)
    {
        left.IsNegative = false;
        right.IsNegative = false;
        return CompareMagnitudeTotal(left, right);
    }

    private static int CompareMagnitudeTotal(BcdNumber left, BcdNumber right)
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
            return BcdNumber.CompareDigits(left.Msd, left.Lsd, right.Msd, right.Lsd);
        }

        var value = CompareFiniteMagnitude(left, right);
        if (value != 0)
        {
            return value;
        }

        // Same value, so this is one cohort: the member with the larger exponent has the
        // shorter coefficient, and counts as the larger.
        return left.Exponent.CompareTo(right.Exponent);
    }

    /// <summary>
    /// Orders two finite values by magnitude alone. Where the leading digits sit decides it
    /// unless they sit in the same place, and then the digits do.
    /// </summary>
    public static int CompareFiniteMagnitude(BcdNumber left, BcdNumber right)
    {
        var leftZero = left.IsZero;
        var rightZero = right.IsZero;
        if (leftZero || rightZero)
        {
            return leftZero && rightZero ? 0 : (leftZero ? -1 : 1);
        }

        if (left.AdjustedExponent != right.AdjustedExponent)
        {
            return left.AdjustedExponent < right.AdjustedExponent ? -1 : 1;
        }

        // The leading digits line up, so the digits themselves settle it; whichever runs
        // out first is padded with zeros, which cannot win.
        var leftDigit = left.Msd;
        var rightDigit = right.Msd;

        while (leftDigit <= left.Lsd && rightDigit <= right.Lsd)
        {
            if (*leftDigit != *rightDigit)
            {
                return *leftDigit < *rightDigit ? -1 : 1;
            }

            leftDigit++;
            rightDigit++;
        }

        for (; leftDigit <= left.Lsd; leftDigit++)
        {
            if (*leftDigit != 0)
            {
                return 1;
            }
        }

        for (; rightDigit <= right.Lsd; rightDigit++)
        {
            if (*rightDigit != 0)
            {
                return -1;
            }
        }

        return 0;
    }

    private static int Rank(BcdNumber value)
    {
        return value.Kind switch
        {
            DecimalKind.Finite => 1,
            DecimalKind.Infinity => 2,
            DecimalKind.SignalingNaN => 3,
            _ => 4
        };
    }
}
