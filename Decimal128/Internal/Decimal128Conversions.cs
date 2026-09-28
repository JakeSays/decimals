// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Numerics;

namespace Decimals.Internal;

/// <summary>
/// Conversions between <see cref="Decimal128"/> and the other numeric types, behind the
/// six hooks <see cref="INumberBase{TSelf}"/> builds <c>CreateChecked</c>,
/// <c>CreateSaturating</c>, and <c>CreateTruncating</c> on.
/// </summary>
/// <remarks>
/// Integers convert exactly; every built-in one fits thirty-four digits. The binary
/// floating-point types go through their shortest round-trippable text, which is the only
/// reading of a <see cref="double"/> that means what the writer wrote: <c>0.1</c> becomes
/// the decimal 0.1, not the binary fraction nearest to it. Any other numeric type converts
/// through its invariant text, which is what keeps the wide integer types out of this
/// code while still letting them in as operands.
/// </remarks>
internal static unsafe class Decimal128Conversions
{
    /// <summary>Room for the shortest text of any built-in binary float, and for any integer part.</summary>
    private const int TextBufferLength = 64;

    public static bool TryFrom<TOther>(TOther value, out Decimal128Integer bits)
        where TOther : INumberBase<TOther>
    {
        switch (value)
        {
            case byte number:
                bits = FromUInt64(number, false);
                return true;
            case ushort number:
                bits = FromUInt64(number, false);
                return true;
            case uint number:
                bits = FromUInt64(number, false);
                return true;
            case ulong number:
                bits = FromUInt64(number, false);
                return true;
            case nuint number:
                bits = FromUInt64(number, false);
                return true;
            case char number:
                bits = FromUInt64(number, false);
                return true;
            case sbyte number:
                bits = FromInt64(number);
                return true;
            case short number:
                bits = FromInt64(number);
                return true;
            case int number:
                bits = FromInt64(number);
                return true;
            case long number:
                bits = FromInt64(number);
                return true;
            case nint number:
                bits = FromInt64(number);
                return true;
            case Half number:
                bits = FromBinary(number);
                return true;
            case float number:
                bits = FromBinary(number);
                return true;
            case double number:
                bits = FromBinary(number);
                return true;
            case Decimal128 number:
                bits = number.Bits;
                return true;
            default:
                // Anything else -- System.Decimal, the wide integers, another decimal
                // format -- is read from the text it writes for the invariant culture, which
                // every numeric type produces and which is exact for all of them.
                return TryFromText(value.ToString(null, CultureInfo.InvariantCulture), out bits);
        }
    }

    private static bool TryFromText(string? text, out Decimal128Integer bits)
    {
        if (text is null)
        {
            bits = Decimal128Integer.Zero;
            return false;
        }

        var status = Decimal128Status.None;
        bits = Decimal128Parser.Parse(text.AsSpan(), Decimal128Rounding.HalfEven, ref status);
        return (status & Decimal128Status.ConversionSyntax) == 0;
    }

    public static bool TryTo<TOther>(Decimal128Integer bits, out TOther? value)
        where TOther : INumberBase<TOther>?
    {
        value = default;

        if (typeof(TOther) == typeof(Decimal128))
        {
            value = (TOther)(object)Decimal128.FromInternal(bits);
            return true;
        }

        if (typeof(TOther) == typeof(double) || typeof(TOther) == typeof(float)
            || typeof(TOther) == typeof(Half))
        {
            value = TOther.CreateSaturating(Decimal128Formatter.ToDouble(bits));
            return true;
        }

        if (Decimal128Encoding.IsSpecial(bits))
        {
            // An integer type has nowhere to put a NaN or an infinity, but it does know
            // what to do with them: saturating from a double gives zero for a NaN and the
            // target's own limits for an infinity, which is what converting a double to an
            // integer produces and so what a caller expects here.
            var infinity = Decimal128Encoding.IsNegative(bits)
                ? double.NegativeInfinity
                : double.PositiveInfinity;

            value = TOther.CreateSaturating(Decimal128Encoding.IsNaN(bits) ? double.NaN : infinity);
            return true;
        }

        // Everything else is an integer type, so the value is truncated toward zero and
        // clamped into whatever range the target has.
        value = ToIntegerSaturating<TOther>(bits);
        return true;
    }

    /// <summary>
    /// The magnitude of the value truncated toward zero, when it fits two machine words.
    /// </summary>
    private static bool TryTruncate(Decimal128Integer coefficient, int exponent, out Decimal128Integer magnitude)
    {
        magnitude = coefficient;

        if (exponent < 0)
        {
            magnitude = -exponent > Decimal128Tables.MaxWidePower
                ? Decimal128Integer.Zero
                : Decimal128Tables.DivRemWidePowerOfTen(coefficient, -exponent, out _);

            return true;
        }

        if (exponent == 0 || coefficient.IsZero)
        {
            return true;
        }

        // Thirty-eight digits is the most two words hold whatever their leading digit, so
        // anything wider is out before the multiply.
        if (Decimal128Tables.CountDigits(coefficient) + exponent > Decimal128Tables.MaxWidePower)
        {
            return false;
        }

        magnitude = Decimal128Tables.Scale(coefficient, exponent);
        return true;
    }

    /// <summary>The magnitude at which a negative value stops fitting a signed word.</summary>
    private const ulong NegativeLimit = 1UL << 63;

    /// <summary>
    /// The value as a whole number, truncated toward zero and narrowed into the target,
    /// which is what a cast to an integer type asks for. A value the target cannot hold
    /// throws, as a checked conversion does.
    /// </summary>
    public static TInteger ToInteger<TInteger>(Decimal128Integer bits)
        where TInteger : INumberBase<TInteger>
    {
        if (Decimal128Encoding.IsSpecial(bits))
        {
            throw new OverflowException("A NaN or an infinity has no integer value.");
        }

        var coefficient = Decimal128Encoding.Unpack(bits, out var exponent);
        if (!TryTruncate(coefficient, exponent, out var magnitude) || !magnitude.IsWord)
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
    /// The same conversion, clamped to the target's range rather than throwing. A magnitude
    /// past one word reaches the target through its integer text, which is exact for any
    /// target wide enough to hold it and saturates the rest.
    /// </summary>
    public static TInteger ToIntegerSaturating<TInteger>(Decimal128Integer bits)
        where TInteger : INumberBase<TInteger>?
    {
        var coefficient = Decimal128Encoding.Unpack(bits, out var exponent);
        var negative = Decimal128Encoding.IsNegative(bits);

        if (!TryTruncate(coefficient, exponent, out var magnitude))
        {
            // Past two words, which only a positive exponent reaches: the integer is the
            // coefficient's digits followed by that many zeros.
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
    /// Parses the target from the digits of an integer written out in full: a sign, the
    /// coefficient, and as many zeros as the exponent asks for. A target the integer does
    /// not fit refuses the text and gets its own limit instead.
    /// </summary>
    private static TInteger FromIntegerText<TInteger>(Decimal128Integer coefficient, int zeros, bool negative)
        where TInteger : INumberBase<TInteger>?
    {
        var count = Decimal128Tables.CountDigits(coefficient);
        var length = 1 + count + zeros;

        char[]? rented = null;
        Span<char> buffer = length <= TextBufferLength
            ? stackalloc char[TextBufferLength]
            : (rented = System.Buffers.ArrayPool<char>.Shared.Rent(length));

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
                System.Buffers.ArrayPool<char>.Shared.Return(rented);
            }
        }
    }

    public static Decimal128Integer FromUInt64(ulong magnitude, bool negative)
    {
        return Decimal128Encoding.Pack(negative, 0, Decimal128Integer.FromUInt64(magnitude));
    }

    public static Decimal128Integer FromInt64(long number)
    {
        var negative = number < 0;
        var magnitude = negative ? unchecked((ulong)(-number)) : (ulong)number;
        return FromUInt64(magnitude, negative);
    }

    /// <summary>
    /// From a binary float, through its own shortest round-trip text. Widening a float to a
    /// double first would hand the parser 0.100000001490116119384765625 for 0.1f, so each
    /// width writes its own.
    /// </summary>
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

    public static Decimal128Integer FromBinary(double number, Decimal128BinaryConversion conversion)
    {
        return conversion == Decimal128BinaryConversion.ExactValue ? FromExactBinary(number) : FromBinary(number);
    }

    public static Decimal128Integer FromBinary(float number, Decimal128BinaryConversion conversion)
    {
        return conversion == Decimal128BinaryConversion.ExactValue ? FromExactBinary(number) : FromBinary(number);
    }

    public static Decimal128Integer FromBinary(Half number, Decimal128BinaryConversion conversion)
    {
        return conversion == Decimal128BinaryConversion.ExactValue
            ? FromExactBinary((double)number)
            : FromBinary(number);
    }

    /// <summary>
    /// Units the exact decimal form of a double needs: 5**1074 is 751 digits and the
    /// mantissa contributes sixteen more.
    /// </summary>
    private const int ExactBinaryUnits = 96;

    /// <summary>Twos that fit one unit: 2**29 is under a billion, 2**30 is not.</summary>
    private const int TwosPerStep = 29;

    /// <summary>Fives that fit one unit: 5**12 is under a billion, 5**13 is not.</summary>
    private const int FivesPerStep = 12;

    /// <summary>
    /// IEEE 754's convertFormat: the binary value exactly, rounded to the format. A binary
    /// float is a whole number times a power of two, and a negative power of two is a power
    /// of five over a power of ten -- so the value has an exact decimal form, and all this
    /// has to do is write it down and round it.
    /// </summary>
    /// <remarks>
    /// A float or a Half widens to a double without loss, so all three arrive here.
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

        // A subnormal has no hidden bit and a fixed exponent; everything else carries one.
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
            // Two to the power, a unit at a time: 2**29 is the largest that fits one, so
            // this is a few dozen passes rather than a wide exponentiation.
            length = RaiseBySmallSteps(units, length, 2, TwosPerStep, exponent);
            scale = 0;
        }
        else
        {
            // m * 2**-k is m * 5**k * 10**-k, which is exact and needs no division.
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

        // The exact form carries trailing zeros whenever the mantissa is even, and they say
        // nothing: 2.5 would arrive as 2.500000000000000000000000000000000. Strip them back
        // to the shortest form that still holds the value exactly, stopping at a zero
        // exponent so an integer stays written out.
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
    /// fits a single unit. Building the power itself would need an intermediate far wider
    /// than the result.
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
