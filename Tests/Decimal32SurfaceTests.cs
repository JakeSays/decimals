// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Numerics;
using Decimals.Internal;


namespace Decimals.Tests;

/// <summary>
/// The members of <see cref="Decimal32"/> a .NET caller reaches for by name: rounding,
/// selection, sign and clamp, the constants, the generic math hooks, and the conversions
/// that have to refuse a value.
/// </summary>
public class Decimal32SurfaceTests
{
    [Theory]
    [InlineData("2.7", "3", "2", "2", "3")]
    [InlineData("-2.7", "-2", "-3", "-2", "-3")]
    [InlineData("2.5", "3", "2", "2", "2")]
    [InlineData("-2.5", "-2", "-3", "-2", "-2")]
    [InlineData("3.5", "4", "3", "3", "4")]
    [InlineData("2", "2", "2", "2", "2")]
    [InlineData("-0.4", "-0", "-1", "-0", "-0")]
    public void RoundingToAnInteger(string text, string ceiling, string floor, string truncate, string round)
    {
        var value = Decimal32.Parse(text);

        Assert.Equal(ceiling, Decimal32.Ceiling(value).ToString());
        Assert.Equal(floor, Decimal32.Floor(value).ToString());
        Assert.Equal(truncate, Decimal32.Truncate(value).ToString());
        Assert.Equal(round, Decimal32.Round(value).ToString());
    }

    [Fact]
    public void RoundingToPlaces()
    {
        var value = Decimal32.Parse("2.345");

        Assert.Equal("2.34", Decimal32.Round(value, 2).ToString());
        Assert.Equal("2.35", Decimal32.Round(value, 2, MidpointRounding.AwayFromZero).ToString());
        Assert.Equal("2", Decimal32.Round(value, MidpointRounding.ToZero).ToString());

        // Asking for more places than the value carries leaves it alone, quantum and all.
        Assert.Equal("2.345", Decimal32.Round(value, 8).ToString());

        Assert.True(Decimal32.IsNaN(Decimal32.Round(Decimal32.NaN, 2)));
        Assert.Equal(Decimal32.PositiveInfinity, Decimal32.Round(Decimal32.PositiveInfinity, 2));
        Assert.Equal(Decimal32.Parse("1E+90"), Decimal32.Truncate(Decimal32.Parse("1E+90")));
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
        Assert.Equal(expected, Decimal32.Sign(Decimal32.Parse(text)));
    }

    [Fact]
    public void SignOfANaNThrows()
    {
        Assert.Throws<ArithmeticException>(() => Decimal32.Sign(Decimal32.NaN));
    }

    [Fact]
    public void Clamp()
    {
        var low = Decimal32.Parse("0");
        var high = Decimal32.Parse("10");

        Assert.Equal(Decimal32.Parse("5"), Decimal32.Clamp(Decimal32.Parse("5"), low, high));
        Assert.Equal(high, Decimal32.Clamp(Decimal32.Parse("50"), low, high));
        Assert.Equal(low, Decimal32.Clamp(Decimal32.Parse("-50"), low, high));
        Assert.True(Decimal32.IsNaN(Decimal32.Clamp(Decimal32.NaN, low, high)));
        Assert.Throws<ArgumentException>(() => Decimal32.Clamp(low, high, low));
    }

    [Fact]
    public void SelectionAndNaN()
    {
        var one = Decimal32.One;

        Assert.True(Decimal32.IsNaN(Decimal32.Max(Decimal32.NaN, one)));
        Assert.True(Decimal32.IsNaN(Decimal32.Min(one, Decimal32.NaN)));
        Assert.Equal(one, Decimal32.MaxNumber(Decimal32.NaN, one));
        Assert.Equal(one, Decimal32.MinNumber(one, Decimal32.NaN));
        Assert.Equal(Decimal32.Parse("-3"), Decimal32.MaxMagnitudeNumber(Decimal32.Parse("-3"), Decimal32.Parse("2")));
        Assert.Equal(Decimal32.Parse("2"), Decimal32.MinMagnitudeNumber(Decimal32.Parse("-3"), Decimal32.Parse("2")));

        // A signaling NaN is invalid under every reading and comes back quiet.
        var signaling = Decimal32.Parse("sNaN5");
        Assert.Equal("NaN5", Decimal32.Max(signaling, one).ToString());
    }

    [Fact]
    public void ConstantsAreTheCorrectlyRoundedValues()
    {
        Assert.Equal("2.718282", Decimal32.E.ToString());
        Assert.Equal("3.141593", Decimal32.Pi.ToString());
        Assert.Equal("6.283185", Decimal32.Tau.ToString());
        Assert.Equal(Decimal32.E, Decimal32.Exp(Decimal32.One));
        Assert.Equal("9.999999E+96", Decimal32.MaxValue.ToString());
        Assert.Equal("-9.999999E+96", Decimal32.MinValue.ToString());
        Assert.Equal("1E-101", Decimal32.Epsilon.ToString());
        Assert.Equal(10, Decimal32.Radix);
    }

    [Fact]
    public void EqualityIsNumericAndHashingFollowsIt()
    {
        var one = Decimal32.Parse("1");
        var alsoOne = Decimal32.Parse("1.00");
        var scaled = Decimal32.Parse("100E-2");

        Assert.True(one == alsoOne);
        Assert.True(one.Equals(alsoOne));
        Assert.True(one.Equals(scaled));
        Assert.Equal(one.GetHashCode(), alsoOne.GetHashCode());
        Assert.Equal(one.GetHashCode(), scaled.GetHashCode());
        Assert.NotEqual(one.ToBits(), alsoOne.ToBits());
        Assert.True(Decimal32.CompareTotal(one, alsoOne) > 0);

        Assert.True(Decimal32.Zero == Decimal32.NegativeZero);
        Assert.Equal(Decimal32.Zero.GetHashCode(), Decimal32.NegativeZero.GetHashCode());

        Assert.False(Decimal32.NaN == Decimal32.NaN);
        Assert.True(Decimal32.NaN.Equals(Decimal32.NaN));
        Assert.False(Decimal32.NaN < one);
        Assert.False(Decimal32.NaN >= one);
        Assert.True(Decimal32.NaN.CompareTo(one) < 0);
        Assert.Equal(0, Decimal32.NaN.CompareTo(Decimal32.Parse("sNaN3")));

        var set = new HashSet<Decimal32> { one, alsoOne, scaled, Decimal32.Zero, Decimal32.NegativeZero };
        Assert.Equal(2, set.Count);
    }

    [Fact]
    public void GenericMathWorks()
    {
        Assert.Equal("2.5", Mean<Decimal32>([Decimal32.Parse("1"), Decimal32.Parse("2"), Decimal32.Parse("3"), Decimal32.Parse("4")]).ToString());
        Assert.Equal(Decimal32.Parse("42"), Decimal32.CreateChecked(42));
        Assert.Equal(Decimal32.Parse("42"), Decimal32.CreateChecked(42.0));
        Assert.Equal(Decimal32.Parse("0.1"), Decimal32.CreateChecked(0.1));
        Assert.Equal(Decimal32.Parse("1.5"), Decimal32.CreateChecked(1.5m));
        Assert.Equal(Decimal32.Parse("1.234568E+30"), Decimal32.CreateChecked(BigInteger.Parse("1234567890123456789012345678901")));
        Assert.Equal(Decimal32.Parse("1.234568E+17"), Decimal32.CreateSaturating((Int128)123456789012345678));
        Assert.Equal(42, int.CreateChecked(Decimal32.Parse("42.9")));
        Assert.Equal(-42, int.CreateChecked(Decimal32.Parse("-42.9")));
        Assert.Equal(int.MaxValue, int.CreateSaturating(Decimal32.Parse("1E+30")));
        Assert.Equal(long.MinValue, long.CreateSaturating(Decimal32.NegativeInfinity));
        Assert.Equal(0, int.CreateSaturating(Decimal32.NaN));
        Assert.Equal(1.5, double.CreateChecked(Decimal32.Parse("1.5")));
        Assert.Equal(Int128.Parse("100000000000000000000"), Int128.CreateSaturating(Decimal32.Parse("1E+20")));
    }

    [Fact]
    public void IntegerConversionsRefuseWhatDoesNotFit()
    {
        Assert.Equal(42, (int)Decimal32.Parse("42.9"));
        Assert.Equal(-42, (int)Decimal32.Parse("-42.9"));
        // The limits of long and ulong round to seven digits on the way in, and the rounded
        // values sit inside the limits, so they are the largest that convert; one unit in
        // the seventh digit above them does not.
        Assert.Equal(-9223372000000000000, (long)Decimal32.Parse("-9.223372E+18"));
        Assert.Equal(18446740000000000000, (ulong)Decimal32.Parse("1.844674E+19"));
        Assert.Equal(0, (int)Decimal32.Parse("0.999"));
        Assert.Equal(1000, (int)Decimal32.Parse("1E+3"));

        Assert.Throws<OverflowException>(() => (int)Decimal32.Parse("3000000000"));
        Assert.Throws<OverflowException>(() => (long)Decimal32.Parse("9.223373E+18"));
        Assert.Throws<OverflowException>(() => (ulong)Decimal32.Parse("1.844675E+19"));
        Assert.Throws<OverflowException>(() => (ulong)Decimal32.Parse("-1"));
        Assert.Throws<OverflowException>(() => (byte)Decimal32.Parse("256"));
        Assert.Throws<OverflowException>(() => (int)Decimal32.NaN);
        Assert.Throws<OverflowException>(() => (int)Decimal32.PositiveInfinity);
        Assert.Throws<OverflowException>(() => (ulong)Decimal32.Parse("1E+20"));
    }

    [Fact]
    public void BinaryConversionsHaveTwoReadings()
    {
        Assert.Equal("0.1", ((Decimal32)0.1).ToString());
        Assert.Equal("0.1", ((Decimal32)0.1f).ToString());
        Assert.Equal("0.1000000", Decimal32.FromBinary(0.1, Decimal32BinaryConversion.ExactValue).ToString());
        Assert.Equal("2.5", Decimal32.FromBinary(2.5, Decimal32BinaryConversion.ExactValue).ToString());
        // 0.3 is held as 0.299999999999999988897769753748..., which rounds back up at seven
        // digits; a value with fewer bits shows its binary tail whole.
        Assert.Equal("0.3000000", Decimal32.FromBinary(0.3, Decimal32BinaryConversion.ExactValue).ToString());
        Assert.Equal("0.0009765625", Decimal32.FromBinary(0.0009765625, Decimal32BinaryConversion.ExactValue).ToString());
        Assert.Equal("1.100000", Decimal32.FromBinary(1.1, Decimal32BinaryConversion.ExactValue).ToString());
        Assert.Equal(0.1, (double)Decimal32.Parse("0.1"));
        Assert.True(double.IsNaN((double)Decimal32.NaN));
        Assert.Equal(1E+96, (double)Decimal32.Parse("1E+96"));
        Assert.Equal(1E-101, (double)Decimal32.Parse("1E-101"));
    }

    [Fact]
    public void SpecialsHaveCanonicalForms()
    {
        var garbage = Decimal32.FromBits(0x78000123);
        Assert.False(Decimal32.IsCanonical(garbage));
        Assert.True(Decimal32.IsInfinity(garbage));
        Assert.Equal(Decimal32.PositiveInfinity.ToBits(), Decimal32.Canonical(garbage).ToBits());

        // The long form carrying 10^7, one past the largest coefficient.
        var wideCoefficient = Decimal32.FromBits(0x6CB89680);
        Assert.False(Decimal32.IsCanonical(wideCoefficient));
        Assert.True(Decimal32.IsZero(wideCoefficient));

        Assert.True(Decimal32.IsCanonical(Decimal32.Parse("1.5")));
        Assert.True(Decimal32.IsCanonical(Decimal32.Parse("sNaN12")));
        Assert.Equal(Decimal32.Parse("1.5").ToBits(), Decimal32.FromDpdBits(Decimal32.Parse("1.5").ToDpdBits()).ToBits());
    }

    [Fact]
    public void ConditionsDoNotLeakBetweenOperations()
    {
        // A subnormal result underflows only when it is itself inexact; an earlier
        // operation's inexactness in the same context must not make it so.
        var context = new Decimal32Context();
        Decimal32.Divide(Decimal32.One, Decimal32.Parse("3"), ref context);
        Assert.True(context.HasRaised(Decimal32Status.Inexact));

        var tiny = Decimal32.Add(Decimal32.Parse("1E-101"), Decimal32.Parse("1E-101"), ref context);
        Assert.Equal("2E-101", tiny.ToString());
        Assert.True(context.HasRaised(Decimal32Status.Subnormal));
        Assert.False(context.HasRaised(Decimal32Status.Underflow));
    }

    /// <summary>
    /// A base of zero or infinity makes the divisor infinite, so the quotient is a zero at
    /// an exponent far below the format; it has to come back clamped rather than packed as
    /// whatever bits that exponent makes.
    /// </summary>
    [Fact]
    public void LogarithmInADegenerateBaseIsAClampedZero()
    {
        var context = new Decimal32Context();
        Assert.Equal("-0E-101", Decimal32.Log(Decimal32.Parse("8"), Decimal32.Zero, ref context).ToString());
        Assert.True(context.HasRaised(Decimal32Status.Clamped));

        Assert.Equal("0E-101", Decimal32.Log(Decimal32.Parse("8"), Decimal32.PositiveInfinity).ToString());

        // A base of one makes the divisor zero, which is a division by zero rather than an
        // invalid operation: the quotient is an infinity.
        var byOne = new Decimal32Context();
        Assert.True(Decimal32.IsPositiveInfinity(Decimal32.Log(Decimal32.Parse("8"), Decimal32.One, ref byOne)));
        Assert.True(byOne.HasRaised(Decimal32Status.DivisionByZero));

        Assert.Equal("3", Decimal32.Log(Decimal32.Parse("8"), Decimal32.Parse("2")).ToString());
    }

    /// <summary>
    /// The integer overload of ScaleB applies the same limit as the operand form: a shift
    /// no operand could express is invalid, not an overflow.
    /// </summary>
    [Fact]
    public void ScaleByAnIntegerHonorsTheOperandLimit()
    {
        Assert.Equal("1E+10", Decimal32.ScaleB(Decimal32.One, 10).ToString());
        Assert.Equal("Infinity", Decimal32.ScaleB(Decimal32.One, 206).ToString());
        Assert.True(Decimal32.IsNaN(Decimal32.ScaleB(Decimal32.One, 207)));
        Assert.True(Decimal32.IsNaN(Decimal32.ScaleB(Decimal32.One, -207)));
        Assert.Equal("NaN7", Decimal32.ScaleB(Decimal32.Parse("NaN7"), 207).ToString());
    }

    [Fact]
    public void ExponentAndSignificandBytes()
    {
        IFloatingPoint<Decimal32> value = Decimal32.Parse("1.5");
        Span<byte> exponent = stackalloc byte[4];
        Span<byte> significand = stackalloc byte[4];

        Assert.True(value.TryWriteExponentLittleEndian(exponent, out var exponentWritten));
        Assert.Equal(4, exponentWritten);
        Assert.Equal(-1, BitConverter.ToInt32(exponent));

        Assert.True(value.TryWriteSignificandLittleEndian(significand, out var significandWritten));
        Assert.Equal(4, significandWritten);
        Assert.Equal(15U, BitConverter.ToUInt32(significand));
    }

    private static T Mean<T>(ReadOnlySpan<T> values)
        where T : INumber<T>
    {
        var total = T.Zero;
        foreach (var value in values)
        {
            total += value;
        }

        return total / T.CreateChecked(values.Length);
    }
}
