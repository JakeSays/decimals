// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;


namespace Decimals.Internal;

/// <summary>
/// The exact product of two coefficients, held as two base-10^16 limbs, and the fused
/// multiply-add built on it.
/// </summary>
/// <remarks>
/// <para>
/// A binary 128-bit product would need a 128-bit division to round, and there is no such
/// instruction. Instead, each coefficient is split into two 8-digit halves. The four small
/// products combine into base 10^16 using only 64-bit multiplies and one division by a
/// constant. A 16-digit limb is what rounding needs: the number of digits in the high limb
/// is the number of digits to drop.
/// </para>
/// <para>
/// The fused multiply-add keeps the product exact and folds only the addend. When the
/// leading digits of the product and addend are close enough to cancel in a subtraction,
/// it keeps both exact in three limbs. Either way, at most one inexact value reaches the
/// rounding, which is what a residue can handle correctly.
/// </para>
/// </remarks>
internal static class Decimal64Product
{
    private const ulong Limb = 10000000000000000;

    private const ulong HalfSplit = 100000000;

    /// <summary>
    /// Multiplies two coefficients of up to 16 digits into a high and a low base-10^16 limb.
    /// </summary>
    /// <param name="left">The first coefficient, below 10^16.</param>
    /// <param name="right">The second coefficient, below 10^16.</param>
    /// <param name="high">Receives the product divided by 10^16.</param>
    /// <param name="low">Receives the product modulo 10^16.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Multiply(ulong left, ulong right, out ulong high, out ulong low)
    {
        var leftHigh = left / HalfSplit;
        var leftLow = left - (leftHigh * HalfSplit);
        var rightHigh = right / HalfSplit;
        var rightLow = right - (rightHigh * HalfSplit);

        var top = leftHigh * rightHigh;
        var middle = (leftHigh * rightLow) + (leftLow * rightHigh);
        var bottom = leftLow * rightLow;

        var middleHigh = middle / HalfSplit;
        var middleLow = middle - (middleHigh * HalfSplit);

        low = bottom + (middleLow * HalfSplit);
        var carry = 0UL;
        if (low >= Limb)
        {
            low -= Limb;
            carry = 1;
        }

        high = top + middleHigh + carry;
    }

    /// <summary>
    /// Rounds a two-limb product into the format. The number of digits in the high limb is
    /// the number of digits to drop from the low limb. The dropped digits become the residue.
    /// </summary>
    /// <param name="negative">Whether the product is negative.</param>
    /// <param name="high">The product divided by 10^16.</param>
    /// <param name="low">The product modulo 10^16.</param>
    /// <param name="exponent">The product's exponent.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the rounding raises.</param>
    /// <returns>The encoded, rounded product.</returns>
    public static ulong Reduce(bool negative, ulong high, ulong low, int exponent,
        Decimal64Rounding rounding, ref Decimal64Status status)
    {
        if (high == 0)
        {
            return Decimal64Finalizer.Finalize(negative, low, exponent, Decimal64Residue.Exact, rounding, ref status);
        }

        // Digits are discarded here. The specification counts that as rounding, even if
        // the digits are zero.
        status |= Decimal64Status.Rounded;

        var drop = Decimal64Tables.CountDigits(high);
        var kept = Decimal64Tables.DivRemPowerOfTen(low, drop, out var discarded);
        var coefficient = (high * Decimal64Tables.PowerOfTen(Decimal64Encoding.Precision - drop)) + kept;
        var residue = Decimal64Rounder.Of(discarded, Decimal64Tables.HalfPowerOfTen(drop));

        return Decimal64Finalizer.Finalize(negative, coefficient, exponent + drop, residue, rounding, ref status);
    }

    /// <summary>
    /// Adds an addend to a product that is too wide for one word, with a single rounding.
    /// The product is exact in its two limbs. Which operand is folded, if either, depends
    /// on the addend's position relative to the product.
    /// </summary>
    /// <param name="productNegative">Whether the product is negative.</param>
    /// <param name="high">The product divided by 10^16. It is not zero.</param>
    /// <param name="low">The product modulo 10^16.</param>
    /// <param name="productExponent">The product's exponent.</param>
    /// <param name="addendNegative">Whether the addend is negative.</param>
    /// <param name="addend">The addend's coefficient.</param>
    /// <param name="addendExponent">The addend's exponent.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded sum, rounded once.</returns>
    public static ulong FusedAdd(bool productNegative, ulong high, ulong low, int productExponent,
        bool addendNegative, ulong addend, int addendExponent, Decimal64Rounding rounding,
        ref Decimal64Status status)
    {
        if (addend == 0)
        {
            return Reduce(productNegative, high, low, productExponent, rounding, ref status);
        }

        var shift = addendExponent - productExponent;
        var addendDigits = Decimal64Tables.CountDigits(addend);

        if (shift < 0)
        {
            return AddBelow(productNegative, high, low, productExponent, addendNegative, addend, -shift,
                rounding, ref status);
        }

        if (addendDigits + shift <= 33)
        {
            return AddExact(productNegative, high, low, productExponent, addendNegative, addend, shift,
                rounding, ref status);
        }

        return AddAbove(productNegative, high, low, productExponent, addendNegative, addend,
            addendDigits, addendExponent, rounding, ref status);
    }

    /// <summary>
    /// The addend is below the product's last digit, so it cannot cancel the product. The
    /// addend is folded to the product's exponent, and its discarded digits become the
    /// residue.
    /// </summary>
    private static ulong AddBelow(bool productNegative, ulong high, ulong low, int exponent,
        bool addendNegative, ulong addend, int drop, Decimal64Rounding rounding, ref Decimal64Status status)
    {
        // The exact sum extends down to the addend's last digit, below a product that is
        // already wider than the format. So digits are discarded, whatever their values.
        status |= Decimal64Status.Rounded;

        ulong folded;
        Decimal64Residue residue;
        if (drop > Decimal64Encoding.Precision)
        {
            folded = 0;
            residue = Decimal64Residue.BelowHalf;
        }
        else
        {
            folded = Decimal64Tables.DivRemPowerOfTen(addend, drop, out var discarded);
            residue = Decimal64Rounder.Of(discarded, Decimal64Tables.HalfPowerOfTen(drop));
        }

        if (productNegative == addendNegative)
        {
            var top = 0UL;
            low += folded;
            if (low >= Limb)
            {
                low -= Limb;
                high++;
            }

            if (high >= Limb)
            {
                high -= Limb;
                top = 1;
            }

            return ReduceLimbs(productNegative, top, high, low, exponent, residue, rounding, ref status);
        }

        // Subtracting an inexact operand: subtract the whole units, then one more unit for
        // the fraction. The residue is flipped to describe what remains of that unit.
        if (residue != Decimal64Residue.Exact)
        {
            folded++;
            residue = Decimal64Rounder.Flip(residue);
        }

        if (low < folded)
        {
            low += Limb - folded;
            high--;
        }
        else
        {
            low -= folded;
        }

        return ReduceLimbs(productNegative, 0, high, low, exponent, residue, rounding, ref status);
    }

    /// <summary>
    /// The addend, scaled to the product's exponent, fits in three limbs. The sum or
    /// difference is computed exactly and rounded once.
    /// </summary>
    private static ulong AddExact(bool productNegative, ulong high, ulong low, int exponent,
        bool addendNegative, ulong addend, int shift, Decimal64Rounding rounding, ref Decimal64Status status)
    {
        ScaleToLimbs(addend, shift, out var addendTop, out var addendHigh, out var addendLow);

        if (productNegative == addendNegative)
        {
            Add(0, high, low, addendTop, addendHigh, addendLow, out var top, out var sumHigh, out var sumLow);
            return ReduceLimbs(productNegative, top, sumHigh, sumLow, exponent, Decimal64Residue.Exact,
                rounding, ref status);
        }

        var comparison = Compare(0, high, low, addendTop, addendHigh, addendLow);
        if (comparison == 0)
        {
            // Opposite signs that cancel exactly give positive zero, except when rounding
            // toward negative infinity.
            return Decimal64Finalizer.Zero(rounding == Decimal64Rounding.Floor, exponent, ref status);
        }

        if (comparison > 0)
        {
            Subtract(0, high, low, addendTop, addendHigh, addendLow, out var top, out var differenceHigh,
                out var differenceLow);

            return ReduceLimbs(productNegative, top, differenceHigh, differenceLow, exponent,
                Decimal64Residue.Exact, rounding, ref status);
        }

        Subtract(addendTop, addendHigh, addendLow, 0, high, low, out var largerTop, out var largerHigh,
            out var largerLow);

        return ReduceLimbs(addendNegative, largerTop, largerHigh, largerLow, exponent,
            Decimal64Residue.Exact, rounding, ref status);
    }

    /// <summary>
    /// The addend's leading digit is at least two places above the product's, so nothing
    /// can cancel. The addend is widened to 19 digits and the product is folded below it.
    /// </summary>
    private static ulong AddAbove(bool productNegative, ulong high, ulong low, int productExponent,
        bool addendNegative, ulong addend, int addendDigits, int addendExponent,
        Decimal64Rounding rounding, ref Decimal64Status status)
    {
        var widen = Decimal64Tables.MaxPower - addendDigits;
        var wide = addend * Decimal64Tables.PowerOfTen(widen);
        var exponent = addendExponent - widen;
        var folded = Fold(high, low, exponent - productExponent, out var residue);

        if (productNegative == addendNegative)
        {
            return Decimal64Finalizer.Finalize(addendNegative, wide + folded, exponent, residue, rounding, ref status);
        }

        if (residue == Decimal64Residue.Exact)
        {
            return Decimal64Finalizer.Finalize(addendNegative, wide - folded, exponent, Decimal64Residue.Exact,
                rounding, ref status);
        }

        return Decimal64Finalizer.Finalize(addendNegative, wide - folded - 1, exponent, Decimal64Rounder.Flip(residue),
            rounding, ref status);
    }

    /// <summary>
    /// The product divided by 10 to the power <paramref name="shift"/>, which is at least 15.
    /// The discarded part becomes the residue.
    /// </summary>
    private static ulong Fold(ulong high, ulong low, int shift, out Decimal64Residue residue)
    {
        if (shift > 32)
        {
            residue = (high | low) != 0 ? Decimal64Residue.BelowHalf : Decimal64Residue.Exact;
            return 0;
        }

        if (shift == Decimal64Encoding.Precision - 1)
        {
            var kept = Decimal64Tables.DivRemPowerOfTen(low, shift, out var discarded);
            residue = Decimal64Rounder.Of(discarded, Decimal64Tables.HalfPowerOfTen(shift));
            return (high * 10) + kept;
        }

        var drop = shift - Decimal64Encoding.Precision;
        if (drop == 0)
        {
            residue = Decimal64Rounder.Of(low, Limb / 2);
            return high;
        }

        // The discarded part is the dropped digits of the high limb followed by the whole low
        // limb. The halfway point falls entirely within the high limb's dropped digits.
        var quotient = Decimal64Tables.DivRemPowerOfTen(high, drop, out var droppedHigh);
        var halfHigh = Decimal64Tables.HalfPowerOfTen(drop);

        if (droppedHigh > halfHigh)
        {
            residue = Decimal64Residue.AboveHalf;
        }
        else if (droppedHigh < halfHigh)
        {
            residue = (droppedHigh | low) != 0 ? Decimal64Residue.BelowHalf : Decimal64Residue.Exact;
        }
        else
        {
            residue = low != 0 ? Decimal64Residue.AboveHalf : Decimal64Residue.Half;
        }

        return quotient;
    }

    /// <summary>
    /// A coefficient times 10 to the power <paramref name="shift"/>, which is at most 32, as
    /// three limbs.
    /// </summary>
    private static void ScaleToLimbs(ulong value, int shift, out ulong top, out ulong high, out ulong low)
    {
        if (shift < Decimal64Encoding.Precision)
        {
            top = 0;
            high = Decimal64Tables.DivRemPowerOfTen(value, Decimal64Encoding.Precision - shift, out var rest);
            low = rest * Decimal64Tables.PowerOfTen(shift);
            return;
        }

        if (shift < 2 * Decimal64Encoding.Precision)
        {
            low = 0;
            top = Decimal64Tables.DivRemPowerOfTen(value, (2 * Decimal64Encoding.Precision) - shift, out var rest);
            high = rest * Decimal64Tables.PowerOfTen(shift - Decimal64Encoding.Precision);
            return;
        }

        top = value;
        high = 0;
        low = 0;
    }

    private static void Add(ulong leftTop, ulong leftHigh, ulong leftLow, ulong rightTop, ulong rightHigh,
        ulong rightLow, out ulong top, out ulong high, out ulong low)
    {
        low = leftLow + rightLow;
        var carry = 0UL;
        if (low >= Limb)
        {
            low -= Limb;
            carry = 1;
        }

        high = leftHigh + rightHigh + carry;
        carry = 0;
        if (high >= Limb)
        {
            high -= Limb;
            carry = 1;
        }

        top = leftTop + rightTop + carry;
    }

    /// <summary>Subtracts the smaller three-limb value from the larger one.</summary>
    private static void Subtract(ulong leftTop, ulong leftHigh, ulong leftLow, ulong rightTop, ulong rightHigh,
        ulong rightLow, out ulong top, out ulong high, out ulong low)
    {
        var borrow = 0UL;
        if (leftLow < rightLow)
        {
            low = leftLow + Limb - rightLow;
            borrow = 1;
        }
        else
        {
            low = leftLow - rightLow;
        }

        var rightHighTotal = rightHigh + borrow;
        borrow = 0;
        if (leftHigh < rightHighTotal)
        {
            high = leftHigh + Limb - rightHighTotal;
            borrow = 1;
        }
        else
        {
            high = leftHigh - rightHighTotal;
        }

        top = leftTop - rightTop - borrow;
    }

    private static int Compare(ulong leftTop, ulong leftHigh, ulong leftLow, ulong rightTop, ulong rightHigh,
        ulong rightLow)
    {
        if (leftTop != rightTop)
        {
            return leftTop < rightTop ? -1 : 1;
        }

        if (leftHigh != rightHigh)
        {
            return leftHigh < rightHigh ? -1 : 1;
        }

        if (leftLow != rightLow)
        {
            return leftLow < rightLow ? -1 : 1;
        }

        return 0;
    }

    /// <summary>
    /// Reduces a value of up to three limbs to at most 19 digits and a residue, then
    /// finalizes it. The top limb never has more than two digits, because it comes from a
    /// carry or from an addend scaled to at most 33 digits.
    /// </summary>
    private static ulong ReduceLimbs(bool negative, ulong top, ulong high, ulong low, int exponent,
        Decimal64Residue residue, Decimal64Rounding rounding, ref Decimal64Status status)
    {
        if (top == 0)
        {
            if (high == 0)
            {
                return Decimal64Finalizer.Finalize(negative, low, exponent, residue, rounding, ref status);
            }

            var highDigits = Decimal64Tables.CountDigits(high);
            if (highDigits <= 3)
            {
                var whole = (high * Limb) + low;
                return Decimal64Finalizer.Finalize(negative, whole, exponent, residue, rounding, ref status);
            }

            status |= Decimal64Status.Rounded;
            var drop = highDigits - 3;
            var kept = Decimal64Tables.DivRemPowerOfTen(low, drop, out var discarded);
            var coefficient = (high * Decimal64Tables.PowerOfTen(Decimal64Encoding.Precision - drop)) + kept;
            residue = Decimal64Rounder.Combine(discarded, Decimal64Tables.HalfPowerOfTen(drop), residue);
            return Decimal64Finalizer.Finalize(negative, coefficient, exponent + drop, residue, rounding, ref status);
        }

        status |= Decimal64Status.Rounded;
        var topDigits = Decimal64Tables.CountDigits(top);
        var lowDrop = (Decimal64Encoding.Precision - 3) + topDigits;
        var lowKept = Decimal64Tables.DivRemPowerOfTen(low, lowDrop, out var lowDiscarded);

        var wide = (top * Decimal64Tables.PowerOfTen((2 * Decimal64Encoding.Precision) - lowDrop))
            + (high * Decimal64Tables.PowerOfTen(Decimal64Encoding.Precision - lowDrop))
            + lowKept;

        residue = Decimal64Rounder.Combine(lowDiscarded, Decimal64Tables.HalfPowerOfTen(lowDrop), residue);
        return Decimal64Finalizer.Finalize(negative, wide, exponent + lowDrop, residue, rounding, ref status);
    }
}
