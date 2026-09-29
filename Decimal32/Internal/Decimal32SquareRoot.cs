// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// The correctly rounded square root, computed in a 64-bit word.
/// </summary>
/// <remarks>
/// <para>
/// The coefficient is scaled by an even power of ten to 15 or 16 digits. Its integer root
/// then has 8 digits: 7 to keep and one to round on. If the root squared equals the
/// radicand, nothing lies below the round digit. The root's exponent is half the operand's
/// exponent, which is why the scale must be even.
/// </para>
/// <para>
/// The radicand is below 10^16, so its conversion to double is off by at most 1, and the
/// double's square root is within 1 of the integer root. Comparing exact squares settles
/// it. Every square fits in a 64-bit word.
/// </para>
/// <para>
/// An exact root drops trailing zeros until its exponent reaches the preferred exponent,
/// which is half the operand's. This removes the zeros that the scaling added.
/// </para>
/// </remarks>
internal static class Decimal32SquareRoot
{
    /// <summary>The number of digits the radicand is scaled to, or one fewer to keep the exponent even.</summary>
    private const int RadicandDigits = 16;

    /// <summary>The square root, correctly rounded.</summary>
    /// <param name="value">The encoded operand.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded root, or a quiet NaN if the operand is negative and not zero.</returns>
    public static uint SquareRoot(uint value, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsSpecial(value))
        {
            if (Decimal32Encoding.IsNaN(value))
            {
                return Decimal32Arithmetic.PropagateNaN(value, ref status);
            }

            if (Decimal32Encoding.IsNegative(value))
            {
                return Decimal32Arithmetic.Invalid(ref status);
            }

            return Decimal32Encoding.Infinity(false);
        }

        ulong coefficient = Decimal32Encoding.Unpack(value, out var exponent);

        // Half the exponent, rounded toward negative infinity for either sign.
        var idealExponent = exponent >> 1;

        if (coefficient == 0)
        {
            // Both zeros keep their sign: the root of a negative zero is a negative zero.
            return Decimal32Finalizer.Zero(Decimal32Encoding.IsNegative(value), idealExponent, ref status);
        }

        if (Decimal32Encoding.IsNegative(value))
        {
            return Decimal32Arithmetic.Invalid(ref status);
        }

        // Make an odd exponent even by multiplying the coefficient by 10.
        var odd = exponent & 1;
        coefficient *= 1UL + (9UL * (ulong)odd);
        exponent -= odd;

        var digits = Decimal32Tables.CountDigits(coefficient);
        var scale = (RadicandDigits - digits) & ~1;
        var radicand = coefficient * Decimal32Tables.PowerOfTen(scale);

        var root = IntegerSquareRoot(radicand, out var exact);
        var rootExponent = (exponent - scale) / 2;

        if (exact)
        {
            Decimal32Shaping.StripTrailingZeros(ref root, ref rootExponent, idealExponent);
            return Decimal32Finalizer.Finalize(false, root, rootExponent, Decimal32Residue.Exact, rounding, ref status);
        }

        // 8 digits with a non-zero remainder below them. The last digit is the round digit,
        // and the remainder acts as a sticky digit below it.
        var kept = root / 10;
        var roundDigit = root - (kept * 10);
        var residue = Decimal32Rounder.Combine(roundDigit, 5, Decimal32Residue.BelowHalf);

        return Decimal32Finalizer.Finalize(false, kept, rootExponent + 1, residue, rounding, ref status);
    }

    /// <summary>
    /// The largest integer whose square does not exceed the radicand. The radicand is below
    /// 10^16, so the root is below 10^8.
    /// </summary>
    private static ulong IntegerSquareRoot(ulong radicand, out bool exact)
    {
        var estimate = (ulong)(long)Math.Sqrt((double)(long)radicand);

        // The double's root is within 1 of the integer root. The radicand converts to double
        // with an error of at most 1, and for values below 10^16 that changes the root by
        // much less than 1.
        var square = estimate * estimate;
        if (square > radicand)
        {
            estimate--;
            square = estimate * estimate;
        }
        else
        {
            var next = estimate + 1;
            var nextSquare = next * next;
            if (nextSquare <= radicand)
            {
                estimate = next;
                square = nextSquare;
            }
        }

        exact = square == radicand;
        return estimate;
    }
}
