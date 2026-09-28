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
internal static class Decimal64Logical
{
    private const uint FieldMask = (1u << Decimal64Encoding.Precision) - 1;

    public static ulong And(ulong left, ulong right, ref Decimal64Status status)
    {
        if (!TryToBits(left, out var first) || !TryToBits(right, out var second))
        {
            return Decimal64Arithmetic.Invalid(ref status);
        }

        return FromBits(first & second);
    }

    public static ulong Or(ulong left, ulong right, ref Decimal64Status status)
    {
        if (!TryToBits(left, out var first) || !TryToBits(right, out var second))
        {
            return Decimal64Arithmetic.Invalid(ref status);
        }

        return FromBits(first | second);
    }

    public static ulong Xor(ulong left, ulong right, ref Decimal64Status status)
    {
        if (!TryToBits(left, out var first) || !TryToBits(right, out var second))
        {
            return Decimal64Arithmetic.Invalid(ref status);
        }

        return FromBits(first ^ second);
    }

    /// <summary>
    /// Inverts every digit of the format's full width, so a short coefficient's leading
    /// zeros become ones.
    /// </summary>
    public static ulong Invert(ulong value, ref Decimal64Status status)
    {
        if (!TryToBits(value, out var bits))
        {
            return Decimal64Arithmetic.Invalid(ref status);
        }

        return FromBits(~bits & FieldMask);
    }

    /// <summary>
    /// Reads a logical number as a bit per digit, least significant digit first, refusing
    /// anything that is not one.
    /// </summary>
    private static bool TryToBits(ulong value, out uint bits)
    {
        bits = 0;

        if (Decimal64Encoding.IsSpecial(value) || Decimal64Encoding.IsNegative(value))
        {
            return false;
        }

        var coefficient = Decimal64Encoding.Unpack(value, out var exponent);
        if (exponent != 0)
        {
            return false;
        }

        for (var position = 0; position < Decimal64Encoding.Precision && coefficient != 0; position++)
        {
            var next = coefficient / 10;
            var digit = coefficient - (next * 10);
            if (digit > 1)
            {
                return false;
            }

            bits |= (uint)digit << position;
            coefficient = next;
        }

        return true;
    }

    private static ulong FromBits(uint bits)
    {
        var coefficient = 0UL;
        for (var position = Decimal64Encoding.Precision - 1; position >= 0; position--)
        {
            coefficient = (coefficient * 10) + ((bits >> position) & 1);
        }

        return Decimal64Encoding.Pack(false, 0, coefficient);
    }
}
