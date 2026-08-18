// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Tests;

/// <summary>
/// Parsing and formatting, with cases taken from the ddBase testcase group.
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
        var context = new DecimalContext(DecimalRounding.HalfUp);
        var value = Decimal64.FromString(text, ref context);

        Assert.Equal(expected, value.ToString());
        Assert.True(context.HasRaised(DecimalStatus.Inexact));
        Assert.True(context.HasRaised(DecimalStatus.Rounded));
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

        var context = new DecimalContext();
        var value = Decimal64.FromString(text, ref context);

        Assert.True(context.HasRaised(DecimalStatus.ConversionSyntax));
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
        (DecimalRounding Rounding, string Expected)[] cases =
        [
            (DecimalRounding.HalfEven, "Infinity"),
            (DecimalRounding.Up, "Infinity"),
            (DecimalRounding.Ceiling, "Infinity"),
            (DecimalRounding.Down, "9.999999999999999E+384"),
            (DecimalRounding.Floor, "9.999999999999999E+384"),
            (DecimalRounding.ZeroFiveUp, "9.999999999999999E+384")
        ];

        foreach (var (rounding, expected) in cases)
        {
            var context = new DecimalContext(rounding);
            var value = Decimal64.FromString("1E+385", ref context);

            Assert.Equal(expected, value.ToString());
            Assert.True(context.HasRaised(DecimalStatus.Overflow));
            Assert.True(context.HasRaised(DecimalStatus.Inexact));
        }
    }

    [Fact]
    public void SubnormalValuesRaiseSubnormal()
    {
        var context = new DecimalContext();
        var value = Decimal64.FromString("1E-398", ref context);

        Assert.Equal("1E-398", value.ToString());
        Assert.True(context.HasRaised(DecimalStatus.Subnormal));
        Assert.False(context.HasRaised(DecimalStatus.Inexact));
        Assert.True(Decimal64.IsSubnormal(value));
    }

    [Fact]
    public void UnderflowToZeroRaisesTheWholeChain()
    {
        var context = new DecimalContext();
        var value = Decimal64.FromString("1E-400", ref context);

        Assert.Equal("0E-398", value.ToString());
        Assert.True(context.HasRaised(DecimalStatus.Subnormal));
        Assert.True(context.HasRaised(DecimalStatus.Underflow));
        Assert.True(context.HasRaised(DecimalStatus.Inexact));
        Assert.True(context.HasRaised(DecimalStatus.Rounded));
    }

    [Fact]
    public void LargeExponentsFoldDownAndClamp()
    {
        // 1E+385 overflows, but 1E+384 fits once the coefficient carries the magnitude as
        // trailing zeros, because the format's largest exponent is +369.
        var context = new DecimalContext();
        var value = Decimal64.FromString("1E+384", ref context);

        Assert.Equal("1.000000000000000E+384", value.ToString());
        Assert.True(context.HasRaised(DecimalStatus.Clamped));
        Assert.False(context.HasRaised(DecimalStatus.Inexact));
    }
}
