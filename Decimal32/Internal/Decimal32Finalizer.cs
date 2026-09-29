// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// Fits a result into the format. It rounds the coefficient to 7 digits, applies the
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
internal static class Decimal32Finalizer
{
    /// <summary>Rounds a result to the format, applies the exponent limits, and encodes it.</summary>
    /// <param name="negative">Whether the result is negative.</param>
    /// <param name="coefficient">The result's coefficient, of up to 20 digits.</param>
    /// <param name="exponent">The result's exponent, which can be outside the format's range.</param>
    /// <param name="residue">The residue of the digits already discarded below <paramref name="coefficient"/>.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the rounding and the exponent limits raise.</param>
    /// <returns>The encoded result: a finite value, or an infinity on overflow.</returns>
    public static uint Finalize(bool negative, ulong coefficient, int exponent, Decimal32Residue residue,
        Decimal32Rounding rounding, ref Decimal32Status status)
    {
        var digits = Decimal32Tables.CountDigits(coefficient);
        if (digits > Decimal32Encoding.Precision)
        {
            var drop = digits - Decimal32Encoding.Precision;
            coefficient = Decimal32Rounder.DropDigits(coefficient, drop, residue, out residue);
            exponent += drop;
            digits = Decimal32Encoding.Precision;
            status |= Decimal32Status.Rounded;
        }

        if (residue != Decimal32Residue.Exact)
        {
            status |= Decimal32Status.Inexact | Decimal32Status.Rounded;
        }

        if (coefficient == 0)
        {
            return Zero(negative, exponent, ref status);
        }

        var adjusted = exponent + digits - 1;
        if (adjusted < Decimal32Encoding.MinExponent)
        {
            return Subnormal(negative, coefficient, exponent, residue, rounding, ref status);
        }

        if (residue != Decimal32Residue.Exact
            && Decimal32Rounder.ShouldIncrement(coefficient, residue, negative, rounding))
        {
            coefficient++;
            if (coefficient == Decimal32Encoding.CoefficientLimit)
            {
                // Rounding up added a digit: 9999999 became 10000000, which is stored as
                // 1000000 with the exponent one higher.
                coefficient = Decimal32Encoding.CoefficientLimit / 10;
                exponent++;
                adjusted++;
            }
        }

        if (adjusted > Decimal32Encoding.MaxExponent)
        {
            return Overflowed(negative, rounding, ref status);
        }

        if (exponent > Decimal32Encoding.MaxQuantumExponent)
        {
            // Fold down: the value fits only if the coefficient takes the extra magnitude as
            // trailing zeros and the exponent is lowered to the maximum.
            coefficient *= Decimal32Tables.PowerOfTen(exponent - Decimal32Encoding.MaxQuantumExponent);
            exponent = Decimal32Encoding.MaxQuantumExponent;
            status |= Decimal32Status.Clamped;
        }

        return Decimal32Encoding.Pack(negative, exponent, (uint)coefficient);
    }

    /// <summary>
    /// A zero has no digits to place, so only its exponent must be brought into range. A
    /// zero is never subnormal, whatever its exponent.
    /// </summary>
    /// <param name="negative">Whether the zero is negative.</param>
    /// <param name="exponent">The zero's exponent, which can be outside the format's range.</param>
    /// <param name="status">Receives Clamped if the exponent was changed.</param>
    /// <returns>The encoded zero.</returns>
    public static uint Zero(bool negative, int exponent, ref Decimal32Status status)
    {
        if (exponent < Decimal32Encoding.MinQuantumExponent)
        {
            exponent = Decimal32Encoding.MinQuantumExponent;
            status |= Decimal32Status.Clamped;
        }
        else if (exponent > Decimal32Encoding.MaxQuantumExponent)
        {
            exponent = Decimal32Encoding.MaxQuantumExponent;
            status |= Decimal32Status.Clamped;
        }

        return Decimal32Encoding.Zero(negative, exponent);
    }

    private static uint Subnormal(bool negative, ulong coefficient, int exponent, Decimal32Residue residue,
        Decimal32Rounding rounding, ref Decimal32Status status)
    {
        status |= Decimal32Status.Subnormal;

        var drop = Decimal32Encoding.MinQuantumExponent - exponent;
        if (drop > 0)
        {
            coefficient = Decimal32Rounder.DropDigits(coefficient, drop, residue, out residue);
            exponent = Decimal32Encoding.MinQuantumExponent;
            status |= Decimal32Status.Rounded;
            if (residue != Decimal32Residue.Exact)
            {
                status |= Decimal32Status.Inexact;
            }
        }

        if (residue != Decimal32Residue.Exact
            && Decimal32Rounder.ShouldIncrement(coefficient, residue, negative, rounding))
        {
            // The coefficient has fewer than 7 digits here, so the increment cannot carry
            // beyond the precision. At most it reaches Nmin, which is still reported as
            // underflow.
            coefficient++;
        }

        // IEEE 754 default rule: a subnormal result underflows if and only if it is inexact.
        if ((status & Decimal32Status.Inexact) != 0)
        {
            status |= Decimal32Status.Underflow;
        }

        if (coefficient == 0)
        {
            // Everything was rounded away. The exponent the value needed was below the
            // format's minimum, so it was clamped up to get here.
            status |= Decimal32Status.Clamped;
        }

        return Decimal32Encoding.Pack(negative, exponent, (uint)coefficient);
    }

    /// <summary>
    /// The overflow result depends on the rounding mode. Modes that round away from zero
    /// give infinity. Modes that round toward zero give the largest finite value.
    /// </summary>
    /// <param name="negative">Whether the result is negative.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives Overflow, Inexact, and Rounded.</param>
    /// <returns>The encoded infinity or largest finite value, with the result's sign.</returns>
    public static uint Overflowed(bool negative, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        status |= Decimal32Status.Overflow | Decimal32Status.Inexact | Decimal32Status.Rounded;

        var givesLargestFinite = rounding switch
        {
            Decimal32Rounding.Down => true,
            Decimal32Rounding.ZeroFiveUp => true,
            Decimal32Rounding.Ceiling => negative,
            Decimal32Rounding.Floor => !negative,
            _ => false
        };

        if (!givesLargestFinite)
        {
            return Decimal32Encoding.Infinity(negative);
        }

        return Decimal32Encoding.Pack(negative, Decimal32Encoding.MaxQuantumExponent, Decimal32Encoding.MaxCoefficient);
    }
}
