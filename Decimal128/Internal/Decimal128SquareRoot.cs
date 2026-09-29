// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace Decimals.Internal;

/// <summary>
/// The correctly rounded square root, computed with 64-bit words.
/// </summary>
/// <remarks>
/// <para>
/// The coefficient is scaled by an even power of ten to 69 or 70 digits. Its integer root
/// then has 35 digits: 34 to keep and one to round on. If the root squared equals the
/// radicand, nothing lies below the round digit. The root's exponent is half the operand's
/// exponent, which is why the scale must be even.
/// </para>
/// <para>
/// The radicand takes four words. Its root is first estimated in floating point from the
/// top two words, which gives 53 of its 116 bits. Two Newton steps then bring the estimate
/// to between 1 below the root and 2 above it. Each step uses the exact difference between
/// the radicand and the estimate's square. Comparing exact squares settles the last units.
/// Nothing is wider than a 64-by-64 multiply.
/// </para>
/// <para>
/// The computation does not branch on the data. The sign of each correction is applied
/// with a mask, and the settling step checks all four candidates at once instead of
/// stepping toward the root. Whether the estimate is above or below the root is random,
/// and a branch on it mispredicted about half the time.
/// </para>
/// <para>
/// An exact root drops trailing zeros until its exponent reaches the preferred exponent,
/// which is half the operand's. This removes the zeros that the scaling added.
/// </para>
/// </remarks>
[SkipLocalsInit]
internal static class Decimal128SquareRoot
{
    /// <summary>The number of digits the radicand is scaled to, or one fewer to keep the exponent even.</summary>
    private const int RadicandDigits = 70;

    private const double WordScale = 18446744073709551616.0;

    private const double TwoWordScale = WordScale * WordScale;

    /// <summary>The square root, correctly rounded.</summary>
    /// <param name="value">The encoded operand.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded root, or a quiet NaN if the operand is negative and not zero.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Decimal128Integer SquareRoot(Decimal128Integer value, Decimal128Rounding rounding,
        ref Decimal128Status status)
    {
        if (Decimal128Encoding.IsSpecial(value))
        {
            if (Decimal128Encoding.IsNaN(value))
            {
                return Decimal128Arithmetic.PropagateNaN(value, ref status);
            }

            if (Decimal128Encoding.IsNegative(value))
            {
                return Decimal128Arithmetic.Invalid(ref status);
            }

            return Decimal128Encoding.Infinity(false);
        }

        var coefficient = Decimal128Encoding.Unpack(value, out var exponent);

        // Half the exponent, rounded toward negative infinity for either sign.
        var idealExponent = exponent >> 1;

        if (coefficient.IsZero)
        {
            // Both zeros keep their sign: the root of a negative zero is a negative zero.
            return Decimal128Finalizer.Zero(Decimal128Encoding.IsNegative(value), idealExponent, ref status);
        }

        if (Decimal128Encoding.IsNegative(value))
        {
            return Decimal128Arithmetic.Invalid(ref status);
        }

        // Make an odd exponent even by multiplying the coefficient by 10. The multiply is
        // done in both cases, by 1 or 10, because a branch on the exponent's parity would
        // depend on the data.
        var odd = exponent & 1;
        coefficient = coefficient.MultiplyBy(1UL + (9UL * (ulong)odd));
        exponent -= odd;

        var digits = Decimal128Tables.CountDigits(coefficient);
        var scale = (RadicandDigits - digits) & ~1;

        var radicand = Decimal128Tables.ScaleLong(coefficient, scale);
        var root = IntegerSquareRoot(radicand, out var exact);
        var rootExponent = (exponent - scale) / 2;

        if (exact)
        {
            Decimal128Shaping.StripTrailingZeros(ref root, ref rootExponent, idealExponent);
            return Decimal128Finalizer.Finalize(false, root, rootExponent, Decimal128Residue.Exact, rounding, ref status);
        }

        // 35 digits with a non-zero remainder below them. The last digit is the round digit,
        // and the remainder acts as a sticky digit below it.
        var kept = Decimal128Tables.DivRemPowerOfTen(root, 1, out var roundDigit);
        var residue = Decimal128Rounder.Combine(roundDigit, 5, Decimal128Residue.BelowHalf);

        return Decimal128Finalizer.Finalize(false, kept, rootExponent + 1, residue, rounding, ref status);
    }

    /// <summary>
    /// The largest integer whose square does not exceed the radicand. The radicand has 69
    /// or 70 digits, so the root is below 10^35.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The seed can be off by up to 2^65, and the root can need 117 bits, so the first
    /// correction uses two words. It leaves the estimate within 2^16 of the root, so the
    /// second correction fits in one word.
    /// </para>
    /// <para>
    /// The second step leaves the estimate at least one below the integer root and at most
    /// two above it. The exact Newton iterate is never below the real root, and flooring
    /// the correction moves the result by less than one. The correction is computed in a
    /// double from the difference with its lowest word dropped, so its floor can also come
    /// out one short of the exact correction's floor. That happens when the exact
    /// correction is a whole number plus a sliver, which is the case when the real root
    /// lies just below a whole number: for the root <c>r + 0.99999...</c>, an estimate
    /// above it can land on <c>r + 2</c>.
    /// </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Decimal128Integer IntegerSquareRoot(Decimal128LongInteger radicand, out bool exact)
    {
        // The radicand's top two words hold at least 97 of its bits, more than a double
        // keeps, so the seed is computed from those two words only.
        var top = (Decimal128Integer.WordToDouble(radicand.Word3) * WordScale)
            + Decimal128Integer.WordToDouble(radicand.Word2);
        var estimateDouble = Math.Sqrt(top) * WordScale;
        var estimate = Decimal128Integer.FromDouble(estimateDouble);

        var correction = Correction(radicand, estimate, estimateDouble, out var negate);
        var step = Decimal128Integer.FromDouble(correction);
        estimate += new Decimal128Integer(step.High ^ negate, step.Low ^ negate) + (negate & 1);
        estimateDouble += Math.CopySign(correction, (double)(long)negate);

        correction = Correction(radicand, estimate, estimateDouble, out negate);
        var narrowStep = (ulong)(long)correction;
        estimate += new Decimal128Integer(negate, narrowStep ^ negate) + (negate & 1);

        return Settle(radicand, estimate, out exact);
    }

    /// <summary>
    /// One Newton correction: (radicand - estimate squared) / (2 * estimate), floored. It
    /// returns the magnitude and puts the sign in a mask. The difference is exact and
    /// signed, and its magnitude is below 2^183, so the magnitude's second and third words
    /// are enough to convert it to a double.
    /// </summary>
    /// <remarks>
    /// The magnitude is taken first because a two's complement value cannot be converted to
    /// double one word at a time. Rounding an all-ones word loses every word below it, and
    /// the sum gets the wrong sign.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Correction(Decimal128LongInteger radicand, Decimal128Integer estimate,
        double estimateDouble, out ulong negate)
    {
        var square = Decimal128LongInteger.Square(estimate);
        var difference = radicand - square;

        negate = 0UL - Unsafe.BitCast<bool, byte>((long)difference.Word3 < 0);
        var magnitude = Negate(difference, negate);
        var value = (Decimal128Integer.WordToDouble(magnitude.Word2) * TwoWordScale)
            + (Decimal128Integer.WordToDouble(magnitude.Word1) * WordScale);

        return Math.Floor(value / (2.0 * estimateDouble));
    }

    /// <summary>Returns the value unchanged for a zero mask, or its two's complement for an all-ones mask.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Decimal128LongInteger Negate(Decimal128LongInteger value, ulong mask)
    {
        var word0 = value.Word0 ^ mask;
        var word1 = value.Word1 ^ mask;
        var word2 = value.Word2 ^ mask;
        var word3 = value.Word3 ^ mask;

        var carry = mask & 1;
        word0 += carry;
        carry = Unsafe.BitCast<bool, byte>(word0 < carry);
        word1 += carry;
        carry = Unsafe.BitCast<bool, byte>(word1 < carry);
        word2 += carry;
        carry = Unsafe.BitCast<bool, byte>(word2 < carry);
        word3 += carry;

        return new Decimal128LongInteger(word3, word2, word1, word0);
    }

    /// <summary>
    /// Finds the root from an estimate that is between 1 below it and 2 above it. The root
    /// is the largest of the four candidates, from 2 below the estimate to 1 above it, whose
    /// square does not exceed the radicand. Each square is the previous square plus the
    /// next odd number. The code counts how many candidates fit instead of looping.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Decimal128Integer Settle(Decimal128LongInteger radicand, Decimal128Integer estimate, out bool exact)
    {
        var lowest = estimate - 2;
        var square = Decimal128LongInteger.Square(lowest);
        var odd = Decimal128LongInteger.FromInteger(lowest + lowest + 1);
        var two = Decimal128LongInteger.FromInteger(Decimal128Integer.FromUInt64(2));

        var matched = square == radicand;
        var count = 0;

        square += odd;
        count += Unsafe.BitCast<bool, byte>(square <= radicand);
        matched |= square == radicand;

        odd += two;
        square += odd;
        count += Unsafe.BitCast<bool, byte>(square <= radicand);
        matched |= square == radicand;

        odd += two;
        square += odd;
        count += Unsafe.BitCast<bool, byte>(square <= radicand);
        matched |= square == radicand;

        exact = matched;
        return lowest + (ulong)count;
    }
}
