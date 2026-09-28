// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// The digit-wise logical operations, which read a coefficient as a string of ones and
/// zeros rather than as a number.
/// </summary>
/// <remarks>
/// An operand has to be a logical number: finite, unsigned, with a zero exponent and no
/// digit other than 0 or 1. Anything else is an invalid operation. The digits map onto a
/// bit apiece, the operation is one machine instruction, and the bits map back.
/// </remarks>
internal static class Decimal32Logical
{
    private const uint FieldMask = (1u << Decimal32Encoding.Precision) - 1;

    public static uint And(uint left, uint right, ref Decimal32Status status)
    {
        if (!TryToBits(left, out var first) || !TryToBits(right, out var second))
        {
            return Decimal32Arithmetic.Invalid(ref status);
        }

        return FromBits(first & second);
    }

    public static uint Or(uint left, uint right, ref Decimal32Status status)
    {
        if (!TryToBits(left, out var first) || !TryToBits(right, out var second))
        {
            return Decimal32Arithmetic.Invalid(ref status);
        }

        return FromBits(first | second);
    }

    public static uint Xor(uint left, uint right, ref Decimal32Status status)
    {
        if (!TryToBits(left, out var first) || !TryToBits(right, out var second))
        {
            return Decimal32Arithmetic.Invalid(ref status);
        }

        return FromBits(first ^ second);
    }

    /// <summary>
    /// Inverts every digit of the format's full width, so a short coefficient's leading
    /// zeros become ones.
    /// </summary>
    public static uint Invert(uint value, ref Decimal32Status status)
    {
        if (!TryToBits(value, out var bits))
        {
            return Decimal32Arithmetic.Invalid(ref status);
        }

        return FromBits(~bits & FieldMask);
    }

    /// <summary>
    /// Reads a logical number as a bit per digit, least significant digit first, refusing
    /// anything that is not one.
    /// </summary>
    private static bool TryToBits(uint value, out uint bits)
    {
        bits = 0;

        if (Decimal32Encoding.IsSpecial(value) || Decimal32Encoding.IsNegative(value))
        {
            return false;
        }

        var coefficient = Decimal32Encoding.Unpack(value, out var exponent);
        if (exponent != 0)
        {
            return false;
        }

        for (var position = 0; position < Decimal32Encoding.Precision && coefficient != 0; position++)
        {
            var next = coefficient / 10;
            var digit = coefficient - (next * 10);
            if (digit > 1)
            {
                return false;
            }

            bits |= digit << position;
            coefficient = next;
        }

        return true;
    }

    private static uint FromBits(uint bits)
    {
        var coefficient = 0u;
        for (var position = Decimal32Encoding.Precision - 1; position >= 0; position--)
        {
            coefficient = (coefficient * 10) + ((bits >> position) & 1);
        }

        return Decimal32Encoding.Pack(false, 0, coefficient);
    }
}
