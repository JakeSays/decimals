// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// Reading a value's standing rather than computing with it: what class it is, whether it
/// is subnormal, where it falls in the total order, and which of two values a selection
/// picks.
/// </summary>
internal static class Decimal32Ordering
{
    public static Decimal32Class Classify(uint bits)
    {
        var negative = Decimal32Encoding.IsNegative(bits);

        if (Decimal32Encoding.IsSpecial(bits))
        {
            if (Decimal32Encoding.IsSignalingNaN(bits))
            {
                return Decimal32Class.SignalingNaN;
            }

            if (Decimal32Encoding.IsNaN(bits))
            {
                return Decimal32Class.QuietNaN;
            }

            return negative ? Decimal32Class.NegativeInfinity : Decimal32Class.PositiveInfinity;
        }

        var coefficient = Decimal32Encoding.Unpack(bits, out var exponent);
        if (coefficient == 0)
        {
            return negative ? Decimal32Class.NegativeZero : Decimal32Class.PositiveZero;
        }

        if (IsSubnormal(coefficient, exponent))
        {
            return negative ? Decimal32Class.NegativeSubnormal : Decimal32Class.PositiveSubnormal;
        }

        return negative ? Decimal32Class.NegativeNormal : Decimal32Class.PositiveNormal;
    }

    /// <summary>
    /// Whether a non-zero finite value's leading digit sits below where the normal range
    /// starts.
    /// </summary>
    public static bool IsSubnormal(ulong coefficient, int exponent)
    {
        return coefficient != 0
            && exponent + Decimal32Tables.CountDigits(coefficient) - 1 < Decimal32Encoding.MinExponent;
    }

    public static bool IsSubnormal(uint bits)
    {
        if (Decimal32Encoding.IsSpecial(bits))
        {
            return false;
        }

        var coefficient = Decimal32Encoding.Unpack(bits, out var exponent);
        return IsSubnormal(coefficient, exponent);
    }

    /// <summary>
    /// IEEE 754's total order, which the specification calls compare-total. Every value is
    /// ordered against every other, negative zero sits below positive zero, NaNs sort
    /// outside the infinities by payload, and two members of one cohort are separated by
    /// their exponents.
    /// </summary>
    public static int CompareTotal(uint left, uint right)
    {
        var leftNegative = Decimal32Encoding.IsNegative(left);
        var rightNegative = Decimal32Encoding.IsNegative(right);

        if (leftNegative != rightNegative)
        {
            return leftNegative ? -1 : 1;
        }

        var magnitude = CompareTotalMagnitude(left, right);
        return leftNegative ? -magnitude : magnitude;
    }

    public static int CompareTotalMagnitude(uint left, uint right)
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
            var leftPayload = Decimal32Encoding.Payload(left);
            var rightPayload = Decimal32Encoding.Payload(right);
            if (leftPayload == rightPayload)
            {
                return 0;
            }

            return leftPayload < rightPayload ? -1 : 1;
        }

        var leftCoefficient = Decimal32Encoding.Unpack(left, out var leftExponent);
        var rightCoefficient = Decimal32Encoding.Unpack(right, out var rightExponent);

        var value = CompareFiniteMagnitude(leftCoefficient, leftExponent, rightCoefficient, rightExponent);
        if (value != 0)
        {
            return value;
        }

        // Same value, so this is one cohort: the member with the larger exponent has the
        // shorter coefficient, and counts as the larger.
        return leftExponent.CompareTo(rightExponent);
    }

    /// <summary>Orders two finite values by magnitude alone, zeros included.</summary>
    public static int CompareFiniteMagnitude(ulong leftCoefficient, int leftExponent, ulong rightCoefficient,
        int rightExponent)
    {
        if (leftCoefficient == 0 || rightCoefficient == 0)
        {
            if (leftCoefficient == 0 && rightCoefficient == 0)
            {
                return 0;
            }

            return leftCoefficient == 0 ? -1 : 1;
        }

        return Decimal32Arithmetic.CompareMagnitude(leftCoefficient, leftExponent, rightCoefficient, rightExponent);
    }

    private static int Rank(uint bits)
    {
        if (!Decimal32Encoding.IsSpecial(bits))
        {
            return 1;
        }

        if (Decimal32Encoding.IsInfinity(bits))
        {
            return 2;
        }

        return Decimal32Encoding.IsSignalingNaN(bits) ? 3 : 4;
    }

    /// <summary>
    /// The min and max family. A quiet NaN beside a number loses: these hand back the
    /// number. Two values that compare equal still differ, so the total order settles which
    /// member of the pair the caller gets -- negative zero loses to positive zero for max,
    /// and 1.0 loses to 1.
    /// </summary>
    public static uint Select(uint left, uint right, bool wantLarger, bool byMagnitude, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsSignalingNaN(left) || Decimal32Encoding.IsSignalingNaN(right))
        {
            status |= Decimal32Status.InvalidOperation;
            return Decimal32Encoding.Quiet(Decimal32Encoding.IsSignalingNaN(left) ? left : right);
        }

        var leftNaN = Decimal32Encoding.IsNaN(left);
        var rightNaN = Decimal32Encoding.IsNaN(right);

        if (leftNaN && rightNaN)
        {
            return Decimal32Encoding.Quiet(left);
        }

        if (leftNaN || rightNaN)
        {
            var number = leftNaN ? right : left;
            if (IsSubnormal(number))
            {
                status |= Decimal32Status.Subnormal;
            }

            return Decimal32Encoding.Canonical(number);
        }

        var leftCompared = byMagnitude ? left & ~Decimal32Encoding.SignMask : left;
        var rightCompared = byMagnitude ? right & ~Decimal32Encoding.SignMask : right;
        var comparison = CompareValues(leftCompared, rightCompared);

        var chooseLeft = comparison == 0
            ? CompareTotal(left, right) > 0 == wantLarger
            : comparison > 0 == wantLarger;

        var chosen = chooseLeft ? left : right;
        if (IsSubnormal(chosen))
        {
            status |= Decimal32Status.Subnormal;
        }

        return Decimal32Encoding.Canonical(chosen);
    }

    /// <summary>
    /// The numeric comparison of two values neither of which is a NaN, which is what the
    /// selection above needs and what the infinities make more than a magnitude question.
    /// </summary>
    public static int CompareValues(uint left, uint right)
    {
        if (Decimal32Encoding.IsSpecial(left) || Decimal32Encoding.IsSpecial(right))
        {
            return Decimal32Arithmetic.CompareInfinity(left, right);
        }

        var leftCoefficient = Decimal32Encoding.Unpack(left, out var leftExponent);
        var rightCoefficient = Decimal32Encoding.Unpack(right, out var rightExponent);

        return Decimal32Arithmetic.CompareFinite(Decimal32Encoding.IsNegative(left), leftCoefficient, leftExponent,
            Decimal32Encoding.IsNegative(right), rightCoefficient, rightExponent);
    }

    /// <summary>
    /// A hash of the value rather than of the bits, so that members of one cohort agree and
    /// the two zeros agree, as numeric equality requires.
    /// </summary>
    public static int ValueHashCode(uint bits)
    {
        if (Decimal32Encoding.IsSpecial(bits))
        {
            if (Decimal32Encoding.IsNaN(bits))
            {
                return 0x7FC00000;
            }

            return Decimal32Encoding.IsNegative(bits) ? int.MinValue : int.MaxValue;
        }

        ulong coefficient = Decimal32Encoding.Unpack(bits, out var exponent);
        if (coefficient == 0)
        {
            return 0;
        }

        // Every member of a cohort has to hash alike, so the trailing zeros come off all
        // the way down, however far past a zero exponent that goes.
        Decimal32Shaping.StripTrailingZeros(ref coefficient, ref exponent, int.MaxValue);

        return HashCode.Combine(Decimal32Encoding.IsNegative(bits), exponent, coefficient);
    }
}
