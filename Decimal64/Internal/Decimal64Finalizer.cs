// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// Fits a result into the format. It rounds the coefficient to 16 digits, applies the
/// pending residue once, brings the exponent into range, and raises the conditions for each
/// step. This is decNumber's <c>decSetCoeff</c>, <c>decApplyRound</c>, <c>decFinalize</c>,
/// and <c>decSetSubnormal</c>, done in a 64-bit word.
/// </summary>
/// <remarks>
/// <para>
/// The coefficient can have up to 20 digits, and the residue summarizes what the operation
/// discarded below it. Digits beyond the precision are folded into the residue first. A
/// result that turns out to be subnormal is then rounded only once, at the position the
/// subnormal range requires, not once to precision and again for the subnormal range.
/// </para>
/// <para>
/// As in decNumber, subnormal is decided before the residue is applied. A value below
/// Nmin that rounds up to Nmin is still reported as subnormal and as underflow.
/// </para>
/// </remarks>
internal static class Decimal64Finalizer
{
    /// <summary>Rounds a result to the format, applies the exponent limits, and encodes it.</summary>
    /// <param name="negative">Whether the result is negative.</param>
    /// <param name="coefficient">The result's coefficient, of up to 20 digits.</param>
    /// <param name="exponent">The result's exponent, which can be outside the format's range.</param>
    /// <param name="residue">The residue of the digits already discarded below <paramref name="coefficient"/>.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the rounding and the exponent limits raise.</param>
    /// <returns>The encoded result: a finite value, or an infinity on overflow.</returns>
    public static ulong Finalize(bool negative, ulong coefficient, int exponent, Decimal64Residue residue,
        Decimal64Rounding rounding, ref Decimal64Status status)
    {
        var digits = Decimal64Tables.CountDigits(coefficient);
        if (digits > Decimal64Encoding.Precision)
        {
            var drop = digits - Decimal64Encoding.Precision;
            coefficient = Decimal64Rounder.DropDigits(coefficient, drop, residue, out residue);
            exponent += drop;
            digits = Decimal64Encoding.Precision;
            status |= Decimal64Status.Rounded;
        }

        if (residue != Decimal64Residue.Exact)
        {
            status |= Decimal64Status.Inexact | Decimal64Status.Rounded;
        }

        if (coefficient == 0)
        {
            return Zero(negative, exponent, ref status);
        }

        var adjusted = exponent + digits - 1;
        if (adjusted < Decimal64Encoding.MinExponent)
        {
            return Subnormal(negative, coefficient, exponent, residue, rounding, ref status);
        }

        if (residue != Decimal64Residue.Exact
            && Decimal64Rounder.ShouldIncrement(coefficient, residue, negative, rounding))
        {
            coefficient++;
            if (coefficient == Decimal64Encoding.CoefficientLimit)
            {
                // Rounding up added a digit: 9999999999999999 became 10000000000000000,
                // which is stored as 1000000000000000 with the exponent one higher.
                coefficient = Decimal64Encoding.CoefficientLimit / 10;
                exponent++;
                adjusted++;
            }
        }

        if (adjusted > Decimal64Encoding.MaxExponent)
        {
            return Overflowed(negative, rounding, ref status);
        }

        if (exponent > Decimal64Encoding.MaxQuantumExponent)
        {
            // Fold down: the value fits only if the coefficient takes the extra magnitude as
            // trailing zeros and the exponent is lowered to the maximum.
            coefficient *= Decimal64Tables.PowerOfTen(exponent - Decimal64Encoding.MaxQuantumExponent);
            exponent = Decimal64Encoding.MaxQuantumExponent;
            status |= Decimal64Status.Clamped;
        }

        return Decimal64Encoding.Pack(negative, exponent, coefficient);
    }

    /// <summary>
    /// A zero has no digits to place, so only its exponent must be brought into range. A
    /// zero is never subnormal, whatever its exponent.
    /// </summary>
    /// <param name="negative">Whether the zero is negative.</param>
    /// <param name="exponent">The zero's exponent, which can be outside the format's range.</param>
    /// <param name="status">Receives Clamped if the exponent was changed.</param>
    /// <returns>The encoded zero.</returns>
    public static ulong Zero(bool negative, int exponent, ref Decimal64Status status)
    {
        if (exponent < Decimal64Encoding.MinQuantumExponent)
        {
            exponent = Decimal64Encoding.MinQuantumExponent;
            status |= Decimal64Status.Clamped;
        }
        else if (exponent > Decimal64Encoding.MaxQuantumExponent)
        {
            exponent = Decimal64Encoding.MaxQuantumExponent;
            status |= Decimal64Status.Clamped;
        }

        return Decimal64Encoding.Zero(negative, exponent);
    }

    private static ulong Subnormal(bool negative, ulong coefficient, int exponent, Decimal64Residue residue,
        Decimal64Rounding rounding, ref Decimal64Status status)
    {
        status |= Decimal64Status.Subnormal;

        var drop = Decimal64Encoding.MinQuantumExponent - exponent;
        if (drop > 0)
        {
            coefficient = Decimal64Rounder.DropDigits(coefficient, drop, residue, out residue);
            exponent = Decimal64Encoding.MinQuantumExponent;
            status |= Decimal64Status.Rounded;
            if (residue != Decimal64Residue.Exact)
            {
                status |= Decimal64Status.Inexact;
            }
        }

        if (residue != Decimal64Residue.Exact
            && Decimal64Rounder.ShouldIncrement(coefficient, residue, negative, rounding))
        {
            // The coefficient has fewer than 16 digits here, so the increment cannot carry
            // beyond the precision. At most it reaches Nmin, which is still reported as
            // underflow.
            coefficient++;
        }

        // IEEE 754 default rule: a subnormal result underflows if and only if it is inexact.
        if ((status & Decimal64Status.Inexact) != 0)
        {
            status |= Decimal64Status.Underflow;
        }

        if (coefficient == 0)
        {
            // Everything was rounded away. The exponent the value needed was below the
            // format's minimum, so it was clamped up to get here.
            status |= Decimal64Status.Clamped;
        }

        return Decimal64Encoding.Pack(negative, exponent, coefficient);
    }

    /// <summary>
    /// The overflow result depends on the rounding mode. Modes that round away from zero
    /// give infinity. Modes that round toward zero give the largest finite value.
    /// </summary>
    /// <param name="negative">Whether the result is negative.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives Overflow, Inexact, and Rounded.</param>
    /// <returns>The encoded infinity or largest finite value, with the result's sign.</returns>
    public static ulong Overflowed(bool negative, Decimal64Rounding rounding, ref Decimal64Status status)
    {
        status |= Decimal64Status.Overflow | Decimal64Status.Inexact | Decimal64Status.Rounded;

        var givesLargestFinite = rounding switch
        {
            Decimal64Rounding.Down => true,
            Decimal64Rounding.ZeroFiveUp => true,
            Decimal64Rounding.Ceiling => negative,
            Decimal64Rounding.Floor => !negative,
            _ => false
        };

        if (!givesLargestFinite)
        {
            return Decimal64Encoding.Infinity(negative);
        }

        return Decimal64Encoding.Pack(negative, Decimal64Encoding.MaxQuantumExponent, Decimal64Encoding.MaxCoefficient);
    }
}
