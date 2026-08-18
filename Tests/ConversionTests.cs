// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Numerics;

namespace Decimals.Tests;

/// <summary>
/// Conversions between the three formats and to and from the other numeric types. The
/// question a conversion has to answer is what it does when the value does not fit, so most
/// of what is here is the cases that do not.
/// </summary>
public class ConversionTests
{
    /// <summary>
    /// Widening keeps the value and the quantum: the exponent survives, so a value that
    /// was written with trailing zeros still has them.
    /// </summary>
    [Theory]
    [InlineData("1.5")]
    [InlineData("-1.5")]
    [InlineData("0")]
    [InlineData("-0")]
    [InlineData("1.000")]
    [InlineData("9999999")]
    [InlineData("1E-95")]
    [InlineData("9.999999E+96")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void WideningFromDecimal32IsExact(string text)
    {
        var narrow = Decimal32.Parse(text);

        Decimal64 middle = narrow;
        Decimal128 wide = narrow;

        Assert.Equal(text, middle.ToString());
        Assert.Equal(text, wide.ToString());
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("-1.5")]
    [InlineData("1.000000000000000")]
    [InlineData("9999999999999999")]
    [InlineData("1E-383")]
    [InlineData("9.999999999999999E+384")]
    public void WideningFromDecimal64IsExact(string text)
    {
        Decimal128 wide = Decimal64.Parse(text);
        Assert.Equal(text, wide.ToString());
    }

    /// <summary>
    /// Narrowing rounds to the target's precision, half to even, the way any operation in
    /// that format would.
    /// </summary>
    [Theory]
    [InlineData("1.5", "1.5")]
    [InlineData("-1.5", "-1.5")]
    [InlineData("1.2345678", "1.234568")]
    [InlineData("1.2345675", "1.234568")]
    [InlineData("1.2345665", "1.234566")]
    [InlineData("0", "0")]
    [InlineData("-0", "-0")]
    public void NarrowingToDecimal32Rounds(string text, string expected)
    {
        Assert.Equal(expected, ((Decimal32)Decimal64.Parse(text)).ToString());
        Assert.Equal(expected, ((Decimal32)Decimal128.Parse(text)).ToString());
    }

    /// <summary>
    /// A value past the narrower format's range is not representable, so it comes back as
    /// an infinity or as a subnormal, exactly as an operation that overflowed would.
    /// </summary>
    [Fact]
    public void NarrowingOutOfRange()
    {
        Assert.Equal("Infinity", ((Decimal32)Decimal64.Parse("1E+300")).ToString());
        Assert.Equal("-Infinity", ((Decimal32)Decimal64.Parse("-1E+300")).ToString());

        // Below the smallest Decimal32 subnormal, which is 1E-101.
        Assert.Equal("0E-101", ((Decimal32)Decimal64.Parse("1E-300")).ToString());

        // In the subnormal range but representable there.
        Assert.Equal("1E-101", ((Decimal32)Decimal64.Parse("1E-101")).ToString());
    }

    [Fact]
    public void NaNsKeepTheirSignAndWhatFitsOfTheirPayload()
    {
        Assert.Equal("NaN123", ((Decimal64)Decimal32.Parse("NaN123")).ToString());
        Assert.Equal("-NaN123", ((Decimal64)Decimal32.Parse("-NaN123")).ToString());
        Assert.Equal("sNaN123", ((Decimal64)Decimal32.Parse("sNaN123")).ToString());

        // A payload too wide for the target keeps its low digits.
        Assert.Equal("NaN234567", ((Decimal32)Decimal64.Parse("NaN1234567")).ToString());
    }

    /// <summary>
    /// The generic-math entry points reach the same conversion. This is what was broken:
    /// CreateChecked ran through an integer and threw the fraction away.
    /// </summary>
    [Fact]
    public void CreateCheckedBetweenFormatsKeepsTheFraction()
    {
        Assert.Equal("1.5", Decimal128.CreateChecked(Decimal64.Parse("1.5")).ToString());
        Assert.Equal("1.5", Decimal64.CreateChecked(Decimal32.Parse("1.5")).ToString());
        Assert.Equal("1.5", Decimal32.CreateChecked(Decimal128.Parse("1.5")).ToString());
        Assert.Equal("1.5", Decimal64.CreateSaturating(Decimal128.Parse("1.5")).ToString());
        Assert.Equal("1.5", Decimal32.CreateTruncating(Decimal64.Parse("1.5")).ToString());
    }

    [Fact]
    public void FromTheIntegerTypes()
    {
        Decimal64 fromInt = 1234567890;
        Assert.Equal("1234567890", fromInt.ToString());

        Decimal32 fromShort = (short)-12345;
        Assert.Equal("-12345", fromShort.ToString());

        Decimal128 fromLong = 1234567890123456789L;
        Assert.Equal("1234567890123456789", fromLong.ToString());

        // Past the format's precision, so it rounds rather than refusing.
        Assert.Equal("1.234568E+9", ((Decimal32)1234567890).ToString());
        Assert.Equal("1.234567890123457E+18", ((Decimal64)1234567890123456789L).ToString());
    }

    [Fact]
    public void FromTheBinaryFloats()
    {
        // A tenth is what was written, not the binary fraction nearest it.
        Assert.Equal("0.1", ((Decimal64)0.1).ToString());
        Assert.Equal("0.1", ((Decimal64)0.1f).ToString());
        Assert.Equal("-2.5", ((Decimal64)(-2.5)).ToString());
        Assert.Equal("NaN", ((Decimal64)double.NaN).ToString());
        Assert.Equal("Infinity", ((Decimal64)double.PositiveInfinity).ToString());
        Assert.Equal("-Infinity", ((Decimal64)double.NegativeInfinity).ToString());
    }

    [Fact]
    public void ToTheIntegerTypesTruncatesTowardZero()
    {
        Assert.Equal(1, (int)Decimal64.Parse("1.9"));
        Assert.Equal(-1, (int)Decimal64.Parse("-1.9"));
        Assert.Equal(0, (int)Decimal64.Parse("0.9"));
        Assert.Equal(1234567890123456789L, (long)Decimal128.Parse("1234567890123456789"));
        Assert.Equal((byte)255, (byte)Decimal32.Parse("255.99"));
    }

    [Fact]
    public void ToAnIntegerThatCannotHoldTheValueThrows()
    {
        Assert.Throws<OverflowException>(() => (byte)Decimal64.Parse("256"));
        Assert.Throws<OverflowException>(() => (int)Decimal64.Parse("1E+30"));
        Assert.Throws<OverflowException>(() => (uint)Decimal64.Parse("-1"));
        Assert.Throws<OverflowException>(() => (int)Decimal64.NaN);
        Assert.Throws<OverflowException>(() => (int)Decimal64.PositiveInfinity);
    }

    [Fact]
    public void ToTheBinaryFloats()
    {
        Assert.Equal(0.1, (double)Decimal64.Parse("0.1"));
        Assert.Equal(-2.5, (double)Decimal64.Parse("-2.5"));
        Assert.Equal(2.5f, (float)Decimal64.Parse("2.5"));
        Assert.True(double.IsNaN((double)Decimal64.NaN));
        Assert.True(double.IsPositiveInfinity((double)Decimal64.PositiveInfinity));

        // Past double's range at the top of Decimal128.
        Assert.True(double.IsPositiveInfinity((double)Decimal128.Parse("1E+400")));
    }

    /// <summary>
    /// The other reading of a binary float: the value it actually holds, correctly rounded,
    /// which is IEEE 754's convertFormat. Thirty-four digits is wide enough to show where
    /// the two readings part company.
    /// </summary>
    [Fact]
    public void ExactValueReadsWhatTheBinaryActuallyHolds()
    {
        Assert.Equal("0.1000000000000000055511151231257827",
            Decimal128.FromBinary(0.1, BinaryConversion.ExactValue).ToString());
        Assert.Equal("0.1",
            Decimal128.FromBinary(0.1, BinaryConversion.ShortestRoundTrip).ToString());

        // A tenth as a float is a different binary value again, and says so. It is exactly
        // representable in thirty-four digits, so nothing is rounded and the shortest
        // quantum survives.
        Assert.Equal("0.100000001490116119384765625",
            Decimal128.FromBinary(0.1f, BinaryConversion.ExactValue).ToString());

        // Values a binary float holds exactly read the same either way.
        Assert.Equal("2.5", Decimal128.FromBinary(2.5, BinaryConversion.ExactValue).ToString());
        Assert.Equal("-0", Decimal128.FromBinary(-0.0, BinaryConversion.ExactValue).ToString());
        Assert.Equal("0", Decimal128.FromBinary(0.0, BinaryConversion.ExactValue).ToString());
    }

    /// <summary>
    /// At sixteen digits the binary error does not reach the last digit, so the two
    /// readings of a short literal come to the same value. They do not come to the same
    /// quantum: the exact reading had to round, and a rounded result is full width, so 0.1
    /// arrives as 0.1000000000000000. Equality here is numeric, which is the question worth
    /// asking.
    /// </summary>
    [Fact]
    public void TheTwoReadingsAgreeInValueWhereThePrecisionCannotShowTheDifference()
    {
        Assert.True(Decimal64.FromBinary(0.1, BinaryConversion.ExactValue) == (Decimal64)0.1);
        Assert.Equal("0.1000000000000000",
            Decimal64.FromBinary(0.1, BinaryConversion.ExactValue).ToString());

        // A value the binary format holds exactly needs no rounding, so it keeps the short
        // quantum as well as the value.
        Assert.Equal("2.5", Decimal64.FromBinary(2.5, BinaryConversion.ExactValue).ToString());
    }

    [Fact]
    public void ExactValueAtTheEndsOfTheBinaryRange()
    {
        // double.Epsilon, the smallest subnormal, is 4.94065645841247E-324 to the digits a
        // Decimal64 keeps -- inside its range, since it goes down to 1E-398.
        Assert.Equal("4.940656458412465E-324",
            Decimal64.FromBinary(double.Epsilon, BinaryConversion.ExactValue).ToString());

        // Decimal32 stops at 1E-101, so the same value underflows away entirely.
        Assert.Equal("0E-101",
            Decimal32.FromBinary(double.Epsilon, BinaryConversion.ExactValue).ToString());

        // And double.MaxValue is past what a Decimal32 holds, but not a Decimal64.
        Assert.Equal("Infinity",
            Decimal32.FromBinary(double.MaxValue, BinaryConversion.ExactValue).ToString());
        Assert.Equal("1.797693134862316E+308",
            Decimal64.FromBinary(double.MaxValue, BinaryConversion.ExactValue).ToString());
    }

    /// <summary>
    /// The exact reading is what it says: converting back to a double returns the value it
    /// started from, for any double at all.
    /// </summary>
    [Fact]
    public void ExactValueRoundTripsEveryDouble()
    {
        var random = new Random(20260817);
        for (var trial = 0; trial < 10000; trial++)
        {
            var bits = (ulong)random.NextInt64();
            var value = BitConverter.UInt64BitsToDouble(bits);
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                continue;
            }

            var exact = Decimal128.FromBinary(value, BinaryConversion.ExactValue);

            // A Decimal128 carries 34 digits, and 17 are enough to pin any double.
            Assert.Equal(value, (double)exact);
        }
    }

    /// <summary>
    /// Every Decimal32 value survives a round trip through the wider formats, which is the
    /// property that makes the widening conversions safe to leave implicit.
    /// </summary>
    [Fact]
    public void EveryDecimal32RoundTripsThroughTheWiderFormats()
    {
        var random = new Random(20260817);
        for (var trial = 0; trial < 20000; trial++)
        {
            var coefficient = random.Next(0, 10_000_000);
            var exponent = random.Next(-101, 91);
            var value = Decimal32.Parse(coefficient.ToString() + "E" + exponent.ToString());

            Decimal64 middle = value;
            Decimal128 wide = value;

            Assert.Equal(value.ToString(), ((Decimal32)middle).ToString());
            Assert.Equal(value.ToString(), ((Decimal32)wide).ToString());
            Assert.Equal(middle.ToString(), ((Decimal64)wide).ToString());
        }
    }

    /// <summary>
    /// The integer conversions agree with BigInteger, which is the only oracle that holds
    /// every value under discussion.
    /// </summary>
    [Fact]
    public void IntegerConversionsAgreeWithTheOracle()
    {
        var random = new Random(1618033);
        for (var trial = 0; trial < 20000; trial++)
        {
            var value = random.NextInt64(long.MinValue, long.MaxValue);
            Decimal128 wide = value;

            Assert.Equal(new BigInteger(value).ToString(), wide.ToString());
            Assert.Equal(value, (long)wide);
        }
    }
}
