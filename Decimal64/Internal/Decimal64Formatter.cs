// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Unicode;


namespace Decimals.Internal;

/// <summary>
/// The specification's <c>to-scientific-string</c> and <c>to-engineering-string</c>, and the
/// standard .NET format strings on top of them.
/// </summary>
/// <remarks>
/// <para>
/// Which notation is used is decided by the value, not by the caller: plain notation when
/// the exponent is at most zero and the adjusted exponent is at least -6, exponential
/// otherwise. So <c>5E-6</c> prints as <c>0.000005</c> and <c>5E-7</c> prints as itself.
/// Trailing zeros are part of the value -- <c>1.00</c> and <c>1.0</c> are different members
/// of the same cohort and print differently.
/// </para>
/// <para>
/// A finite value is planned as a <see cref="Decimal64TextLayout"/> and then written into its
/// destination in one pass: a string is allocated at its exact length and filled in place,
/// and a caller's span is written directly once its length is known to suffice. Nothing is
/// copied on the way. A culture whose signs and separator are the ordinary characters
/// takes the same path as the invariant one; only other symbols go through the general
/// writer.
/// </para>
/// </remarks>
[SkipLocalsInit]
internal static class Decimal64Formatter
{
    /// <summary>
    /// A buffer that holds any finite value under the invariant symbols, with the room the
    /// wide writer reaches past the text.
    /// </summary>
    public const int InvariantLength = Decimal64TextLayout.WideLength;

    /// <summary>A stack buffer that covers every culture whose symbols are of ordinary length.</summary>
    private const int StackBufferLength = 64;

    /// <summary>The digits a NaN payload can carry, and the buffer that holds any coefficient.</summary>
    private const int DigitBufferLength = 20;

    public static string ToScientificString(ulong bits)
    {
        return Render(bits, false);
    }

    public static string ToEngineeringString(ulong bits)
    {
        return Render(bits, true);
    }

    private static string Render(ulong bits, bool engineering)
    {
        if (Decimal64Encoding.IsSpecial(bits))
        {
            return RenderSpecial(bits, NumberFormatInfo.InvariantInfo);
        }

        var coefficient = Decimal64Encoding.Unpack(bits, out var exponent);
        var negative = Decimal64Encoding.IsNegative(bits);
        var layout = Decimal64TextLayout.Plan(coefficient, exponent, engineering);
        var length = layout.Length(negative, 1, 1, 1);

        // Filled in place through the delegate. Writing to the stack and copying into the
        // string was tried and cost seven nanoseconds more.
        return string.Create(length, (layout, negative), static (span, state) => state.layout.Write(span, state.negative));
    }

    private static string Render(ulong bits, bool engineering, NumberFormatInfo numberFormat)
    {
        if (HasPlainSymbols(numberFormat))
        {
            return Render(bits, engineering);
        }

        if (Decimal64Encoding.IsSpecial(bits))
        {
            return RenderSpecial(bits, numberFormat);
        }

        var coefficient = Decimal64Encoding.Unpack(bits, out var exponent);
        var negative = Decimal64Encoding.IsNegative(bits);
        var layout = Decimal64TextLayout.Plan(coefficient, exponent, engineering);
        var length = layout.Length(negative, numberFormat.NegativeSign.Length, numberFormat.PositiveSign.Length,
            numberFormat.NumberDecimalSeparator.Length);

        return string.Create(length, (layout, negative, numberFormat),
            static (span, state) => state.layout.Write(span, state.negative, state.numberFormat));
    }

    private static string RenderSpecial(ulong bits, NumberFormatInfo numberFormat)
    {
        var required = SpecialUpperBound(numberFormat);
        Span<char> buffer = required <= StackBufferLength
            ? stackalloc char[StackBufferLength]
            : new char[required];

        var written = WriteSpecial(buffer, bits, numberFormat);
        return new string(buffer[..written]);
    }

    /// <summary>
    /// The last read-only culture format found to write its symbols plainly. A culture's
    /// format is one instance for the life of the process, so remembering it turns the
    /// three comparisons below into one; a format that can still be changed is never
    /// remembered.
    /// </summary>
    private static NumberFormatInfo? PlainFormat;

    /// <summary>
    /// Whether a culture writes its signs and separator the way the specification does, in
    /// which case its text is the invariant text. Nearly every culture .NET knows does for
    /// the signs, and most do for the separator.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasPlainSymbols(NumberFormatInfo numberFormat)
    {
        // The invariant format is not named here: reading it is a call with a lazy
        // initialization behind it, and it is remembered like any other once seen.
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
    /// The most room a special value can need: the widest of the culture's symbols, a
    /// sign, an s, and a payload.
    /// </summary>
    private static int SpecialUpperBound(NumberFormatInfo numberFormat)
    {
        var symbols = Math.Max(numberFormat.NaNSymbol.Length + 1,
            Math.Max(numberFormat.PositiveInfinitySymbol.Length, numberFormat.NegativeInfinitySymbol.Length));

        return numberFormat.NegativeSign.Length + symbols + DigitBufferLength;
    }

    private static int WriteSpecial(Span<char> destination, ulong bits, NumberFormatInfo numberFormat)
    {
        var index = 0;

        // An infinity carries its sign inside the symbol, which is why it is written before
        // the sign rather than after it.
        if (Decimal64Encoding.IsInfinity(bits))
        {
            Put(destination, ref index, Decimal64Encoding.IsNegative(bits)
                ? numberFormat.NegativeInfinitySymbol
                : numberFormat.PositiveInfinitySymbol);

            return index;
        }

        if (Decimal64Encoding.IsNegative(bits))
        {
            Put(destination, ref index, numberFormat.NegativeSign);
        }

        // Neither a signaling NaN nor a diagnostic payload has a culture spelling, so both
        // keep the specification's: an "s" ahead of the symbol, the payload's digits behind.
        if (Decimal64Encoding.IsSignalingNaN(bits))
        {
            destination[index++] = 's';
        }

        Put(destination, ref index, numberFormat.NaNSymbol);

        var payload = Decimal64Encoding.Payload(bits);
        if (payload != 0)
        {
            var count = Decimal64Tables.CountDigits(payload);
            Decimal64Digits.Write(payload, count, ref destination[index]);
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
    /// Formats under a standard .NET format string. An empty or "G" format gives the
    /// specification's scientific form, which is what <c>ToString()</c> produces, and "E"
    /// the engineering form; "F" and "N" are done here on the digits, and the rest go to
    /// the framework's formatting of the nearest double.
    /// </summary>
    public static string Format(ulong bits, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        var numberFormat = NumberFormatInfo.GetInstance(provider);

        if (TryReadNotation(format, out var engineering))
        {
            return Render(bits, engineering, numberFormat);
        }

        if (Decimal64Encoding.IsSpecial(bits))
        {
            return Render(bits, false, numberFormat);
        }

        var specifier = char.ToUpperInvariant(format[0]);
        if ((specifier == 'F' || specifier == 'N') && TryReadPlaces(format[1..], out var places)
            && TryFixed(bits, places, specifier == 'N', numberFormat, out var fixedText))
        {
            return fixedText;
        }

        return ToDouble(bits).ToString(format.ToString(), provider);
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

    private static bool TryReadPlaces(ReadOnlySpan<char> digits, out int places)
    {
        if (digits.IsEmpty)
        {
            places = 2;
            return true;
        }

        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out places)
            && places <= DigitBufferLength;
    }

    /// <summary>
    /// Fixed-point output to a number of places, rounded half to even the way the
    /// framework's own fixed-point formatting rounds. A value whose integer part runs past
    /// a machine word is left to the double path.
    /// </summary>
    private static bool TryFixed(ulong bits, int places, bool grouped, NumberFormatInfo numberFormat, out string text)
    {
        text = string.Empty;

        var coefficient = Decimal64Encoding.Unpack(bits, out var exponent);
        var negative = Decimal64Encoding.IsNegative(bits);

        var lift = exponent + places;
        ulong scaled;
        if (lift < 0)
        {
            scaled = Decimal64Rounder.DropDigits(coefficient, -lift, Decimal64Residue.Exact, out var residue);
            if (residue != Decimal64Residue.Exact
                && Decimal64Rounder.ShouldIncrement(scaled, residue, negative, Decimal64Rounding.HalfEven))
            {
                scaled++;
            }
        }
        else
        {
            if (Decimal64Tables.CountDigits(coefficient) + lift > Decimal64Tables.MaxPower)
            {
                return false;
            }

            scaled = coefficient * Decimal64Tables.PowerOfTen(lift);
        }

        Span<char> digits = stackalloc char[DigitBufferLength];
        var count = Decimal64Digits.WriteTrailing(scaled, digits);
        var all = digits[^count..];

        var whole = count > places ? all[..(count - places)] : "0".AsSpan();
        var fraction = count > places ? all[(count - places)..] : all;

        var builder = new Decimal64TextBuilder(stackalloc char[StackBufferLength]);
        if (negative && scaled != 0)
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

        text = builder.ToString();
        return true;
    }

    private static void AppendWhole(ref Decimal64TextBuilder builder, ReadOnlySpan<char> whole, bool grouped,
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryFormat(ulong bits, Span<char> destination, out int written, ReadOnlySpan<char> format,
        IFormatProvider? provider)
    {
        // The common call: no format, a finite value. Everything else steps aside.
        if (format.IsEmpty && !Decimal64Encoding.IsSpecial(bits))
        {
            return TryWriteFinite(bits, destination, out written, false, NumberFormatInfo.GetInstance(provider));
        }

        return TryFormatSlow(bits, destination, out written, format, provider);
    }

    private static bool TryFormatSlow(ulong bits, Span<char> destination, out int written, ReadOnlySpan<char> format,
        IFormatProvider? provider)
    {
        if (TryReadNotation(format, out var engineering))
        {
            var numberFormat = NumberFormatInfo.GetInstance(provider);

            if (!Decimal64Encoding.IsSpecial(bits))
            {
                return TryWriteFinite(bits, destination, out written, engineering, numberFormat);
            }

            return TryWriteSpecial(bits, destination, out written, numberFormat);
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
    private static bool TryWriteFinite(ulong bits, Span<char> destination, out int written, bool engineering,
        NumberFormatInfo numberFormat)
    {
        if (!HasPlainSymbols(numberFormat))
        {
            return TryWriteFiniteCulture(bits, destination, out written, engineering, numberFormat);
        }

        // The layout stays in registers here because nothing in this method takes its
        // address; the culture path, which hands it to a writer by reference, plans its own.
        var coefficient = Decimal64Encoding.Unpack(bits, out var exponent);
        var negative = Decimal64Encoding.IsNegative(bits);
        var layout = Decimal64TextLayout.Plan(coefficient, exponent, engineering);

        // A destination with room to spare, which a stack buffer or a string builder's
        // is, takes the writer that never branches on the value's shape and never has to
        // know the text's length ahead of writing it.
        if (destination.Length >= Decimal64TextLayout.WideLength)
        {
            written = layout.WriteWide(ref MemoryMarshal.GetReference(destination), negative);
            return true;
        }

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
    private static bool TryWriteFiniteCulture(ulong bits, Span<char> destination, out int written, bool engineering,
        NumberFormatInfo numberFormat)
    {
        var coefficient = Decimal64Encoding.Unpack(bits, out var exponent);
        var negative = Decimal64Encoding.IsNegative(bits);
        var layout = Decimal64TextLayout.Plan(coefficient, exponent, engineering);

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

    private static bool TryWriteSpecial(ulong bits, Span<char> destination, out int written,
        NumberFormatInfo numberFormat)
    {
        var required = SpecialUpperBound(numberFormat);

        // A span already wide enough for the longest this value could be is written into
        // directly. The scratch buffer below is only there to keep a shorter one from
        // being half filled before the length is known.
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
    /// The same output as UTF-8. The digits and the exponent are ASCII, but a culture's
    /// signs and separators need not be, so the text is transcoded rather than narrowed.
    /// </summary>
    public static bool TryFormat(ulong bits, Span<byte> utf8Destination, out int written, ReadOnlySpan<char> format,
        IFormatProvider? provider)
    {
        if (TryReadNotation(format, out var engineering))
        {
            var numberFormat = NumberFormatInfo.GetInstance(provider);
            var required = Decimal64Encoding.IsSpecial(bits)
                ? SpecialUpperBound(numberFormat)
                : InvariantLength + numberFormat.NegativeSign.Length + numberFormat.PositiveSign.Length
                    + numberFormat.NumberDecimalSeparator.Length;

            Span<char> buffer = required <= StackBufferLength
                ? stackalloc char[StackBufferLength]
                : new char[required];

            int length;
            if (Decimal64Encoding.IsSpecial(bits))
            {
                length = WriteSpecial(buffer, bits, numberFormat);
            }
            else
            {
                TryWriteFinite(bits, buffer, out length, engineering, numberFormat);
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
    /// The nearest double, read from the value's own text so that the decimal value is what
    /// is read rather than whichever binary fraction sits nearest its coefficient.
    /// </summary>
    public static double ToDouble(ulong bits)
    {
        if (Decimal64Encoding.IsNaN(bits))
        {
            return double.NaN;
        }

        if (Decimal64Encoding.IsInfinity(bits))
        {
            return Decimal64Encoding.IsNegative(bits) ? double.NegativeInfinity : double.PositiveInfinity;
        }

        var coefficient = Decimal64Encoding.Unpack(bits, out var exponent);
        var negative = Decimal64Encoding.IsNegative(bits);
        var layout = Decimal64TextLayout.Plan(coefficient, exponent, false);

        Span<char> buffer = stackalloc char[InvariantLength];
        var written = layout.WriteWide(ref MemoryMarshal.GetReference(buffer), negative);

        if (double.TryParse(buffer[..written], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        // Out of double's range either way: the sign says which end.
        return negative ? double.NegativeInfinity : double.PositiveInfinity;
    }
}
