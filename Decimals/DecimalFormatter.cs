// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Unicode;

namespace Decimals;

/// <summary>
/// The specification's <c>to-scientific-string</c> and <c>to-engineering-string</c>.
/// </summary>
/// <remarks>
/// Which notation is used is decided by the value, not by the caller: plain notation when
/// the exponent is at most zero and the adjusted exponent is at least -6, exponential
/// otherwise. So <c>5E-6</c> prints as <c>0.000005</c> and <c>5E-7</c> prints as itself.
/// Trailing zeros are part of the value -- <c>1.00</c> and <c>1.0</c> are different members
/// of the same cohort and print differently.
/// </remarks>
internal static class DecimalFormatter
{
    private const int PlainNotationFloor = -6;

    /// <summary>
    /// The specification's own spelling, which is what the invariant culture spells too:
    /// a period for the point, a hyphen for the sign, and <c>Infinity</c> and <c>NaN</c>
    /// written out.
    /// </summary>
    private static NumberFormatInfo Invariant => CultureInfo.InvariantCulture.NumberFormat;

    public static string ToScientificString(UnpackedDecimal<UInt128> value) =>
        ToScientificString(value, Invariant);

    public static string ToScientificString(UnpackedDecimal<UInt128> value,
        NumberFormatInfo numberFormat)
    {
        var builder = new StringBuilder(48);
        Write(builder, value, engineering: false, numberFormat);
        return builder.ToString();
    }

    public static string ToEngineeringString(UnpackedDecimal<UInt128> value) =>
        ToEngineeringString(value, Invariant);

    public static string ToEngineeringString(UnpackedDecimal<UInt128> value,
        NumberFormatInfo numberFormat)
    {
        var builder = new StringBuilder(48);
        Write(builder, value, engineering: true, numberFormat);
        return builder.ToString();
    }

    /// <summary>
    /// Formats under a standard .NET format string. An empty or "G" format gives the
    /// specification's scientific form, which is what <c>ToString()</c> produces; the rest
    /// are handed to the framework's own formatting of the value, so a Decimal64 prints
    /// under "F2" or "N0" the way any other number does.
    /// </summary>
    public static string Format(UnpackedDecimal<UInt128> value, ReadOnlySpan<char> format,
        IFormatProvider? provider)
    {
        var numberFormat = NumberFormatInfo.GetInstance(provider);

        if (format.IsEmpty || format.Equals("G", StringComparison.OrdinalIgnoreCase))
        {
            return ToScientificString(value, numberFormat);
        }

        if (format.Equals("E", StringComparison.OrdinalIgnoreCase))
        {
            return ToEngineeringString(value, numberFormat);
        }

        if (value.Kind != DecimalKind.Finite)
        {
            return ToScientificString(value, numberFormat);
        }

        // Fixed-point and grouped output are done here rather than handed to double: a
        // Decimal128 carries 34 digits and double would lose everything past the
        // seventeenth. The rest of the standard formats go to the framework, which does
        // cost that precision -- they are presentational, and reimplementing currency and
        // percent layout for every culture is not worth it.
        var specifier = char.ToUpperInvariant(format[0]);
        if ((specifier == 'F' || specifier == 'N') && TryReadPlaces(format[1..], out var places))
        {
            return Fixed(value, places, specifier == 'N', provider);
        }

        return ToDouble(value).ToString(format.ToString(), provider);
    }

    private static bool TryReadPlaces(ReadOnlySpan<char> digits, out int places)
    {
        if (digits.IsEmpty)
        {
            places = 2;
            return true;
        }

        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out places);
    }

    private static string Fixed(UnpackedDecimal<UInt128> value, int places, bool grouped,
        IFormatProvider? provider)
    {
        var numberFormat = NumberFormatInfo.GetInstance(provider);

        // Round to the requested number of places, half to even, which is what the
        // framework's own fixed-point formatting does.
        var scaled = DecimalRounder.Round(value.Coefficient, -value.Exponent - places,
            value.IsNegative, DecimalRounding.HalfEven, out _);

        if (value.Exponent + places > 0)
        {
            scaled *= PowersOfTen.UInt128(value.Exponent + places);
        }

        Span<char> digits = stackalloc char[64];
        var count = WriteDigits(scaled, digits);
        var text = digits[^count..];

        var whole = count > places ? text[..(count - places)] : "0".AsSpan();
        var fraction = count > places ? text[(count - places)..] : text;

        var builder = new StringBuilder(48);
        if (value.IsNegative && (scaled != UInt128.Zero))
        {
            builder.Append(numberFormat.NegativeSign);
        }

        AppendWhole(builder, whole, grouped, numberFormat);

        if (places > 0)
        {
            builder.Append(numberFormat.NumberDecimalSeparator);
            builder.Append('0', places - fraction.Length);
            builder.Append(fraction);
        }

        return builder.ToString();
    }

    private static void AppendWhole(StringBuilder builder, ReadOnlySpan<char> whole, bool grouped,
        NumberFormatInfo numberFormat)
    {
        if (!grouped)
        {
            builder.Append(whole);
            return;
        }

        var size = numberFormat.NumberGroupSizes.Length > 0 ? numberFormat.NumberGroupSizes[0] : 3;
        if (size <= 0)
        {
            builder.Append(whole);
            return;
        }

        for (var index = 0; index < whole.Length; index++)
        {
            if (index > 0 && (whole.Length - index) % size == 0)
            {
                builder.Append(numberFormat.NumberGroupSeparator);
            }

            builder.Append(whole[index]);
        }
    }

    public static bool TryFormat(UnpackedDecimal<UInt128> value, Span<char> destination,
        out int written, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        var text = Format(value, format, provider);
        if (text.Length > destination.Length)
        {
            written = 0;
            return false;
        }

        text.CopyTo(destination);
        written = text.Length;
        return true;
    }

    /// <summary>
    /// The same output as UTF-8. The digits and the exponent are ASCII, but a culture's
    /// signs and separators need not be, so the text is transcoded rather than narrowed.
    /// </summary>
    public static bool TryFormat(UnpackedDecimal<UInt128> value, Span<byte> utf8Destination,
        out int written, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        var text = Format(value, format, provider);
        if (Utf8.FromUtf16(text, utf8Destination, out _, out written) != OperationStatus.Done)
        {
            written = 0;
            return false;
        }

        return true;
    }

    private static double ToDouble(UnpackedDecimal<UInt128> value)
    {
        return double.TryParse(ToScientificString(value), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var number)
            ? number
            : double.NaN;
    }

    private static void Write(StringBuilder builder, UnpackedDecimal<UInt128> value, bool engineering,
        NumberFormatInfo numberFormat)
    {
        // An infinity carries its sign inside the symbol, which is why it is written before
        // the sign rather than after it.
        if (value.Kind == DecimalKind.Infinity)
        {
            builder.Append(value.IsNegative
                ? numberFormat.NegativeInfinitySymbol
                : numberFormat.PositiveInfinitySymbol);
            return;
        }

        if (value.IsNegative)
        {
            builder.Append(numberFormat.NegativeSign);
        }

        if (value.IsNaN)
        {
            // Neither a signaling NaN nor a diagnostic payload has a culture spelling, so
            // both keep the specification's: an "s" ahead of the symbol, the payload's
            // digits behind it.
            if (value.Kind == DecimalKind.SignalingNaN)
            {
                builder.Append('s');
            }

            builder.Append(numberFormat.NaNSymbol);
            if (value.Coefficient != UInt128.Zero)
            {
                builder.Append(value.Coefficient.ToString(CultureInfo.InvariantCulture));
            }

            return;
        }

        Span<char> digits = stackalloc char[40];
        var digitCount = WriteDigits(value.Coefficient, digits);
        var coefficient = digits[^digitCount..];
        var adjusted = value.Exponent + digitCount - 1;

        if (value.Exponent <= 0 && adjusted >= PlainNotationFloor)
        {
            WritePlain(builder, coefficient, value.Exponent, adjusted, numberFormat);
            return;
        }

        WriteExponential(builder, coefficient, adjusted, engineering, numberFormat);
    }

    private static void WritePlain(StringBuilder builder, ReadOnlySpan<char> coefficient,
        int exponent, int adjusted, NumberFormatInfo numberFormat)
    {
        if (exponent == 0)
        {
            builder.Append(coefficient);
            return;
        }

        if (adjusted >= 0)
        {
            var integerLength = adjusted + 1;
            builder.Append(coefficient[..integerLength]);
            builder.Append(numberFormat.NumberDecimalSeparator);
            builder.Append(coefficient[integerLength..]);
            return;
        }

        builder.Append('0');
        builder.Append(numberFormat.NumberDecimalSeparator);
        builder.Append('0', -adjusted - 1);
        builder.Append(coefficient);
    }

    private static void WriteExponential(StringBuilder builder, ReadOnlySpan<char> coefficient,
        int adjusted, bool engineering, NumberFormatInfo numberFormat)
    {
        var integerLength = 1;

        if (engineering)
        {
            var offset = adjusted % 3;
            if (offset < 0)
            {
                offset += 3;
            }

            if (coefficient.Length == 1 && coefficient[0] == '0')
            {
                // A zero has no digits to move left, so the exponent goes up to the next
                // multiple of three instead and the gap is filled after the point:
                // 0E+1 is written 0.00E+3.
                builder.Append('0');
                if (offset != 0)
                {
                    builder.Append(numberFormat.NumberDecimalSeparator);
                    builder.Append('0', 3 - offset);
                    adjusted += 3 - offset;
                }

                WriteExponent(builder, adjusted, numberFormat);
                return;
            }

            // Otherwise digits move left across the point until the exponent is a multiple
            // of three, leaving one, two, or three of them ahead of it.
            integerLength = offset + 1;
            adjusted -= offset;
        }

        if (coefficient.Length <= integerLength)
        {
            builder.Append(coefficient);
            builder.Append('0', integerLength - coefficient.Length);
        }
        else
        {
            builder.Append(coefficient[..integerLength]);
            builder.Append(numberFormat.NumberDecimalSeparator);
            builder.Append(coefficient[integerLength..]);
        }

        WriteExponent(builder, adjusted, numberFormat);
    }

    /// <summary>
    /// Writes the exponent, unless engineering notation has brought it to zero -- 10E+1
    /// is written 100, with no exponent part at all.
    /// </summary>
    private static void WriteExponent(StringBuilder builder, int adjusted,
        NumberFormatInfo numberFormat)
    {
        if (adjusted == 0)
        {
            return;
        }

        builder.Append('E');
        builder.Append(adjusted < 0
            ? numberFormat.NegativeSign
            : numberFormat.PositiveSign);
        builder.Append(Math.Abs((long)adjusted).ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Writes the coefficient's digits into the tail of <paramref name="destination"/> and
    /// returns how many there are. A zero coefficient is one digit.
    /// </summary>
    private static int WriteDigits(UInt128 coefficient, Span<char> destination)
    {
        var index = destination.Length;
        do
        {
            var next = coefficient / 10;
            destination[--index] = (char)('0' + (uint)(coefficient - (next * 10)));
            coefficient = next;
        }
        while (coefficient != UInt128.Zero);

        return destination.Length - index;
    }
}
