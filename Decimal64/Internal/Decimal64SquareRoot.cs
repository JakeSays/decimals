// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// The square root, correctly rounded, on machine words.
/// </summary>
/// <remarks>
/// <para>
/// The coefficient is scaled by an even power of ten until it has thirty-three or
/// thirty-four digits, whose integer root has seventeen: sixteen to keep and one to round
/// on. Whether the root squares back to the radicand says whether anything lies below that
/// digit. Halving the exponent is what makes the root's, which is why the scaling keeps it
/// even.
/// </para>
/// <para>
/// The radicand is held in two words. Its root is estimated in floating point from the
/// eighteen-digit coefficient and the exact power of ten that scaled it, pulled to within
/// one by a correction from the exact remainder, and then settled by comparing exact
/// squares -- nothing wider than a 64-by-64 multiply, and nothing that leaves the signed
/// range of a word, so every conversion to and from double is the single instruction.
/// </para>
/// <para>
/// An exact root is shortened toward the exponent the specification prefers, which is half
/// the operand's, giving back the trailing zeros the scaling introduced.
/// </para>
/// </remarks>
internal static class Decimal64SquareRoot
{
    /// <summary>
    /// Digits the coefficient is widened to before the last power of ten is applied: the
    /// most that leaves it inside a signed word, so that it converts to a double directly.
    /// </summary>
    private const int WidenedDigits = 18;

    public static ulong SquareRoot(ulong value, Decimal64Rounding rounding, ref Decimal64Status status)
    {
        if (Decimal64Encoding.IsSpecial(value))
        {
            if (Decimal64Encoding.IsNaN(value))
            {
                return Decimal64Arithmetic.PropagateNaN(value, ref status);
            }

            if (Decimal64Encoding.IsNegative(value))
            {
                return Decimal64Arithmetic.Invalid(ref status);
            }

            return Decimal64Encoding.Infinity(false);
        }

        var coefficient = Decimal64Encoding.Unpack(value, out var exponent);
        var idealExponent = exponent >= 0 ? exponent / 2 : (exponent - 1) / 2;

        if (coefficient == 0)
        {
            // Both zeros keep their sign: the root of a negative zero is a negative zero.
            return Decimal64Finalizer.Zero(Decimal64Encoding.IsNegative(value), idealExponent, ref status);
        }

        if (Decimal64Encoding.IsNegative(value))
        {
            return Decimal64Arithmetic.Invalid(ref status);
        }

        if ((exponent & 1) != 0)
        {
            coefficient *= 10;
            exponent--;
        }

        var digits = Decimal64Tables.CountDigits(coefficient);
        var scale = 34 - digits;
        if ((scale & 1) != 0)
        {
            scale--;
        }

        // The radicand as two words: the coefficient is first widened to eighteen digits,
        // which fits, and the rest of the scaling -- fifteen or sixteen more -- is one full
        // multiply. Both factors are exact as doubles, which is what the estimate needs.
        var widen = WidenedDigits - digits;
        var wide = coefficient * Decimal64Tables.PowerOfTen(widen);
        var power = Decimal64Tables.PowerOfTen(scale - widen);
        var high = Math.BigMul(wide, power, out var low);

        var root = IntegerSquareRoot(wide, power, high, low, out var exact);
        var rootExponent = (exponent - scale) / 2;

        if (exact)
        {
            Decimal64Shaping.StripTrailingZeros(ref root, ref rootExponent, idealExponent);
            return Decimal64Finalizer.Finalize(false, root, rootExponent, Decimal64Residue.Exact, rounding, ref status);
        }

        // Seventeen digits with something non-zero below them: the last digit is the round
        // digit and the remainder is a sticky below it.
        var kept = root / 10;
        var roundDigit = root - (kept * 10);
        var residue = Decimal64Rounder.Combine(roundDigit, 5, Decimal64Residue.BelowHalf);

        return Decimal64Finalizer.Finalize(false, kept, rootExponent + 1, residue, rounding, ref status);
    }

    /// <summary>
    /// The largest integer whose square does not exceed the two-word value
    /// <c>wide * power</c>, which is below 10^34 and so has a root below 10^17.
    /// </summary>
    private static ulong IntegerSquareRoot(ulong wide, ulong power, ulong high, ulong low, out bool exact)
    {
        var estimate = (ulong)(long)Math.Sqrt((double)(long)wide * (double)(long)power);

        // The double carried fifty-three bits of a root that can need fifty-seven, so the
        // estimate can be off by a couple of dozen. The difference between the radicand and
        // the estimate's square is then well inside a signed word, so its low word is the
        // whole of it, and one correction from it brings the estimate within one.
        var squareHigh = Math.BigMul(estimate, estimate, out var squareLow);
        var difference = (double)(long)(low - squareLow);
        estimate = (ulong)((long)estimate + (long)(difference / (2.0 * (double)(long)estimate)));

        squareHigh = Math.BigMul(estimate, estimate, out squareLow);
        while (IsAbove(squareHigh, squareLow, high, low))
        {
            estimate--;
            squareHigh = Math.BigMul(estimate, estimate, out squareLow);
        }

        while (true)
        {
            var next = estimate + 1;
            var nextHigh = Math.BigMul(next, next, out var nextLow);
            if (IsAbove(nextHigh, nextLow, high, low))
            {
                break;
            }

            estimate = next;
            squareHigh = nextHigh;
            squareLow = nextLow;
        }

        exact = squareHigh == high && squareLow == low;
        return estimate;
    }

    /// <summary>Whether a two-word square exceeds the two-word radicand.</summary>
    private static bool IsAbove(ulong squareHigh, ulong squareLow, ulong high, ulong low)
    {
        if (squareHigh != high)
        {
            return squareHigh > high;
        }

        return squareLow > low;
    }
}
