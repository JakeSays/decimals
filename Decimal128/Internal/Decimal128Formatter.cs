// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Unicode;

namespace Decimals.Internal;

/// <summary>
/// The specification's <c>to-scientific-string</c> and <c>to-engineering-string</c>
/// conversions, and the standard .NET format strings built on them.
/// </summary>
/// <remarks>
/// <para>
/// The value chooses the notation, not the caller. Plain notation is used when the
/// exponent is at most zero and the adjusted exponent is at least -6. Otherwise
/// exponential notation is used. So <c>5E-6</c> prints as <c>0.000005</c>, and <c>5E-7</c>
/// prints as <c>5E-7</c>. Trailing zeros are part of the value: <c>1.00</c> and <c>1.0</c>
/// are different members of the same cohort and print differently.
/// </para>
/// <para>
/// A finite value is planned as a <see cref="Decimal128TextLayout"/> and then written to
/// its destination in one pass. A string is allocated at its exact length and filled in
/// place. A caller's span is written directly once it is known to be long enough. Nothing
/// is copied. A culture whose signs and decimal separator are the ASCII characters takes
/// the same path as the invariant culture. Only other symbols go through the general
/// writer.
/// </para>
/// </remarks>
[SkipLocalsInit]
internal static class Decimal128Formatter
{
    /// <summary>A buffer length that holds any finite value with the invariant symbols.</summary>
    public const int InvariantLength = Decimal128TextLayout.MaxLength;

    /// <summary>A stack buffer length that covers every culture whose symbols have normal lengths.</summary>
    private const int StackBufferLength = 96;

    /// <summary>
    /// A buffer length that holds the digits of any coefficient or NaN payload. It is also
    /// the largest number of decimal places the F and N formats accept.
    /// </summary>
    private const int DigitBufferLength = 40;

    /// <summary>The specification's to-scientific-string conversion.</summary>
    /// <param name="bits">The encoded value.</param>
    /// <returns>The value in scientific notation.</returns>
    public static string ToScientificString(Decimal128Integer bits)
    {
        return Render(bits, false);
    }

    /// <summary>The specification's to-engineering-string conversion.</summary>
    /// <param name="bits">The encoded value.</param>
    /// <returns>The value in engineering notation.</returns>
    public static string ToEngineeringString(Decimal128Integer bits)
    {
        return Render(bits, true);
    }

    private static string Render(Decimal128Integer bits, bool engineering)
    {
        if (Decimal128Encoding.IsSpecial(bits))
        {
            return RenderSpecial(bits, NumberFormatInfo.InvariantInfo);
        }

        var coefficient = Decimal128Encoding.Unpack(bits, out var exponent);
        var negative = Decimal128Encoding.IsNegative(bits);
        var layout = Decimal128TextLayout.Plan(coefficient, exponent, engineering);
        var length = layout.Length(negative, 1, 1, 1);

        return string.Create(length, (layout, negative), static (span, state) => state.layout.Write(span, state.negative));
    }

    private static string Render(Decimal128Integer bits, bool engineering, NumberFormatInfo numberFormat)
    {
        if (HasPlainSymbols(numberFormat))
        {
            return Render(bits, engineering);
        }

        if (Decimal128Encoding.IsSpecial(bits))
        {
            return RenderSpecial(bits, numberFormat);
        }

        var coefficient = Decimal128Encoding.Unpack(bits, out var exponent);
        var negative = Decimal128Encoding.IsNegative(bits);
        var layout = Decimal128TextLayout.Plan(coefficient, exponent, engineering);
        var length = layout.Length(negative, numberFormat.NegativeSign.Length, numberFormat.PositiveSign.Length,
            numberFormat.NumberDecimalSeparator.Length);

        return string.Create(length, (layout, negative, numberFormat),
            static (span, state) => state.layout.Write(span, state.negative, state.numberFormat));
    }

    private static string RenderSpecial(Decimal128Integer bits, NumberFormatInfo numberFormat)
    {
        var required = SpecialUpperBound(numberFormat);
        Span<char> buffer = required <= StackBufferLength
            ? stackalloc char[StackBufferLength]
            : new char[required];

        var written = WriteSpecial(buffer, bits, numberFormat);
        return new string(buffer[..written]);
    }

    /// <summary>
    /// The last read-only culture format found to use the plain symbols. A culture's format
    /// is a single instance for the life of the process, so caching it replaces the three
    /// comparisons below with one. A format that can still be changed is never cached.
    /// </summary>
    private static NumberFormatInfo? PlainFormat;

    /// <summary>
    /// Whether a culture writes its signs and decimal separator the same way as the
    /// specification. If it does, its text is the same as the invariant text. Nearly every
    /// culture in .NET does for the signs, and most do for the separator.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasPlainSymbols(NumberFormatInfo numberFormat)
    {
        // The invariant format is not checked by name here. Reading it is a call with lazy
        // initialization behind it, and it is cached like any other format once seen.
        return ReferenceEquals(numberFormat, PlainFormat) || CheckPlainSymbols(numberFormat);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool CheckPlainSymbols(NumberFormatInfo numberFormat)
    {
        var plain = IsSingle(numberFormat.NegativeSign, '-')
            && IsSingle(numberFormat.PositiveSign, '+')
            && IsSingle(numberFormat.NumberDecimalSeparator, '.');

        if (plain && numberFormat.IsReadOnly)
        {
            PlainFormat = numberFormat;
        }

        return plain;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsSingle(string symbol, char expected)
    {
        return symbol.Length == 1 && symbol[0] == expected;
    }

    /// <summary>
    /// The largest length a special value can need: a sign, the longest of the culture's
    /// symbols, an s, and a payload.
    /// </summary>
    private static int SpecialUpperBound(NumberFormatInfo numberFormat)
    {
        var symbols = Math.Max(numberFormat.NaNSymbol.Length + 1,
            Math.Max(numberFormat.PositiveInfinitySymbol.Length, numberFormat.NegativeInfinitySymbol.Length));

        return numberFormat.NegativeSign.Length + symbols + DigitBufferLength;
    }

    private static int WriteSpecial(Span<char> destination, Decimal128Integer bits, NumberFormatInfo numberFormat)
    {
        var index = 0;

        // An infinity symbol includes its sign, so infinity is handled before the sign is
        // written.
        if (Decimal128Encoding.IsInfinity(bits))
        {
            Put(destination, ref index, Decimal128Encoding.IsNegative(bits)
                ? numberFormat.NegativeInfinitySymbol
                : numberFormat.PositiveInfinitySymbol);

            return index;
        }

        if (Decimal128Encoding.IsNegative(bits))
        {
            Put(destination, ref index, numberFormat.NegativeSign);
        }

        // A signaling NaN and a diagnostic payload have no culture spelling, so both use the
        // specification's: an "s" before the symbol and the payload digits after it.
        if (Decimal128Encoding.IsSignalingNaN(bits))
        {
            destination[index++] = 's';
        }

        Put(destination, ref index, numberFormat.NaNSymbol);

        var payload = Decimal128Encoding.Payload(bits);
        if (!payload.IsZero)
        {
            var count = Decimal128Tables.CountDigits(payload);
            Decimal128Digits.Write(payload, count, ref destination[index]);
            index += count;
        }

        return index;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Put(Span<char> destination, ref int index, ReadOnlySpan<char> text)
    {
        text.CopyTo(destination[index..]);
        index += text.Length;
    }

    /// <summary>
    /// Formats with a standard .NET format string. An empty format or "G" gives the
    /// specification's scientific form, which is what <c>ToString()</c> returns. "E" gives
    /// the engineering form. "F" and "N" are formatted here from the digits. Other formats
    /// use the framework's formatting of the nearest double.
    /// </summary>
    /// <param name="bits">The encoded value.</param>
    /// <param name="format">The format string.</param>
    /// <param name="provider">The culture's symbols, or null for the current culture.</param>
    /// <returns>The formatted value.</returns>
    public static string Format(Decimal128Integer bits, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        var numberFormat = NumberFormatInfo.GetInstance(provider);

        if (ReadNotation(format, out var engineering))
        {
            return Render(bits, engineering, numberFormat);
        }

        if (Decimal128Encoding.IsSpecial(bits))
        {
            return Render(bits, false, numberFormat);
        }

        var specifier = char.ToUpperInvariant(format[0]);
        if ((specifier == 'F' || specifier == 'N') && ReadPlaces(format[1..]) is { } places
            && FormatFixed(bits, places, specifier == 'N', numberFormat) is { } fixedText)
        {
            return fixedText;
        }

        return ToDouble(bits).ToString(format.ToString(), provider);
    }

    /// <summary>
    /// Whether this class writes the format itself, and which notation the format requests.
    /// Other formats go through <see cref="Format"/>.
    /// </summary>
    private static bool ReadNotation(ReadOnlySpan<char> format, out bool engineering)
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
    /// The number of decimal places after an F or N specifier. No digits means two places.
    /// The result is null if the digits are not a count this class accepts.
    /// </summary>
    private static int? ReadPlaces(ReadOnlySpan<char> digits)
    {
        if (digits.IsEmpty)
        {
            return 2;
        }

        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var places)
            || places > DigitBufferLength)
        {
            return null;
        }

        return places;
    }

    /// <summary>
    /// Fixed-point output to a number of decimal places, rounded half to even like the
    /// framework's fixed-point formatting. A value that needs more than 38 digits at that
    /// scale gives null, and the caller uses the double path.
    /// </summary>
    private static string? FormatFixed(Decimal128Integer bits, int places, bool grouped, NumberFormatInfo numberFormat)
    {
        var coefficient = Decimal128Encoding.Unpack(bits, out var exponent);
        var negative = Decimal128Encoding.IsNegative(bits);

        var lift = exponent + places;
        Decimal128Integer scaled;
        if (lift < 0)
        {
            scaled = Decimal128Rounder.DropDigits(coefficient, -lift, Decimal128Residue.Exact, out var residue);
            if (residue != Decimal128Residue.Exact
                && Decimal128Rounder.ShouldIncrement(scaled, residue, negative, Decimal128Rounding.HalfEven))
            {
                scaled += 1;
            }
        }
        else
        {
            if (Decimal128Tables.CountDigits(coefficient) + lift > Decimal128Tables.MaxWidePower)
            {
                return null;
            }

            scaled = Decimal128Tables.Scale(coefficient, lift);
        }

        Span<char> digits = stackalloc char[DigitBufferLength];
        var count = Decimal128Digits.WriteTrailing(scaled, digits);
        var all = digits[^count..];

        var whole = count > places ? all[..(count - places)] : "0".AsSpan();
        var fraction = count > places ? all[(count - places)..] : all;

        var builder = new Decimal128TextBuilder(stackalloc char[StackBufferLength]);
        if (negative && !scaled.IsZero)
        {
            builder.Append(numberFormat.NegativeSign);
        }

        AppendWhole(ref builder, whole, grouped, numberFormat);

        if (places > 0)
        {
            builder.Append(numberFormat.NumberDecimalSeparator);
            builder.Append('0', places - fraction.Length);
            builder.Append(fraction);
        }

        return builder.ToString();
    }

    private static void AppendWhole(ref Decimal128TextBuilder builder, ReadOnlySpan<char> whole, bool grouped,
        NumberFormatInfo numberFormat)
    {
        var size = numberFormat.NumberGroupSizes.Length > 0 ? numberFormat.NumberGroupSizes[0] : 3;
        if (!grouped || size <= 0)
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

    /// <summary>Formats the value into a span of characters, with the same formats as <see cref="Format"/>.</summary>
    /// <param name="bits">The encoded value.</param>
    /// <param name="destination">Receives the text.</param>
    /// <param name="written">Receives the number of characters written, or zero if the destination is too short.</param>
    /// <param name="format">The format string. Empty gives scientific notation.</param>
    /// <param name="provider">The culture's symbols, or null for the current culture.</param>
    /// <returns>True if the text fit in <paramref name="destination"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryFormat(Decimal128Integer bits, Span<char> destination, out int written,
        ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        // The common call: no format and a finite value. Everything else takes the slow path.
        if (format.IsEmpty && !Decimal128Encoding.IsSpecial(bits))
        {
            return WriteFinite(bits, destination, out written, false, NumberFormatInfo.GetInstance(provider));
        }

        return FormatSlow(bits, destination, out written, format, provider);
    }

    private static bool FormatSlow(Decimal128Integer bits, Span<char> destination, out int written,
        ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        if (ReadNotation(format, out var engineering))
        {
            var numberFormat = NumberFormatInfo.GetInstance(provider);

            if (!Decimal128Encoding.IsSpecial(bits))
            {
                return WriteFinite(bits, destination, out written, engineering, numberFormat);
            }

            return WriteSpecialIfFits(bits, destination, out written, numberFormat);
        }

        var text = Format(bits, format, provider);
        if (text.Length > destination.Length)
        {
            written = 0;
            return false;
        }

        text.CopyTo(destination);
        written = text.Length;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool WriteFinite(Decimal128Integer bits, Span<char> destination, out int written,
        bool engineering, NumberFormatInfo numberFormat)
    {
        if (!HasPlainSymbols(numberFormat))
        {
            return WriteFiniteCulture(bits, destination, out written, engineering, numberFormat);
        }

        var coefficient = Decimal128Encoding.Unpack(bits, out var exponent);
        var negative = Decimal128Encoding.IsNegative(bits);
        var layout = Decimal128TextLayout.Plan(coefficient, exponent, engineering);

        var length = layout.Length(negative, 1, 1, 1);
        if (length > destination.Length)
        {
            written = 0;
            return false;
        }

        written = layout.Write(destination, negative);
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool WriteFiniteCulture(Decimal128Integer bits, Span<char> destination, out int written,
        bool engineering, NumberFormatInfo numberFormat)
    {
        var coefficient = Decimal128Encoding.Unpack(bits, out var exponent);
        var negative = Decimal128Encoding.IsNegative(bits);
        var layout = Decimal128TextLayout.Plan(coefficient, exponent, engineering);

        var required = layout.Length(negative, numberFormat.NegativeSign.Length, numberFormat.PositiveSign.Length,
            numberFormat.NumberDecimalSeparator.Length);

        if (required > destination.Length)
        {
            written = 0;
            return false;
        }

        written = layout.Write(destination, negative, numberFormat);
        return true;
    }

    private static bool WriteSpecialIfFits(Decimal128Integer bits, Span<char> destination, out int written,
        NumberFormatInfo numberFormat)
    {
        var required = SpecialUpperBound(numberFormat);

        // A span long enough for the longest possible text is written directly. The scratch
        // buffer below keeps a shorter span from being partly filled before the length is
        // known.
        if (destination.Length >= required)
        {
            written = WriteSpecial(destination, bits, numberFormat);
            return true;
        }

        Span<char> buffer = required <= StackBufferLength
            ? stackalloc char[StackBufferLength]
            : new char[required];

        var length = WriteSpecial(buffer, bits, numberFormat);
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
    /// Writes the same output as UTF-8. The digits and the exponent are ASCII, but a
    /// culture's signs and separators can be non-ASCII, so the text is transcoded instead
    /// of narrowed.
    /// </summary>
    /// <param name="bits">The encoded value.</param>
    /// <param name="utf8Destination">Receives the UTF-8 text.</param>
    /// <param name="written">Receives the number of bytes written, or zero if the destination is too short.</param>
    /// <param name="format">The format string. Empty gives scientific notation.</param>
    /// <param name="provider">The culture's symbols, or null for the current culture.</param>
    /// <returns>True if the text fit in <paramref name="utf8Destination"/>.</returns>
    public static bool TryFormat(Decimal128Integer bits, Span<byte> utf8Destination, out int written,
        ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        if (ReadNotation(format, out var engineering))
        {
            var numberFormat = NumberFormatInfo.GetInstance(provider);
            var required = Decimal128Encoding.IsSpecial(bits)
                ? SpecialUpperBound(numberFormat)
                : InvariantLength + numberFormat.NegativeSign.Length + numberFormat.PositiveSign.Length
                    + numberFormat.NumberDecimalSeparator.Length;

            Span<char> buffer = required <= StackBufferLength
                ? stackalloc char[StackBufferLength]
                : new char[required];

            int length;
            if (Decimal128Encoding.IsSpecial(bits))
            {
                length = WriteSpecial(buffer, bits, numberFormat);
            }
            else
            {
                WriteFinite(bits, buffer, out length, engineering, numberFormat);
            }

            if (Utf8.FromUtf16(buffer[..length], utf8Destination, out _, out written) != OperationStatus.Done)
            {
                written = 0;
                return false;
            }

            return true;
        }

        var text = Format(bits, format, provider);
        if (Utf8.FromUtf16(text, utf8Destination, out _, out written) != OperationStatus.Done)
        {
            written = 0;
            return false;
        }

        return true;
    }

    /// <summary>
    /// The nearest double, computed by parsing the value's text. The decimal value is
    /// rounded to binary only once.
    /// </summary>
    /// <param name="bits">The encoded value.</param>
    /// <returns>The nearest double. A NaN gives NaN, and a value out of range gives an infinity.</returns>
    public static double ToDouble(Decimal128Integer bits)
    {
        if (Decimal128Encoding.IsNaN(bits))
        {
            return double.NaN;
        }

        if (Decimal128Encoding.IsInfinity(bits))
        {
            return Decimal128Encoding.IsNegative(bits) ? double.NegativeInfinity : double.PositiveInfinity;
        }

        var coefficient = Decimal128Encoding.Unpack(bits, out var exponent);
        var negative = Decimal128Encoding.IsNegative(bits);
        var layout = Decimal128TextLayout.Plan(coefficient, exponent, false);

        Span<char> buffer = stackalloc char[InvariantLength];
        var written = layout.Write(buffer, negative);

        if (double.TryParse(buffer[..written], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        // The value is outside double's range. The sign chooses the infinity.
        return negative ? double.NegativeInfinity : double.PositiveInfinity;
    }
}
