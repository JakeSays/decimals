// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// Fits a result into the format: rounds the coefficient to sixteen digits, applies the
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
internal static class Decimal64Finalizer
{
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
                // Rounding up carried into a new digit: 9999999999999999 became
                // 10000000000000000, which is 1000000000000000 one decade up.
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
            // Folded down: the value fits, but only if the coefficient carries the extra
            // magnitude as trailing zeros rather than the exponent.
            coefficient *= Decimal64Tables.PowerOfTen(exponent - Decimal64Encoding.MaxQuantumExponent);
            exponent = Decimal64Encoding.MaxQuantumExponent;
            status |= Decimal64Status.Clamped;
        }

        return Decimal64Encoding.Pack(negative, exponent, coefficient);
    }

    /// <summary>
    /// A zero has no digits to place, so only its exponent has to be brought into range.
    /// It is never subnormal, whatever its exponent.
    /// </summary>
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
            // Sixteen digits were rounded down to fewer, so the increment cannot carry past
            // the precision; at most it reaches Nmin, which is still reported as underflow.
            coefficient++;
        }

        // IEEE 754's default rule: a subnormal result underflows exactly when it is inexact.
        if ((status & Decimal64Status.Inexact) != 0)
        {
            status |= Decimal64Status.Underflow;
        }

        if (coefficient == 0)
        {
            // Everything was rounded away. The exponent the value wanted was below the
            // smallest the format holds, so it has been clamped up to reach here.
            status |= Decimal64Status.Clamped;
        }

        return Decimal64Encoding.Pack(negative, exponent, coefficient);
    }

    /// <summary>
    /// What overflow produces depends on the rounding mode: the modes that round away from
    /// the value give an infinity, the ones that round toward it give the largest finite.
    /// </summary>
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
