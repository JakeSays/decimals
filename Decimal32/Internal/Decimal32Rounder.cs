// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace Decimals.Internal;

/// <summary>
/// Discards digits into a residue, and decides for each of the eight rounding modes
/// whether a residue increments the last kept digit.
/// </summary>
internal static class Decimal32Rounder
{
    /// <summary>
    /// The residue of a discarded part with nothing below it. <paramref name="half"/> is
    /// half of the power of ten that was divided out.
    /// </summary>
    /// <param name="discarded">The discarded digits, as an integer.</param>
    /// <param name="half">Half of the power of ten the discarded digits were divided out by.</param>
    /// <returns>Where the discarded part lies relative to half a unit of the kept digits.</returns>
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
    /// The residue of a discarded part that has an earlier residue below it. The earlier
    /// residue is too small to reach half by itself. It can only turn an exact half into
    /// above half, or a zero part into below half.
    /// </summary>
    /// <param name="discarded">The discarded digits, as an integer.</param>
    /// <param name="half">Half of the power of ten the discarded digits were divided out by.</param>
    /// <param name="below">The residue of digits discarded earlier, below these.</param>
    /// <returns>Where the whole discarded part lies relative to half a unit of the kept digits.</returns>
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
    /// The residue of 1 minus the fraction a residue represents. Subtracting an inexact
    /// operand leaves this: below half becomes above half, and half stays half. Only the
    /// category matters, so no arithmetic is needed.
    /// </summary>
    /// <param name="residue">The residue of the fraction.</param>
    /// <returns>The residue of 1 minus the fraction.</returns>
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
    /// Drops the last <paramref name="count"/> digits of a coefficient and folds them, with
    /// any residue already below them, into a new residue. If the count is more digits than
    /// a 64-bit word holds, everything is discarded and the residue is below half.
    /// </summary>
    /// <param name="coefficient">The coefficient to shorten.</param>
    /// <param name="count">The number of digits to drop. It must be at least 1.</param>
    /// <param name="below">The residue of digits discarded earlier, below the dropped digits.</param>
    /// <param name="residue">Receives the residue of everything discarded.</param>
    /// <returns>The kept digits.</returns>
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
    /// True if the rounding mode increments the last kept digit. The caller has already
    /// checked that the residue is not exact.
    /// </summary>
    /// <param name="coefficient">The kept digits.</param>
    /// <param name="residue">The residue of the discarded digits. It must not be exact.</param>
    /// <param name="negative">Whether the value is negative.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <returns>True if the coefficient must be incremented.</returns>
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
                // ZeroFiveUp increments only when the last kept digit is 0 or 5. This keeps
                // the result correct if it is rounded again later.
                var tens = coefficient / 10;
                var last = coefficient - (tens * 10);
                return last == 0 || last == 5;
        }
    }
}
