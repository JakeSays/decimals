// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace Decimals.Internal;

/// <summary>
/// Discarding digits into a residue, and deciding what a residue does to the last digit
/// kept under each of the eight rounding modes.
/// </summary>
internal static class Decimal32Rounder
{
    /// <summary>
    /// The residue of a discarded part that stands alone, measured against half of the
    /// power it was discarded by.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal32Residue Of(ulong discarded, ulong half)
    {
        if (discarded == 0)
        {
            return Decimal32Residue.Exact;
        }

        if (discarded < half)
        {
            return Decimal32Residue.BelowHalf;
        }

        return discarded == half ? Decimal32Residue.Half : Decimal32Residue.AboveHalf;
    }

    /// <summary>
    /// The residue of a discarded part with an older residue already lying below it. The
    /// older one is too far down to reach halfway on its own, so it can only break a tie and
    /// make a zero part non-zero.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal32Residue Combine(ulong discarded, ulong half, Decimal32Residue below)
    {
        if (discarded > half)
        {
            return Decimal32Residue.AboveHalf;
        }

        if (discarded == half)
        {
            return below != Decimal32Residue.Exact ? Decimal32Residue.AboveHalf : Decimal32Residue.Half;
        }

        if (discarded != 0 || below != Decimal32Residue.Exact)
        {
            return Decimal32Residue.BelowHalf;
        }

        return Decimal32Residue.Exact;
    }

    /// <summary>
    /// The residue of one less the fraction a residue stands for, which is what subtracting
    /// an inexact operand leaves: a part below half comes out above it, and a half stays a
    /// half. Only the category matters, so no arithmetic is needed to flip it.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal32Residue Flip(Decimal32Residue residue)
    {
        if (residue == Decimal32Residue.BelowHalf)
        {
            return Decimal32Residue.AboveHalf;
        }

        return residue == Decimal32Residue.AboveHalf ? Decimal32Residue.BelowHalf : residue;
    }

    /// <summary>
    /// Drops the last <paramref name="count"/> digits of a coefficient, folding them and any
    /// residue already below them into a new residue. A count past the width of a machine
    /// word discards everything, and everything is then below half.
    /// </summary>
    public static ulong DropDigits(ulong coefficient, int count, Decimal32Residue below, out Decimal32Residue residue)
    {
        if (count > Decimal32Tables.MaxPower)
        {
            residue = coefficient != 0 || below != Decimal32Residue.Exact
                ? Decimal32Residue.BelowHalf
                : Decimal32Residue.Exact;

            return 0;
        }

        var kept = Decimal32Tables.DivRemPowerOfTen(coefficient, count, out var discarded);
        residue = Combine(discarded, Decimal32Tables.HalfPowerOfTen(count), below);
        return kept;
    }

    /// <summary>
    /// Whether the last digit kept moves up under the rounding mode. The caller has already
    /// established that the residue is not exact.
    /// </summary>
    public static bool ShouldIncrement(ulong coefficient, Decimal32Residue residue, bool negative, Decimal32Rounding rounding)
    {
        switch (rounding)
        {
            case Decimal32Rounding.HalfEven:
                if (residue == Decimal32Residue.AboveHalf)
                {
                    return true;
                }

                return residue == Decimal32Residue.Half && (coefficient & 1) != 0;
            case Decimal32Rounding.HalfUp:
                return residue >= Decimal32Residue.Half;
            case Decimal32Rounding.HalfDown:
                return residue == Decimal32Residue.AboveHalf;
            case Decimal32Rounding.Down:
                return false;
            case Decimal32Rounding.Up:
                return true;
            case Decimal32Rounding.Ceiling:
                return !negative;
            case Decimal32Rounding.Floor:
                return negative;
            default:
                // ZeroFiveUp keeps the discarded part recoverable by a later rounding: it
                // only moves the last digit when that digit is a 0 or a 5.
                var tens = coefficient / 10;
                var last = coefficient - (tens * 10);
                return last == 0 || last == 5;
        }
    }
}
