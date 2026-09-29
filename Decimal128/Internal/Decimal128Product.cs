// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace Decimals.Internal;

/// <summary>
/// Rounds the exact four-word product of two coefficients, and implements the fused
/// multiply-add on top of it.
/// </summary>
/// <remarks>
/// <para>
/// Two 34-digit coefficients multiply to at most 68 digits, which four words hold exactly.
/// Rounding the product to the format is a division by a power of ten. <see cref="Decimal128Tables"/>
/// divides one word at a time from the top, and the discarded digits become the residue.
/// </para>
/// <para>
/// The fused multiply-add has three cases. The product stays exact and only the addend is
/// folded. Or both stay exact in four words, when the addend overlaps the product's
/// digits. Or the addend is far above, so it is widened and the product is folded below
/// it. In every case, at most one inexact value reaches the rounding, which is what a
/// residue can handle correctly.
/// </para>
/// </remarks>
internal static class Decimal128Product
{
    /// <summary>
    /// The largest number of digits a scaled addend can have and still be added to the
    /// product exactly: the product's 68 digits plus one. Four words still hold the sum.
    /// </summary>
    private const int ExactDigits = 69;

    /// <summary>
    /// Rounds a four-word value to the format. Digits beyond the precision are discarded
    /// from the bottom, even if they are zero, and become the residue.
    /// </summary>
    /// <param name="negative">Whether the value is negative.</param>
    /// <param name="value">The exact four-word value, such as a product of two coefficients.</param>
    /// <param name="exponent">The value's exponent.</param>
    /// <param name="residue">The residue of any digits already discarded below <paramref name="value"/>.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the rounding raises.</param>
    /// <returns>The encoded, rounded value.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Decimal128Integer Reduce(bool negative, Decimal128LongInteger value, int exponent,
        Decimal128Residue residue, Decimal128Rounding rounding, ref Decimal128Status status)
    {
        var digits = Decimal128Tables.CountDigits(value);
        if (digits <= Decimal128Encoding.Precision)
        {
            return Decimal128Finalizer.Finalize(negative, value.ToInteger(), exponent, residue, rounding, ref status);
        }

        // The specification counts discarding digits as rounding, even if they are zero.
        status |= Decimal128Status.Rounded;

        var drop = digits - Decimal128Encoding.Precision;
        var kept = Decimal128Rounder.DropDigits(value, drop, residue, out residue);
        return Decimal128Finalizer.Finalize(negative, kept.ToInteger(), exponent + drop, residue, rounding, ref status);
    }

    /// <summary>
    /// Adds an addend to an exact product, with a single rounding. Which operand is folded,
    /// if either, depends on the addend's position relative to the product.
    /// </summary>
    /// <param name="productNegative">Whether the product is negative.</param>
    /// <param name="product">The exact product, in four words.</param>
    /// <param name="productExponent">The product's exponent.</param>
    /// <param name="addendNegative">Whether the addend is negative.</param>
    /// <param name="addend">The addend's coefficient.</param>
    /// <param name="addendExponent">The addend's exponent.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded sum, rounded once.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Decimal128Integer FusedAdd(bool productNegative, Decimal128LongInteger product, int productExponent,
        bool addendNegative, Decimal128Integer addend, int addendExponent, Decimal128Rounding rounding,
        ref Decimal128Status status)
    {
        if (addend.IsZero)
        {
            return Reduce(productNegative, product, productExponent, Decimal128Residue.Exact, rounding, ref status);
        }

        var shift = addendExponent - productExponent;
        var addendDigits = Decimal128Tables.CountDigits(addend);

        if (shift < 0)
        {
            return AddBelow(productNegative, product, productExponent, addendNegative, addend, addendExponent,
                -shift, rounding, ref status);
        }

        if (addendDigits + shift <= ExactDigits)
        {
            return AddExact(productNegative, product, productExponent, addendNegative, addend, shift, rounding,
                ref status);
        }

        return AddAbove(productNegative, product, productExponent, addendNegative, addend, addendDigits,
            addendExponent, rounding, ref status);
    }

    /// <summary>
    /// The addend's exponent is below the product's. The addend is folded to the product's
    /// exponent, its discarded digits become the residue, and the sum or difference is
    /// computed exactly. The product is never zero here, but it can be smaller than the
    /// addend. Then the difference takes the addend's sign and the residue is unchanged.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Decimal128Integer AddBelow(bool productNegative, Decimal128LongInteger product, int productExponent,
        bool addendNegative, Decimal128Integer addend, int addendExponent, int drop, Decimal128Rounding rounding,
        ref Decimal128Status status)
    {
        Decimal128Integer folded;
        Decimal128Integer discarded;
        Decimal128Residue residue;
        if (drop > Decimal128Encoding.Precision)
        {
            folded = Decimal128Integer.Zero;
            discarded = addend;
            residue = Decimal128Residue.BelowHalf;
        }
        else
        {
            folded = Decimal128Tables.DivRemWidePowerOfTen(addend, drop, out discarded);
            residue = Decimal128Rounder.Of(discarded, Decimal128Tables.WideHalfPowerOfTen(drop));
        }

        var wideFolded = Decimal128LongInteger.FromInteger(folded);

        if (productNegative == addendNegative)
        {
            return Reduce(productNegative, product + wideFolded, productExponent, residue, rounding, ref status);
        }

        var comparison = product.CompareTo(wideFolded);
        if (comparison > 0)
        {
            // Subtracting an inexact operand: subtract the whole units, then one more unit
            // for the fraction. The residue is flipped to describe what remains of that unit.
            var difference = product - wideFolded;
            if (residue != Decimal128Residue.Exact)
            {
                difference -= Decimal128LongInteger.FromInteger(Decimal128Integer.One);
                residue = Decimal128Rounder.Flip(residue);
            }

            return Reduce(productNegative, difference, productExponent, residue, rounding, ref status);
        }

        if (comparison < 0)
        {
            // The addend is larger, so its fraction below the folded units is still added,
            // not subtracted, and the residue is unchanged.
            return Decimal128Finalizer.Finalize(addendNegative, folded - product.ToInteger(), productExponent,
                residue, rounding, ref status);
        }

        if (residue == Decimal128Residue.Exact)
        {
            // Opposite signs that cancel exactly give positive zero, except when rounding
            // toward negative infinity.
            return Decimal128Finalizer.Zero(rounding == Decimal128Rounding.Floor, addendExponent, ref status);
        }

        // The units cancel, and only the addend's discarded digits remain. They are an
        // exact value at the addend's exponent.
        return Decimal128Finalizer.Finalize(addendNegative, discarded, addendExponent, Decimal128Residue.Exact,
            rounding, ref status);
    }

    /// <summary>
    /// The addend, scaled to the product's exponent, fits in four words with the product.
    /// The sum or difference is computed exactly and rounded once.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Decimal128Integer AddExact(bool productNegative, Decimal128LongInteger product, int exponent,
        bool addendNegative, Decimal128Integer addend, int shift, Decimal128Rounding rounding,
        ref Decimal128Status status)
    {
        var scaled = Decimal128Tables.ScaleLong(addend, shift);

        if (productNegative == addendNegative)
        {
            return Reduce(productNegative, product + scaled, exponent, Decimal128Residue.Exact, rounding, ref status);
        }

        var comparison = product.CompareTo(scaled);
        if (comparison == 0)
        {
            return Decimal128Finalizer.Zero(rounding == Decimal128Rounding.Floor, exponent, ref status);
        }

        if (comparison > 0)
        {
            return Reduce(productNegative, product - scaled, exponent, Decimal128Residue.Exact, rounding, ref status);
        }

        return Reduce(addendNegative, scaled - product, exponent, Decimal128Residue.Exact, rounding, ref status);
    }

    /// <summary>
    /// The addend's last digit is far enough above the product that, after the addend is
    /// widened to 38 digits, the product folded below it has at most 36 digits. Nothing can
    /// cancel into the folded part, so the product's discarded digits are the residue.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Decimal128Integer AddAbove(bool productNegative, Decimal128LongInteger product, int productExponent,
        bool addendNegative, Decimal128Integer addend, int addendDigits, int addendExponent,
        Decimal128Rounding rounding, ref Decimal128Status status)
    {
        var widen = Decimal128Tables.MaxWidePower - addendDigits;
        var wide = Decimal128Tables.Scale(addend, widen);
        var exponent = addendExponent - widen;
        var folded = Decimal128Rounder.DropDigits(product, exponent - productExponent, Decimal128Residue.Exact,
            out var residue).ToInteger();

        if (productNegative == addendNegative)
        {
            return Decimal128Finalizer.Finalize(addendNegative, wide + folded, exponent, residue, rounding, ref status);
        }

        if (residue == Decimal128Residue.Exact)
        {
            return Decimal128Finalizer.Finalize(addendNegative, wide - folded, exponent, Decimal128Residue.Exact,
                rounding, ref status);
        }

        return Decimal128Finalizer.Finalize(addendNegative, wide - folded - 1, exponent,
            Decimal128Rounder.Flip(residue), rounding, ref status);
    }
}
