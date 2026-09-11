// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// The digit-wise logical operations, which read a coefficient as a string of ones and
/// zeros rather than as a number.
/// </summary>
/// <remarks>
/// <para>
/// An operand has to be a logical number: finite, unsigned, with a zero exponent and no
/// digit other than 0 or 1. Anything else is an invalid operation.
/// </para>
/// <para>
/// <see cref="DecimalLogical"/> maps the coefficient onto a bit per digit, applies one
/// machine instruction, and maps back. On the digit form there is nothing to map: the
/// digits are already one per byte, so the operation is the walk.
/// </para>
/// </remarks>
internal static unsafe class BcdLogical
{
    public static bool TryAnd<TFormat>(BcdNumber left, BcdNumber right, byte* buffer,
        out BcdNumber result)
        where TFormat : IDecimalFormat
    {
        return TryCombine<TFormat>(left, right, BcdLogicalOperation.And, buffer, out result);
    }

    public static bool TryOr<TFormat>(BcdNumber left, BcdNumber right, byte* buffer,
        out BcdNumber result)
        where TFormat : IDecimalFormat
    {
        return TryCombine<TFormat>(left, right, BcdLogicalOperation.Or, buffer, out result);
    }

    public static bool TryXor<TFormat>(BcdNumber left, BcdNumber right, byte* buffer,
        out BcdNumber result)
        where TFormat : IDecimalFormat
    {
        return TryCombine<TFormat>(left, right, BcdLogicalOperation.Xor, buffer, out result);
    }

    /// <summary>
    /// Inverts every digit of the format's full width, so a short coefficient's leading
    /// zeros become ones.
    /// </summary>
    public static bool TryInvert<TFormat>(BcdNumber value, byte* buffer, out BcdNumber result)
        where TFormat : IDecimalFormat
    {
        result = default;

        if (!IsLogical<TFormat>(value))
        {
            return false;
        }

        var msd = buffer + 1;
        var valueDigit = value.Lsd;

        for (var position = TFormat.Precision - 1; position >= 0; position--)
        {
            var digit = valueDigit >= value.Msd ? *valueDigit : (byte)0;
            msd[position] = (byte)(digit ^ 1);
            valueDigit--;
        }

        result = new BcdNumber(DecimalKind.Finite, false, 0, msd, msd + TFormat.Precision - 1);
        result.Trim();
        return true;
    }

    private static bool TryCombine<TFormat>(BcdNumber left, BcdNumber right,
        BcdLogicalOperation operation, byte* buffer, out BcdNumber result)
        where TFormat : IDecimalFormat
    {
        result = default;

        if (!IsLogical<TFormat>(left) || !IsLogical<TFormat>(right))
        {
            return false;
        }

        var msd = buffer + 1;
        var leftDigit = left.Lsd;
        var rightDigit = right.Lsd;

        for (var position = TFormat.Precision - 1; position >= 0; position--)
        {
            // A coefficient shorter than the width is padded with the zeros it would have
            // been written with.
            var first = leftDigit >= left.Msd ? *leftDigit : (byte)0;
            var second = rightDigit >= right.Msd ? *rightDigit : (byte)0;

            msd[position] = operation switch
            {
                BcdLogicalOperation.And => (byte)(first & second),
                BcdLogicalOperation.Or => (byte)(first | second),
                _ => (byte)(first ^ second)
            };

            leftDigit--;
            rightDigit--;
        }

        result = new BcdNumber(DecimalKind.Finite, false, 0, msd, msd + TFormat.Precision - 1);
        result.Trim();
        return true;
    }

    /// <summary>
    /// Whether an operand is a logical number: finite, unsigned, at an exponent of zero,
    /// and written with nothing but ones and zeros.
    /// </summary>
    private static bool IsLogical<TFormat>(BcdNumber value)
        where TFormat : IDecimalFormat
    {
        if (value.Kind != DecimalKind.Finite || value.IsNegative || value.Exponent != 0)
        {
            return false;
        }

        if (value.DigitCount > TFormat.Precision)
        {
            return false;
        }

        for (var digit = value.Msd; digit <= value.Lsd; digit++)
        {
            if (*digit > 1)
            {
                return false;
            }
        }

        return true;
    }
}
