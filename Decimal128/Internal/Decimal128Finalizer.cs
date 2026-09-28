// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace Decimals.Internal;

/// <summary>
/// Fits a result into the format: rounds the coefficient to thirty-four digits, applies the
/// pending residue once, brings the exponent into range, and raises the conditions each
/// step calls for. This is decNumber's <c>decSetCoeff</c>, <c>decApplyRound</c>,
/// <c>decFinalize</c>, and <c>decSetSubnormal</c> on two machine words.
/// </summary>
/// <remarks>
/// <para>
/// The coefficient may arrive at any width two words hold, and the residue describes
/// whatever the operation discarded below it. Digits above the precision are folded into
/// the residue first, so a value that turns out subnormal is rounded exactly once, at the
/// place the subnormal range dictates, rather than to precision and then again.
/// </para>
/// <para>
/// Subnormal is decided before the residue is applied, as decNumber does it: a value below
/// Nmin that rounds up to Nmin is still reported as a subnormal that underflowed.
/// </para>
/// <para>
/// Whether a result is inexact, and whether it rounds up, are decided by the data, so the
/// conditions they raise and the increment they make are computed from the residue rather
/// than branched on; the branches that remain are the ones that are almost never taken.
/// </para>
/// </remarks>
internal static class Decimal128Finalizer
{
    /// <remarks>
    /// Called rather than inlined: every operation ends here, and inlining it into each
    /// would have the operation's own inlining budget spent on it, which is what left the
    /// two-word operators as calls.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Decimal128Integer Finalize(bool negative, Decimal128Integer coefficient, int exponent,
        Decimal128Residue residue, Decimal128Rounding rounding, ref Decimal128Status status)
    {
        var digits = Decimal128Tables.CountDigits(coefficient);
        if (digits > Decimal128Encoding.Precision)
        {
            var drop = digits - Decimal128Encoding.Precision;
            coefficient = Decimal128Rounder.DropDigits(coefficient, drop, residue, out residue);
            exponent += drop;
            digits = Decimal128Encoding.Precision;
            status |= Decimal128Status.Rounded;
        }

        var inexact = Unsafe.BitCast<bool, byte>(residue != Decimal128Residue.Exact);
        status |= (Decimal128Status)(-(int)inexact & (int)(Decimal128Status.Inexact | Decimal128Status.Rounded));

        if (coefficient.IsZero)
        {
            return Zero(negative, exponent, ref status);
        }

        var adjusted = exponent + digits - 1;
        if (adjusted < Decimal128Encoding.MinExponent)
        {
            return Subnormal(negative, coefficient, exponent, residue, rounding, ref status);
        }

        coefficient += Unsafe.BitCast<bool, byte>(
            Decimal128Rounder.ShouldIncrement(coefficient, residue, negative, rounding));

        if (coefficient == Decimal128Encoding.CoefficientLimit)
        {
            // Rounding up carried into a new digit: thirty-four nines became a one and
            // thirty-four zeros, which is a one and thirty-three zeros one decade up.
            coefficient = Decimal128Tables.WidePowerOfTen(Decimal128Encoding.Precision - 1);
            exponent++;
            adjusted++;
        }

        if (adjusted > Decimal128Encoding.MaxExponent)
        {
            return Overflowed(negative, rounding, ref status);
        }

        if (exponent > Decimal128Encoding.MaxQuantumExponent)
        {
            // Folded down: the value fits, but only if the coefficient carries the extra
            // magnitude as trailing zeros rather than the exponent. The shift is at most
            // thirty-three, since the digits and the shift together stay inside the
            // precision, so the power fits two words and the product two as well.
            var shift = exponent - Decimal128Encoding.MaxQuantumExponent;
            coefficient = coefficient.Multiply(Decimal128Tables.WidePowerOfTen(shift));
            exponent = Decimal128Encoding.MaxQuantumExponent;
            status |= Decimal128Status.Clamped;
        }

        return Decimal128Encoding.Pack(negative, exponent, coefficient);
    }

    /// <summary>
    /// A zero has no digits to place, so only its exponent has to be brought into range.
    /// It is never subnormal, whatever its exponent.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Integer Zero(bool negative, int exponent, ref Decimal128Status status)
    {
        if (exponent < Decimal128Encoding.MinQuantumExponent)
        {
            exponent = Decimal128Encoding.MinQuantumExponent;
            status |= Decimal128Status.Clamped;
        }
        else if (exponent > Decimal128Encoding.MaxQuantumExponent)
        {
            exponent = Decimal128Encoding.MaxQuantumExponent;
            status |= Decimal128Status.Clamped;
        }

        return Decimal128Encoding.Zero(negative, exponent);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Decimal128Integer Subnormal(bool negative, Decimal128Integer coefficient, int exponent,
        Decimal128Residue residue, Decimal128Rounding rounding, ref Decimal128Status status)
    {
        status |= Decimal128Status.Subnormal;

        var drop = Decimal128Encoding.MinQuantumExponent - exponent;
        if (drop > 0)
        {
            coefficient = Decimal128Rounder.DropDigits(coefficient, drop, residue, out residue);
            exponent = Decimal128Encoding.MinQuantumExponent;
            status |= Decimal128Status.Rounded;
            if (residue != Decimal128Residue.Exact)
            {
                status |= Decimal128Status.Inexact;
            }
        }

        // Thirty-four digits were rounded down to fewer, so the increment cannot carry
        // past the precision; at most it reaches Nmin, which is still reported as
        // underflow.
        coefficient += Unsafe.BitCast<bool, byte>(
            Decimal128Rounder.ShouldIncrement(coefficient, residue, negative, rounding));

        // IEEE 754's default rule: a subnormal result underflows exactly when it is inexact.
        if ((status & Decimal128Status.Inexact) != 0)
        {
            status |= Decimal128Status.Underflow;
        }

        if (coefficient.IsZero)
        {
            // Everything was rounded away. The exponent the value wanted was below the
            // smallest the format holds, so it has been clamped up to reach here.
            status |= Decimal128Status.Clamped;
        }

        return Decimal128Encoding.Pack(negative, exponent, coefficient);
    }

    /// <summary>
    /// What overflow produces depends on the rounding mode: the modes that round away from
    /// the value give an infinity, the ones that round toward it give the largest finite.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Decimal128Integer Overflowed(bool negative, Decimal128Rounding rounding, ref Decimal128Status status)
    {
        status |= Decimal128Status.Overflow | Decimal128Status.Inexact | Decimal128Status.Rounded;

        var givesLargestFinite = rounding switch
        {
            Decimal128Rounding.Down => true,
            Decimal128Rounding.ZeroFiveUp => true,
            Decimal128Rounding.Ceiling => negative,
            Decimal128Rounding.Floor => !negative,
            _ => false
        };

        if (!givesLargestFinite)
        {
            return Decimal128Encoding.Infinity(negative);
        }

        return Decimal128Encoding.Pack(negative, Decimal128Encoding.MaxQuantumExponent,
            Decimal128Encoding.MaxCoefficient);
    }
}
