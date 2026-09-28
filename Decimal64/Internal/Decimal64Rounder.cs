// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;


namespace Decimals.Internal;

/// <summary>
/// Discarding digits into a residue, and deciding what a residue does to the last digit
/// kept under each of the eight rounding modes.
/// </summary>
internal static class Decimal64Rounder
{
    /// <summary>
    /// The residue of a discarded part that stands alone, measured against half of the
    /// power it was discarded by.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal64Residue Of(ulong discarded, ulong half)
    {
        if (discarded == 0)
        {
            return Decimal64Residue.Exact;
        }

        if (discarded < half)
        {
            return Decimal64Residue.BelowHalf;
        }

        return discarded == half ? Decimal64Residue.Half : Decimal64Residue.AboveHalf;
    }

    /// <summary>
    /// The residue of a discarded part with an older residue already lying below it. The
    /// older one is too far down to reach halfway on its own, so it can only break a tie and
    /// make a zero part non-zero.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal64Residue Combine(ulong discarded, ulong half, Decimal64Residue below)
    {
        if (discarded > half)
        {
            return Decimal64Residue.AboveHalf;
        }

        if (discarded == half)
        {
            return below != Decimal64Residue.Exact ? Decimal64Residue.AboveHalf : Decimal64Residue.Half;
        }

        if (discarded != 0 || below != Decimal64Residue.Exact)
        {
            return Decimal64Residue.BelowHalf;
        }

        return Decimal64Residue.Exact;
    }

    /// <summary>
    /// The residue of one less the fraction a residue stands for, which is what subtracting
    /// an inexact operand leaves: a part below half comes out above it, and a half stays a
    /// half. Only the category matters, so no arithmetic is needed to flip it.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal64Residue Flip(Decimal64Residue residue)
    {
        if (residue == Decimal64Residue.BelowHalf)
        {
            return Decimal64Residue.AboveHalf;
        }

        return residue == Decimal64Residue.AboveHalf ? Decimal64Residue.BelowHalf : residue;
    }

    /// <summary>
    /// Drops the last <paramref name="count"/> digits of a coefficient, folding them and any
    /// residue already below them into a new residue. A count past the width of a machine
    /// word discards everything, and everything is then below half.
    /// </summary>
    public static ulong DropDigits(ulong coefficient, int count, Decimal64Residue below, out Decimal64Residue residue)
    {
        if (count > Decimal64Tables.MaxPower)
        {
            residue = coefficient != 0 || below != Decimal64Residue.Exact
                ? Decimal64Residue.BelowHalf
                : Decimal64Residue.Exact;

            return 0;
        }

        var kept = Decimal64Tables.DivRemPowerOfTen(coefficient, count, out var discarded);
        residue = Combine(discarded, Decimal64Tables.HalfPowerOfTen(count), below);
        return kept;
    }

    /// <summary>
    /// Whether the last digit kept moves up under the rounding mode. The caller has already
    /// established that the residue is not exact.
    /// </summary>
    public static bool ShouldIncrement(ulong coefficient, Decimal64Residue residue, bool negative, Decimal64Rounding rounding)
    {
        switch (rounding)
        {
            case Decimal64Rounding.HalfEven:
                if (residue == Decimal64Residue.AboveHalf)
                {
                    return true;
                }

                return residue == Decimal64Residue.Half && (coefficient & 1) != 0;
            case Decimal64Rounding.HalfUp:
                return residue >= Decimal64Residue.Half;
            case Decimal64Rounding.HalfDown:
                return residue == Decimal64Residue.AboveHalf;
            case Decimal64Rounding.Down:
                return false;
            case Decimal64Rounding.Up:
                return true;
            case Decimal64Rounding.Ceiling:
                return !negative;
            case Decimal64Rounding.Floor:
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
