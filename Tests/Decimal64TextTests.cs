// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Globalization;


namespace Decimals.Tests;

/// <summary>
/// Parsing and formatting of <see cref="Decimal64"/>, with cases taken from the ddBase
/// testcase group, and the culture and format-string surface on top of them.
/// </summary>
public class Decimal64TextTests
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
    [InlineData("9.999999999999999E+384")]
    [InlineData("1E-398")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("NaN")]
    [InlineData("-NaN")]
    [InlineData("sNaN")]
    [InlineData("NaN123")]
    [InlineData("sNaN7234")]
    public void ScientificFormRoundTrips(string text)
    {
        Assert.Equal(text, Decimal64.Parse(text).ToString());
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
    public void ParsingNormalizes(string text, string expected)
    {
        Assert.Equal(expected, Decimal64.Parse(text).ToString());
    }

    // Rounded to 16 digits, with the dot in every position.
    [Theory]
    [InlineData(".1234567890123456123", "0.1234567890123456")]
    [InlineData("1.234567890123456123", "1.234567890123456")]
    [InlineData("12.34567890123456123", "12.34567890123456")]
    [InlineData("1234567.890123456123", "1234567.890123456")]
    [InlineData("1234567890123456123.", "1.234567890123456E+18")]
    public void ParsingRoundsToPrecision(string text, string expected)
    {
        var context = new Decimal64Context(Decimal64Rounding.HalfUp);
        var value = Decimal64.FromString(text, ref context);

        Assert.Equal(expected, value.ToString());
        Assert.True(context.HasRaised(Decimal64Status.Inexact));
        Assert.True(context.HasRaised(Decimal64Status.Rounded));
    }

    /// <summary>
    /// Digits past the nineteen a word holds become a sticky residue. That is only sound
    /// while the rounding discards digits above it, and here it always does: the digit that
    /// decides the rounding is the seventeenth, and the sticky sits below the nineteenth.
    /// </summary>
    [Theory]
    [InlineData("1234567890123456499999999999999999999", "1.234567890123456E+36")]
    [InlineData("1234567890123456500000000000000000001", "1.234567890123457E+36")]
    [InlineData("1234567890123456500000000000000000000", "1.234567890123456E+36")]
    [InlineData("1234567890123457500000000000000000000", "1.234567890123458E+36")]
    [InlineData("0.00000000000000000000000000000000000000000000000000000000000000000000000000001", "1E-77")]
    public void LongInputRoundsCorrectly(string text, string expected)
    {
        Assert.Equal(expected, Decimal64.Parse(text).ToString());
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
    [InlineData("NaN1234567890123456")]
    [InlineData("NaN123e+1")]
    [InlineData("NaN12.45")]
    [InlineData("NaN-12")]
    [InlineData("sNaN7234561234567890")]
    public void MalformedStringsRaiseConversionSyntax(string text)
    {
        Assert.False(Decimal64.TryParse(text, out _));

        var context = new Decimal64Context();
        var value = Decimal64.FromString(text, ref context);

        Assert.True(context.HasRaised(Decimal64Status.ConversionSyntax));
        Assert.True(Decimal64.IsNaN(value));
    }

    [Fact]
    public void ParseThrowsAndTryParseDoesNot()
    {
        Assert.Throws<FormatException>(() => Decimal64.Parse("1..2"));
        Assert.False(Decimal64.TryParse("1..2", out _));
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
    public void EngineeringFormMovesTheExponentToAMultipleOfThree(string text, string expected)
    {
        Assert.Equal(expected, Decimal64.Parse(text).ToEngineeringString());
    }

    [Fact]
    public void OverflowGivesInfinityOrTheLargestFiniteByRoundingMode()
    {
        (Decimal64Rounding Rounding, string Expected)[] cases =
        [
            (Decimal64Rounding.HalfEven, "Infinity"),
            (Decimal64Rounding.Up, "Infinity"),
            (Decimal64Rounding.Ceiling, "Infinity"),
            (Decimal64Rounding.Down, "9.999999999999999E+384"),
            (Decimal64Rounding.Floor, "9.999999999999999E+384"),
            (Decimal64Rounding.ZeroFiveUp, "9.999999999999999E+384")
        ];

        foreach (var (rounding, expected) in cases)
        {
            var context = new Decimal64Context(rounding);
            var value = Decimal64.FromString("1E+385", ref context);

            Assert.Equal(expected, value.ToString());
            Assert.True(context.HasRaised(Decimal64Status.Overflow));
            Assert.True(context.HasRaised(Decimal64Status.Inexact));
        }
    }

    [Fact]
    public void SubnormalValuesRaiseSubnormal()
    {
        var context = new Decimal64Context();
        var value = Decimal64.FromString("1E-398", ref context);

        Assert.Equal("1E-398", value.ToString());
        Assert.True(context.HasRaised(Decimal64Status.Subnormal));
        Assert.False(context.HasRaised(Decimal64Status.Inexact));
        Assert.True(Decimal64.IsSubnormal(value));
    }

    [Fact]
    public void UnderflowToZeroRaisesTheWholeChain()
    {
        var context = new Decimal64Context();
        var value = Decimal64.FromString("1E-400", ref context);

        Assert.Equal("0E-398", value.ToString());
        Assert.True(context.HasRaised(Decimal64Status.Subnormal));
        Assert.True(context.HasRaised(Decimal64Status.Underflow));
        Assert.True(context.HasRaised(Decimal64Status.Inexact));
        Assert.True(context.HasRaised(Decimal64Status.Rounded));
    }

    [Fact]
    public void LargeExponentsFoldDownAndClamp()
    {
        var context = new Decimal64Context();
        var value = Decimal64.FromString("1E+384", ref context);

        Assert.Equal("1.000000000000000E+384", value.ToString());
        Assert.True(context.HasRaised(Decimal64Status.Clamped));
        Assert.False(context.HasRaised(Decimal64Status.Inexact));
    }

    [Fact]
    public void TryFormatMatchesToString()
    {
        var values = new Decimal64TestValues(20);
        Span<char> buffer = stackalloc char[64];
        Span<byte> utf8 = stackalloc byte[64];

        for (var round = 0; round < 5000; round++)
        {
            var value = Decimal64.Parse(values.Next());
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

            Assert.Equal(value.ToString(), Decimal64.Parse(System.Text.Encoding.UTF8.GetBytes(expected)).ToString());
        }
    }

    [Theory]
    [InlineData("1234.5", "de-DE", "1234,5")]
    [InlineData("-1234.5", "de-DE", "-1234,5")]
    [InlineData("1E+20", "de-DE", "1E+20")]
    [InlineData("1E-20", "fr-FR", "1E-20")]
    [InlineData("Infinity", "de-DE", "∞")]
    [InlineData("-Infinity", "de-DE", "-∞")]
    [InlineData("NaN", "de-DE", "NaN")]
    public void CultureFormattingUsesTheCulturesSymbols(string text, string culture, string expected)
    {
        var provider = CultureInfo.GetCultureInfo(culture);
        var value = Decimal64.Parse(text);

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
        Assert.Equal(expected, Decimal64.Parse(text, provider).ToString());
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
    public void FixedPointFormatsRoundHalfToEven(string text, string format, string expected)
    {
        var value = Decimal64.Parse(text);
        Assert.Equal(expected, value.ToString(format, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void OtherStandardFormatsGoThroughDouble()
    {
        var value = Decimal64.Parse("1234.5678");
        Assert.Equal(1234.5678.ToString("C2", CultureInfo.InvariantCulture),
            value.ToString("C2", CultureInfo.InvariantCulture));

        Assert.Equal(1234.5678.ToString("P1", CultureInfo.InvariantCulture),
            value.ToString("P1", CultureInfo.InvariantCulture));
    }
}
