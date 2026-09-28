// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Decimals.Internal;

/// <summary>
/// Long division of a decimal-scaled dividend by a divisor of up to thirty-four digits,
/// by the two-by-one and three-by-two methods of Moller and Granlund: the divisor is
/// normalized to put its top bit at the top of a word, the reciprocal of that word is
/// found once, and each step turns the remainder so far and the next word of the
/// dividend into a word of quotient with two multiplies and at most two corrections.
/// </summary>
/// <remarks>
/// <para>
/// The dividend is a coefficient followed by zeros -- up to sixty-eight digits -- and is
/// formed exactly in four words. The quotient is below 10^35 by the caller's choice of
/// scaling, so it fits two words, and the division is two steps whatever the divisor's
/// width: a one-word divisor sees the dividend as three words and takes a word of quotient
/// from each of the lower two, and a two-word divisor sees four and does the same.
/// </para>
/// <para>
/// The reciprocal of the normalized top word is the one real division the method needs,
/// and it is done without a divide instruction, by the paper's algorithm 3: a seed of
/// eleven bits from a table is refined three times, each refinement about doubling its
/// bits, and a last adjustment against the exact product makes it the word the steps
/// need. The three-by-two reciprocal is that one brought down for the divisor's low word,
/// by the paper's algorithm 6.
/// </para>
/// </remarks>
internal static class Decimal128Divider
{
    /// <summary>
    /// Divides <paramref name="dividend"/> followed by <paramref name="zeros"/> zeros by
    /// <paramref name="divisor"/>, which must leave a quotient below 10^35, handing back
    /// the remainder as well.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Decimal128Integer Divide(Decimal128Integer dividend, int zeros, Decimal128Integer divisor,
        out Decimal128Integer remainder)
    {
        var scaled = Decimal128Tables.ScaleLong(dividend, zeros);
        if (divisor.High == 0)
        {
            return DivideByWord(scaled, divisor.Low, out remainder);
        }

        return DivideByTwoWords(scaled, divisor, out remainder);
    }

    /// <summary>
    /// A one-word divisor. The dividend is below the divisor times 2^128, so once both are
    /// shifted up by the divisor's leading zeros it fits three words, the top of which is
    /// below the normalized divisor.
    /// </summary>
    private static Decimal128Integer DivideByWord(Decimal128LongInteger dividend, ulong divisor,
        out Decimal128Integer remainder)
    {
        var shift = BitOperations.LeadingZeroCount(divisor);
        var normalized = divisor << shift;
        var inverse = ReciprocalWord(normalized);

        // A shift of sixty-four is undefined for a word, so the bits that cross a word
        // boundary are taken with two shifts of at most sixty-three between them.
        var top = (dividend.Word2 << shift) | ((dividend.Word1 >> 1) >> (63 - shift));
        var upper = (dividend.Word1 << shift) | ((dividend.Word0 >> 1) >> (63 - shift));
        var bottom = dividend.Word0 << shift;

        var high = Decimal128Tables.DivideWord(ref top, upper, normalized, inverse);
        var low = Decimal128Tables.DivideWord(ref top, bottom, normalized, inverse);
        remainder = Decimal128Integer.FromUInt64(top >> shift);
        return new Decimal128Integer(high, low);
    }

    /// <summary>
    /// A two-word divisor, shifted up by its high word's leading zeros with the dividend
    /// shifted along, which stays within four words because the quotient is below 2^117.
    /// The top two words of the shifted dividend are below the shifted divisor for the
    /// same reason.
    /// </summary>
    private static Decimal128Integer DivideByTwoWords(Decimal128LongInteger dividend, Decimal128Integer divisor,
        out Decimal128Integer remainder)
    {
        var shift = BitOperations.LeadingZeroCount(divisor.High);
        var divisorHigh = (divisor.High << shift) | ((divisor.Low >> 1) >> (63 - shift));
        var divisorLow = divisor.Low << shift;
        var inverse = ReciprocalTwoWords(divisorHigh, divisorLow);

        var word3 = (dividend.Word3 << shift) | ((dividend.Word2 >> 1) >> (63 - shift));
        var word2 = (dividend.Word2 << shift) | ((dividend.Word1 >> 1) >> (63 - shift));
        var word1 = (dividend.Word1 << shift) | ((dividend.Word0 >> 1) >> (63 - shift));
        var word0 = dividend.Word0 << shift;

        var high = Decimal128Tables.DivideThreeByTwo(ref word3, ref word2, word1, divisorHigh, divisorLow, inverse);
        var low = Decimal128Tables.DivideThreeByTwo(ref word3, ref word2, word0, divisorHigh, divisorLow, inverse);

        remainder = new Decimal128Integer(word3 >> shift, (word2 >> shift) | ((word3 << 1) << (63 - shift)));
        return new Decimal128Integer(high, low);
    }

    /// <summary>
    /// The reciprocal word of a normalized divisor d: <c>floor((2^128 - 1) / d) - 2^64</c>,
    /// which is what the two-by-one step multiplies by.
    /// </summary>
    /// <remarks>
    /// Each refinement is a Newton step at a scale that keeps every product in a word:
    /// the seed approximates 2^19 over the top nine bits, the first refinement 2^84 over
    /// the divisor, the second 2^97 over it, and the third 2^128 over it, whose low word
    /// is the reciprocal to within one. The last line finds which of the two it is from
    /// the product of the candidate and the divisor, which lands just under 2^128 when
    /// the candidate is right and a word further down when it is one short.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ulong ReciprocalWord(ulong divisor)
    {
        var lowBit = divisor & 1;
        var topNine = (int)(divisor >> 55);
        var topForty = (divisor >> 24) + 1;
        var half = (divisor >> 1) + lowBit;

        var seed = Decimal128Tables.ReciprocalSeed(topNine);
        var first = (seed << 11) - ((seed * seed * topForty) >> 40) - 1;
        var second = (first << 13) + ((first * ((1UL << 60) - (first * topForty))) >> 47);
        var residual = ((second >> 1) & (0UL - lowBit)) - (second * half);
        var third = (Math.BigMul(second, residual, out _) >> 1) + (second << 31);

        var productHigh = Math.BigMul(third, divisor, out var productLow);
        productLow += divisor;
        productHigh += Unsafe.BitCast<bool, byte>(productLow < divisor);
        return third - productHigh - divisor;
    }

    /// <summary>
    /// The reciprocal word of a normalized two-word divisor: the reciprocal of its high
    /// word, brought down for the low word by the adjustment of Moller and Granlund's
    /// algorithm 6. The adjustments are taken with masks, since whether each is needed is
    /// decided by the divisor's bits.
    /// </summary>
    internal static ulong ReciprocalTwoWords(ulong high, ulong low)
    {
        var inverse = ReciprocalWord(high);

        var partial = (high * inverse) + low;
        var carried = 0UL - Unsafe.BitCast<bool, byte>(partial < low);
        inverse += carried;
        var again = carried & (0UL - Unsafe.BitCast<bool, byte>(partial >= high));
        inverse += again;
        partial -= high & again;
        partial -= high & carried;

        var productHigh = Math.BigMul(inverse, low, out var productLow);
        partial += productHigh;
        var carriedAgain = 0UL - Unsafe.BitCast<bool, byte>(partial < productHigh);
        inverse += carriedAgain;
        var beyond = (partial > high) | ((partial == high) & (productLow >= low));
        inverse += carriedAgain & (0UL - Unsafe.BitCast<bool, byte>(beyond));
        return inverse;
    }
}
