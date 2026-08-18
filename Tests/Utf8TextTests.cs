// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text;

namespace Decimals.Tests;

/// <summary>
/// Formatting to and parsing from UTF-8. The output is the same text the char overloads
/// produce, transcoded; the input grammar is ASCII throughout, so a byte past that range
/// is not a number rather than a decoding problem.
/// </summary>
public class Utf8TextTests
{
    [Fact]
    public void FormatsTheSameTextAsTheCharOverloads()
    {
        var value = Decimal64.Parse("1234.5");

        Assert.Equal("1234.5", Format(value, ""));
        Assert.Equal("1234.50", Format(value, "F2"));
        Assert.Equal("1,234.50", Format(value, "N2"));

        // "E" is the engineering form here, whose exponent is a multiple of three.
        Assert.Equal("150E+18", Format(Decimal64.Parse("1.5E+20"), "E"));
    }

    [Fact]
    public void FormatsTheSpecials()
    {
        Assert.Equal("NaN", Format(Decimal64.NaN, ""));
        Assert.Equal("-Infinity", Format(Decimal64.NegativeInfinity, ""));
        Assert.Equal("-0", Format(Decimal64.NegativeZero, ""));
    }

    /// <summary>
    /// A culture reaches the UTF-8 output too, and its symbols need not be ASCII: German
    /// writes an infinity as a lone U+221E, which is three bytes here.
    /// </summary>
    [Fact]
    public void FormatsUnderACulture()
    {
        var german = CultureInfo.GetCultureInfo("de-DE");

        Assert.Equal("1234,5", Format(Decimal64.Parse("1234.5"), "", german));
        Assert.Equal("1.234,50", Format(Decimal64.Parse("1234.5"), "N2", german));
        Assert.Equal("-∞", Format(Decimal64.NegativeInfinity, "", german));
        Assert.Equal(Decimal64.NegativeInfinity, RoundTrip(Decimal64.NegativeInfinity, german));
        Assert.Equal(Decimal64.Parse("1234.5"), RoundTrip(Decimal64.Parse("1234.5"), german));
    }

    [Fact]
    public void ReportsFalseWhenTheBufferIsTooShort()
    {
        var value = Decimal64.Parse("1234.5");

        Span<byte> buffer = stackalloc byte[3];
        Assert.False(value.TryFormat(buffer, out var written, "", CultureInfo.InvariantCulture));
        Assert.Equal(0, written);
    }

    [Fact]
    public void Parses()
    {
        Assert.Equal(Decimal64.Parse("2.345"), Decimal64.Parse("2.345"u8));
        Assert.Equal(Decimal64.Parse("-1E+20"), Decimal64.Parse("-1E+20"u8));
        Assert.Equal(Decimal64.NegativeInfinity, Decimal64.Parse("-Infinity"u8));
        Assert.True(Decimal64.IsNaN(Decimal64.Parse("NaN"u8)));

        // The cohort survives, as it does through the char overloads.
        Assert.Equal("1.00", Decimal64.Parse("1.00"u8).ToString());
    }

    [Fact]
    public void RejectsWhatIsNotANumber()
    {
        Assert.False(Decimal64.TryParse("1..5"u8, out _));
        Assert.False(Decimal64.TryParse(" 1.5"u8, out _));
        Assert.False(Decimal64.TryParse(""u8, out _));

        // A middle dot is not a separator in any culture reached here.
        Assert.False(Decimal64.TryParse("1·5"u8, CultureInfo.InvariantCulture, out _));

        // Invalid UTF-8 is not a number either. This is a lone continuation byte.
        Assert.False(Decimal64.TryParse([0x31, 0x80, 0x35], CultureInfo.InvariantCulture, out _));

        Assert.Throws<FormatException>(() => Decimal64.Parse("nonsense"u8));
    }

    /// <summary>
    /// Input longer than the buffer widened on the stack, which takes the rented path.
    /// </summary>
    [Fact]
    public void ParsesInputLongerThanTheStackBuffer()
    {
        var digits = new string('9', 200);
        var text = Encoding.UTF8.GetBytes("0." + digits);

        Assert.Equal(Decimal64.Parse("1"), Decimal64.Parse(text));
        Assert.Equal(Decimal128.Parse("0." + digits), Decimal128.Parse(text));
    }

    [Fact]
    public void RoundTripsThroughTheInterfaces()
    {
        Assert.Equal(Decimal32.Parse("-0.0001"), RoundTrip(Decimal32.Parse("-0.0001")));
        Assert.Equal(Decimal64.Parse("1234.5"), RoundTrip(Decimal64.Parse("1234.5")));
        Assert.Equal(Decimal128.Parse("1.234567890123456789012345678901234"),
            RoundTrip(Decimal128.Parse("1.234567890123456789012345678901234")));
    }

    [Fact]
    public void TheOtherTwoWidths()
    {
        Assert.Equal("1.5", Format(Decimal32.Parse("1.5"), ""));
        Assert.Equal(Decimal32.Parse("1.5"), Decimal32.Parse("1.5"u8));
        Assert.True(Decimal32.TryParse("1.5"u8, null, out _));

        Assert.Equal("1.5", Format(Decimal128.Parse("1.5"), ""));
        Assert.Equal(Decimal128.Parse("1.5"), Decimal128.Parse("1.5"u8));
        Assert.True(Decimal128.TryParse("1.5"u8, null, out _));
    }

    private static string Format<T>(T value, string format)
        where T : IUtf8SpanFormattable => Format(value, format, CultureInfo.InvariantCulture);

    private static string Format<T>(T value, string format, IFormatProvider provider)
        where T : IUtf8SpanFormattable
    {
        Span<byte> buffer = stackalloc byte[64];
        Assert.True(value.TryFormat(buffer, out var written, format, provider));
        return Encoding.UTF8.GetString(buffer[..written]);
    }

    private static T RoundTrip<T>(T value)
        where T : IUtf8SpanFormattable, IUtf8SpanParsable<T> =>
        RoundTrip(value, CultureInfo.InvariantCulture);

    private static T RoundTrip<T>(T value, IFormatProvider provider)
        where T : IUtf8SpanFormattable, IUtf8SpanParsable<T>
    {
        Span<byte> buffer = stackalloc byte[64];
        Assert.True(value.TryFormat(buffer, out var written, "", provider));
        return T.Parse(buffer[..written], provider);
    }
}
