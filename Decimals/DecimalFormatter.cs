// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
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
[SkipLocalsInit]
internal static class DecimalFormatter
{
    private const int PlainNotationFloor = -6;

    /// <summary>
    /// The specification's own spelling, which is what the invariant culture spells too:
    /// a period for the point, a hyphen for the sign, and <c>Infinity</c> and <c>NaN</c>
    /// written out.
    /// </summary>
    private static NumberFormatInfo Invariant => CultureInfo.InvariantCulture.NumberFormat;

    /// <summary>The widest a <see cref="UInt128"/> coefficient is written.</summary>
    private const int MaximumDigits = 39;

    /// <summary>The "0." and the zeros behind it that plain notation can put in front.</summary>
    private const int PlainLeadingZeros = 7;

    /// <summary>"E", a sign, and the digits of an exponent no format exceeds.</summary>
    private const int ExponentLength = 7;

    /// <summary>Covers every culture whose signs and separator are of ordinary length.</summary>
    private const int StackBufferLength = 64;

    public static string ToScientificString(UnpackedDecimal<UInt128> value) =>
        ToScientificString(value, Invariant);

    public static string ToScientificString(UnpackedDecimal<UInt128> value,
        NumberFormatInfo numberFormat) =>
        Render(value, engineering: false, numberFormat);

    public static string ToEngineeringString(UnpackedDecimal<UInt128> value) =>
        ToEngineeringString(value, Invariant);

    public static string ToEngineeringString(UnpackedDecimal<UInt128> value,
        NumberFormatInfo numberFormat) =>
        Render(value, engineering: true, numberFormat);

    /// <summary>
    /// A finite value written into a stack buffer and handed over as a string, which costs
    /// one allocation. Going through a <see cref="StringBuilder"/> costs two -- the builder's
    /// own array and then the string copied out of it -- and that was most of what formatting
    /// spent its time on. The specials keep the builder: they are rare, and a culture's
    /// symbols are strings of its own choosing rather than anything this can size for.
    /// </summary>
    private static string Render(UnpackedDecimal<UInt128> value, bool engineering,
        NumberFormatInfo numberFormat)
    {
        if (value.Kind != DecimalKind.Finite)
        {
            var builder = new StringBuilder(48);
            WriteSpecial(builder, value, numberFormat);
            return builder.ToString();
        }

        var required = FiniteUpperBound(numberFormat);
        Span<char> buffer = required <= StackBufferLength
            ? stackalloc char[StackBufferLength]
            : new char[required];

        var written = WriteFinite(buffer, value, engineering, numberFormat);
        return new string(buffer[..written]);
    }

    /// <summary>
    /// The most room a finite value can need. Everything in it is either a fixed width or a
    /// string the culture supplies, so this is exact rather than a guess.
    /// </summary>
    private static int FiniteUpperBound(NumberFormatInfo numberFormat)
    {
        return numberFormat.NegativeSign.Length
            + numberFormat.PositiveSign.Length
            + numberFormat.NumberDecimalSeparator.Length
            + MaximumDigits + PlainLeadingZeros + ExponentLength;
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
        // The two notations this library writes itself go straight into the caller's span.
        // The whole point of a TryFormat is to keep a string out of it, and building one
        // here to copy out of was the last place formatting allocated for no reason.
        if (value.Kind == DecimalKind.Finite && TryReadNotation(format, out var engineering))
        {
            return TryWriteFinite(value, destination, out written, engineering, provider);
        }

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

    private static bool TryWriteFinite(UnpackedDecimal<UInt128> value, Span<char> destination,
        out int written, bool engineering, IFormatProvider? provider)
    {
        var numberFormat = NumberFormatInfo.GetInstance(provider);
        var required = FiniteUpperBound(numberFormat);

        // A span already wide enough for the longest this value could be is written into
        // directly. The scratch buffer below is only there to keep a shorter one from
        // being half filled before the length is known.
        if (destination.Length >= required)
        {
            written = WriteFinite(destination, value, engineering, numberFormat);
            return true;
        }

        Span<char> buffer = required <= StackBufferLength
            ? stackalloc char[StackBufferLength]
            : new char[required];

        var length = WriteFinite(buffer, value, engineering, numberFormat);
        if (length > destination.Length)
        {
            written = 0;
            return false;
        }

        buffer[..length].CopyTo(destination);
        written = length;
        return true;
    }

    /// <summary>
    /// Whether the format is one this writes itself, and which of the two notations it asks
    /// for. Everything else goes through <see cref="Format"/>.
    /// </summary>
    private static bool TryReadNotation(ReadOnlySpan<char> format, out bool engineering)
    {
        if (format.IsEmpty || format.Equals("G", StringComparison.OrdinalIgnoreCase))
        {
            engineering = false;
            return true;
        }

        if (format.Equals("E", StringComparison.OrdinalIgnoreCase))
        {
            engineering = true;
            return true;
        }

        engineering = false;
        return false;
    }

    /// <summary>
    /// The same output as UTF-8. The digits and the exponent are ASCII, but a culture's
    /// signs and separators need not be, so the text is transcoded rather than narrowed.
    /// </summary>
    public static bool TryFormat(UnpackedDecimal<UInt128> value, Span<byte> utf8Destination,
        out int written, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        // As with the UTF-16 overload: the notations this writes itself are transcoded
        // out of a stack buffer rather than out of a string built to be thrown away.
        if (value.Kind == DecimalKind.Finite && TryReadNotation(format, out var engineering))
        {
            var numberFormat = NumberFormatInfo.GetInstance(provider);
            var required = FiniteUpperBound(numberFormat);

            Span<char> buffer = required <= StackBufferLength
                ? stackalloc char[StackBufferLength]
                : new char[required];

            var length = WriteFinite(buffer, value, engineering, numberFormat);
            if (Utf8.FromUtf16(buffer[..length], utf8Destination, out _, out written)
                != OperationStatus.Done)
            {
                written = 0;
                return false;
            }

            return true;
        }

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

    private static void WriteSpecial(StringBuilder builder, UnpackedDecimal<UInt128> value,
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
    }

    /// <summary>
    /// A finite value, written straight into <paramref name="destination"/>. The caller sizes
    /// it from <see cref="FiniteUpperBound"/>, so nothing here has to test for room.
    /// </summary>
    private static int WriteFinite(Span<char> destination, UnpackedDecimal<UInt128> value,
        bool engineering, NumberFormatInfo numberFormat)
    {
        var index = 0;
        if (value.IsNegative)
        {
            Put(destination, ref index, numberFormat.NegativeSign);
        }

        // Thirty-nine digits is the widest a UInt128 goes, and the chunked writer pads each
        // chunk to nineteen, so it can put down that many exactly. The rest is headroom.
        Span<char> digits = stackalloc char[48];
        var digitCount = WriteDigits(value.Coefficient, digits);
        var coefficient = digits[^digitCount..];
        var adjusted = value.Exponent + digitCount - 1;

        if (value.Exponent <= 0 && adjusted >= PlainNotationFloor)
        {
            WritePlain(destination, ref index, coefficient, value.Exponent, adjusted, numberFormat);
            return index;
        }

        WriteExponential(destination, ref index, coefficient, adjusted, engineering, numberFormat);
        return index;
    }

    private static void WritePlain(Span<char> destination, ref int index,
        ReadOnlySpan<char> coefficient, int exponent, int adjusted, NumberFormatInfo numberFormat)
    {
        if (exponent == 0)
        {
            Put(destination, ref index, coefficient);
            return;
        }

        if (adjusted >= 0)
        {
            var integerLength = adjusted + 1;
            Put(destination, ref index, coefficient[..integerLength]);
            Put(destination, ref index, numberFormat.NumberDecimalSeparator);
            Put(destination, ref index, coefficient[integerLength..]);
            return;
        }

        destination[index++] = '0';
        Put(destination, ref index, numberFormat.NumberDecimalSeparator);
        Fill(destination, ref index, '0', -adjusted - 1);
        Put(destination, ref index, coefficient);
    }

    private static void WriteExponential(Span<char> destination, ref int index,
        ReadOnlySpan<char> coefficient, int adjusted, bool engineering,
        NumberFormatInfo numberFormat)
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
                destination[index++] = '0';
                if (offset != 0)
                {
                    Put(destination, ref index, numberFormat.NumberDecimalSeparator);
                    Fill(destination, ref index, '0', 3 - offset);
                    adjusted += 3 - offset;
                }

                WriteExponent(destination, ref index, adjusted, numberFormat);
                return;
            }

            // Otherwise digits move left across the point until the exponent is a multiple
            // of three, leaving one, two, or three of them ahead of it.
            integerLength = offset + 1;
            adjusted -= offset;
        }

        if (coefficient.Length <= integerLength)
        {
            Put(destination, ref index, coefficient);
            Fill(destination, ref index, '0', integerLength - coefficient.Length);
        }
        else
        {
            Put(destination, ref index, coefficient[..integerLength]);
            Put(destination, ref index, numberFormat.NumberDecimalSeparator);
            Put(destination, ref index, coefficient[integerLength..]);
        }

        WriteExponent(destination, ref index, adjusted, numberFormat);
    }

    /// <summary>
    /// Writes the exponent, unless engineering notation has brought it to zero -- 10E+1
    /// is written 100, with no exponent part at all.
    /// </summary>
    private static void WriteExponent(Span<char> destination, ref int index, int adjusted,
        NumberFormatInfo numberFormat)
    {
        if (adjusted == 0)
        {
            return;
        }

        destination[index++] = 'E';
        Put(destination, ref index, adjusted < 0
            ? numberFormat.NegativeSign
            : numberFormat.PositiveSign);

        // The digit writer fills from the tail, so the cursor starts at the end.
        Span<char> digits = stackalloc char[16];
        var cursor = digits.Length;
        WriteDigits((ulong)Math.Abs((long)adjusted), digits, ref cursor);
        Put(destination, ref index, digits[cursor..]);
    }

    private static void Put(Span<char> destination, ref int index, ReadOnlySpan<char> text)
    {
        text.CopyTo(destination[index..]);
        index += text.Length;
    }

    private static void Fill(Span<char> destination, ref int index, char character, int count)
    {
        destination.Slice(index, count).Fill(character);
        index += count;
    }

    /// <summary>
    /// Writes the coefficient's digits into the tail of <paramref name="destination"/> and
    /// returns how many there are. A zero coefficient is one digit.
    /// </summary>
    private static int WriteDigits(UInt128 coefficient, Span<char> destination)
    {
        var index = destination.Length;

        // Peel nineteen digits at a time until what is left fits a machine word. Written the
        // obvious way this costs a 128-bit division per digit, and there is no hardware for
        // one; this costs a 128-bit division per nineteen, and everything after that is
        // machine-word arithmetic whose divisions by a constant the JIT emits as multiplies.
        if (coefficient > ulong.MaxValue)
        {
            var reciprocal = PowersOfTen.Reciprocal(PowersOfTen.MaxUInt64Power);
            do
            {
                var next = reciprocal.Divide(coefficient);
                WritePaddedDigits((ulong)(coefficient - (next * reciprocal.PowerOfTen)),
                    PowersOfTen.MaxUInt64Power, destination, ref index);
                coefficient = next;
            }
            while (coefficient > ulong.MaxValue);
        }

        WriteDigits((ulong)coefficient, destination, ref index);
        return destination.Length - index;
    }

    /// <summary>
    /// A chunk written in full, leading zeros and all, because more digits follow it.
    /// </summary>
    private static void WritePaddedDigits(ulong value, int digits, Span<char> destination,
        ref int index)
    {
        var start = index;
        WriteDigits(value, destination, ref index);

        for (var written = start - index; written < digits; written++)
        {
            destination[--index] = '0';
        }
    }

    /// <summary>
    /// The digits of a machine word, two at a time. One division per pair rather than per
    /// digit, and the compiler turns each of them into a multiply.
    /// </summary>
    private static void WriteDigits(ulong value, Span<char> destination, ref int index)
    {
        while (value >= 100)
        {
            var next = value / 100;
            var pair = (uint)(value - (next * 100));
            destination[--index] = (char)('0' + (pair % 10));
            destination[--index] = (char)('0' + (pair / 10));
            value = next;
        }

        if (value >= 10)
        {
            destination[--index] = (char)('0' + (uint)(value % 10));
            destination[--index] = (char)('0' + (uint)(value / 10));
            return;
        }

        destination[--index] = (char)('0' + (uint)value);
    }
}
