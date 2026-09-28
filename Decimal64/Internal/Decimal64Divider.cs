// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;


namespace Decimals.Internal;

/// <summary>
/// Long division of a decimal-scaled dividend by a sixteen-digit divisor, in chunks of up
/// to nine digits, with each chunk's quotient estimated in floating point and corrected
/// exactly in integers.
/// </summary>
/// <remarks>
/// <para>
/// The dividend is a coefficient followed by zeros -- up to thirty-two digits -- and does
/// not fit a machine word, but it never has to: the division is schoolbook in base 10^9.
/// Each step brings in the next nine digits, estimates that step's quotient, and keeps an
/// exact remainder. Two steps cover every quotient the format can ask for.
/// </para>
/// <para>
/// A hardware 64-bit divide costs upward of forty cycles on many cores, so the estimate is
/// a double multiply by a precomputed reciprocal. The partial quotient is below 10^9 and
/// the arithmetic carries fifty-two bits, so the estimate is within a fraction of one of
/// the truth and a single compare against the divisor settles it. The remainder update runs
/// modulo 2^64: the true remainder before correction lies in <c>(-d, 2d)</c> with d below
/// 2^54, so the wrapped value is unambiguous when read as signed.
/// </para>
/// </remarks>
internal static class Decimal64Divider
{
    private const ulong ChunkBase = 1000000000;

    private const double ChunkBaseDouble = 1000000000.0;

    /// <summary>
    /// Divides <c>leading * 10^chunkDigits + chunks</c> by <paramref name="divisor"/>, where
    /// <paramref name="leading"/> is already below the divisor and <paramref name="chunks"/>
    /// holds the remaining <paramref name="chunkDigits"/> digits, at most eighteen.
    /// </summary>
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
    /// One step: the remainder so far times the chunk's scale plus the chunk, divided by
    /// the divisor. The chunk is below its scale and the remainder below the divisor, so the
    /// quotient is below the scale.
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
