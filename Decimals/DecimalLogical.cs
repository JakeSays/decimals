// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// The digit-wise logical operations, which read a coefficient as a string of ones and
/// zeros rather than as a number.
/// </summary>
/// <remarks>
/// An operand has to be a logical number: finite, unsigned, with a zero exponent and no
/// digit other than 0 or 1. Anything else is an invalid operation. Since only two digit
/// values are legal, a coefficient maps one-for-one onto a bit per digit, and the three
/// binary operations are that map, one machine instruction, and the map back.
/// </remarks>
internal static class DecimalLogical
{
    public static UnpackedDecimal<UInt128> And<TFormat>(UnpackedDecimal<UInt128> left,
        UnpackedDecimal<UInt128> right, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (!TryReadDigits<TFormat>(left, out var first) || !TryReadDigits<TFormat>(right, out var second))
        {
            return Invalid(ref status);
        }

        return FromDigits(first & second);
    }

    public static UnpackedDecimal<UInt128> Or<TFormat>(UnpackedDecimal<UInt128> left,
        UnpackedDecimal<UInt128> right, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (!TryReadDigits<TFormat>(left, out var first) || !TryReadDigits<TFormat>(right, out var second))
        {
            return Invalid(ref status);
        }

        return FromDigits(first | second);
    }

    public static UnpackedDecimal<UInt128> Xor<TFormat>(UnpackedDecimal<UInt128> left,
        UnpackedDecimal<UInt128> right, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (!TryReadDigits<TFormat>(left, out var first) || !TryReadDigits<TFormat>(right, out var second))
        {
            return Invalid(ref status);
        }

        return FromDigits(first ^ second);
    }

    public static UnpackedDecimal<UInt128> Invert<TFormat>(UnpackedDecimal<UInt128> value,
        ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (!TryReadDigits<TFormat>(value, out var digits))
        {
            return Invalid(ref status);
        }

        // Inverting fills the format's full width, so 0 becomes a coefficient of all ones.
        var mask = TFormat.Precision == 64 ? ulong.MaxValue : (1UL << TFormat.Precision) - 1;
        return FromDigits(~digits & mask);
    }

    /// <summary>
    /// Maps a logical number onto one bit per digit, or reports that it is not one.
    /// </summary>
    private static bool TryReadDigits<TFormat>(UnpackedDecimal<UInt128> value, out ulong digits)
        where TFormat : IDecimalFormat
    {
        digits = 0;

        if (value.Kind != DecimalKind.Finite || value.IsNegative || value.Exponent != 0)
        {
            return false;
        }

        var coefficient = value.Coefficient;
        for (var position = 0; position < TFormat.Precision; position++)
        {
            if (coefficient == UInt128.Zero)
            {
                return true;
            }

            var digit = (uint)(coefficient % 10);
            if (digit > 1)
            {
                return false;
            }

            digits |= (ulong)digit << position;
            coefficient /= 10;
        }

        // More digits than the format holds cannot happen for a value that came out of one,
        // but a coefficient still carrying something here would be one of those.
        return coefficient == UInt128.Zero;
    }

    private static UnpackedDecimal<UInt128> FromDigits(ulong digits)
    {
        var coefficient = UInt128.Zero;
        var scale = UInt128.One;
        while (digits != 0)
        {
            if ((digits & 1) != 0)
            {
                coefficient += scale;
            }

            digits >>= 1;
            scale *= 10;
        }

        return new UnpackedDecimal<UInt128>(DecimalKind.Finite, false, 0, coefficient);
    }

    private static UnpackedDecimal<UInt128> Invalid(ref DecimalStatus status)
    {
        status |= DecimalStatus.InvalidOperation;
        return new UnpackedDecimal<UInt128>(DecimalKind.QuietNaN, false, 0, UInt128.Zero);
    }
}
