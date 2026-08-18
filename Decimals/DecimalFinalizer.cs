// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// Fits an exact result into a format: rounds the coefficient to precision, then brings the
/// exponent into range, raising the conditions each step calls for.
/// </summary>
/// <remarks>
/// <para>
/// This follows decNumber's <c>decFinish</c> and <c>decSetSubnormal</c> rather than
/// decFloat's <c>decFinalize</c>. The two agree on the arithmetic, but the decFloat sources
/// never raise <see cref="DecimalStatus.Rounded"/>, <see cref="DecimalStatus.Clamped"/>, or
/// <see cref="DecimalStatus.Subnormal"/> at all, and the testcases expect all three.
/// </para>
/// <para>
/// Order matters. Precision rounding happens first, then the exponent range, because
/// <see cref="DecimalStatus.Subnormal"/> is decided on the value as it stands after
/// rounding to precision but before it is rescaled up to the smallest exponent.
/// </para>
/// </remarks>
internal static class DecimalFinalizer
{
    /// <summary>
    /// Finalizes a result computed at the width arithmetic needs. Everything past the guard
    /// digit is folded into one sticky digit first, which is enough to round correctly:
    /// the sticky can only break a tie, never move the value across one.
    /// </summary>
    public static UnpackedDecimal<UInt128> Finalize<TFormat>(bool isNegative, UInt256 coefficient,
        int exponent, DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        var digits = coefficient.CountDigits();
        if (digits > TFormat.Precision + 2)
        {
            var drop = digits - (TFormat.Precision + 1);
            coefficient = UInt256.DivideByPowerOfTen(coefficient, drop, out var discarded);
            exponent += drop - 1;

            coefficient = UInt256.MultiplyByUInt64(coefficient, 10);
            if (discarded)
            {
                coefficient += UInt256.One;
            }
        }

        return Finalize<TFormat>(isNegative, coefficient.ToUInt128(), exponent, rounding, ref status);
    }

    public static UnpackedDecimal<UInt128> Finalize<TFormat>(bool isNegative, UInt128 coefficient,
        int exponent, DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        var digits = DecimalRounder.CountDigits(coefficient);

        if (digits > TFormat.Precision)
        {
            var drop = digits - TFormat.Precision;
            coefficient = DecimalRounder.Round(coefficient, drop, isNegative, rounding, out var inexact);
            exponent += drop;
            status |= DecimalStatus.Rounded;
            if (inexact)
            {
                status |= DecimalStatus.Inexact;
            }

            // Rounding up can carry into a new digit -- 999 becomes 100 with the exponent
            // one higher -- which has to come back out.
            if (DecimalRounder.CountDigits(coefficient) > TFormat.Precision)
            {
                coefficient /= 10;
                exponent++;
            }

            digits = DecimalRounder.CountDigits(coefficient);
        }

        if (coefficient == UInt128.Zero)
        {
            return Zero<TFormat>(isNegative, exponent, ref status);
        }

        var adjusted = exponent + digits - 1;

        if (adjusted > TFormat.MaxExponent)
        {
            return Overflowed<TFormat>(isNegative, rounding, ref status);
        }

        if (adjusted < TFormat.MinExponent)
        {
            return Subnormal<TFormat>(isNegative, coefficient, exponent, rounding, ref status);
        }

        if (exponent > TFormat.MaxQuantumExponent)
        {
            // Folded down: the value fits, but only if the coefficient carries the extra
            // magnitude as trailing zeros rather than the exponent.
            coefficient *= PowersOfTen.UInt128(exponent - TFormat.MaxQuantumExponent);
            exponent = TFormat.MaxQuantumExponent;
            status |= DecimalStatus.Clamped;
        }

        return new UnpackedDecimal<UInt128>(DecimalKind.Finite, isNegative, exponent, coefficient);
    }

    /// <summary>
    /// A zero has no digits to place, so only its exponent has to be brought into range.
    /// It is never subnormal, whatever its exponent.
    /// </summary>
    private static UnpackedDecimal<UInt128> Zero<TFormat>(bool isNegative, int exponent,
        ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (exponent < TFormat.MinQuantumExponent)
        {
            exponent = TFormat.MinQuantumExponent;
            status |= DecimalStatus.Clamped;
        }
        else if (exponent > TFormat.MaxQuantumExponent)
        {
            exponent = TFormat.MaxQuantumExponent;
            status |= DecimalStatus.Clamped;
        }

        return new UnpackedDecimal<UInt128>(DecimalKind.Finite, isNegative, exponent, UInt128.Zero);
    }

    private static UnpackedDecimal<UInt128> Subnormal<TFormat>(bool isNegative, UInt128 coefficient,
        int exponent, DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        status |= DecimalStatus.Subnormal;

        if (exponent < TFormat.MinQuantumExponent)
        {
            var drop = TFormat.MinQuantumExponent - exponent;
            coefficient = DecimalRounder.Round(coefficient, drop, isNegative, rounding, out var inexact);
            exponent = TFormat.MinQuantumExponent;
            status |= DecimalStatus.Rounded;
            if (inexact)
            {
                status |= DecimalStatus.Inexact;
            }
        }

        // IEEE 754's default rule: a subnormal result underflows exactly when it is inexact.
        if ((status & DecimalStatus.Inexact) != 0)
        {
            status |= DecimalStatus.Underflow;
        }

        if (coefficient == UInt128.Zero)
        {
            // Everything was rounded away. The exponent the value wanted was below the
            // smallest the format holds, so it has been clamped up to reach here.
            status |= DecimalStatus.Clamped;
        }

        return new UnpackedDecimal<UInt128>(DecimalKind.Finite, isNegative, exponent, coefficient);
    }

    /// <summary>
    /// What overflow produces depends on the rounding mode: the modes that round away from
    /// the value give an infinity, the ones that round toward it give the largest finite.
    /// </summary>
    private static UnpackedDecimal<UInt128> Overflowed<TFormat>(bool isNegative,
        DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        status |= DecimalStatus.Overflow | DecimalStatus.Inexact | DecimalStatus.Rounded;

        var givesLargestFinite = rounding switch
        {
            DecimalRounding.Down => true,
            DecimalRounding.ZeroFiveUp => true,
            DecimalRounding.Ceiling => isNegative,
            DecimalRounding.Floor => !isNegative,
            _ => false
        };

        if (!givesLargestFinite)
        {
            return new UnpackedDecimal<UInt128>(DecimalKind.Infinity, isNegative, 0, UInt128.Zero);
        }

        return new UnpackedDecimal<UInt128>(
            DecimalKind.Finite,
            isNegative,
            TFormat.MaxQuantumExponent,
            PowersOfTen.UInt128(TFormat.Precision) - UInt128.One);
    }
}
