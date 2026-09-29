// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;


namespace Decimals.Internal;

/// <summary>
/// Long division of a scaled dividend by a divisor of up to 16 digits. It works in chunks
/// of up to 9 digits. Each chunk's quotient is estimated in floating point and corrected
/// exactly with integers.
/// </summary>
/// <remarks>
/// <para>
/// The dividend is a coefficient followed by zeros, up to 32 digits. It does not fit in a
/// 64-bit word, but it never needs to, because the division is schoolbook division in base
/// 10^9. Each step brings in the next 9 digits, estimates the step's quotient, and keeps an
/// exact remainder. Two steps cover every quotient the format needs.
/// </para>
/// <para>
/// A hardware 64-bit divide takes more than 40 cycles on many processors, so the estimate
/// is a double multiply by a precomputed reciprocal. The partial quotient is below 10^9 and
/// a double has 52 bits of precision, so the estimate is within one of the true quotient,
/// and one comparison with the divisor corrects it. The remainder is computed modulo 2^64.
/// Before correction the true remainder is in <c>(-d, 2d)</c> with d below 2^54, so the
/// wrapped value is unambiguous when read as signed.
/// </para>
/// </remarks>
internal static class Decimal64Divider
{
    private const ulong ChunkBase = 1000000000;

    private const double ChunkBaseDouble = 1000000000.0;

    /// <summary>
    /// Divides <c>leading * 10^chunkDigits + chunks</c> by <paramref name="divisor"/>.
    /// <paramref name="leading"/> must be below the divisor. <paramref name="chunks"/> holds
    /// the remaining <paramref name="chunkDigits"/> digits, at most 18.
    /// </summary>
    /// <param name="leading">The leading part of the dividend. It must be below <paramref name="divisor"/>.</param>
    /// <param name="chunks">The remaining digits of the dividend, as an integer.</param>
    /// <param name="chunkDigits">The number of digits in <paramref name="chunks"/>, at most 18.</param>
    /// <param name="divisor">The divisor. It must not be zero.</param>
    /// <param name="remainder">Receives the remainder.</param>
    /// <returns>The quotient, which has at most <paramref name="chunkDigits"/> digits.</returns>
    public static ulong Divide(ulong leading, ulong chunks, int chunkDigits, ulong divisor, out ulong remainder)
    {
        var inverse = 1.0 / (double)(long)divisor;
        remainder = leading;

        if (chunkDigits <= 9)
        {
            var scale = Decimal64Tables.PowerOfTen(chunkDigits);
            return Step(ref remainder, chunks, scale, (double)(long)scale, inverse, divisor);
        }

        var high = chunks / ChunkBase;
        var low = chunks - (high * ChunkBase);
        var highScale = Decimal64Tables.PowerOfTen(chunkDigits - 9);

        var first = Step(ref remainder, high, highScale, (double)(long)highScale, inverse, divisor);
        var second = Step(ref remainder, low, ChunkBase, ChunkBaseDouble, inverse, divisor);
        return (first * ChunkBase) + second;
    }

    /// <summary>
    /// One step: divides (remainder * scale + chunk) by the divisor. The chunk is below the
    /// scale and the remainder is below the divisor, so the quotient is below the scale.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Step(ref ulong remainder, ulong chunk, ulong scale, double scaleDouble,
        double inverse, ulong divisor)
    {
        var current = remainder;
        var wrapped = (current * scale) + chunk;
        var estimate = (((double)(long)current * scaleDouble) + (double)(long)chunk) * inverse;
        var quotient = (ulong)(long)estimate;
        var next = wrapped - (quotient * divisor);

        if ((long)next < 0)
        {
            quotient--;
            next += divisor;
        }
        else if (next >= divisor)
        {
            quotient++;
            next -= divisor;
        }

        remainder = next;
        return quotient;
    }
}
