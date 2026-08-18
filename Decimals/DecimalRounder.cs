// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// Discards digits from a coefficient under one of the eight rounding modes. Every
/// operation that can lose precision funnels through here, so the modes are written once.
/// </summary>
internal static class DecimalRounder
{
    /// <summary>
    /// Shortens <paramref name="coefficient"/> by <paramref name="discardedDigits"/> digits.
    /// The result can gain a digit -- rounding 999 up by one digit gives 100 -- and the
    /// caller has to check for that.
    /// </summary>
    /// <param name="coefficient">The coefficient to shorten.</param>
    /// <param name="discardedDigits">How many low digits to remove.</param>
    /// <param name="isNegative">The value's sign, which Ceiling and Floor need.</param>
    /// <param name="rounding">Which way to break the discarded part.</param>
    /// <param name="inexact">Set when any discarded digit was non-zero.</param>
    public static UInt128 Round(UInt128 coefficient, int discardedDigits, bool isNegative,
        DecimalRounding rounding, out bool inexact)
    {
        if (discardedDigits <= 0)
        {
            inexact = false;
            return coefficient;
        }

        UInt128 quotient;
        UInt128 remainder;
        UInt128 half;
        if (discardedDigits > PowersOfTen.MaxUInt128Power)
        {
            // Everything goes, and half of the discarded power is wider than any coefficient,
            // so the remainder is unambiguously below the halfway point.
            quotient = UInt128.Zero;
            remainder = coefficient;
            half = UInt128.MaxValue;
        }
        else
        {
            var power = PowersOfTen.UInt128(discardedDigits);
            quotient = coefficient / power;
            remainder = coefficient - (quotient * power);
            half = power >> 1;
        }

        inexact = remainder != UInt128.Zero;
        if (!inexact)
        {
            return quotient;
        }

        return ShouldIncrement(quotient, remainder, half, isNegative, rounding)
            ? quotient + UInt128.One
            : quotient;
    }

    private static bool ShouldIncrement(UInt128 quotient, UInt128 remainder, UInt128 half,
        bool isNegative, DecimalRounding rounding)
    {
        switch (rounding)
        {
            case DecimalRounding.Ceiling:
                return !isNegative;
            case DecimalRounding.Floor:
                return isNegative;
            case DecimalRounding.Down:
                return false;
            case DecimalRounding.Up:
                return true;
            case DecimalRounding.HalfUp:
                return remainder >= half;
            case DecimalRounding.HalfDown:
                return remainder > half;
            case DecimalRounding.HalfEven:
                if (remainder > half)
                {
                    return true;
                }

                return remainder == half && (quotient & UInt128.One) != UInt128.Zero;
            default:
                // ZeroFiveUp keeps the discarded part recoverable by a later rounding: it
                // only moves the last digit when that digit is a 0 or a 5.
                return quotient % 5 == UInt128.Zero;
        }
    }

    /// <summary>
    /// How many digits a coefficient is written with. Zero counts as one.
    /// </summary>
    /// <remarks>
    /// The bit length gives the count to within one, since a bit is worth log10(2) of a
    /// digit, so one comparison settles it. Walking the table of powers instead -- which is
    /// what this did first -- costs a comparison per digit, and a sixteen-digit coefficient
    /// pays that twice on every addition.
    /// </remarks>
    public static int CountDigits(UInt128 value)
    {
        if (value == UInt128.Zero)
        {
            return 1;
        }

        var bits = 128 - (int)UInt128.LeadingZeroCount(value);
        var digits = (int)((bits * 30103L) / 100000) + 1;
        if (digits > 1 && value < PowersOfTen.UInt128(digits - 1))
        {
            digits--;
        }

        return digits;
    }
}
