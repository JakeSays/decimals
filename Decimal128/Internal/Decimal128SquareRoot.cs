// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace Decimals.Internal;

/// <summary>
/// The square root, correctly rounded, on machine words.
/// </summary>
/// <remarks>
/// <para>
/// The coefficient is scaled by an even power of ten until it has sixty-nine or seventy
/// digits, whose integer root has thirty-five: thirty-four to keep and one to round on.
/// Whether the root squares back to the radicand says whether anything lies below that
/// digit. Halving the exponent is what makes the root's, which is why the scaling keeps it
/// even.
/// </para>
/// <para>
/// The radicand is held in four words. Its root is estimated in floating point from the
/// top two of them, which gives fifty-three of its hundred and sixteen bits, and pulled to
/// between one below the root and two above it by two Newton steps, each computed from the
/// exact difference between the radicand and the estimate's square; the last units are
/// settled by comparing exact squares. Nothing wider than a 64-by-64 multiply is used.
/// </para>
/// <para>
/// Nothing in the root's computation branches on the data: the sign of a correction is
/// applied through a mask, and the settling compares the four candidates around the
/// estimate at once rather than walking to the root. Whether the estimate lies above or
/// below the root is a coin flip, and a branch on it mispredicted as often as not.
/// </para>
/// <para>
/// An exact root is shortened toward the exponent the specification prefers, which is half
/// the operand's, giving back the trailing zeros the scaling introduced.
/// </para>
/// </remarks>
[SkipLocalsInit]
internal static class Decimal128SquareRoot
{
    /// <summary>Digits the radicand is scaled to, or one fewer when that keeps the exponent even.</summary>
    private const int RadicandDigits = 70;

    private const double WordScale = 18446744073709551616.0;

    private const double TwoWordScale = WordScale * WordScale;

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

        // An odd exponent is made even by moving a digit into the coefficient. The
        // multiply is done either way rather than branched on, since which it is follows
        // the data.
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

        // Thirty-five digits with something non-zero below them: the last digit is the
        // round digit and the remainder is a sticky below it.
        var kept = Decimal128Tables.DivRemPowerOfTen(root, 1, out var roundDigit);
        var residue = Decimal128Rounder.Combine(roundDigit, 5, Decimal128Residue.BelowHalf);

        return Decimal128Finalizer.Finalize(false, kept, rootExponent + 1, residue, rounding, ref status);
    }

    /// <summary>
    /// The largest integer whose square does not exceed the radicand, which has at least
    /// sixty-nine digits and fewer than seventy-one, and so a root below 10^35.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The seed is off by up to 2^65 for a root that can need a hundred and seventeen
    /// bits, so the first correction goes through two words; it leaves the estimate within
    /// 2^16, so the second fits a word.
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
        // The radicand's top two words hold at least ninety-seven of its bits, which is
        // more than a double keeps, so they are all the seed is taken from.
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
    /// One Newton correction: the difference between the radicand and the estimate's
    /// square over twice the estimate, floored, as a magnitude with its sign in a mask.
    /// The difference is exact and signed, and below 2^183 in magnitude either way, so its
    /// magnitude's second and third words carry it to a double.
    /// </summary>
    /// <remarks>
    /// The magnitude is taken first because a two's complement value cannot go to a
    /// double word by word: the rounding of an all-ones word swallows every word below
    /// it, and the sum comes out with the wrong sign.
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

    /// <summary>The value as it is under a zero mask, and its two's complement under an all-ones one.</summary>
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
    /// The root from an estimate between one below it and two above it: the largest of the
    /// four candidates from two below the estimate to one above it whose square does not
    /// exceed the radicand. The squares come from the lowest candidate's by adding the
    /// successive odd numbers above twice it, and the candidates that fit are counted
    /// rather than walked.
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
