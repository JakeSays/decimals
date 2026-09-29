// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Decimals.Internal;

/// <summary>
/// Long division of a scaled dividend by a divisor of up to 34 digits, using the 2-by-1 and
/// 3-by-2 methods of Möller and Granlund. The divisor is shifted so its top bit is at the
/// top of a word, and the reciprocal of that word is computed once. Each step then turns
/// the remainder and the next dividend word into one word of quotient, with two multiplies
/// and at most two corrections.
/// </summary>
/// <remarks>
/// <para>
/// The dividend is a coefficient followed by zeros, up to 68 digits, and is formed exactly
/// in four words. The caller chooses the scale so the quotient is below 10^35, which fits
/// in two words. So the division always takes two steps. A one-word divisor treats the
/// dividend as three words and gets one quotient word from each of the lower two. A
/// two-word divisor treats it as four words and does the same.
/// </para>
/// <para>
/// The reciprocal of the normalized top word is the one real division the method needs.
/// It is computed without a divide instruction, by algorithm 3 of the paper: an 11-bit seed
/// from a table is refined three times, each refinement roughly doubling the correct bits,
/// and a final adjustment against the exact product makes it exact. The 3-by-2 reciprocal
/// adjusts that one for the divisor's low word, by algorithm 6 of the paper.
/// </para>
/// </remarks>
internal static class Decimal128Divider
{
    /// <summary>
    /// Divides <paramref name="dividend"/> followed by <paramref name="zeros"/> zeros by
    /// <paramref name="divisor"/>, and also returns the remainder. The quotient must be
    /// below 10^35.
    /// </summary>
    /// <param name="dividend">The leading digits of the dividend.</param>
    /// <param name="zeros">The number of zeros that follow <paramref name="dividend"/>.</param>
    /// <param name="divisor">The divisor. It must not be zero.</param>
    /// <param name="remainder">Receives the remainder.</param>
    /// <returns>The quotient.</returns>
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
    /// Division by a one-word divisor. The dividend is below the divisor times 2^128. So
    /// after both are shifted left by the divisor's leading zero count, the dividend fits in
    /// three words, and its top word is below the normalized divisor.
    /// </summary>
    private static Decimal128Integer DivideByWord(Decimal128LongInteger dividend, ulong divisor,
        out Decimal128Integer remainder)
    {
        var shift = BitOperations.LeadingZeroCount(divisor);
        var normalized = divisor << shift;
        var inverse = ReciprocalWord(normalized);

        // A shift by 64 is undefined for a 64-bit word, so the bits that cross a word
        // boundary are moved with two shifts, neither larger than 63.
        var top = (dividend.Word2 << shift) | ((dividend.Word1 >> 1) >> (63 - shift));
        var upper = (dividend.Word1 << shift) | ((dividend.Word0 >> 1) >> (63 - shift));
        var bottom = dividend.Word0 << shift;

        var high = Decimal128Tables.DivideWord(ref top, upper, normalized, inverse);
        var low = Decimal128Tables.DivideWord(ref top, bottom, normalized, inverse);
        remainder = Decimal128Integer.FromUInt64(top >> shift);
        return new Decimal128Integer(high, low);
    }

    /// <summary>
    /// Division by a two-word divisor. The divisor is shifted left by its high word's
    /// leading zero count, and the dividend by the same amount. The dividend still fits in
    /// four words, because the quotient is below 2^117. For the same reason, the dividend's
    /// top two words are below the shifted divisor.
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
    /// The reciprocal word of a normalized divisor d: <c>floor((2^128 - 1) / d) - 2^64</c>.
    /// The 2-by-1 division step multiplies by this value.
    /// </summary>
    /// <remarks>
    /// Each refinement is a Newton step, scaled so every product fits in a word. The seed
    /// approximates 2^19 divided by the top 9 bits. The first refinement approximates 2^84
    /// divided by the divisor, the second 2^97, and the third 2^128. The low word of the
    /// third is the reciprocal or one less. The last line decides which, from the product
    /// of the candidate and the divisor: the product is just below 2^128 if the candidate is
    /// right, and one word lower if it is one short.
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
    /// The reciprocal word of a normalized two-word divisor. It starts from the reciprocal
    /// of the high word and adjusts it for the low word, using algorithm 6 of Möller and
    /// Granlund. The adjustments use masks instead of branches, because whether each one is
    /// needed depends on the divisor's bits.
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
