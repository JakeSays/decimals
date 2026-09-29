// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// Classifies and orders values without computing with them: the class, whether a value
/// is subnormal, the total order, min and max selection, and the hash.
/// </summary>
internal static class Decimal64Ordering
{
    /// <summary>The specification's class of a value.</summary>
    /// <param name="bits">The encoded value.</param>
    /// <returns>The class: a NaN kind, or an infinity, normal, subnormal, or zero of either sign.</returns>
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
    /// True if a non-zero finite value's adjusted exponent is below the normal range.
    /// </summary>
    /// <param name="coefficient">The value's coefficient.</param>
    /// <param name="exponent">The value's quantum exponent.</param>
    /// <returns>True if the coefficient is not zero and the adjusted exponent is below <see cref="Decimal64Encoding.MinExponent"/>.</returns>
    public static bool IsSubnormal(ulong coefficient, int exponent)
    {
        return coefficient != 0
            && exponent + Decimal64Tables.CountDigits(coefficient) - 1 < Decimal64Encoding.MinExponent;
    }

    /// <summary>True if the value is finite, non-zero, and below the normal range.</summary>
    /// <param name="bits">The encoded value.</param>
    /// <returns>True if the value is subnormal.</returns>
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
    /// The IEEE 754 total order, which the specification calls compare-total. Every value
    /// is ordered against every other. Negative zero is below positive zero. NaNs sort
    /// outside the infinities, ordered by payload. Equal values with different exponents
    /// are ordered by exponent.
    /// </summary>
    /// <param name="left">The first encoded value.</param>
    /// <param name="right">The second encoded value.</param>
    /// <returns>A negative number if <paramref name="left"/> comes first, zero if the encodings are equal in the order, or a positive number if it comes second.</returns>
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

    /// <summary>The IEEE 754 total order on the absolute values of two values.</summary>
    /// <param name="left">The first encoded value.</param>
    /// <param name="right">The second encoded value.</param>
    /// <returns>A negative number if <paramref name="left"/> comes first, zero if they are equal in the order, or a positive number if it comes second.</returns>
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
            // Both are infinite. Infinities of the same sign are equal.
            return 0;
        }

        if (leftRank > 2)
        {
            // Both are the same kind of NaN. The payload decides.
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

        // Equal values: the one with the larger exponent has the shorter coefficient and
        // counts as larger.
        return leftExponent.CompareTo(rightExponent);
    }

    /// <summary>Compares two finite values by magnitude only, including zeros.</summary>
    /// <param name="leftCoefficient">The first value's coefficient.</param>
    /// <param name="leftExponent">The first value's quantum exponent.</param>
    /// <param name="rightCoefficient">The second value's coefficient.</param>
    /// <param name="rightExponent">The second value's quantum exponent.</param>
    /// <returns>-1 if the first magnitude is smaller, 0 if they are equal, or 1 if it is larger.</returns>
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
    /// The min and max operations. If one operand is a quiet NaN and the other is a number,
    /// they return the number. Two values that compare equal can still differ, so the total
    /// order decides which one is returned: for max, negative zero loses to positive zero,
    /// and 1.0 loses to 1.
    /// </summary>
    /// <param name="left">The first encoded value.</param>
    /// <param name="right">The second encoded value.</param>
    /// <param name="wantLarger">True for max, false for min.</param>
    /// <param name="byMagnitude">True to compare absolute values.</param>
    /// <param name="status">Receives InvalidOperation if an operand is a signaling NaN.</param>
    /// <returns>The selected value in its canonical encoding, or a quiet NaN.</returns>
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
    /// Compares two values numerically. Neither may be NaN. Infinities are handled here, so
    /// this is more than a magnitude comparison.
    /// </summary>
    /// <param name="left">The first encoded value.</param>
    /// <param name="right">The second encoded value.</param>
    /// <returns>-1 if <paramref name="left"/> is smaller, 0 if they are equal, or 1 if it is larger.</returns>
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
    /// A hash of the numeric value, not the bits. Equal values with different exponents,
    /// and the two zeros, hash the same, as numeric equality requires.
    /// </summary>
    /// <param name="bits">The encoded value.</param>
    /// <returns>The hash code.</returns>
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

        // All equal values must hash the same, so remove every trailing zero, even past
        // exponent zero.
        Decimal64Shaping.StripTrailingZeros(ref coefficient, ref exponent, int.MaxValue);

        return HashCode.Combine(Decimal64Encoding.IsNegative(bits), exponent, coefficient);
    }
}
