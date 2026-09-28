// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace Decimals.Internal;

/// <summary>
/// Division of a scaled dividend of at most fourteen digits by a divisor of at most seven,
/// which is one floating-point division: both fit a double exactly, the quotient is below
/// 10^8, and at that size the double's fifty-three bits place it within a hundredth of a
/// millionth, closer than any non-integer quotient of a seven-digit divisor comes to an
/// integer. The exact remainder is kept and the two corrections it could call for are
/// never taken, so their branches cost nothing.
/// </summary>
internal static class Decimal32Divider
{
    /// <summary>
    /// Divides <paramref name="dividend"/>, which is below 2^53, by <paramref name="divisor"/>,
    /// which is below 10^7, for a quotient below 10^8, handing back the remainder as well.
    /// </summary>
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
