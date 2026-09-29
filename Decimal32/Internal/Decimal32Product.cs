// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// The fused multiply-add for a product that is too wide for the format. At this width the
/// product still fits in a 64-bit word: two 7-digit coefficients multiply to at most 14
/// digits.
/// </summary>
/// <remarks>
/// There are three cases. The product stays exact and only the addend is folded. Or both
/// stay exact in one word, when the addend scaled to the product's exponent fits. Or the
/// addend is far above, so it is widened and the product is folded below it. In every
/// case, at most one inexact value reaches the rounding, which is what a residue can
/// handle correctly. The product has at least 8 digits here, and a folded addend has at
/// most 6, so the addend can never cancel the product.
/// </remarks>
internal static class Decimal32Product
{
    private const int WideDigits = Decimal32Tables.MaxPower;

    /// <summary>
    /// Adds an addend to a product of 8 to 14 digits, with a single rounding. Which operand
    /// is folded, if either, depends on the addend's position relative to the product.
    /// </summary>
    /// <param name="productNegative">Whether the product is negative.</param>
    /// <param name="product">The exact product, of 8 to 14 digits.</param>
    /// <param name="productExponent">The product's exponent.</param>
    /// <param name="addendNegative">Whether the addend is negative.</param>
    /// <param name="addend">The addend's coefficient.</param>
    /// <param name="addendExponent">The addend's exponent.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded sum, rounded once.</returns>
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
    /// The addend is below the product's last digit. The addend is folded to the product's
    /// exponent, and its discarded digits become the residue. The product has at least 8
    /// digits and the folded addend at most 6, so nothing cancels.
    /// </summary>
    private static uint AddBelow(bool productNegative, ulong product, int exponent, bool addendNegative,
        ulong addend, int drop, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        // The exact sum extends down to the addend's last digit, below a product that is
        // already wider than the format. So digits are discarded, whatever their values.
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

        // Subtracting an inexact operand: subtract the whole units, then one more unit for
        // the fraction. The residue is flipped to describe what remains of that unit.
        if (residue != Decimal32Residue.Exact)
        {
            folded++;
            residue = Decimal32Rounder.Flip(residue);
        }

        return Decimal32Finalizer.Finalize(productNegative, product - folded, exponent, residue, rounding, ref status);
    }

    /// <summary>
    /// The addend, scaled to the product's exponent, fits in the same word as the product.
    /// The sum or difference is computed exactly and rounded once.
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
            // Opposite signs that cancel exactly give positive zero, except when rounding
            // toward negative infinity.
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
    /// The addend's last digit is far enough above the product that, after the addend is
    /// widened to 19 digits, the product folded below it has at most 13 digits. Nothing can
    /// cancel into the folded part, so the product's discarded digits are the residue.
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
