// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// Reading a value's standing rather than computing with it: what class it is, whether it
/// is subnormal, where it falls in the total order, and which of two values a selection
/// picks.
/// </summary>
internal static class Decimal64Ordering
{
    public static Decimal64Class Classify(ulong bits)
    {
        var negative = Decimal64Encoding.IsNegative(bits);

        if (Decimal64Encoding.IsSpecial(bits))
        {
            if (Decimal64Encoding.IsSignalingNaN(bits))
            {
                return Decimal64Class.SignalingNaN;
            }

            if (Decimal64Encoding.IsNaN(bits))
            {
                return Decimal64Class.QuietNaN;
            }

            return negative ? Decimal64Class.NegativeInfinity : Decimal64Class.PositiveInfinity;
        }

        var coefficient = Decimal64Encoding.Unpack(bits, out var exponent);
        if (coefficient == 0)
        {
            return negative ? Decimal64Class.NegativeZero : Decimal64Class.PositiveZero;
        }

        if (IsSubnormal(coefficient, exponent))
        {
            return negative ? Decimal64Class.NegativeSubnormal : Decimal64Class.PositiveSubnormal;
        }

        return negative ? Decimal64Class.NegativeNormal : Decimal64Class.PositiveNormal;
    }

    /// <summary>
    /// Whether a non-zero finite value's leading digit sits below where the normal range
    /// starts.
    /// </summary>
    public static bool IsSubnormal(ulong coefficient, int exponent)
    {
        return coefficient != 0
            && exponent + Decimal64Tables.CountDigits(coefficient) - 1 < Decimal64Encoding.MinExponent;
    }

    public static bool IsSubnormal(ulong bits)
    {
        if (Decimal64Encoding.IsSpecial(bits))
        {
            return false;
        }

        var coefficient = Decimal64Encoding.Unpack(bits, out var exponent);
        return IsSubnormal(coefficient, exponent);
    }

    /// <summary>
    /// IEEE 754's total order, which the specification calls compare-total. Every value is
    /// ordered against every other, negative zero sits below positive zero, NaNs sort
    /// outside the infinities by payload, and two members of one cohort are separated by
    /// their exponents.
    /// </summary>
    public static int CompareTotal(ulong left, ulong right)
    {
        var leftNegative = Decimal64Encoding.IsNegative(left);
        var rightNegative = Decimal64Encoding.IsNegative(right);

        if (leftNegative != rightNegative)
        {
            return leftNegative ? -1 : 1;
        }

        var magnitude = CompareTotalMagnitude(left, right);
        return leftNegative ? -magnitude : magnitude;
    }

    public static int CompareTotalMagnitude(ulong left, ulong right)
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
            var leftPayload = Decimal64Encoding.Payload(left);
            var rightPayload = Decimal64Encoding.Payload(right);
            if (leftPayload == rightPayload)
            {
                return 0;
            }

            return leftPayload < rightPayload ? -1 : 1;
        }

        var leftCoefficient = Decimal64Encoding.Unpack(left, out var leftExponent);
        var rightCoefficient = Decimal64Encoding.Unpack(right, out var rightExponent);

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

        return Decimal64Arithmetic.CompareMagnitude(leftCoefficient, leftExponent, rightCoefficient, rightExponent);
    }

    private static int Rank(ulong bits)
    {
        if (!Decimal64Encoding.IsSpecial(bits))
        {
            return 1;
        }

        if (Decimal64Encoding.IsInfinity(bits))
        {
            return 2;
        }

        return Decimal64Encoding.IsSignalingNaN(bits) ? 3 : 4;
    }

    /// <summary>
    /// The min and max family. A quiet NaN beside a number loses: these hand back the
    /// number. Two values that compare equal still differ, so the total order settles which
    /// member of the pair the caller gets -- negative zero loses to positive zero for max,
    /// and 1.0 loses to 1.
    /// </summary>
    public static ulong Select(ulong left, ulong right, bool wantLarger, bool byMagnitude, ref Decimal64Status status)
    {
        if (Decimal64Encoding.IsSignalingNaN(left) || Decimal64Encoding.IsSignalingNaN(right))
        {
            status |= Decimal64Status.InvalidOperation;
            return Decimal64Encoding.Quiet(Decimal64Encoding.IsSignalingNaN(left) ? left : right);
        }

        var leftNaN = Decimal64Encoding.IsNaN(left);
        var rightNaN = Decimal64Encoding.IsNaN(right);

        if (leftNaN && rightNaN)
        {
            return Decimal64Encoding.Quiet(left);
        }

        if (leftNaN || rightNaN)
        {
            var number = leftNaN ? right : left;
            if (IsSubnormal(number))
            {
                status |= Decimal64Status.Subnormal;
            }

            return Decimal64Encoding.Canonical(number);
        }

        var leftCompared = byMagnitude ? left & ~Decimal64Encoding.SignMask : left;
        var rightCompared = byMagnitude ? right & ~Decimal64Encoding.SignMask : right;
        var comparison = CompareValues(leftCompared, rightCompared);

        var chooseLeft = comparison == 0
            ? CompareTotal(left, right) > 0 == wantLarger
            : comparison > 0 == wantLarger;

        var chosen = chooseLeft ? left : right;
        if (IsSubnormal(chosen))
        {
            status |= Decimal64Status.Subnormal;
        }

        return Decimal64Encoding.Canonical(chosen);
    }

    /// <summary>
    /// The numeric comparison of two values neither of which is a NaN, which is what the
    /// selection above needs and what the infinities make more than a magnitude question.
    /// </summary>
    public static int CompareValues(ulong left, ulong right)
    {
        if (Decimal64Encoding.IsSpecial(left) || Decimal64Encoding.IsSpecial(right))
        {
            return Decimal64Arithmetic.CompareInfinity(left, right);
        }

        var leftCoefficient = Decimal64Encoding.Unpack(left, out var leftExponent);
        var rightCoefficient = Decimal64Encoding.Unpack(right, out var rightExponent);

        return Decimal64Arithmetic.CompareFinite(Decimal64Encoding.IsNegative(left), leftCoefficient, leftExponent,
            Decimal64Encoding.IsNegative(right), rightCoefficient, rightExponent);
    }

    /// <summary>
    /// A hash of the value rather than of the bits, so that members of one cohort agree and
    /// the two zeros agree, as numeric equality requires.
    /// </summary>
    public static int ValueHashCode(ulong bits)
    {
        if (Decimal64Encoding.IsSpecial(bits))
        {
            if (Decimal64Encoding.IsNaN(bits))
            {
                return 0x7FC00000;
            }

            return Decimal64Encoding.IsNegative(bits) ? int.MinValue : int.MaxValue;
        }

        var coefficient = Decimal64Encoding.Unpack(bits, out var exponent);
        if (coefficient == 0)
        {
            return 0;
        }

        // Every member of a cohort has to hash alike, so the trailing zeros come off all
        // the way down, however far past a zero exponent that goes.
        Decimal64Shaping.StripTrailingZeros(ref coefficient, ref exponent, int.MaxValue);

        return HashCode.Combine(Decimal64Encoding.IsNegative(bits), exponent, coefficient);
    }
}
