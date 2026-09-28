// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Numerics;

namespace Decimals.Internal;

/// <summary>
/// Conversions between <see cref="Decimal32"/> and the other numeric types, behind the six
/// hooks <see cref="INumberBase{TSelf}"/> builds <c>CreateChecked</c>,
/// <c>CreateSaturating</c>, and <c>CreateTruncating</c> on.
/// </summary>
/// <remarks>
/// Integers convert exactly, rounding only if they carry more digits than the format holds.
/// The binary floating-point types go through their shortest round-trippable text, which is
/// the only reading of a <see cref="double"/> that means what the writer wrote:
/// <c>0.1</c> becomes the decimal 0.1, not the binary fraction nearest to it. Any other
/// numeric type converts through its invariant text, which is what keeps the wide integer
/// types out of this code while still letting them in as operands.
/// </remarks>
internal static unsafe class Decimal32Conversions
{
    /// <summary>Room for the shortest text of any built-in binary float.</summary>
    private const int TextBufferLength = 64;

    public static bool TryFrom<TOther>(TOther value, out uint bits)
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
            case Decimal32 number:
                bits = number.ToBits();
                return true;
            default:
                // Anything else -- System.Decimal, the wide integers, another decimal
                // format -- is read from the text it writes for the invariant culture, which
                // every numeric type produces and which is exact for all of them.
                return TryFromText(value.ToString(null, CultureInfo.InvariantCulture), out bits);
        }
    }

    private static bool TryFromText(string? text, out uint bits)
    {
        if (text is null)
        {
            bits = 0;
            return false;
        }

        var status = Decimal32Status.None;
        bits = Decimal32Parser.Parse(text.AsSpan(), Decimal32Rounding.HalfEven, ref status);
        return (status & Decimal32Status.ConversionSyntax) == 0;
    }

    public static bool TryTo<TOther>(uint bits, out TOther? value)
        where TOther : INumberBase<TOther>?
    {
        value = default;

        if (typeof(TOther) == typeof(Decimal32))
        {
            value = (TOther)(object)Decimal32.FromBits(bits);
            return true;
        }

        if (typeof(TOther) == typeof(double) || typeof(TOther) == typeof(float)
            || typeof(TOther) == typeof(Half))
        {
            value = TOther.CreateSaturating(Decimal32Formatter.ToDouble(bits));
            return true;
        }

        if (Decimal32Encoding.IsSpecial(bits))
        {
            // An integer type has nowhere to put a NaN or an infinity, but it does know
            // what to do with them: saturating from a double gives zero for a NaN and the
            // target's own limits for an infinity, which is what converting a double to an
            // integer produces and so what a caller expects here.
            var infinity = Decimal32Encoding.IsNegative(bits)
                ? double.NegativeInfinity
                : double.PositiveInfinity;

            value = TOther.CreateSaturating(Decimal32Encoding.IsNaN(bits) ? double.NaN : infinity);
            return true;
        }

        // Everything else is an integer type, so the value is truncated toward zero and
        // clamped into whatever range the target has.
        value = ToIntegerSaturating<TOther>(bits);
        return true;
    }

    /// <summary>
    /// The magnitude of the value truncated toward zero, when it fits a machine word.
    /// </summary>
    private static bool TryTruncate(ulong coefficient, int exponent, out ulong magnitude)
    {
        magnitude = coefficient;

        if (exponent < 0)
        {
            magnitude = -exponent > Decimal32Tables.MaxPower
                ? 0
                : Decimal32Tables.DivRemPowerOfTen(coefficient, -exponent, out _);

            return true;
        }

        if (exponent == 0 || coefficient == 0)
        {
            return true;
        }

        // Twenty digits is the most a word holds, so anything wider is out before the
        // multiply; what is left may still overflow, which the high word says.
        if (Decimal32Tables.CountDigits(coefficient) + exponent > 20)
        {
            return false;
        }

        var high = Math.BigMul(coefficient, Decimal32Tables.PowerOfTen(exponent), out magnitude);
        return high == 0;
    }

    /// <summary>The magnitude at which a negative value stops fitting a signed word.</summary>
    private const ulong NegativeLimit = 1UL << 63;

    /// <summary>
    /// The value as a whole number, truncated toward zero and narrowed into the target,
    /// which is what a cast to an integer type asks for. A value the target cannot hold
    /// throws, as a checked conversion does.
    /// </summary>
    public static TInteger ToInteger<TInteger>(uint bits)
        where TInteger : INumberBase<TInteger>
    {
        if (Decimal32Encoding.IsSpecial(bits))
        {
            throw new OverflowException("A NaN or an infinity has no integer value.");
        }

        var coefficient = Decimal32Encoding.Unpack(bits, out var exponent);
        if (!TryTruncate(coefficient, exponent, out var magnitude))
        {
            throw new OverflowException("The value is too large for the target type.");
        }

        if (!Decimal32Encoding.IsNegative(bits))
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
    /// The same conversion, clamped to the target's range rather than throwing. A magnitude
    /// past a machine word reaches the target as a double, which is exact for every built-in
    /// integer it could still fit in and saturates the rest; a negative one past a signed
    /// word goes through its text, which a wider target reads exactly.
    /// </summary>
    public static TInteger ToIntegerSaturating<TInteger>(uint bits)
        where TInteger : INumberBase<TInteger>?
    {
        var coefficient = Decimal32Encoding.Unpack(bits, out var exponent);
        var negative = Decimal32Encoding.IsNegative(bits);

        if (!TryTruncate(coefficient, exponent, out var magnitude))
        {
            return TInteger.CreateSaturating(Decimal32Formatter.ToDouble(bits));
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
    /// Parses the target from the text of a negative integer whose magnitude is past a
    /// signed word. A target it does not fit refuses the text and gets its own minimum.
    /// </summary>
    private static TInteger FromNegativeText<TInteger>(ulong magnitude)
        where TInteger : INumberBase<TInteger>?
    {
        Span<char> buffer = stackalloc char[TextBufferLength];
        buffer[0] = '-';
        var count = Decimal32Tables.CountDigits(magnitude);
        Decimal32Digits.Write(magnitude, count, ref buffer[1]);

        if (TInteger.TryParse(buffer[..(count + 1)], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
            out var parsed))
        {
            return parsed;
        }

        return TInteger.CreateSaturating(double.NegativeInfinity);
    }

    public static uint FromUInt64(ulong magnitude, bool negative)
    {
        var status = Decimal32Status.None;
        return Decimal32Finalizer.Finalize(negative, magnitude, 0, Decimal32Residue.Exact, Decimal32Rounding.HalfEven,
            ref status);
    }

    public static uint FromInt64(long number)
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
    public static uint FromBinary(double number)
    {
        if (double.IsNaN(number))
        {
            return Decimal32Encoding.QuietNaN();
        }

        if (double.IsInfinity(number))
        {
            return Decimal32Encoding.Infinity(double.IsNegative(number));
        }

        Span<char> buffer = stackalloc char[TextBufferLength];
        number.TryFormat(buffer, out var written, "R", CultureInfo.InvariantCulture);
        return FromShortestText(buffer[..written]);
    }

    public static uint FromBinary(float number)
    {
        if (float.IsNaN(number))
        {
            return Decimal32Encoding.QuietNaN();
        }

        if (float.IsInfinity(number))
        {
            return Decimal32Encoding.Infinity(float.IsNegative(number));
        }

        Span<char> buffer = stackalloc char[TextBufferLength];
        number.TryFormat(buffer, out var written, "R", CultureInfo.InvariantCulture);
        return FromShortestText(buffer[..written]);
    }

    public static uint FromBinary(Half number)
    {
        if (Half.IsNaN(number))
        {
            return Decimal32Encoding.QuietNaN();
        }

        if (Half.IsInfinity(number))
        {
            return Decimal32Encoding.Infinity(Half.IsNegative(number));
        }

        Span<char> buffer = stackalloc char[TextBufferLength];
        number.TryFormat(buffer, out var written, "R", CultureInfo.InvariantCulture);
        return FromShortestText(buffer[..written]);
    }

    private static uint FromShortestText(ReadOnlySpan<char> text)
    {
        var status = Decimal32Status.None;
        return Decimal32Parser.Parse(text, Decimal32Rounding.HalfEven, ref status);
    }

    public static uint FromBinary(double number, Decimal32BinaryConversion conversion)
    {
        return conversion == Decimal32BinaryConversion.ExactValue ? FromExactBinary(number) : FromBinary(number);
    }

    public static uint FromBinary(float number, Decimal32BinaryConversion conversion)
    {
        return conversion == Decimal32BinaryConversion.ExactValue ? FromExactBinary(number) : FromBinary(number);
    }

    public static uint FromBinary(Half number, Decimal32BinaryConversion conversion)
    {
        return conversion == Decimal32BinaryConversion.ExactValue
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
    private static uint FromExactBinary(double number)
    {
        if (double.IsNaN(number))
        {
            return Decimal32Encoding.QuietNaN();
        }

        if (double.IsInfinity(number))
        {
            return Decimal32Encoding.Infinity(double.IsNegative(number));
        }

        var negative = double.IsNegative(number);
        if (number == 0.0)
        {
            return Decimal32Encoding.Zero(negative, 0);
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
            units[length] = (uint)(remaining % Decimal32WideNumber.UnitBase);
            remaining /= Decimal32WideNumber.UnitBase;
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

        var value = default(Decimal32WideNumber);
        value.Lsu = units;
        value.Units = length;
        value.Kind = Decimal32Kind.Finite;
        value.IsNegative = negative;
        value.Exponent = scale;
        value.CountDigits();

        // The exact form carries trailing zeros whenever the mantissa is even, and they say
        // nothing: 2.5 would arrive as 2.500000000000000000000000000000000. Strip them back
        // to the shortest form that still holds the value exactly, stopping at a zero
        // exponent so an integer stays written out.
        while (value.Exponent < 0
            && Decimal32WideUnits.DigitAt(value.Lsu, value.Units, 0) == 0
            && !value.IsZero)
        {
            value.Units = Decimal32WideUnits.ShiftDown(value.Lsu, value.Units, 1);
            value.Exponent++;
            value.CountDigits();
        }

        var status = Decimal32Status.None;
        var context = Decimal32WideContext.ForFormat(Decimal32Rounding.HalfEven);
        var residue = 0;

        Decimal32WideRounding.SetCoefficient(ref value, context.Digits, ref residue, ref status);
        Decimal32WideRounding.Finalize(ref value, residue, context, ref status);

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
            length = Decimal32WideUnits.MultiplyBySmall(units, length, stepMultiplier);
            power -= perStep;
        }

        var remainderMultiplier = 1u;
        for (var index = 0; index < power; index++)
        {
            remainderMultiplier *= value;
        }

        return Decimal32WideUnits.MultiplyBySmall(units, length, remainderMultiplier);
    }
}
