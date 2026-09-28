// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// Reading a value's standing rather than computing with it: what class it is, whether it
/// is subnormal, where it falls in the total order, and which of two values a selection
/// picks.
/// </summary>
internal static class Decimal128Ordering
{
    public static Decimal128Class Classify(Decimal128Integer bits)
    {
        var negative = Decimal128Encoding.IsNegative(bits);

        if (Decimal128Encoding.IsSpecial(bits))
        {
            if (Decimal128Encoding.IsSignalingNaN(bits))
            {
                return Decimal128Class.SignalingNaN;
            }

            if (Decimal128Encoding.IsNaN(bits))
            {
                return Decimal128Class.QuietNaN;
            }

            return negative ? Decimal128Class.NegativeInfinity : Decimal128Class.PositiveInfinity;
        }

        var coefficient = Decimal128Encoding.Unpack(bits, out var exponent);
        if (coefficient.IsZero)
        {
            return negative ? Decimal128Class.NegativeZero : Decimal128Class.PositiveZero;
        }

        if (IsSubnormal(coefficient, exponent))
        {
            return negative ? Decimal128Class.NegativeSubnormal : Decimal128Class.PositiveSubnormal;
        }

        return negative ? Decimal128Class.NegativeNormal : Decimal128Class.PositiveNormal;
    }

    /// <summary>
    /// Whether a non-zero finite value's leading digit sits below where the normal range
    /// starts.
    /// </summary>
    public static bool IsSubnormal(Decimal128Integer coefficient, int exponent)
    {
        return !coefficient.IsZero
            && exponent + Decimal128Tables.CountDigits(coefficient) - 1 < Decimal128Encoding.MinExponent;
    }

    public static bool IsSubnormal(Decimal128Integer bits)
    {
        if (Decimal128Encoding.IsSpecial(bits))
        {
            return false;
        }

        var coefficient = Decimal128Encoding.Unpack(bits, out var exponent);
        return IsSubnormal(coefficient, exponent);
    }

    /// <summary>
    /// IEEE 754's total order, which the specification calls compare-total. Every value is
    /// ordered against every other, negative zero sits below positive zero, NaNs sort
    /// outside the infinities by payload, and two members of one cohort are separated by
    /// their exponents.
    /// </summary>
    public static int CompareTotal(Decimal128Integer left, Decimal128Integer right)
    {
        var leftNegative = Decimal128Encoding.IsNegative(left);
        var rightNegative = Decimal128Encoding.IsNegative(right);

        if (leftNegative != rightNegative)
        {
            return leftNegative ? -1 : 1;
        }

        var magnitude = CompareTotalMagnitude(left, right);
        return leftNegative ? -magnitude : magnitude;
    }

    public static int CompareTotalMagnitude(Decimal128Integer left, Decimal128Integer right)
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
            return Decimal128Encoding.Payload(left).CompareTo(Decimal128Encoding.Payload(right));
        }

        var leftCoefficient = Decimal128Encoding.Unpack(left, out var leftExponent);
        var rightCoefficient = Decimal128Encoding.Unpack(right, out var rightExponent);

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
    public static int CompareFiniteMagnitude(Decimal128Integer leftCoefficient, int leftExponent,
        Decimal128Integer rightCoefficient, int rightExponent)
    {
        if (leftCoefficient.IsZero || rightCoefficient.IsZero)
        {
            if (leftCoefficient.IsZero && rightCoefficient.IsZero)
            {
                return 0;
            }

            return leftCoefficient.IsZero ? -1 : 1;
        }

        return Decimal128Arithmetic.CompareMagnitude(leftCoefficient, leftExponent, rightCoefficient, rightExponent);
    }

    private static int Rank(Decimal128Integer bits)
    {
        if (!Decimal128Encoding.IsSpecial(bits))
        {
            return 1;
        }

        if (Decimal128Encoding.IsInfinity(bits))
        {
            return 2;
        }

        return Decimal128Encoding.IsSignalingNaN(bits) ? 3 : 4;
    }

    /// <summary>
    /// The min and max family. A quiet NaN beside a number loses: these hand back the
    /// number. Two values that compare equal still differ, so the total order settles which
    /// member of the pair the caller gets -- negative zero loses to positive zero for max,
    /// and 1.0 loses to 1.
    /// </summary>
    public static Decimal128Integer Select(Decimal128Integer left, Decimal128Integer right, bool wantLarger,
        bool byMagnitude, ref Decimal128Status status)
    {
        if (Decimal128Encoding.IsSignalingNaN(left) || Decimal128Encoding.IsSignalingNaN(right))
        {
            status |= Decimal128Status.InvalidOperation;
            return Decimal128Encoding.Quiet(Decimal128Encoding.IsSignalingNaN(left) ? left : right);
        }

        var leftNaN = Decimal128Encoding.IsNaN(left);
        var rightNaN = Decimal128Encoding.IsNaN(right);

        if (leftNaN && rightNaN)
        {
            return Decimal128Encoding.Quiet(left);
        }

        if (leftNaN || rightNaN)
        {
            var number = leftNaN ? right : left;
            if (IsSubnormal(number))
            {
                status |= Decimal128Status.Subnormal;
            }

            return Decimal128Encoding.Canonical(number);
        }

        var leftCompared = byMagnitude ? Magnitude(left) : left;
        var rightCompared = byMagnitude ? Magnitude(right) : right;
        var comparison = CompareValues(leftCompared, rightCompared);

        var chooseLeft = comparison == 0
            ? CompareTotal(left, right) > 0 == wantLarger
            : comparison > 0 == wantLarger;

        var chosen = chooseLeft ? left : right;
        if (IsSubnormal(chosen))
        {
            status |= Decimal128Status.Subnormal;
        }

        return Decimal128Encoding.Canonical(chosen);
    }

    /// <summary>The bits with the sign cleared.</summary>
    public static Decimal128Integer Magnitude(Decimal128Integer bits)
    {
        return new Decimal128Integer(bits.High & ~Decimal128Encoding.SignMask, bits.Low);
    }

    /// <summary>
    /// The numeric comparison of two values neither of which is a NaN, which is what the
    /// selection above needs and what the infinities make more than a magnitude question.
    /// </summary>
    public static int CompareValues(Decimal128Integer left, Decimal128Integer right)
    {
        if (Decimal128Encoding.IsSpecial(left) || Decimal128Encoding.IsSpecial(right))
        {
            return Decimal128Arithmetic.CompareInfinity(left, right);
        }

        var leftCoefficient = Decimal128Encoding.Unpack(left, out var leftExponent);
        var rightCoefficient = Decimal128Encoding.Unpack(right, out var rightExponent);

        return Decimal128Arithmetic.CompareFinite(Decimal128Encoding.IsNegative(left), leftCoefficient, leftExponent,
            Decimal128Encoding.IsNegative(right), rightCoefficient, rightExponent);
    }

    /// <summary>
    /// A hash of the value rather than of the bits, so that members of one cohort agree and
    /// the two zeros agree, as numeric equality requires.
    /// </summary>
    public static int ValueHashCode(Decimal128Integer bits)
    {
        if (Decimal128Encoding.IsSpecial(bits))
        {
            if (Decimal128Encoding.IsNaN(bits))
            {
                return 0x7FC00000;
            }

            return Decimal128Encoding.IsNegative(bits) ? int.MinValue : int.MaxValue;
        }

        var coefficient = Decimal128Encoding.Unpack(bits, out var exponent);
        if (coefficient.IsZero)
        {
            return 0;
        }

        // Every member of a cohort has to hash alike, so the trailing zeros come off all
        // the way down, however far past a zero exponent that goes.
        Decimal128Shaping.StripTrailingZeros(ref coefficient, ref exponent, int.MaxValue);

        return HashCode.Combine(Decimal128Encoding.IsNegative(bits), exponent, coefficient.High, coefficient.Low);
    }
}
