// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace Decimals.Internal;

/// <summary>
/// Fits a result into the format. It rounds the coefficient to 34 digits, applies the
/// pending residue once, brings the exponent into range, and raises the conditions for each
/// step. This is decNumber's <c>decSetCoeff</c>, <c>decApplyRound</c>, <c>decFinalize</c>,
/// and <c>decSetSubnormal</c>, done in two 64-bit words.
/// </summary>
/// <remarks>
/// <para>
/// The coefficient can have as many digits as two words hold, and the residue summarizes
/// what the operation discarded below it. Digits beyond the precision are folded into the
/// residue first. A result that turns out to be subnormal is then rounded only once, at the
/// position the subnormal range requires, not once to precision and again for the
/// subnormal range.
/// </para>
/// <para>
/// As in decNumber, subnormal is decided before the residue is applied. A value below
/// Nmin that rounds up to Nmin is still reported as subnormal and as underflow.
/// </para>
/// <para>
/// Whether a result is inexact, and whether it rounds up, depend on the data. So the
/// conditions they raise and the increment are computed from the residue without
/// branching. The branches that remain are almost never taken.
/// </para>
/// </remarks>
internal static class Decimal128Finalizer
{
    /// <summary>Rounds a result to the format, applies the exponent limits, and encodes it.</summary>
    /// <remarks>
    /// Not inlined. Every operation ends here, and inlining it into each one would use up
    /// that operation's inlining budget. That once left the two-word operators as calls.
    /// </remarks>
    /// <param name="negative">Whether the result is negative.</param>
    /// <param name="coefficient">The result's coefficient, of up to 38 digits.</param>
    /// <param name="exponent">The result's exponent, which can be outside the format's range.</param>
    /// <param name="residue">The residue of the digits already discarded below <paramref name="coefficient"/>.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the rounding and the exponent limits raise.</param>
    /// <returns>The encoded result: a finite value, or an infinity on overflow.</returns>
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
            // Rounding up added a digit: 34 nines became 1 followed by 34 zeros, which is
            // stored as 1 followed by 33 zeros with the exponent one higher.
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
            // Fold down: the value fits only if the coefficient takes the extra magnitude as
            // trailing zeros and the exponent is lowered to the maximum. The shift is at most
            // 33, because the digits and the shift together stay within the precision. So
            // the power fits in two words, and so does the product.
            var shift = exponent - Decimal128Encoding.MaxQuantumExponent;
            coefficient = coefficient.Multiply(Decimal128Tables.WidePowerOfTen(shift));
            exponent = Decimal128Encoding.MaxQuantumExponent;
            status |= Decimal128Status.Clamped;
        }

        return Decimal128Encoding.Pack(negative, exponent, coefficient);
    }

    /// <summary>
    /// A zero has no digits to place, so only its exponent must be brought into range. A
    /// zero is never subnormal, whatever its exponent.
    /// </summary>
    /// <param name="negative">Whether the zero is negative.</param>
    /// <param name="exponent">The zero's exponent, which can be outside the format's range.</param>
    /// <param name="status">Receives Clamped if the exponent was changed.</param>
    /// <returns>The encoded zero.</returns>
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

        // The coefficient has fewer than 34 digits here, so the increment cannot carry
        // beyond the precision. At most it reaches Nmin, which is still reported as
        // underflow.
        coefficient += Unsafe.BitCast<bool, byte>(
            Decimal128Rounder.ShouldIncrement(coefficient, residue, negative, rounding));

        // IEEE 754 default rule: a subnormal result underflows if and only if it is inexact.
        if ((status & Decimal128Status.Inexact) != 0)
        {
            status |= Decimal128Status.Underflow;
        }

        if (coefficient.IsZero)
        {
            // Everything was rounded away. The exponent the value needed was below the
            // format's minimum, so it was clamped up to get here.
            status |= Decimal128Status.Clamped;
        }

        return Decimal128Encoding.Pack(negative, exponent, coefficient);
    }

    /// <summary>
    /// The overflow result depends on the rounding mode. Modes that round away from zero
    /// give infinity. Modes that round toward zero give the largest finite value.
    /// </summary>
    /// <param name="negative">Whether the result is negative.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives Overflow, Inexact, and Rounded.</param>
    /// <returns>The encoded infinity or largest finite value, with the result's sign.</returns>
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
