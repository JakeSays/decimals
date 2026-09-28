// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// The fused multiply-add of a product too wide for the format, which at this width still
/// fits the word: two coefficients of at most seven digits multiply to at most fourteen.
/// </summary>
/// <remarks>
/// The product stays exact and only the addend is folded, or both stay exact in the word
/// when the addend scaled to the product's exponent fits beside it, or the addend is
/// widened and the product folded under it when the addend sits far above. Either way one
/// inexact quantity at most reaches the rounding, which is what a residue can carry
/// correctly. The product is at least eight digits here, so a folded addend, which has at
/// most six, can never cancel it.
/// </remarks>
internal static class Decimal32Product
{
    private const int WideDigits = Decimal32Tables.MaxPower;

    /// <summary>
    /// Adds an addend to a product of eight to fourteen digits, rounding once. Which of the
    /// two operands gets folded, if either, depends on where the addend sits against the
    /// product.
    /// </summary>
    public static uint FusedAdd(bool productNegative, ulong product, int productExponent, bool addendNegative,
        ulong addend, int addendExponent, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        if (addend == 0)
        {
            return Decimal32Finalizer.Finalize(productNegative, product, productExponent, Decimal32Residue.Exact,
                rounding, ref status);
        }

        var shift = addendExponent - productExponent;
        var addendDigits = Decimal32Tables.CountDigits(addend);

        if (shift < 0)
        {
            return AddBelow(productNegative, product, productExponent, addendNegative, addend, -shift,
                rounding, ref status);
        }

        if (addendDigits + shift <= WideDigits)
        {
            return AddExact(productNegative, product, productExponent, addendNegative, addend, shift,
                rounding, ref status);
        }

        return AddAbove(productNegative, product, productExponent, addendNegative, addend, addendDigits,
            addendExponent, rounding, ref status);
    }

    /// <summary>
    /// The addend lies below the product's last digit: the addend is folded to the
    /// product's exponent and its discarded digits become the residue. The product is at
    /// least eight digits and the folded addend at most six, so nothing cancels.
    /// </summary>
    private static uint AddBelow(bool productNegative, ulong product, int exponent, bool addendNegative,
        ulong addend, int drop, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        // The exact sum reaches down to the addend's last digit, below a product already
        // wider than the format, so digits are discarded whatever they hold.
        status |= Decimal32Status.Rounded;

        ulong folded;
        Decimal32Residue residue;
        if (drop > Decimal32Encoding.Precision)
        {
            folded = 0;
            residue = Decimal32Residue.BelowHalf;
        }
        else
        {
            folded = Decimal32Tables.DivRemPowerOfTen(addend, drop, out var discarded);
            residue = Decimal32Rounder.Of(discarded, Decimal32Tables.HalfPowerOfTen(drop));
        }

        if (productNegative == addendNegative)
        {
            return Decimal32Finalizer.Finalize(productNegative, product + folded, exponent, residue, rounding,
                ref status);
        }

        // Subtracting an inexact operand: the whole units come off, one more unit comes off
        // for the fraction, and the fraction's residue flips to what is left of that unit.
        if (residue != Decimal32Residue.Exact)
        {
            folded++;
            residue = Decimal32Rounder.Flip(residue);
        }

        return Decimal32Finalizer.Finalize(productNegative, product - folded, exponent, residue, rounding, ref status);
    }

    /// <summary>
    /// The addend, scaled to the product's exponent, fits the word beside the product: the
    /// sum or difference is formed exactly and rounded once.
    /// </summary>
    private static uint AddExact(bool productNegative, ulong product, int exponent, bool addendNegative,
        ulong addend, int shift, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        var scaled = addend * Decimal32Tables.PowerOfTen(shift);

        if (productNegative == addendNegative)
        {
            return Decimal32Finalizer.Finalize(productNegative, product + scaled, exponent, Decimal32Residue.Exact,
                rounding, ref status);
        }

        if (product == scaled)
        {
            // Opposite signs canceling exactly gives a positive zero, except when the
            // rounding runs toward negative infinity.
            return Decimal32Finalizer.Zero(rounding == Decimal32Rounding.Floor, exponent, ref status);
        }

        if (product > scaled)
        {
            return Decimal32Finalizer.Finalize(productNegative, product - scaled, exponent, Decimal32Residue.Exact,
                rounding, ref status);
        }

        return Decimal32Finalizer.Finalize(addendNegative, scaled - product, exponent, Decimal32Residue.Exact,
            rounding, ref status);
    }

    /// <summary>
    /// The addend's last digit sits far enough above the product that, once the addend is
    /// widened to nineteen digits, the product folded under it has at most thirteen:
    /// nothing can cancel down into the fold, so the product's discarded digits are the
    /// residue.
    /// </summary>
    private static uint AddAbove(bool productNegative, ulong product, int productExponent, bool addendNegative,
        ulong addend, int addendDigits, int addendExponent, Decimal32Rounding rounding,
        ref Decimal32Status status)
    {
        var widen = WideDigits - addendDigits;
        var wide = addend * Decimal32Tables.PowerOfTen(widen);
        var exponent = addendExponent - widen;
        var folded = Decimal32Rounder.DropDigits(product, exponent - productExponent, Decimal32Residue.Exact,
            out var residue);

        if (productNegative == addendNegative)
        {
            return Decimal32Finalizer.Finalize(addendNegative, wide + folded, exponent, residue, rounding, ref status);
        }

        if (residue == Decimal32Residue.Exact)
        {
            return Decimal32Finalizer.Finalize(addendNegative, wide - folded, exponent, Decimal32Residue.Exact,
                rounding, ref status);
        }

        return Decimal32Finalizer.Finalize(addendNegative, wide - folded - 1, exponent, Decimal32Rounder.Flip(residue),
            rounding, ref status);
    }
}
