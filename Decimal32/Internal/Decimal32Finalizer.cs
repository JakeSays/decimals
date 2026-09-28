// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// Fits a result into the format: rounds the coefficient to seven digits, applies the
/// pending residue once, brings the exponent into range, and raises the conditions each
/// step calls for. This is decNumber's <c>decSetCoeff</c>, <c>decApplyRound</c>,
/// <c>decFinalize</c>, and <c>decSetSubnormal</c> on a machine word.
/// </summary>
/// <remarks>
/// <para>
/// The coefficient may arrive up to twenty digits wide, and the residue describes whatever
/// the operation discarded below it. Digits above the precision are folded into the residue
/// first, so a value that turns out subnormal is rounded exactly once, at the place the
/// subnormal range dictates, rather than to precision and then again.
/// </para>
/// <para>
/// Subnormal is decided before the residue is applied, as decNumber does it: a value below
/// Nmin that rounds up to Nmin is still reported as a subnormal that underflowed.
/// </para>
/// </remarks>
internal static class Decimal32Finalizer
{
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
                // Rounding up carried into a new digit: 9999999 became 10000000, which is
                // 1000000 one decade up.
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
            // Folded down: the value fits, but only if the coefficient carries the extra
            // magnitude as trailing zeros rather than the exponent.
            coefficient *= Decimal32Tables.PowerOfTen(exponent - Decimal32Encoding.MaxQuantumExponent);
            exponent = Decimal32Encoding.MaxQuantumExponent;
            status |= Decimal32Status.Clamped;
        }

        return Decimal32Encoding.Pack(negative, exponent, (uint)coefficient);
    }

    /// <summary>
    /// A zero has no digits to place, so only its exponent has to be brought into range.
    /// It is never subnormal, whatever its exponent.
    /// </summary>
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
            // Seven digits were rounded down to fewer, so the increment cannot carry past
            // the precision; at most it reaches Nmin, which is still reported as underflow.
            coefficient++;
        }

        // IEEE 754's default rule: a subnormal result underflows exactly when it is inexact.
        if ((status & Decimal32Status.Inexact) != 0)
        {
            status |= Decimal32Status.Underflow;
        }

        if (coefficient == 0)
        {
            // Everything was rounded away. The exponent the value wanted was below the
            // smallest the format holds, so it has been clamped up to reach here.
            status |= Decimal32Status.Clamped;
        }

        return Decimal32Encoding.Pack(negative, exponent, (uint)coefficient);
    }

    /// <summary>
    /// What overflow produces depends on the rounding mode: the modes that round away from
    /// the value give an infinity, the ones that round toward it give the largest finite.
    /// </summary>
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
