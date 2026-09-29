// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Buffers;
using System.Globalization;
using System.Numerics;

namespace Decimals.Internal;

/// <summary>
/// Conversions between <see cref="Decimal128"/> and the other numeric types. They
/// implement the six methods that <see cref="INumberBase{TSelf}"/> uses to build
/// <c>CreateChecked</c>, <c>CreateSaturating</c>, and <c>CreateTruncating</c>.
/// </summary>
/// <remarks>
/// Integers convert exactly, because every built-in integer fits in 34 digits. The binary
/// floating-point types convert through their shortest round-trip text. That text is what
/// the writer of a <see cref="double"/> meant: <c>0.1</c> becomes the decimal 0.1, not the
/// nearest binary fraction. Any other numeric type converts through its invariant-culture
/// text. This keeps code for the wide integer types out of this class while still
/// accepting them as operands.
/// </remarks>
internal static unsafe class Decimal128Conversions
{
    /// <summary>Enough room for the shortest text of any built-in binary floating-point value, and for any integer part.</summary>
    private const int TextBufferLength = 64;

    /// <summary>
    /// Converts a value of another numeric type to the format, rounding half to even when it
    /// has more digits than the format holds.
    /// </summary>
    /// <typeparam name="TOther">The source numeric type.</typeparam>
    /// <param name="value">The value to convert.</param>
    /// <returns>The encoded value, or null if the source type has no direct conversion and its invariant-culture text is not a number.</returns>
    public static Decimal128Integer? ConvertFrom<TOther>(TOther value)
        where TOther : INumberBase<TOther>
    {
        switch (value)
        {
            case byte number:
                return FromUInt64(number, false);
            case ushort number:
                return FromUInt64(number, false);
            case uint number:
                return FromUInt64(number, false);
            case ulong number:
                return FromUInt64(number, false);
            case nuint number:
                return FromUInt64(number, false);
            case char number:
                return FromUInt64(number, false);
            case sbyte number:
                return FromInt64(number);
            case short number:
                return FromInt64(number);
            case int number:
                return FromInt64(number);
            case long number:
                return FromInt64(number);
            case nint number:
                return FromInt64(number);
            case Half number:
                return FromBinary(number);
            case float number:
                return FromBinary(number);
            case double number:
                return FromBinary(number);
            case Decimal128 number:
                return number.Bits;
            default:
                // Any other type (System.Decimal, the wide integers, another decimal format)
                // is parsed from its invariant-culture text. Every numeric type produces
                // that text, and it is exact for all of them.
                return ParseInvariant(value.ToString(null, CultureInfo.InvariantCulture));
        }
    }

    /// <summary>The value of invariant-culture text, or null if the text is null or not a number.</summary>
    private static Decimal128Integer? ParseInvariant(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var status = Decimal128Status.None;
        var bits = Decimal128Parser.Parse(text.AsSpan(), Decimal128Rounding.HalfEven, ref status);
        if ((status & Decimal128Status.ConversionSyntax) != 0)
        {
            return null;
        }

        return bits;
    }

    /// <summary>
    /// Converts an encoded value to another numeric type. A binary floating-point target
    /// gets the nearest value. An integer target gets the value truncated toward zero and
    /// clamped to its range.
    /// </summary>
    /// <typeparam name="TOther">The target numeric type.</typeparam>
    /// <param name="bits">The encoded value.</param>
    /// <returns>The converted value.</returns>
    public static TOther? ConvertSaturating<TOther>(Decimal128Integer bits)
        where TOther : INumberBase<TOther>?
    {
        if (typeof(TOther) == typeof(Decimal128))
        {
            return (TOther)(object)Decimal128.FromInternal(bits);
        }

        if (typeof(TOther) == typeof(double) || typeof(TOther) == typeof(float)
            || typeof(TOther) == typeof(Half))
        {
            return TOther.CreateSaturating(Decimal128Formatter.ToDouble(bits));
        }

        if (Decimal128Encoding.IsSpecial(bits))
        {
            // An integer type cannot hold a NaN or an infinity. Saturating from a double
            // gives zero for a NaN and the target's limits for an infinity. That matches
            // converting a double to an integer, so it is what a caller expects.
            var infinity = Decimal128Encoding.IsNegative(bits)
                ? double.NegativeInfinity
                : double.PositiveInfinity;

            return TOther.CreateSaturating(Decimal128Encoding.IsNaN(bits) ? double.NaN : infinity);
        }

        // Every other target is an integer type, so the value is truncated toward zero and
        // clamped to the target's range.
        return ToIntegerSaturating<TOther>(bits);
    }

    /// <summary>
    /// The magnitude of the value truncated toward zero, or null if it does not fit in two
    /// 64-bit words.
    /// </summary>
    private static Decimal128Integer? TruncatedMagnitude(Decimal128Integer coefficient, int exponent)
    {
        if (exponent < 0)
        {
            return -exponent > Decimal128Tables.MaxWidePower
                ? Decimal128Integer.Zero
                : Decimal128Tables.DivRemWidePowerOfTen(coefficient, -exponent, out _);
        }

        if (exponent == 0 || coefficient.IsZero)
        {
            return coefficient;
        }

        // Two words hold every value of up to 38 digits, so a wider value is rejected
        // before the multiply.
        if (Decimal128Tables.CountDigits(coefficient) + exponent > Decimal128Tables.MaxWidePower)
        {
            return null;
        }

        return Decimal128Tables.Scale(coefficient, exponent);
    }

    /// <summary>The largest magnitude of a negative value that fits in a signed 64-bit word.</summary>
    private const ulong NegativeLimit = 1UL << 63;

    /// <summary>
    /// The value truncated toward zero and converted to the target, as a cast to an integer
    /// type requires. Throws if the target cannot hold the value, like a checked conversion.
    /// </summary>
    /// <typeparam name="TInteger">The target integer type.</typeparam>
    /// <param name="bits">The encoded value.</param>
    /// <returns>The value truncated toward zero.</returns>
    public static TInteger ToInteger<TInteger>(Decimal128Integer bits)
        where TInteger : INumberBase<TInteger>
    {
        if (Decimal128Encoding.IsSpecial(bits))
        {
            throw new OverflowException("A NaN or an infinity has no integer value.");
        }

        var coefficient = Decimal128Encoding.Unpack(bits, out var exponent);
        if (TruncatedMagnitude(coefficient, exponent) is not { IsWord: true } magnitude)
        {
            throw new OverflowException("The value is too large for the target type.");
        }

        if (!Decimal128Encoding.IsNegative(bits))
        {
            return TInteger.CreateChecked(magnitude.Low);
        }

        if (magnitude.Low > NegativeLimit)
        {
            throw new OverflowException("The value is too large for the target type.");
        }

        return TInteger.CreateChecked(unchecked(-(long)magnitude.Low));
    }

    /// <summary>
    /// The same conversion, clamped to the target's range instead of throwing. A magnitude
    /// that does not fit in one word is passed to the target as integer text. The text is
    /// exact for any target wide enough to hold the value, and the others saturate.
    /// </summary>
    /// <typeparam name="TInteger">The target integer type.</typeparam>
    /// <param name="bits">The encoded value. It must be finite.</param>
    /// <returns>The value truncated toward zero and clamped to the target's range.</returns>
    public static TInteger ToIntegerSaturating<TInteger>(Decimal128Integer bits)
        where TInteger : INumberBase<TInteger>?
    {
        var coefficient = Decimal128Encoding.Unpack(bits, out var exponent);
        var negative = Decimal128Encoding.IsNegative(bits);

        if (TruncatedMagnitude(coefficient, exponent) is not { } magnitude)
        {
            // The value does not fit in two words, which only happens with a positive
            // exponent. The integer is the coefficient's digits followed by that many zeros.
            return FromIntegerText<TInteger>(coefficient, exponent, negative);
        }

        if (magnitude.IsWord)
        {
            if (!negative)
            {
                return TInteger.CreateSaturating(magnitude.Low);
            }

            if (magnitude.Low <= NegativeLimit)
            {
                return TInteger.CreateSaturating(unchecked(-(long)magnitude.Low));
            }
        }

        return FromIntegerText<TInteger>(magnitude, 0, negative);
    }

    /// <summary>
    /// Parses the target from the full text of an integer: a sign, the coefficient, and
    /// <paramref name="zeros"/> zeros. If the target cannot hold the integer, the result is
    /// the target's limit.
    /// </summary>
    private static TInteger FromIntegerText<TInteger>(Decimal128Integer coefficient, int zeros, bool negative)
        where TInteger : INumberBase<TInteger>?
    {
        var count = Decimal128Tables.CountDigits(coefficient);
        var length = 1 + count + zeros;

        char[]? rented = null;
        Span<char> buffer = length <= TextBufferLength
            ? stackalloc char[TextBufferLength]
            : (rented = ArrayPool<char>.Shared.Rent(length));

        try
        {
            buffer[0] = '-';
            Decimal128Digits.Write(coefficient, count, ref buffer[1]);
            buffer.Slice(1 + count, zeros).Fill('0');

            var text = negative ? buffer[..length] : buffer.Slice(1, length - 1);
            if (TInteger.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            return TInteger.CreateSaturating(negative ? double.NegativeInfinity : double.PositiveInfinity);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }

    /// <summary>Converts an integer given as a magnitude and a sign. Every such integer fits exactly.</summary>
    /// <param name="magnitude">The integer's magnitude.</param>
    /// <param name="negative">Whether the integer is negative.</param>
    /// <returns>The encoded value, with exponent zero.</returns>
    public static Decimal128Integer FromUInt64(ulong magnitude, bool negative)
    {
        return Decimal128Encoding.Pack(negative, 0, Decimal128Integer.FromUInt64(magnitude));
    }

    /// <summary>Converts a signed integer. Every such integer fits exactly.</summary>
    /// <param name="number">The integer.</param>
    /// <returns>The encoded value, with exponent zero.</returns>
    public static Decimal128Integer FromInt64(long number)
    {
        var negative = number < 0;
        var magnitude = negative ? unchecked((ulong)(-number)) : (ulong)number;
        return FromUInt64(magnitude, negative);
    }

    /// <summary>
    /// Converts a binary floating-point value through its own shortest round-trip text.
    /// Widening a float to double first would give the parser 0.100000001490116119384765625
    /// for 0.1f, so each width formats its own text.
    /// </summary>
    /// <param name="number">The binary value.</param>
    /// <returns>The encoded value of the text.</returns>
    public static Decimal128Integer FromBinary(double number)
    {
        if (double.IsNaN(number))
        {
            return Decimal128Encoding.QuietNaN();
        }

        if (double.IsInfinity(number))
        {
            return Decimal128Encoding.Infinity(double.IsNegative(number));
        }

        Span<char> buffer = stackalloc char[TextBufferLength];
        number.TryFormat(buffer, out var written, "R", CultureInfo.InvariantCulture);
        return FromShortestText(buffer[..written]);
    }

    /// <summary>Converts a float through its own shortest round-trip text.</summary>
    /// <param name="number">The binary value.</param>
    /// <returns>The encoded value of the text.</returns>
    public static Decimal128Integer FromBinary(float number)
    {
        if (float.IsNaN(number))
        {
            return Decimal128Encoding.QuietNaN();
        }

        if (float.IsInfinity(number))
        {
            return Decimal128Encoding.Infinity(float.IsNegative(number));
        }

        Span<char> buffer = stackalloc char[TextBufferLength];
        number.TryFormat(buffer, out var written, "R", CultureInfo.InvariantCulture);
        return FromShortestText(buffer[..written]);
    }

    /// <summary>Converts a <see cref="Half"/> through its own shortest round-trip text.</summary>
    /// <param name="number">The binary value.</param>
    /// <returns>The encoded value of the text.</returns>
    public static Decimal128Integer FromBinary(Half number)
    {
        if (Half.IsNaN(number))
        {
            return Decimal128Encoding.QuietNaN();
        }

        if (Half.IsInfinity(number))
        {
            return Decimal128Encoding.Infinity(Half.IsNegative(number));
        }

        Span<char> buffer = stackalloc char[TextBufferLength];
        number.TryFormat(buffer, out var written, "R", CultureInfo.InvariantCulture);
        return FromShortestText(buffer[..written]);
    }

    private static Decimal128Integer FromShortestText(ReadOnlySpan<char> text)
    {
        var status = Decimal128Status.None;
        return Decimal128Parser.Parse(text, Decimal128Rounding.HalfEven, ref status);
    }

    /// <summary>Converts a double from its shortest round-trip text or from its exact binary value.</summary>
    /// <param name="number">The binary value.</param>
    /// <param name="conversion">Which of the two values to convert.</param>
    /// <returns>The encoded value, rounded to the format.</returns>
    public static Decimal128Integer FromBinary(double number, Decimal128BinaryConversion conversion)
    {
        return conversion == Decimal128BinaryConversion.ExactValue ? FromExactBinary(number) : FromBinary(number);
    }

    /// <summary>Converts a float from its shortest round-trip text or from its exact binary value.</summary>
    /// <param name="number">The binary value.</param>
    /// <param name="conversion">Which of the two values to convert.</param>
    /// <returns>The encoded value, rounded to the format.</returns>
    public static Decimal128Integer FromBinary(float number, Decimal128BinaryConversion conversion)
    {
        return conversion == Decimal128BinaryConversion.ExactValue ? FromExactBinary(number) : FromBinary(number);
    }

    /// <summary>Converts a <see cref="Half"/> from its shortest round-trip text or from its exact binary value.</summary>
    /// <param name="number">The binary value.</param>
    /// <param name="conversion">Which of the two values to convert.</param>
    /// <returns>The encoded value, rounded to the format.</returns>
    public static Decimal128Integer FromBinary(Half number, Decimal128BinaryConversion conversion)
    {
        return conversion == Decimal128BinaryConversion.ExactValue
            ? FromExactBinary((double)number)
            : FromBinary(number);
    }

    /// <summary>
    /// The number of units the exact decimal form of a double needs: 5^1074 has 751 digits,
    /// and the mantissa adds 16 more.
    /// </summary>
    private const int ExactBinaryUnits = 96;

    /// <summary>The largest power of 2 that fits in one unit: 2^29 is below one billion, and 2^30 is not.</summary>
    private const int TwosPerStep = 29;

    /// <summary>The largest power of 5 that fits in one unit: 5^12 is below one billion, and 5^13 is not.</summary>
    private const int FivesPerStep = 12;

    /// <summary>
    /// IEEE 754 convertFormat: the exact binary value, rounded to the format. A binary
    /// floating-point value is an integer times a power of two, and a negative power of two
    /// is a power of five divided by a power of ten. So the value has an exact decimal form,
    /// and this method only has to compute it and round it.
    /// </summary>
    /// <remarks>
    /// A float or a Half widens to a double exactly, so all three types use this method.
    /// </remarks>
    private static Decimal128Integer FromExactBinary(double number)
    {
        if (double.IsNaN(number))
        {
            return Decimal128Encoding.QuietNaN();
        }

        if (double.IsInfinity(number))
        {
            return Decimal128Encoding.Infinity(double.IsNegative(number));
        }

        var negative = double.IsNegative(number);
        if (number == 0.0)
        {
            return Decimal128Encoding.Zero(negative, 0);
        }

        var bits = BitConverter.DoubleToUInt64Bits(number);
        var rawExponent = (int)((bits >> 52) & 0x7FF);
        var rawMantissa = bits & 0xF_FFFF_FFFF_FFFF;

        // A subnormal has no hidden bit and a fixed exponent. Every other value has a hidden bit.
        var mantissa = rawExponent == 0 ? rawMantissa : rawMantissa | (1UL << 52);
        var exponent = rawExponent == 0 ? -1074 : rawExponent - 1075;

        var units = stackalloc uint[ExactBinaryUnits];
        var length = 0;
        var remaining = mantissa;

        do
        {
            units[length] = (uint)(remaining % Decimal128WideNumber.UnitBase);
            remaining /= Decimal128WideNumber.UnitBase;
            length++;
        }
        while (remaining != 0);

        int scale;
        if (exponent >= 0)
        {
            // Multiply by the power of 2 in steps of 2^29, the largest power that fits in one
            // unit. This takes a few dozen passes instead of a wide exponentiation.
            length = RaiseBySmallSteps(units, length, 2, TwosPerStep, exponent);
            scale = 0;
        }
        else
        {
            // m * 2^-k equals m * 5^k * 10^-k, which is exact and needs no division.
            length = RaiseBySmallSteps(units, length, 5, FivesPerStep, -exponent);
            scale = exponent;
        }

        var value = default(Decimal128WideNumber);
        value.Lsu = units;
        value.Units = length;
        value.Kind = Decimal128Kind.Finite;
        value.IsNegative = negative;
        value.Exponent = scale;
        value.CountDigits();

        // The exact form has trailing zeros whenever the mantissa is even, and they carry no
        // information: 2.5 would arrive as 2.500000000000000000000000000000000. Remove them
        // to get the shortest form that still holds the value exactly. Stop at exponent
        // zero so that an integer keeps all its digits.
        while (value.Exponent < 0
            && Decimal128WideUnits.DigitAt(value.Lsu, value.Units, 0) == 0
            && !value.IsZero)
        {
            value.Units = Decimal128WideUnits.ShiftDown(value.Lsu, value.Units, 1);
            value.Exponent++;
            value.CountDigits();
        }

        var status = Decimal128Status.None;
        var context = Decimal128WideContext.ForFormat(Decimal128Rounding.HalfEven);
        var residue = 0;

        Decimal128WideRounding.SetCoefficient(ref value, context.Digits, ref residue, ref status);
        Decimal128WideRounding.Finalize(ref value, residue, context, ref status);

        return value.ToBits();
    }

    /// <summary>
    /// Multiplies by a base raised to a power, in steps small enough that each multiplier
    /// fits in one unit. Computing the power itself would need an intermediate value much
    /// wider than the result.
    /// </summary>
    private static int RaiseBySmallSteps(uint* units, int length, uint value, int perStep, int power)
    {
        var stepMultiplier = 1u;
        for (var index = 0; index < perStep; index++)
        {
            stepMultiplier *= value;
        }

        while (power >= perStep)
        {
            length = Decimal128WideUnits.MultiplyBySmall(units, length, stepMultiplier);
            power -= perStep;
        }

        var remainderMultiplier = 1u;
        for (var index = 0; index < power; index++)
        {
            remainderMultiplier *= value;
        }

        return Decimal128WideUnits.MultiplyBySmall(units, length, remainderMultiplier);
    }
}
