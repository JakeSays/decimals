// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// Classifies and orders values without computing with them: the class, whether a value
/// is subnormal, the total order, min and max selection, and the hash.
/// </summary>
internal static class Decimal128Ordering
{
    /// <summary>The specification's class of a value.</summary>
    /// <param name="bits">The encoded value.</param>
    /// <returns>The class: a NaN kind, or an infinity, normal, subnormal, or zero of either sign.</returns>
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
    /// True if a non-zero finite value's adjusted exponent is below the normal range.
    /// </summary>
    /// <param name="coefficient">The value's coefficient.</param>
    /// <param name="exponent">The value's quantum exponent.</param>
    /// <returns>True if the coefficient is not zero and the adjusted exponent is below <see cref="Decimal128Encoding.MinExponent"/>.</returns>
    public static bool IsSubnormal(Decimal128Integer coefficient, int exponent)
    {
        return !coefficient.IsZero
            && exponent + Decimal128Tables.CountDigits(coefficient) - 1 < Decimal128Encoding.MinExponent;
    }

    /// <summary>True if the value is finite, non-zero, and below the normal range.</summary>
    /// <param name="bits">The encoded value.</param>
    /// <returns>True if the value is subnormal.</returns>
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
    /// The IEEE 754 total order, which the specification calls compare-total. Every value
    /// is ordered against every other. Negative zero is below positive zero. NaNs sort
    /// outside the infinities, ordered by payload. Equal values with different exponents
    /// are ordered by exponent.
    /// </summary>
    /// <param name="left">The first encoded value.</param>
    /// <param name="right">The second encoded value.</param>
    /// <returns>A negative number if <paramref name="left"/> comes first, zero if the encodings are equal in the order, or a positive number if it comes second.</returns>
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

    /// <summary>The IEEE 754 total order on the absolute values of two values.</summary>
    /// <param name="left">The first encoded value.</param>
    /// <param name="right">The second encoded value.</param>
    /// <returns>A negative number if <paramref name="left"/> comes first, zero if they are equal in the order, or a positive number if it comes second.</returns>
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
            // Both are infinite. Infinities of the same sign are equal.
            return 0;
        }

        if (leftRank > 2)
        {
            // Both are the same kind of NaN. The payload decides.
            return Decimal128Encoding.Payload(left).CompareTo(Decimal128Encoding.Payload(right));
        }

        var leftCoefficient = Decimal128Encoding.Unpack(left, out var leftExponent);
        var rightCoefficient = Decimal128Encoding.Unpack(right, out var rightExponent);

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
    /// <param name="bits">The encoded value.</param>
    /// <returns>The same encoding with the sign bit clear.</returns>
    public static Decimal128Integer Magnitude(Decimal128Integer bits)
    {
        return new Decimal128Integer(bits.High & ~Decimal128Encoding.SignMask, bits.Low);
    }

    /// <summary>
    /// Compares two values numerically. Neither may be NaN. Infinities are handled here, so
    /// this is more than a magnitude comparison.
    /// </summary>
    /// <param name="left">The first encoded value.</param>
    /// <param name="right">The second encoded value.</param>
    /// <returns>-1 if <paramref name="left"/> is smaller, 0 if they are equal, or 1 if it is larger.</returns>
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
    /// A hash of the numeric value, not the bits. Equal values with different exponents,
    /// and the two zeros, hash the same, as numeric equality requires.
    /// </summary>
    /// <param name="bits">The encoded value.</param>
    /// <returns>The hash code.</returns>
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

        // All equal values must hash the same, so remove every trailing zero, even past
        // exponent zero.
        Decimal128Shaping.StripTrailingZeros(ref coefficient, ref exponent, int.MaxValue);

        return HashCode.Combine(Decimal128Encoding.IsNegative(bits), exponent, coefficient.High, coefficient.Low);
    }
}
