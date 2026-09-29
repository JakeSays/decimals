// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Numerics;

namespace Decimals.Internal;

/// <summary>
/// Conversions between <see cref="Decimal64"/> and the other numeric types. They implement
/// the six methods that <see cref="INumberBase{TSelf}"/> uses to build
/// <c>CreateChecked</c>, <c>CreateSaturating</c>, and <c>CreateTruncating</c>.
/// </summary>
/// <remarks>
/// Integers convert exactly, and round only if they have more digits than the format
/// holds. The binary floating-point types convert through their shortest round-trip text.
/// That text is what the writer of a <see cref="double"/> meant: <c>0.1</c> becomes the
/// decimal 0.1, not the nearest binary fraction. Any other numeric type converts through
/// its invariant-culture text. This keeps code for the wide integer types out of this
/// class while still accepting them as operands.
/// </remarks>
internal static unsafe class Decimal64Conversions
{
    /// <summary>Enough room for the shortest text of any built-in binary floating-point value.</summary>
    private const int TextBufferLength = 64;

    /// <summary>
    /// Converts a value of another numeric type to the format, rounding half to even when it
    /// has more digits than the format holds.
    /// </summary>
    /// <typeparam name="TOther">The source numeric type.</typeparam>
    /// <param name="value">The value to convert.</param>
    /// <returns>The encoded value, or null if the source type has no direct conversion and its invariant-culture text is not a number.</returns>
    public static ulong? ConvertFrom<TOther>(TOther value)
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
            case Decimal64 number:
                return number.ToBits();
            default:
                // Any other type (System.Decimal, the wide integers, another decimal format)
                // is parsed from its invariant-culture text. Every numeric type produces
                // that text, and it is exact for all of them.
                return ParseInvariant(value.ToString(null, CultureInfo.InvariantCulture));
        }
    }

    /// <summary>The value of invariant-culture text, or null if the text is null or not a number.</summary>
    private static ulong? ParseInvariant(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var status = Decimal64Status.None;
        var bits = Decimal64Parser.Parse(text.AsSpan(), Decimal64Rounding.HalfEven, ref status);
        if ((status & Decimal64Status.ConversionSyntax) != 0)
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
    public static TOther? ConvertSaturating<TOther>(ulong bits)
        where TOther : INumberBase<TOther>?
    {
        if (typeof(TOther) == typeof(Decimal64))
        {
            return (TOther)(object)Decimal64.FromBits(bits);
        }

        if (typeof(TOther) == typeof(double) || typeof(TOther) == typeof(float)
            || typeof(TOther) == typeof(Half))
        {
            return TOther.CreateSaturating(Decimal64Formatter.ToDouble(bits));
        }

        if (Decimal64Encoding.IsSpecial(bits))
        {
            // An integer type cannot hold a NaN or an infinity. Saturating from a double
            // gives zero for a NaN and the target's limits for an infinity. That matches
            // converting a double to an integer, so it is what a caller expects.
            var infinity = Decimal64Encoding.IsNegative(bits)
                ? double.NegativeInfinity
                : double.PositiveInfinity;

            return TOther.CreateSaturating(Decimal64Encoding.IsNaN(bits) ? double.NaN : infinity);
        }

        // Every other target is an integer type, so the value is truncated toward zero and
        // clamped to the target's range.
        return ToIntegerSaturating<TOther>(bits);
    }

    /// <summary>
    /// The magnitude of the value truncated toward zero, or null if it does not fit in a
    /// 64-bit word.
    /// </summary>
    private static ulong? TruncatedMagnitude(ulong coefficient, int exponent)
    {
        if (exponent < 0)
        {
            return -exponent > Decimal64Tables.MaxPower
                ? 0
                : Decimal64Tables.DivRemPowerOfTen(coefficient, -exponent, out _);
        }

        if (exponent == 0 || coefficient == 0)
        {
            return coefficient;
        }

        // A 64-bit word holds at most 20 digits, so a wider value is rejected before the
        // multiply. A value that passes can still overflow, and the product's high word
        // shows that.
        if (Decimal64Tables.CountDigits(coefficient) + exponent > 20)
        {
            return null;
        }

        var high = Math.BigMul(coefficient, Decimal64Tables.PowerOfTen(exponent), out var magnitude);
        if (high != 0)
        {
            return null;
        }

        return magnitude;
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
    public static TInteger ToInteger<TInteger>(ulong bits)
        where TInteger : INumberBase<TInteger>
    {
        if (Decimal64Encoding.IsSpecial(bits))
        {
            throw new OverflowException("A NaN or an infinity has no integer value.");
        }

        var coefficient = Decimal64Encoding.Unpack(bits, out var exponent);
        if (TruncatedMagnitude(coefficient, exponent) is not { } magnitude)
        {
            throw new OverflowException("The value is too large for the target type.");
        }

        if (!Decimal64Encoding.IsNegative(bits))
        {
            return TInteger.CreateChecked(magnitude);
        }

        if (magnitude > NegativeLimit)
        {
            throw new OverflowException("The value is too large for the target type.");
        }

        return TInteger.CreateChecked(unchecked(-(long)magnitude));
    }

    /// <summary>
    /// The same conversion, clamped to the target's range instead of throwing. A magnitude
    /// that does not fit in a 64-bit word is passed to the target as a double. The double
    /// is exact for every built-in integer type that could still hold the value, and it
    /// saturates the others. A negative magnitude that does not fit in a signed word is
    /// converted through its text, which a wider target parses exactly.
    /// </summary>
    /// <typeparam name="TInteger">The target integer type.</typeparam>
    /// <param name="bits">The encoded value. It must be finite.</param>
    /// <returns>The value truncated toward zero and clamped to the target's range.</returns>
    public static TInteger ToIntegerSaturating<TInteger>(ulong bits)
        where TInteger : INumberBase<TInteger>?
    {
        var coefficient = Decimal64Encoding.Unpack(bits, out var exponent);
        var negative = Decimal64Encoding.IsNegative(bits);

        if (TruncatedMagnitude(coefficient, exponent) is not { } magnitude)
        {
            return TInteger.CreateSaturating(Decimal64Formatter.ToDouble(bits));
        }

        if (!negative)
        {
            return TInteger.CreateSaturating(magnitude);
        }

        if (magnitude > NegativeLimit)
        {
            return FromNegativeText<TInteger>(magnitude);
        }

        return TInteger.CreateSaturating(unchecked(-(long)magnitude));
    }

    /// <summary>
    /// Parses the target from the text of a negative integer whose magnitude does not fit
    /// in a signed 64-bit word. If the target cannot hold it, the result is the target's
    /// minimum.
    /// </summary>
    private static TInteger FromNegativeText<TInteger>(ulong magnitude)
        where TInteger : INumberBase<TInteger>?
    {
        Span<char> buffer = stackalloc char[TextBufferLength];
        buffer[0] = '-';
        var count = Decimal64Tables.CountDigits(magnitude);
        Decimal64Digits.Write(magnitude, count, ref buffer[1]);

        if (TInteger.TryParse(buffer[..(count + 1)], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
            out var parsed))
        {
            return parsed;
        }

        return TInteger.CreateSaturating(double.NegativeInfinity);
    }

    /// <summary>
    /// Converts an integer given as a magnitude and a sign. It rounds half to even only if
    /// the integer has more digits than the format holds.
    /// </summary>
    /// <param name="magnitude">The integer's magnitude.</param>
    /// <param name="negative">Whether the integer is negative.</param>
    /// <returns>The encoded value, with exponent zero unless it was rounded.</returns>
    public static ulong FromUInt64(ulong magnitude, bool negative)
    {
        var status = Decimal64Status.None;
        return Decimal64Finalizer.Finalize(negative, magnitude, 0, Decimal64Residue.Exact, Decimal64Rounding.HalfEven,
            ref status);
    }

    /// <summary>Converts a signed integer.</summary>
    /// <param name="number">The integer.</param>
    /// <returns>The encoded value, with exponent zero unless it was rounded.</returns>
    public static ulong FromInt64(long number)
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
    /// <returns>The encoded value of the text, rounded to the format.</returns>
    public static ulong FromBinary(double number)
    {
        if (double.IsNaN(number))
        {
            return Decimal64Encoding.QuietNaN();
        }

        if (double.IsInfinity(number))
        {
            return Decimal64Encoding.Infinity(double.IsNegative(number));
        }

        Span<char> buffer = stackalloc char[TextBufferLength];
        number.TryFormat(buffer, out var written, "R", CultureInfo.InvariantCulture);
        return FromShortestText(buffer[..written]);
    }

    /// <summary>Converts a float through its own shortest round-trip text.</summary>
    /// <param name="number">The binary value.</param>
    /// <returns>The encoded value of the text, rounded to the format.</returns>
    public static ulong FromBinary(float number)
    {
        if (float.IsNaN(number))
        {
            return Decimal64Encoding.QuietNaN();
        }

        if (float.IsInfinity(number))
        {
            return Decimal64Encoding.Infinity(float.IsNegative(number));
        }

        Span<char> buffer = stackalloc char[TextBufferLength];
        number.TryFormat(buffer, out var written, "R", CultureInfo.InvariantCulture);
        return FromShortestText(buffer[..written]);
    }

    /// <summary>Converts a <see cref="Half"/> through its own shortest round-trip text.</summary>
    /// <param name="number">The binary value.</param>
    /// <returns>The encoded value of the text, rounded to the format.</returns>
    public static ulong FromBinary(Half number)
    {
        if (Half.IsNaN(number))
        {
            return Decimal64Encoding.QuietNaN();
        }

        if (Half.IsInfinity(number))
        {
            return Decimal64Encoding.Infinity(Half.IsNegative(number));
        }

        Span<char> buffer = stackalloc char[TextBufferLength];
        number.TryFormat(buffer, out var written, "R", CultureInfo.InvariantCulture);
        return FromShortestText(buffer[..written]);
    }

    private static ulong FromShortestText(ReadOnlySpan<char> text)
    {
        var status = Decimal64Status.None;
        return Decimal64Parser.Parse(text, Decimal64Rounding.HalfEven, ref status);
    }

    /// <summary>Converts a double from its shortest round-trip text or from its exact binary value.</summary>
    /// <param name="number">The binary value.</param>
    /// <param name="conversion">Which of the two values to convert.</param>
    /// <returns>The encoded value, rounded to the format.</returns>
    public static ulong FromBinary(double number, Decimal64BinaryConversion conversion)
    {
        return conversion == Decimal64BinaryConversion.ExactValue ? FromExactBinary(number) : FromBinary(number);
    }

    /// <summary>Converts a float from its shortest round-trip text or from its exact binary value.</summary>
    /// <param name="number">The binary value.</param>
    /// <param name="conversion">Which of the two values to convert.</param>
    /// <returns>The encoded value, rounded to the format.</returns>
    public static ulong FromBinary(float number, Decimal64BinaryConversion conversion)
    {
        return conversion == Decimal64BinaryConversion.ExactValue ? FromExactBinary(number) : FromBinary(number);
    }

    /// <summary>Converts a <see cref="Half"/> from its shortest round-trip text or from its exact binary value.</summary>
    /// <param name="number">The binary value.</param>
    /// <param name="conversion">Which of the two values to convert.</param>
    /// <returns>The encoded value, rounded to the format.</returns>
    public static ulong FromBinary(Half number, Decimal64BinaryConversion conversion)
    {
        return conversion == Decimal64BinaryConversion.ExactValue
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
    private static ulong FromExactBinary(double number)
    {
        if (double.IsNaN(number))
        {
            return Decimal64Encoding.QuietNaN();
        }

        if (double.IsInfinity(number))
        {
            return Decimal64Encoding.Infinity(double.IsNegative(number));
        }

        var negative = double.IsNegative(number);
        if (number == 0.0)
        {
            return Decimal64Encoding.Zero(negative, 0);
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
            units[length] = (uint)(remaining % Decimal64WideNumber.UnitBase);
            remaining /= Decimal64WideNumber.UnitBase;
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

        var value = default(Decimal64WideNumber);
        value.Lsu = units;
        value.Units = length;
        value.Kind = Decimal64Kind.Finite;
        value.IsNegative = negative;
        value.Exponent = scale;
        value.CountDigits();

        // The exact form has trailing zeros whenever the mantissa is even, and they carry no
        // information: 2.5 would arrive as 2.500000000000000000000000000000000. Remove them
        // to get the shortest form that still holds the value exactly. Stop at exponent
        // zero so that an integer keeps all its digits.
        while (value.Exponent < 0
            && Decimal64WideUnits.DigitAt(value.Lsu, value.Units, 0) == 0
            && !value.IsZero)
        {
            value.Units = Decimal64WideUnits.ShiftDown(value.Lsu, value.Units, 1);
            value.Exponent++;
            value.CountDigits();
        }

        var status = Decimal64Status.None;
        var context = Decimal64WideContext.ForFormat(Decimal64Rounding.HalfEven);
        var residue = 0;

        Decimal64WideRounding.SetCoefficient(ref value, context.Digits, ref residue, ref status);
        Decimal64WideRounding.Finalize(ref value, residue, context, ref status);

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
            length = Decimal64WideUnits.MultiplyBySmall(units, length, stepMultiplier);
            power -= perStep;
        }

        var remainderMultiplier = 1u;
        for (var index = 0; index < power; index++)
        {
            remainderMultiplier *= value;
        }

        return Decimal64WideUnits.MultiplyBySmall(units, length, remainderMultiplier);
    }
}
