// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace Decimals.Tests;

/// <summary>
/// Parsing and formatting of <see cref="Decimal128"/>, with cases taken from the dqBase
/// testcase group, and the culture and format-string surface on top of them.
/// </summary>
public class Decimal128TextTests
{
    // toSci round trips: the string in is the string out.
    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("1.0")]
    [InlineData("1.00")]
    [InlineData("10")]
    [InlineData("1000")]
    [InlineData("10.0")]
    [InlineData("-0")]
    [InlineData("0.123")]
    [InlineData("0.012")]
    [InlineData("345678.543210")]
    [InlineData("1.234")]
    [InlineData("1234567890123456789012345678901234")]
    [InlineData("1.234567890123456789012345678901234")]
    [InlineData("0.000001234567890123456789012345678901234")]
    [InlineData("9.999999999999999999999999999999999E+6144")]
    [InlineData("1E-6176")]
    [InlineData("1.234567890123456789012345678901234E-6143")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("NaN")]
    [InlineData("-NaN")]
    [InlineData("sNaN")]
    [InlineData("NaN123")]
    [InlineData("sNaN7234")]
    [InlineData("NaN123456789012345678901234567890123")]
    public void ScientificFormRoundTrips(string text)
    {
        Assert.Equal(text, Decimal128.Parse(text).ToString());
    }

    [Theory]
    [InlineData("+345678.5432", "345678.5432")]
    [InlineData("+00345678.5432", "345678.5432")]
    [InlineData("-00345678.5432", "-345678.5432")]
    [InlineData("5E-6", "0.000005")]
    [InlineData("50E-7", "0.0000050")]
    [InlineData("5E-7", "5E-7")]
    [InlineData(".0", "0.0")]
    [InlineData("0.", "0")]
    [InlineData("-0.", "-0")]
    [InlineData("000000.", "0")]
    [InlineData("NaN0", "NaN")]
    [InlineData("NaN01", "NaN1")]
    [InlineData("NaN001234", "NaN1234")]
    [InlineData("sNaN0000", "sNaN")]
    [InlineData("sNaN007234", "sNaN7234")]
    [InlineData("inf", "Infinity")]
    [InlineData("Inf", "Infinity")]
    [InlineData("-infinity", "-Infinity")]
    [InlineData("INFINITY", "Infinity")]
    [InlineData("00000000000000000000000000000000000000001", "1")]
    [InlineData("0000000000000000000001234567890123456789012345678901234", "1234567890123456789012345678901234")]
    public void ParsingNormalizes(string text, string expected)
    {
        Assert.Equal(expected, Decimal128.Parse(text).ToString());
    }

    // Rounded to 34 digits, with the dot in every position.
    [Theory]
    [InlineData(".12345678901234567890123456789012345678", "0.1234567890123456789012345678901235")]
    [InlineData("1.2345678901234567890123456789012345678", "1.234567890123456789012345678901235")]
    [InlineData("12.345678901234567890123456789012345678", "12.34567890123456789012345678901235")]
    [InlineData("1234567.8901234567890123456789012345678", "1234567.890123456789012345678901235")]
    [InlineData("12345678901234567890123456789012345678.", "1.234567890123456789012345678901235E+37")]
    public void ParsingRoundsToPrecision(string text, string expected)
    {
        var context = new Decimal128Context(Decimal128Rounding.HalfUp);
        var value = Decimal128.FromString(text, ref context);

        Assert.Equal(expected, value.ToString());
        Assert.True(context.HasRaised(Decimal128Status.Inexact));
        Assert.True(context.HasRaised(Decimal128Status.Rounded));
    }

    /// <summary>
    /// Digits past the thirty-eight two words hold become a sticky residue. That is only
    /// sound while the rounding discards digits above it, and here it always does: the
    /// digit that decides the rounding is the thirty-fifth, and the sticky sits below the
    /// thirty-eighth.
    /// </summary>
    [Theory]
    [InlineData("1234567890123456789012345678901234499999999999999999999", "1.234567890123456789012345678901234E+54")]
    [InlineData("1234567890123456789012345678901234500000000000000000001", "1.234567890123456789012345678901235E+54")]
    [InlineData("1234567890123456789012345678901234500000000000000000000", "1.234567890123456789012345678901234E+54")]
    [InlineData("1234567890123456789012345678901235500000000000000000000", "1.234567890123456789012345678901236E+54")]
    [InlineData("0.00000000000000000000000000000000000000000000000000000000000000000000000000001", "1E-77")]
    [InlineData("123456789012345678901234567890123456789", "1.234567890123456789012345678901235E+38")]
    public void LongInputRoundsCorrectly(string text, string expected)
    {
        Assert.Equal(expected, Decimal128.Parse(text).ToString());
    }

    [Theory]
    [InlineData("1..2")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("++1")]
    [InlineData("--1")]
    [InlineData("-+1")]
    [InlineData("+-1")]
    [InlineData("12e")]
    [InlineData("12e++")]
    [InlineData("12f4")]
    [InlineData(" +1")]
    [InlineData("+ 1")]
    [InlineData("12 ")]
    [InlineData("x")]
    [InlineData("-1-")]
    [InlineData("3+")]
    [InlineData("")]
    [InlineData("1e-")]
    [InlineData("7e99999a")]
    [InlineData("e100")]
    [InlineData("123,65")]
    [InlineData("1.34.5")]
    [InlineData("01.35.")]
    [InlineData("111e1e+3")]
    [InlineData("1e1.0")]
    [InlineData("ten")]
    [InlineData("ONE")]
    [InlineData("1ee")]
    [InlineData("NaN1234567890123456789012345678901234")]
    [InlineData("NaN123e+1")]
    [InlineData("NaN12.45")]
    [InlineData("NaN-12")]
    [InlineData("sNaN1234567890123456789012345678901234")]
    public void MalformedStringsRaiseConversionSyntax(string text)
    {
        Assert.False(Decimal128.TryParse(text, out _));

        var context = new Decimal128Context();
        var value = Decimal128.FromString(text, ref context);

        Assert.True(context.HasRaised(Decimal128Status.ConversionSyntax));
        Assert.True(Decimal128.IsNaN(value));
    }

    [Fact]
    public void ParseThrowsAndTryParseDoesNot()
    {
        Assert.Throws<FormatException>(() => Decimal128.Parse("1..2"));
        Assert.False(Decimal128.TryParse("1..2", out _));
    }

    [Theory]
    [InlineData("1", "1")]
    [InlineData("1E+1", "10")]
    [InlineData("1E+2", "100")]
    [InlineData("1E+3", "1E+3")]
    [InlineData("1E+4", "10E+3")]
    [InlineData("1E+5", "100E+3")]
    [InlineData("1E+6", "1E+6")]
    [InlineData("1E-1", "0.1")]
    [InlineData("1E-7", "100E-9")]
    [InlineData("0.000005", "0.000005")]
    [InlineData("1.234E+7", "12.34E+6")]
    [InlineData("10e12", "10E+12")]
    [InlineData("10e11", "1.0E+12")]
    [InlineData("10e10", "100E+9")]
    [InlineData("10e1", "100")]
    [InlineData("10e0", "10")]
    [InlineData("10e-8", "100E-9")]
    [InlineData("7E11", "700E+9")]
    [InlineData("0e+1", "0.00E+3")]
    [InlineData("0.000000000", "0E-9")]
    [InlineData("0.00000000", "0.00E-6")]
    [InlineData("0.0000000", "0.0E-6")]
    [InlineData("0.000000", "0.000000")]
    [InlineData("-0.0000000", "-0.0E-6")]
    [InlineData("1234567890123456789012345678901234E+1", "12.34567890123456789012345678901234E+33")]
    public void EngineeringFormMovesTheExponentToAMultipleOfThree(string text, string expected)
    {
        Assert.Equal(expected, Decimal128.Parse(text).ToEngineeringString());
    }

    [Fact]
    public void OverflowGivesInfinityOrTheLargestFiniteByRoundingMode()
    {
        (Decimal128Rounding Rounding, string Expected)[] cases =
        [
            (Decimal128Rounding.HalfEven, "Infinity"),
            (Decimal128Rounding.Up, "Infinity"),
            (Decimal128Rounding.Ceiling, "Infinity"),
            (Decimal128Rounding.Down, "9.999999999999999999999999999999999E+6144"),
            (Decimal128Rounding.Floor, "9.999999999999999999999999999999999E+6144"),
            (Decimal128Rounding.ZeroFiveUp, "9.999999999999999999999999999999999E+6144")
        ];

        foreach (var (rounding, expected) in cases)
        {
            var context = new Decimal128Context(rounding);
            var value = Decimal128.FromString("1E+6145", ref context);

            Assert.Equal(expected, value.ToString());
            Assert.True(context.HasRaised(Decimal128Status.Overflow));
            Assert.True(context.HasRaised(Decimal128Status.Inexact));
        }
    }

    [Fact]
    public void SubnormalValuesRaiseSubnormal()
    {
        var context = new Decimal128Context();
        var value = Decimal128.FromString("1E-6176", ref context);

        Assert.Equal("1E-6176", value.ToString());
        Assert.True(context.HasRaised(Decimal128Status.Subnormal));
        Assert.False(context.HasRaised(Decimal128Status.Inexact));
        Assert.True(Decimal128.IsSubnormal(value));
    }

    [Fact]
    public void UnderflowToZeroRaisesTheWholeChain()
    {
        var context = new Decimal128Context();
        var value = Decimal128.FromString("1E-6178", ref context);

        Assert.Equal("0E-6176", value.ToString());
        Assert.True(context.HasRaised(Decimal128Status.Subnormal));
        Assert.True(context.HasRaised(Decimal128Status.Underflow));
        Assert.True(context.HasRaised(Decimal128Status.Inexact));
        Assert.True(context.HasRaised(Decimal128Status.Rounded));
    }

    [Fact]
    public void LargeExponentsFoldDownAndClamp()
    {
        var context = new Decimal128Context();
        var value = Decimal128.FromString("1E+6144", ref context);

        Assert.Equal("1.000000000000000000000000000000000E+6144", value.ToString());
        Assert.True(context.HasRaised(Decimal128Status.Clamped));
        Assert.False(context.HasRaised(Decimal128Status.Inexact));
    }

    [Fact]
    public void TryFormatMatchesToString()
    {
        var values = new Decimal128TestValues(20);
        Span<char> buffer = stackalloc char[64];
        Span<byte> utf8 = stackalloc byte[64];

        for (var round = 0; round < 5000; round++)
        {
            var value = Decimal128.Parse(values.Next());
            var expected = value.ToString();

            // A null provider means the current culture, as it does for every .NET number,
            // so the invariant one is named to get the specification's spellings.
            var invariant = CultureInfo.InvariantCulture;

            Assert.True(value.TryFormat(buffer, out var written, default, invariant));
            Assert.Equal(expected, new string(buffer[..written]));

            Assert.True(value.TryFormat(buffer, out written, "E", invariant));
            Assert.Equal(value.ToEngineeringString(), new string(buffer[..written]));

            Assert.True(value.TryFormat(utf8, out var bytes, default, invariant));
            Assert.Equal(expected, System.Text.Encoding.UTF8.GetString(utf8[..bytes]));

            Assert.False(value.TryFormat(buffer[..(expected.Length - 1)], out written, default, invariant));
            Assert.Equal(0, written);

            Assert.Equal(value.ToString(), Decimal128.Parse(System.Text.Encoding.UTF8.GetBytes(expected)).ToString());
        }
    }

    [Theory]
    [InlineData("1234.5", "de-DE", "1234,5")]
    [InlineData("-1234.5", "de-DE", "-1234,5")]
    [InlineData("1E+20", "de-DE", "1E+20")]
    [InlineData("1E-20", "fr-FR", "1E-20")]
    [InlineData("1234567890123456789012345678901234E-30", "de-DE", "1234,567890123456789012345678901234")]
    [InlineData("Infinity", "de-DE", "∞")]
    [InlineData("-Infinity", "de-DE", "-∞")]
    [InlineData("NaN", "de-DE", "NaN")]
    public void CultureFormattingUsesTheCulturesSymbols(string text, string culture, string expected)
    {
        var provider = CultureInfo.GetCultureInfo(culture);
        var value = Decimal128.Parse(text);

        Assert.Equal(expected, value.ToString("G", provider));
        Assert.Equal(expected, value.ToString(null, provider));
    }

    [Theory]
    [InlineData("1.234,5", "de-DE", "1234.5")]
    [InlineData("-1.234,5", "de-DE", "-1234.5")]
    [InlineData("1 234,5", "fr-FR", "1234.5")]
    [InlineData("  12,5  ", "de-DE", "12.5")]
    [InlineData("1,5E+3", "de-DE", "1.5E+3")]
    [InlineData("Infinity", "de-DE", "Infinity")]
    [InlineData("sNaN12", "de-DE", "sNaN12")]
    public void CultureParsingReadsTheCulturesSymbols(string text, string culture, string expected)
    {
        var provider = CultureInfo.GetCultureInfo(culture);
        Assert.Equal(expected, Decimal128.Parse(text, provider).ToString());
    }

    [Theory]
    [InlineData("1234.5678", "F2", "1234.57")]
    [InlineData("1234.5", "F0", "1234")]
    [InlineData("1234.5678", "N2", "1,234.57")]
    [InlineData("-0.005", "F2", "0.00")]
    [InlineData("-0.015", "F2", "-0.02")]
    [InlineData("0.005", "F2", "0.00")]
    [InlineData("1E+3", "F1", "1000.0")]
    [InlineData("12345678.9", "N0", "12,345,679")]
    [InlineData("1234567890123456789012345678901234", "N0", "1,234,567,890,123,456,789,012,345,678,901,234")]
    [InlineData("12345678901234567890123456789012.34", "F3", "12345678901234567890123456789012.340")]
    public void FixedPointFormatsRoundHalfToEven(string text, string format, string expected)
    {
        var value = Decimal128.Parse(text);
        Assert.Equal(expected, value.ToString(format, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void OtherStandardFormatsGoThroughDouble()
    {
        var value = Decimal128.Parse("1234.5678");
        Assert.Equal(1234.5678.ToString("C2", CultureInfo.InvariantCulture),
            value.ToString("C2", CultureInfo.InvariantCulture));

        Assert.Equal(1234.5678.ToString("P1", CultureInfo.InvariantCulture),
            value.ToString("P1", CultureInfo.InvariantCulture));
    }
}
