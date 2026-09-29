// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// The correctly rounded square root, computed with 64-bit words.
/// </summary>
/// <remarks>
/// <para>
/// The coefficient is scaled by an even power of ten to 33 or 34 digits. Its integer root
/// then has 17 digits: 16 to keep and one to round on. If the root squared equals the
/// radicand, nothing lies below the round digit. The root's exponent is half the operand's
/// exponent, which is why the scale must be even.
/// </para>
/// <para>
/// The radicand takes two words. Its root is estimated in floating point from the 18-digit
/// coefficient and the exact power of ten that scaled it. One correction from the exact
/// remainder brings the estimate close, and comparing exact squares settles the last
/// units. Nothing is wider than a 64-by-64 multiply, and no value leaves the signed range
/// of a word, so every conversion to and from double is a single instruction.
/// </para>
/// <para>
/// An exact root drops trailing zeros until its exponent reaches the preferred exponent,
/// which is half the operand's. This removes the zeros that the scaling added.
/// </para>
/// </remarks>
internal static class Decimal64SquareRoot
{
    /// <summary>
    /// The number of digits the coefficient is widened to before the last power of ten is
    /// applied. It is the most that fits in a signed 64-bit integer, so the coefficient
    /// converts to double directly.
    /// </summary>
    private const int WidenedDigits = 18;

    /// <summary>The square root, correctly rounded.</summary>
    /// <param name="value">The encoded operand.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded root, or a quiet NaN if the operand is negative and not zero.</returns>
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

        // Build the two-word radicand. First widen the coefficient to 18 digits, which fits
        // in a word. The remaining 15 or 16 digits of scaling are one full multiply. Both
        // factors are exact as doubles, which the estimate needs.
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

        // 17 digits with a non-zero remainder below them. The last digit is the round digit,
        // and the remainder acts as a sticky digit below it.
        var kept = root / 10;
        var roundDigit = root - (kept * 10);
        var residue = Decimal64Rounder.Combine(roundDigit, 5, Decimal64Residue.BelowHalf);

        return Decimal64Finalizer.Finalize(false, kept, rootExponent + 1, residue, rounding, ref status);
    }

    /// <summary>
    /// The largest integer whose square does not exceed the two-word value
    /// <c>wide * power</c>. That value is below 10^34, so the root is below 10^17.
    /// </summary>
    private static ulong IntegerSquareRoot(ulong wide, ulong power, ulong high, ulong low, out bool exact)
    {
        var estimate = (ulong)(long)Math.Sqrt((double)(long)wide * (double)(long)power);

        // A double has 53 bits and the root can need 57, so the estimate can be off by a
        // few dozen. The difference between the radicand and the estimate's square then
        // fits in a signed word, so its low word holds all of it. One correction from it
        // brings the estimate close, and the loops below settle the rest.
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

    /// <summary>True if a two-word square is greater than the two-word radicand.</summary>
    private static bool IsAbove(ulong squareHigh, ulong squareLow, ulong high, ulong low)
    {
        if (squareHigh != high)
        {
            return squareHigh > high;
        }

        return squareLow > low;
    }
}
