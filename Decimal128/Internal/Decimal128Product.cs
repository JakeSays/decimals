// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace Decimals.Internal;

/// <summary>
/// Rounding the exact product of two coefficients, held in four words, and the fused
/// multiply-add built on it.
/// </summary>
/// <remarks>
/// <para>
/// A product of two thirty-four digit coefficients runs to sixty-eight digits, which four
/// words hold exactly. Rounding it to the format is a division by a power of ten, done a
/// word at a time from the top by <see cref="Decimal128Tables"/>, with what falls off
/// becoming the residue.
/// </para>
/// <para>
/// The fused multiply-add keeps the product exact and folds only the addend, or keeps both
/// exact in four words when the addend reaches into the product's digits, or widens the
/// addend and folds the product when the addend sits far above it. Either way one inexact
/// quantity at most reaches the rounding, which is what a residue can carry correctly.
/// </para>
/// </remarks>
internal static class Decimal128Product
{
    /// <summary>
    /// Digits a scaled addend may run to and still be added to the product exactly: the
    /// product's sixty-eight and one more, which four words still hold beside it.
    /// </summary>
    private const int ExactDigits = 69;

    /// <summary>
    /// Rounds a four-word value into the format. Digits past the precision come off from
    /// the bottom, whether or not they are zero, and are the residue.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Decimal128Integer Reduce(bool negative, Decimal128LongInteger value, int exponent,
        Decimal128Residue residue, Decimal128Rounding rounding, ref Decimal128Status status)
    {
        var digits = Decimal128Tables.CountDigits(value);
        if (digits <= Decimal128Encoding.Precision)
        {
            return Decimal128Finalizer.Finalize(negative, value.ToInteger(), exponent, residue, rounding, ref status);
        }

        // Digits are discarded here, whether or not they are zero, and the specification
        // counts that as rounding.
        status |= Decimal128Status.Rounded;

        var drop = digits - Decimal128Encoding.Precision;
        var kept = Decimal128Rounder.DropDigits(value, drop, residue, out residue);
        return Decimal128Finalizer.Finalize(negative, kept.ToInteger(), exponent + drop, residue, rounding, ref status);
    }

    /// <summary>
    /// Adds an addend to an exact product, rounding once. Which of the two operands gets
    /// folded, if either, depends on where the addend sits against the product.
    /// </summary>
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
    /// The addend's exponent lies below the product's: the addend is folded up to the
    /// product's exponent, its discarded digits become the residue, and the sum or
    /// difference is formed exactly on top. The product is never zero here, but it can
    /// be the smaller of the two, in which case the difference takes the addend's sign and
    /// the residue stands as it is.
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
            // Subtracting an inexact operand: the whole units come off, one more unit
            // comes off for the fraction, and the fraction's residue flips to what is
            // left of that unit.
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
            // The addend is the larger, so the fraction below its folded units is still
            // added rather than taken away, and the residue stands.
            return Decimal128Finalizer.Finalize(addendNegative, folded - product.ToInteger(), productExponent,
                residue, rounding, ref status);
        }

        if (residue == Decimal128Residue.Exact)
        {
            // Opposite signs canceling exactly gives a positive zero, except when the
            // rounding runs toward negative infinity.
            return Decimal128Finalizer.Zero(rounding == Decimal128Rounding.Floor, addendExponent, ref status);
        }

        // The units cancel and only the addend's discarded digits are left, which are an
        // exact value at the addend's own exponent.
        return Decimal128Finalizer.Finalize(addendNegative, discarded, addendExponent, Decimal128Residue.Exact,
            rounding, ref status);
    }

    /// <summary>
    /// The addend, scaled to the product's exponent, fits four words beside the product:
    /// the sum or difference is formed exactly and rounded once.
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
    /// The addend's last digit sits far enough above the product that, once the addend is
    /// widened to thirty-eight digits, the product folded under it has at most thirty-six:
    /// nothing can cancel down into the fold, so the product's discarded digits are the
    /// residue.
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
