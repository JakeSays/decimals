// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// The digit-wise logical operations. They treat a coefficient as a string of 0 and 1
/// digits, not as a number.
/// </summary>
/// <remarks>
/// Each operand must be a logical number: finite, positive, exponent zero, and only the
/// digits 0 and 1. Any other operand is an invalid operation. Each digit maps to one bit,
/// the operation is one machine instruction, and the bits map back to digits. The
/// coefficient is read and written in two halves of 17 digits, each of which fits in a
/// 64-bit word.
/// </remarks>
internal static class Decimal128Logical
{
    private const ulong FieldMask = (1UL << Decimal128Encoding.Precision) - 1;

    private const int HalfDigits = 17;

    /// <summary>The digit-wise AND of two logical operands.</summary>
    /// <param name="left">The first operand: positive, exponent zero, and only the digits 0 and 1.</param>
    /// <param name="right">The second operand, with the same requirements.</param>
    /// <param name="status">Receives InvalidOperation if an operand is not a logical operand.</param>
    /// <returns>The encoded result, or a quiet NaN if an operand is invalid.</returns>
    public static Decimal128Integer And(Decimal128Integer left, Decimal128Integer right, ref Decimal128Status status)
    {
        if (LogicalOperandBits(left) is not { } first || LogicalOperandBits(right) is not { } second)
        {
            return Decimal128Arithmetic.Invalid(ref status);
        }

        return FromBits(first & second);
    }

    /// <summary>The digit-wise OR of two logical operands.</summary>
    /// <param name="left">The first operand: positive, exponent zero, and only the digits 0 and 1.</param>
    /// <param name="right">The second operand, with the same requirements.</param>
    /// <param name="status">Receives InvalidOperation if an operand is not a logical operand.</param>
    /// <returns>The encoded result, or a quiet NaN if an operand is invalid.</returns>
    public static Decimal128Integer Or(Decimal128Integer left, Decimal128Integer right, ref Decimal128Status status)
    {
        if (LogicalOperandBits(left) is not { } first || LogicalOperandBits(right) is not { } second)
        {
            return Decimal128Arithmetic.Invalid(ref status);
        }

        return FromBits(first | second);
    }

    /// <summary>The digit-wise exclusive OR of two logical operands.</summary>
    /// <param name="left">The first operand: positive, exponent zero, and only the digits 0 and 1.</param>
    /// <param name="right">The second operand, with the same requirements.</param>
    /// <param name="status">Receives InvalidOperation if an operand is not a logical operand.</param>
    /// <returns>The encoded result, or a quiet NaN if an operand is invalid.</returns>
    public static Decimal128Integer Xor(Decimal128Integer left, Decimal128Integer right, ref Decimal128Status status)
    {
        if (LogicalOperandBits(left) is not { } first || LogicalOperandBits(right) is not { } second)
        {
            return Decimal128Arithmetic.Invalid(ref status);
        }

        return FromBits(first ^ second);
    }

    /// <summary>
    /// Inverts every digit across the format's full precision, so leading zeros of a short
    /// coefficient become ones.
    /// </summary>
    /// <param name="value">The operand: positive, exponent zero, and only the digits 0 and 1.</param>
    /// <param name="status">Receives InvalidOperation if the operand is not a logical operand.</param>
    /// <returns>The encoded result, or a quiet NaN if the operand is invalid.</returns>
    public static Decimal128Integer Invert(Decimal128Integer value, ref Decimal128Status status)
    {
        if (LogicalOperandBits(value) is not { } bits)
        {
            return Decimal128Arithmetic.Invalid(ref status);
        }

        return FromBits(~bits & FieldMask);
    }

    /// <summary>
    /// A logical number as one bit per digit, least significant digit first, or null if
    /// the value is not a logical number.
    /// </summary>
    private static ulong? LogicalOperandBits(Decimal128Integer value)
    {
        if (Decimal128Encoding.IsSpecial(value) || Decimal128Encoding.IsNegative(value))
        {
            return null;
        }

        var coefficient = Decimal128Encoding.Unpack(value, out var exponent);
        if (exponent != 0)
        {
            return null;
        }

        var upper = Decimal128Tables.DivRemPowerOfTen(coefficient, HalfDigits, out var lower);
        var bits = 0UL;
        if (!ReadHalf(lower, 0, ref bits) || !ReadHalf(upper.Low, HalfDigits, ref bits))
        {
            return null;
        }

        return bits;
    }

    /// <summary>
    /// Adds the bits of one half of the coefficient to <paramref name="bits"/>, starting at
    /// <paramref name="offset"/>. Returns false if the half has a digit other than 0 or 1.
    /// </summary>
    private static bool ReadHalf(ulong half, int offset, ref ulong bits)
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
