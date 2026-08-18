// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Numerics;

namespace Decimals;

/// <summary>
/// Conversions between a decimal format and the other numeric types, behind the six hooks
/// <see cref="INumberBase{TSelf}"/> builds <c>CreateChecked</c>, <c>CreateSaturating</c>,
/// and <c>CreateTruncating</c> on.
/// </summary>
/// <remarks>
/// Integers convert exactly, rounding only if they carry more digits than the format holds.
/// The binary floating-point types go through their shortest round-trippable text, which is
/// the only reading of a <see cref="double"/> that means what the writer wrote:
/// <c>0.1</c> becomes the decimal 0.1, not the binary fraction nearest to it.
/// </remarks>
internal static class DecimalConversions<TFormat, TBits>
    where TFormat : IDecimalFormat<TBits>
    where TBits : IBinaryInteger<TBits>, IUnsignedNumber<TBits>
{
    public static bool TryFrom<TOther>(TOther value, out TBits bits)
        where TOther : INumberBase<TOther>
    {
        bits = default!;

        switch (value)
        {
            case byte number: bits = FromUInt128(number, false); return true;
            case ushort number: bits = FromUInt128(number, false); return true;
            case uint number: bits = FromUInt128(number, false); return true;
            case ulong number: bits = FromUInt128(number, false); return true;
            case UInt128 number: bits = FromUInt128(number, false); return true;
            case nuint number: bits = FromUInt128(number, false); return true;
            case char number: bits = FromUInt128(number, false); return true;
            case sbyte number: bits = FromInt128(number); return true;
            case short number: bits = FromInt128(number); return true;
            case int number: bits = FromInt128(number); return true;
            case long number: bits = FromInt128(number); return true;
            case Int128 number: bits = FromInt128(number); return true;
            case nint number: bits = FromInt128(number); return true;
            case decimal number: bits = FromText(number.ToString(CultureInfo.InvariantCulture)); return true;
            case BigInteger number: bits = FromText(number.ToString(CultureInfo.InvariantCulture)); return true;
            case Half number: bits = FromBinary(number); return true;
            case float number: bits = FromBinary(number); return true;
            case double number: bits = FromBinaryFloat(number); return true;

            // The sibling formats convert as decimals rather than through an integer,
            // which is what the generic path below them would otherwise do -- and it
            // would throw the fraction away.
            case Decimal32 number: bits = FromOtherFormat(Decimal32.Unpacked(number)); return true;
            case Decimal64 number: bits = FromOtherFormat(Decimal64.Unpacked(number)); return true;
            case Decimal128 number: bits = FromOtherFormat(Decimal128.Unpacked(number)); return true;

            default: return false;
        }
    }

    /// <summary>
    /// Reads a value taken apart from one format into this one. Widening keeps the value
    /// and its quantum exactly, since every coefficient and exponent of a narrower format
    /// is in range here; narrowing rounds, and can overflow to an infinity or underflow to
    /// a subnormal, exactly as arithmetic in this format would.
    /// </summary>
    public static TBits FromOtherFormat(UnpackedDecimal<UInt128> value)
    {
        var status = DecimalStatus.None;
        return FromOtherFormat(value, DecimalRounding.HalfEven, ref status);
    }

    public static TBits FromOtherFormat(UnpackedDecimal<UInt128> value, DecimalRounding rounding,
        ref DecimalStatus status)
    {
        if (value.IsNaN)
        {
            // A payload too wide for this format keeps its low digits, the ones a
            // diagnostic writes last. The kind is kept as it is: converting between
            // formats is a copy of the value, not an operation on it, so a signaling NaN
            // stays signaling -- which is what decNumber's decFloatFromWider does.
            var payload = value.Coefficient;
            var limit = PowersOfTen.UInt128(TFormat.Precision - 1);
            if (payload >= limit)
            {
                payload %= limit;
            }

            return TFormat.Pack(new UnpackedDecimal<UInt128>(value.Kind, value.IsNegative, 0, payload));
        }

        if (value.Kind == DecimalKind.Infinity)
        {
            return TFormat.Pack(value);
        }

        return TFormat.Pack(DecimalFinalizer.Finalize<TFormat>(value.IsNegative, value.Coefficient,
            value.Exponent, rounding, ref status));
    }

    public static bool TryTo<TOther>(TBits bits, out TOther? value)
        where TOther : INumberBase<TOther>?
    {
        value = default;
        var unpacked = TFormat.Unpack(bits);

        // A sibling format is asked to read the value as a decimal. Without this the
        // integer path at the end of this method would truncate the fraction away, and
        // report success while doing it.
        if (typeof(TOther) == typeof(Decimal32))
        {
            value = (TOther)(object)Decimal32.FromOtherFormat(unpacked);
            return true;
        }

        if (typeof(TOther) == typeof(Decimal64))
        {
            value = (TOther)(object)Decimal64.FromOtherFormat(unpacked);
            return true;
        }

        if (typeof(TOther) == typeof(Decimal128))
        {
            value = (TOther)(object)Decimal128.FromOtherFormat(unpacked);
            return true;
        }

        if (typeof(TOther) == typeof(double) || typeof(TOther) == typeof(float)
            || typeof(TOther) == typeof(Half))
        {
            // Text again, for the same reason: the decimal value is what should be read,
            // not whichever binary fraction happens to sit nearest its coefficient.
            var text = DecimalFormatter.ToScientificString(unpacked);
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var binary))
            {
                binary = unpacked.IsNaN ? double.NaN
                    : unpacked.IsNegative ? double.NegativeInfinity : double.PositiveInfinity;
            }

            value = TOther.CreateSaturating(binary);
            return true;
        }

        if (unpacked.Kind != DecimalKind.Finite)
        {
            // An integer type has nowhere to put a NaN or an infinity, but it does know
            // what to do with them: saturating from a double gives zero for a NaN and the
            // target's own limits for an infinity, which is what converting a double to an
            // integer produces and so what a caller expects here.
            var infinity = unpacked.IsNegative
                ? double.NegativeInfinity
                : double.PositiveInfinity;
            var binary = unpacked.IsNaN
                ? double.NaN
                : infinity;

            value = TOther.CreateSaturating(binary);
            return true;
        }

        // Everything else is an integer type, so the value is truncated toward zero and
        // handed over as a BigInteger for the target to narrow however it was asked to.
        var whole = ToBigInteger(unpacked);
        value = TOther.CreateSaturating(whole);
        return true;
    }

    private static BigInteger ToBigInteger(UnpackedDecimal<UInt128> value)
    {
        var magnitude = (BigInteger)value.Coefficient;
        if (value.Exponent > 0)
        {
            magnitude *= BigInteger.Pow(10, value.Exponent);
        }
        else if (value.Exponent < 0)
        {
            magnitude /= BigInteger.Pow(10, -value.Exponent);
        }

        return value.IsNegative ? -magnitude : magnitude;
    }

    /// <summary>
    /// The value as a whole number, truncated toward zero, which is what a cast to an
    /// integer type asks for. The caller decides what to do when it does not fit.
    /// </summary>
    public static BigInteger ToInteger(TBits bits)
    {
        var value = TFormat.Unpack(bits);
        if (value.Kind != DecimalKind.Finite)
        {
            // The same answer System.Decimal gives for a value it cannot hold, and a
            // better one than the zero a NaN's empty coefficient would otherwise produce.
            throw new OverflowException("A NaN or an infinity has no integer value.");
        }

        return ToBigInteger(value);
    }

    /// <summary>
    /// The value as a binary float. It goes through the shortest text that reads back as
    /// this value, so 0.1 arrives as the double nearest a tenth rather than as whatever
    /// the coefficient and exponent multiply out to.
    /// </summary>
    public static double ToBinary(TBits bits)
    {
        var value = TFormat.Unpack(bits);
        if (value.IsNaN)
        {
            return double.NaN;
        }

        var text = DecimalFormatter.ToScientificString(value);
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var binary))
        {
            return binary;
        }

        // Out of double's range either way: the sign says which end.
        return value.IsNegative ? double.NegativeInfinity : double.PositiveInfinity;
    }

    public static TBits FromInteger(Int128 number) => FromInt128(number);

    public static TBits FromInteger(UInt128 number) => FromUInt128(number, false);

    public static TBits FromBinary(double number) => FromBinaryFloat(number);

    /// <summary>
    /// From the narrower binary types, whose own shortest text is what the writer meant:
    /// widening 0.1f to a double first would hand the parser 0.100000001490116119384765625.
    /// </summary>
    public static TBits FromBinary(float number)
    {
        if (float.IsNaN(number))
        {
            return FromBinarySpecial(true, false);
        }

        if (float.IsInfinity(number))
        {
            return FromBinarySpecial(false, float.IsNegative(number));
        }

        return FromText(number.ToString("R", CultureInfo.InvariantCulture));
    }

    public static TBits FromBinary(Half number)
    {
        if (Half.IsNaN(number))
        {
            return FromBinarySpecial(true, false);
        }

        if (Half.IsInfinity(number))
        {
            return FromBinarySpecial(false, Half.IsNegative(number));
        }

        return FromText(number.ToString("R", CultureInfo.InvariantCulture));
    }

    public static TBits FromBinary(double number, BinaryConversion conversion) =>
        conversion == BinaryConversion.ExactValue ? FromExactBinary(number) : FromBinary(number);

    public static TBits FromBinary(float number, BinaryConversion conversion) =>
        conversion == BinaryConversion.ExactValue ? FromExactBinary(number) : FromBinary(number);

    public static TBits FromBinary(Half number, BinaryConversion conversion) =>
        conversion == BinaryConversion.ExactValue ? FromExactBinary((double)number) : FromBinary(number);

    /// <summary>
    /// IEEE 754's convertFormat: the binary value exactly, rounded to this format. A
    /// binary float is a whole number times a power of two, and a negative power of two is
    /// a power of five over a power of ten -- so the value has an exact decimal form, and
    /// all this has to do is write it down and round it.
    /// </summary>
    /// <remarks>
    /// A float or a Half widens to a double without loss, so all three arrive here.
    /// </remarks>
    private static TBits FromExactBinary(double number)
    {
        if (double.IsNaN(number))
        {
            return FromBinarySpecial(true, false);
        }

        if (double.IsInfinity(number))
        {
            return FromBinarySpecial(false, double.IsNegative(number));
        }

        var isNegative = double.IsNegative(number);
        if (number == 0.0)
        {
            // Exponent zero rather than the -1074 the decomposition below would give a
            // zero, which the finalizer would then have to clamp.
            return TFormat.Pack(new UnpackedDecimal<UInt128>(DecimalKind.Finite, isNegative, 0,
                UInt128.Zero));
        }

        var bits = BitConverter.DoubleToUInt64Bits(number);
        var rawExponent = (int)((bits >> 52) & 0x7FF);
        var rawMantissa = bits & 0xF_FFFF_FFFF_FFFF;

        // A subnormal has no hidden bit and a fixed exponent; everything else carries one.
        var mantissa = rawExponent == 0 ? (BigInteger)rawMantissa : rawMantissa | (1UL << 52);
        var exponent = rawExponent == 0 ? -1074 : rawExponent - 1075;

        BigInteger coefficient;
        int scale;
        if (exponent >= 0)
        {
            coefficient = mantissa << exponent;
            scale = 0;
        }
        else
        {
            // m * 2**-k is m * 5**k * 10**-k, which is exact and needs no division.
            coefficient = mantissa * BigInteger.Pow(5, -exponent);
            scale = exponent;

            // That form carries trailing zeros whenever the mantissa is even, and they say
            // nothing: 2.5 would arrive as 2.500000000000000000000000000000000. Strip them
            // back to the shortest form that still holds the value exactly, stopping at a
            // zero exponent so an integer stays written out.
            while (scale < 0 && (coefficient % 10).IsZero)
            {
                coefficient /= 10;
                scale++;
            }
        }

        var status = DecimalStatus.None;
        var value = new BigDecimal(DecimalKind.Finite, isNegative, scale, coefficient);
        var context = BigDecimalContext.ForFormat<TFormat>(DecimalRounding.HalfEven);
        return TFormat.Pack(BigDecimal.Round(value, context, ref status).ToUnpacked());
    }

    private static TBits FromBinarySpecial(bool isNaN, bool isNegative)
    {
        return TFormat.Pack(new UnpackedDecimal<UInt128>(
            isNaN ? DecimalKind.QuietNaN : DecimalKind.Infinity, isNegative, 0, UInt128.Zero));
    }

    private static TBits FromInt128(Int128 number)
    {
        var isNegative = number < Int128.Zero;
        var magnitude = isNegative ? (UInt128)(-number) : (UInt128)number;
        return FromUInt128(magnitude, isNegative);
    }

    private static TBits FromUInt128(UInt128 magnitude, bool isNegative)
    {
        var status = DecimalStatus.None;
        return TFormat.Pack(DecimalFinalizer.Finalize<TFormat>(isNegative, magnitude, 0,
            DecimalRounding.HalfEven, ref status));
    }

    private static TBits FromBinaryFloat(double number)
    {
        if (double.IsNaN(number))
        {
            return TFormat.Pack(new UnpackedDecimal<UInt128>(DecimalKind.QuietNaN, false, 0, UInt128.Zero));
        }

        if (double.IsInfinity(number))
        {
            return TFormat.Pack(new UnpackedDecimal<UInt128>(DecimalKind.Infinity,
                double.IsNegative(number), 0, UInt128.Zero));
        }

        return FromText(number.ToString("R", CultureInfo.InvariantCulture));
    }

    private static TBits FromText(string text)
    {
        var status = DecimalStatus.None;
        return TFormat.Pack(DecimalParser.Parse<TFormat>(text, DecimalRounding.HalfEven, ref status));
    }
}
