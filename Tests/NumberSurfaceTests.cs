// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Numerics;

namespace Decimals.Tests;

/// <summary>
/// The members a .NET caller reaches for by name: the rounding family, selection, sign and
/// clamp, and the four IEEE 754 operations spelled the way the built-in types spell them.
/// Every one of these is called as a static here, which is the point of the tests -- the
/// interfaces supply defaults for several, but a default is only reachable through a
/// constraint or a cast.
/// </summary>
public class NumberSurfaceTests
{
    [Theory]
    [InlineData("2.7", "3", "2", "2", "3")]
    [InlineData("-2.7", "-2", "-3", "-2", "-3")]
    [InlineData("2.5", "3", "2", "2", "2")]
    [InlineData("-2.5", "-2", "-3", "-2", "-2")]
    [InlineData("3.5", "4", "3", "3", "4")]
    [InlineData("2", "2", "2", "2", "2")]
    [InlineData("-0.4", "-0", "-1", "-0", "-0")]
    public void RoundingToAnInteger(string text, string ceiling, string floor, string truncate,
        string round)
    {
        var value = Decimal64.Parse(text);

        Assert.Equal(ceiling, Decimal64.Ceiling(value).ToString());
        Assert.Equal(floor, Decimal64.Floor(value).ToString());
        Assert.Equal(truncate, Decimal64.Truncate(value).ToString());
        Assert.Equal(round, Decimal64.Round(value).ToString());
    }

    [Fact]
    public void RoundingToPlaces()
    {
        var value = Decimal64.Parse("2.345");

        Assert.Equal("2.34", Decimal64.Round(value, 2).ToString());
        Assert.Equal("2.35", Decimal64.Round(value, 2, MidpointRounding.AwayFromZero).ToString());
        Assert.Equal("2", Decimal64.Round(value, MidpointRounding.ToZero).ToString());

        // Asking for more places than the value carries leaves it alone, quantum and all.
        // Padding it out to eight places would be a quantize, not a round.
        Assert.Equal("2.345", Decimal64.Round(value, 8).ToString());
    }

    /// <summary>
    /// A NaN has no fractional part to round away, and neither has an infinity. Rescaling
    /// them read the NaN's payload as a coefficient and gave back a finite zero.
    /// </summary>
    [Fact]
    public void RoundingLeavesTheSpecialsAlone()
    {
        Assert.True(Decimal64.IsNaN(Decimal64.Round(Decimal64.NaN, 2)));
        Assert.True(Decimal64.IsNaN(Decimal64.Ceiling(Decimal64.NaN)));
        Assert.True(Decimal64.IsNaN(Decimal64.Truncate(Decimal64.NaN)));

        Assert.Equal(Decimal64.PositiveInfinity, Decimal64.Round(Decimal64.PositiveInfinity, 2));
        Assert.Equal(Decimal64.NegativeInfinity, Decimal64.Floor(Decimal64.NegativeInfinity));
    }

    /// <summary>A value whose exponent is already past the requested places is integral.</summary>
    [Fact]
    public void RoundingAValueWiderThanTheFormat()
    {
        var value = Decimal64.Parse("1E+300");

        Assert.Equal(value, Decimal64.Round(value));
        Assert.Equal(value, Decimal64.Truncate(value));
    }

    [Theory]
    [InlineData("5", 1)]
    [InlineData("-5", -1)]
    [InlineData("0", 0)]
    [InlineData("-0", 0)]
    [InlineData("0.00", 0)]
    [InlineData("Infinity", 1)]
    [InlineData("-Infinity", -1)]
    public void Sign(string text, int expected)
    {
        Assert.Equal(expected, Decimal64.Sign(Decimal64.Parse(text)));
    }

    [Fact]
    public void SignOfANaNThrows()
    {
        Assert.Throws<ArithmeticException>(() => Decimal64.Sign(Decimal64.NaN));
    }

    [Fact]
    public void Clamp()
    {
        var low = Decimal64.Parse("0");
        var high = Decimal64.Parse("10");

        Assert.Equal(Decimal64.Parse("5"), Decimal64.Clamp(Decimal64.Parse("5"), low, high));
        Assert.Equal(high, Decimal64.Clamp(Decimal64.Parse("50"), low, high));
        Assert.Equal(low, Decimal64.Clamp(Decimal64.Parse("-50"), low, high));

        // A NaN is unordered against both bounds, so it passes through.
        Assert.True(Decimal64.IsNaN(Decimal64.Clamp(Decimal64.NaN, low, high)));

        Assert.Throws<ArgumentException>(() => Decimal64.Clamp(low, high, low));
    }

    /// <summary>
    /// The two readings of max and min. IEEE 754's maximum, which .NET's Max means, gives a
    /// NaN when either operand is one; the specification's max, which the Number forms and
    /// the overloads taking a context mean, hands back the number beside it.
    /// </summary>
    [Fact]
    public void SelectionAndNaN()
    {
        var one = Decimal64.One;

        Assert.True(Decimal64.IsNaN(Decimal64.Max(Decimal64.NaN, one)));
        Assert.True(Decimal64.IsNaN(Decimal64.Min(Decimal64.NaN, one)));
        Assert.True(Decimal64.IsNaN(Decimal64.MaxMagnitude(Decimal64.NaN, one)));
        Assert.True(Decimal64.IsNaN(Decimal64.MinMagnitude(Decimal64.NaN, one)));

        Assert.Equal(one, Decimal64.MaxNumber(Decimal64.NaN, one));
        Assert.Equal(one, Decimal64.MinNumber(Decimal64.NaN, one));
        Assert.Equal(one, Decimal64.MaxMagnitudeNumber(Decimal64.NaN, one));
        Assert.Equal(one, Decimal64.MinMagnitudeNumber(Decimal64.NaN, one));

        var context = new DecimalContext();
        Assert.Equal(one, Decimal64.Max(Decimal64.NaN, one, ref context));
        Assert.Equal(one, Decimal64.MaxMagnitude(Decimal64.NaN, one, ref context));
    }

    [Fact]
    public void SelectionOfNumbers()
    {
        var small = Decimal64.Parse("-5");
        var large = Decimal64.Parse("3");

        Assert.Equal(large, Decimal64.Max(small, large));
        Assert.Equal(small, Decimal64.Min(small, large));
        Assert.Equal(small, Decimal64.MaxMagnitude(small, large));
        Assert.Equal(large, Decimal64.MinMagnitude(small, large));

        // Equal magnitudes fall back to the ordinary comparison.
        Assert.Equal(Decimal64.Parse("5"), Decimal64.MaxMagnitude(small, Decimal64.Parse("5")));
        Assert.Equal(small, Decimal64.MinMagnitude(small, Decimal64.Parse("5")));
    }

    [Fact]
    public void ConvertingToAnInteger()
    {
        Assert.Equal(2, Decimal64.ConvertToInteger<int>(Decimal64.Parse("2.9")));
        Assert.Equal(-2, Decimal64.ConvertToInteger<int>(Decimal64.Parse("-2.9")));
        Assert.Equal(2L, Decimal64.ConvertToIntegerNative<long>(Decimal64.Parse("2.9")));

        // Off the end of the target's range it clamps, and a NaN converts to zero -- both
        // of which are what converting a double gives.
        Assert.Equal(int.MaxValue, Decimal64.ConvertToInteger<int>(Decimal64.Parse("1E+300")));
        Assert.Equal(int.MinValue, Decimal64.ConvertToInteger<int>(Decimal64.Parse("-1E+300")));
        Assert.Equal(int.MaxValue, Decimal64.ConvertToInteger<int>(Decimal64.PositiveInfinity));
        Assert.Equal(0, Decimal64.ConvertToInteger<int>(Decimal64.NaN));
    }

    /// <summary>
    /// The same conversions through the generic-math entry points, which used to report
    /// that they could not convert a NaN or an infinity at all.
    /// </summary>
    [Fact]
    public void SaturatingConversionOfTheSpecials()
    {
        Assert.Equal(0, int.CreateSaturating(Decimal64.NaN));
        Assert.Equal(int.MaxValue, int.CreateSaturating(Decimal64.PositiveInfinity));
        Assert.Equal(int.MinValue, int.CreateSaturating(Decimal64.NegativeInfinity));
        Assert.Equal(BigInteger.Zero, BigInteger.CreateSaturating(Decimal64.NaN));
    }

    [Fact]
    public void Ieee754Remainder()
    {
        Assert.Equal(Decimal64.Parse("-1"),
            Decimal64.Ieee754Remainder(Decimal64.Parse("5"), Decimal64.Parse("3")));
        Assert.Equal(Decimal64.Parse("1"),
            Decimal64.Ieee754Remainder(Decimal64.Parse("7"), Decimal64.Parse("3")));
    }

    [Theory]
    [InlineData("1234.5", 3)]
    [InlineData("0.001", -3)]
    [InlineData("1", 0)]
    [InlineData("9.999", 0)]
    [InlineData("10", 1)]
    public void ILogB(string text, int expected)
    {
        Assert.Equal(expected, Decimal64.ILogB(Decimal64.Parse(text)));
    }

    [Fact]
    public void ILogBOffTheEndOfTheRange()
    {
        Assert.Equal(int.MinValue, Decimal64.ILogB(Decimal64.Zero));
        Assert.Equal(int.MaxValue, Decimal64.ILogB(Decimal64.NaN));
        Assert.Equal(int.MaxValue, Decimal64.ILogB(Decimal64.PositiveInfinity));
    }

    /// <summary>
    /// ScaleB moves by powers of the format's radix, which is ten. It keeps the coefficient
    /// and shifts the exponent, so the value is the same one the specification's scaleb
    /// gives and prints in whichever cohort member that leaves.
    /// </summary>
    [Fact]
    public void ScaleB()
    {
        Assert.Equal(Decimal64.Parse("150"), Decimal64.ScaleB(Decimal64.Parse("1.5"), 2));
        Assert.Equal(Decimal64.Parse("0.015"), Decimal64.ScaleB(Decimal64.Parse("1.5"), -2));
        Assert.Equal(Decimal64.Parse("1.5"), Decimal64.ScaleB(Decimal64.Parse("1.5"), 0));
    }

    [Fact]
    public void BitIncrementAndBitDecrement()
    {
        Assert.Equal(Decimal64.Parse("1.000000000000001"), Decimal64.BitIncrement(Decimal64.One));
        Assert.Equal(Decimal64.Parse("0.9999999999999999"), Decimal64.BitDecrement(Decimal64.One));

        Assert.Equal(Decimal64.PositiveInfinity, Decimal64.BitIncrement(Decimal64.PositiveInfinity));
        Assert.Equal(Decimal64.MaxValue, Decimal64.BitDecrement(Decimal64.PositiveInfinity));
    }

    /// <summary>
    /// The same members on the other two widths, which carry their own copy of the surface.
    /// </summary>
    [Fact]
    public void TheOtherTwoWidths()
    {
        Assert.Equal(Decimal32.Parse("-2"), Decimal32.Truncate(Decimal32.Parse("-2.7")));
        Assert.Equal(Decimal32.Parse("-3"), Decimal32.Floor(Decimal32.Parse("-2.7")));
        Assert.Equal(-1, Decimal32.Sign(Decimal32.Parse("-2.7")));
        Assert.True(Decimal32.IsNaN(Decimal32.Max(Decimal32.NaN, Decimal32.One)));
        Assert.Equal(1, Decimal32.ILogB(Decimal32.Parse("42")));

        Assert.Equal(Decimal128.Parse("-2"), Decimal128.Ceiling(Decimal128.Parse("-2.7")));
        Assert.Equal(Decimal128.Parse("3"), Decimal128.Round(Decimal128.Parse("2.7")));
        Assert.Equal(1, Decimal128.Sign(Decimal128.Parse("2.7")));
        Assert.Equal(Decimal128.One, Decimal128.MinNumber(Decimal128.NaN, Decimal128.One));
        Assert.Equal(33, Decimal128.ILogB(Decimal128.Parse("1E+33")));
    }

    /// <summary>
    /// Reaching the same members through a constraint, which is what generic numeric code
    /// does. These bound to interface defaults before; now they bind to the statics above.
    /// </summary>
    [Fact]
    public void ThroughTheInterfaces()
    {
        Assert.Equal(Decimal64.Parse("-2"), TruncateOf(Decimal64.Parse("-2.7")));
        Assert.Equal(Decimal32.Parse("-2"), TruncateOf(Decimal32.Parse("-2.7")));
        Assert.Equal(Decimal128.Parse("-2"), TruncateOf(Decimal128.Parse("-2.7")));

        Assert.Equal(-1, SignOf(Decimal64.Parse("-2.7")));
        Assert.True(Decimal64.IsNaN(MaxOf(Decimal64.NaN, Decimal64.One)));
        Assert.Equal(Decimal64.One, MaxNumberOf(Decimal64.NaN, Decimal64.One));
    }

    private static T TruncateOf<T>(T value)
        where T : IFloatingPoint<T> => T.Truncate(value);

    private static int SignOf<T>(T value)
        where T : INumber<T> => T.Sign(value);

    private static T MaxOf<T>(T left, T right)
        where T : INumber<T> => T.Max(left, right);

    private static T MaxNumberOf<T>(T left, T right)
        where T : INumber<T> => T.MaxNumber(left, right);
}
