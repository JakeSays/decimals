// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// The square root, correctly rounded, on a machine word.
/// </summary>
/// <remarks>
/// <para>
/// The coefficient is scaled by an even power of ten until it has fifteen or sixteen
/// digits, whose integer root has eight: seven to keep and one to round on. Whether the
/// root squares back to the radicand says whether anything lies below that digit. Halving
/// the exponent is what makes the root's, which is why the scaling keeps it even.
/// </para>
/// <para>
/// The radicand is below 10^16, so it goes to a double within a unit, and the double's
/// root is within one of the integer root; comparing exact squares settles it. Every
/// square fits the word.
/// </para>
/// <para>
/// An exact root is shortened toward the exponent the specification prefers, which is half
/// the operand's, giving back the trailing zeros the scaling introduced.
/// </para>
/// </remarks>
internal static class Decimal32SquareRoot
{
    /// <summary>Digits the radicand is scaled to, or one fewer when that keeps the exponent even.</summary>
    private const int RadicandDigits = 16;

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

        // An odd exponent is made even by moving a digit into the coefficient.
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

        // Eight digits with something non-zero below them: the last digit is the round
        // digit and the remainder is a sticky below it.
        var kept = root / 10;
        var roundDigit = root - (kept * 10);
        var residue = Decimal32Rounder.Combine(roundDigit, 5, Decimal32Residue.BelowHalf);

        return Decimal32Finalizer.Finalize(false, kept, rootExponent + 1, residue, rounding, ref status);
    }

    /// <summary>
    /// The largest integer whose square does not exceed the radicand, which is below 10^16
    /// and so has a root below 10^8.
    /// </summary>
    private static ulong IntegerSquareRoot(ulong radicand, out bool exact)
    {
        var estimate = (ulong)(long)Math.Sqrt((double)(long)radicand);

        // The double's root is within one of the integer root: the radicand went to the
        // double within a unit, and the root of a value below 10^16 changes by less than
        // that for a unit of radicand.
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
