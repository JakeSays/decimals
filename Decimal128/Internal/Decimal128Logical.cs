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
/// bit apiece, the operation is one machine instruction, and the bits map back. The
/// coefficient is read and written in two halves of seventeen digits, each of which fits
/// a word.
/// </remarks>
internal static class Decimal128Logical
{
    private const ulong FieldMask = (1UL << Decimal128Encoding.Precision) - 1;

    private const int HalfDigits = 17;

    public static Decimal128Integer And(Decimal128Integer left, Decimal128Integer right, ref Decimal128Status status)
    {
        if (!TryToBits(left, out var first) || !TryToBits(right, out var second))
        {
            return Decimal128Arithmetic.Invalid(ref status);
        }

        return FromBits(first & second);
    }

    public static Decimal128Integer Or(Decimal128Integer left, Decimal128Integer right, ref Decimal128Status status)
    {
        if (!TryToBits(left, out var first) || !TryToBits(right, out var second))
        {
            return Decimal128Arithmetic.Invalid(ref status);
        }

        return FromBits(first | second);
    }

    public static Decimal128Integer Xor(Decimal128Integer left, Decimal128Integer right, ref Decimal128Status status)
    {
        if (!TryToBits(left, out var first) || !TryToBits(right, out var second))
        {
            return Decimal128Arithmetic.Invalid(ref status);
        }

        return FromBits(first ^ second);
    }

    /// <summary>
    /// Inverts every digit of the format's full width, so a short coefficient's leading
    /// zeros become ones.
    /// </summary>
    public static Decimal128Integer Invert(Decimal128Integer value, ref Decimal128Status status)
    {
        if (!TryToBits(value, out var bits))
        {
            return Decimal128Arithmetic.Invalid(ref status);
        }

        return FromBits(~bits & FieldMask);
    }

    /// <summary>
    /// Reads a logical number as a bit per digit, least significant digit first, refusing
    /// anything that is not one.
    /// </summary>
    private static bool TryToBits(Decimal128Integer value, out ulong bits)
    {
        bits = 0;

        if (Decimal128Encoding.IsSpecial(value) || Decimal128Encoding.IsNegative(value))
        {
            return false;
        }

        var coefficient = Decimal128Encoding.Unpack(value, out var exponent);
        if (exponent != 0)
        {
            return false;
        }

        var upper = Decimal128Tables.DivRemPowerOfTen(coefficient, HalfDigits, out var lower);
        return TryReadHalf(lower, 0, ref bits) && TryReadHalf(upper.Low, HalfDigits, ref bits);
    }

    private static bool TryReadHalf(ulong half, int offset, ref ulong bits)
    {
        for (var position = 0; position < HalfDigits && half != 0; position++)
        {
            var next = half / 10;
            var digit = half - (next * 10);
            if (digit > 1)
            {
                return false;
            }

            bits |= digit << (offset + position);
            half = next;
        }

        return true;
    }

    private static Decimal128Integer FromBits(ulong bits)
    {
        var lower = WriteHalf(bits, 0);
        var upper = WriteHalf(bits, HalfDigits);
        var coefficient = Decimal128Integer.FromUInt64(upper).MultiplyBy(Decimal128Tables.PowerOfTen(HalfDigits))
            + lower;

        return Decimal128Encoding.Pack(false, 0, coefficient);
    }

    private static ulong WriteHalf(ulong bits, int offset)
    {
        var half = 0UL;
        for (var position = HalfDigits - 1; position >= 0; position--)
        {
            half = (half * 10) + ((bits >> (offset + position)) & 1);
        }

        return half;
    }
}
