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
/// the operation is one machine instruction, and the bits map back to digits.
/// </remarks>
internal static class Decimal32Logical
{
    private const uint FieldMask = (1u << Decimal32Encoding.Precision) - 1;

    /// <summary>The digit-wise AND of two logical operands.</summary>
    /// <param name="left">The first operand: positive, exponent zero, and only the digits 0 and 1.</param>
    /// <param name="right">The second operand, with the same requirements.</param>
    /// <param name="status">Receives InvalidOperation if an operand is not a logical operand.</param>
    /// <returns>The encoded result, or a quiet NaN if an operand is invalid.</returns>
    public static uint And(uint left, uint right, ref Decimal32Status status)
    {
        if (LogicalOperandBits(left) is not { } first || LogicalOperandBits(right) is not { } second)
        {
            return Decimal32Arithmetic.Invalid(ref status);
        }

        return FromBits(first & second);
    }

    /// <summary>The digit-wise OR of two logical operands.</summary>
    /// <param name="left">The first operand: positive, exponent zero, and only the digits 0 and 1.</param>
    /// <param name="right">The second operand, with the same requirements.</param>
    /// <param name="status">Receives InvalidOperation if an operand is not a logical operand.</param>
    /// <returns>The encoded result, or a quiet NaN if an operand is invalid.</returns>
    public static uint Or(uint left, uint right, ref Decimal32Status status)
    {
        if (LogicalOperandBits(left) is not { } first || LogicalOperandBits(right) is not { } second)
        {
            return Decimal32Arithmetic.Invalid(ref status);
        }

        return FromBits(first | second);
    }

    /// <summary>The digit-wise exclusive OR of two logical operands.</summary>
    /// <param name="left">The first operand: positive, exponent zero, and only the digits 0 and 1.</param>
    /// <param name="right">The second operand, with the same requirements.</param>
    /// <param name="status">Receives InvalidOperation if an operand is not a logical operand.</param>
    /// <returns>The encoded result, or a quiet NaN if an operand is invalid.</returns>
    public static uint Xor(uint left, uint right, ref Decimal32Status status)
    {
        if (LogicalOperandBits(left) is not { } first || LogicalOperandBits(right) is not { } second)
        {
            return Decimal32Arithmetic.Invalid(ref status);
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
    public static uint Invert(uint value, ref Decimal32Status status)
    {
        if (LogicalOperandBits(value) is not { } bits)
        {
            return Decimal32Arithmetic.Invalid(ref status);
        }

        return FromBits(~bits & FieldMask);
    }

    /// <summary>
    /// A logical number as one bit per digit, least significant digit first, or null if
    /// the value is not a logical number.
    /// </summary>
    private static uint? LogicalOperandBits(uint value)
    {
        if (Decimal32Encoding.IsSpecial(value) || Decimal32Encoding.IsNegative(value))
        {
            return null;
        }

        var coefficient = Decimal32Encoding.Unpack(value, out var exponent);
        if (exponent != 0)
        {
            return null;
        }

        var bits = 0u;
        for (var position = 0; position < Decimal32Encoding.Precision && coefficient != 0; position++)
        {
            var next = coefficient / 10;
            var digit = coefficient - (next * 10);
            if (digit > 1)
            {
                return null;
            }

            bits |= digit << position;
            coefficient = next;
        }

        return bits;
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
