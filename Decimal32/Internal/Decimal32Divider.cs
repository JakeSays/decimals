// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace Decimals.Internal;

/// <summary>
/// Divides a scaled dividend of up to 14 digits by a divisor of up to 7 digits with one
/// floating-point division.
/// </summary>
/// <remarks>
/// Both numbers fit in a double exactly, and the quotient is below 10^8. At that size, a
/// double's 53 bits give the quotient to within 10^-8. That is closer than any non-integer
/// quotient with a 7-digit divisor can come to an integer, so truncating gives the right
/// integer quotient. The exact remainder is computed, and the two corrections are never
/// needed, so their branches are always predicted.
/// </remarks>
internal static class Decimal32Divider
{
    /// <summary>
    /// Divides <paramref name="dividend"/> (below 2^53) by <paramref name="divisor"/> (below
    /// 10^7). The quotient is below 10^8. Also returns the remainder.
    /// </summary>
    /// <param name="dividend">The dividend, below 2^53.</param>
    /// <param name="divisor">The divisor, from 1 to 10^7 - 1.</param>
    /// <param name="remainder">Receives the remainder.</param>
    /// <returns>The quotient.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Divide(ulong dividend, ulong divisor, out ulong remainder)
    {
        var quotient = (ulong)(long)((double)(long)dividend / (double)(long)divisor);
        var rest = dividend - (quotient * divisor);

        if ((long)rest < 0)
        {
            quotient--;
            rest += divisor;
        }
        else if (rest >= divisor)
        {
            quotient++;
            rest -= divisor;
        }

        remainder = rest;
        return quotient;
    }
}
