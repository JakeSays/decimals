// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Numerics;
using Decimals.Internal;


namespace Decimals.Tests;

/// <summary>
/// The members of <see cref="Decimal64"/> a .NET caller reaches for by name: rounding,
/// selection, sign and clamp, the constants, the generic math hooks, and the conversions
/// that have to refuse a value.
/// </summary>
public class Decimal64SurfaceTests
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
        Assert.Equal("2.345", Decimal64.Round(value, 8).ToString());

        Assert.True(Decimal64.IsNaN(Decimal64.Round(Decimal64.NaN, 2)));
        Assert.Equal(Decimal64.PositiveInfinity, Decimal64.Round(Decimal64.PositiveInfinity, 2));
        Assert.Equal(Decimal64.Parse("1E+300"), Decimal64.Truncate(Decimal64.Parse("1E+300")));
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
        Assert.True(Decimal64.IsNaN(Decimal64.Clamp(Decimal64.NaN, low, high)));
        Assert.Throws<ArgumentException>(() => Decimal64.Clamp(low, high, low));
    }

    [Fact]
    public void SelectionAndNaN()
    {
        var one = Decimal64.One;

        Assert.True(Decimal64.IsNaN(Decimal64.Max(Decimal64.NaN, one)));
        Assert.True(Decimal64.IsNaN(Decimal64.Min(one, Decimal64.NaN)));
        Assert.Equal(one, Decimal64.MaxNumber(Decimal64.NaN, one));
        Assert.Equal(one, Decimal64.MinNumber(one, Decimal64.NaN));
        Assert.Equal(Decimal64.Parse("-3"), Decimal64.MaxMagnitudeNumber(Decimal64.Parse("-3"), Decimal64.Parse("2")));
        Assert.Equal(Decimal64.Parse("2"), Decimal64.MinMagnitudeNumber(Decimal64.Parse("-3"), Decimal64.Parse("2")));

        // A signaling NaN is invalid under every reading and comes back quiet.
        var signaling = Decimal64.Parse("sNaN5");
        Assert.Equal("NaN5", Decimal64.Max(signaling, one).ToString());
    }

    [Fact]
    public void ConstantsAreTheCorrectlyRoundedValues()
    {
        Assert.Equal("2.718281828459045", Decimal64.E.ToString());
        Assert.Equal("3.141592653589793", Decimal64.Pi.ToString());
        Assert.Equal("6.283185307179586", Decimal64.Tau.ToString());
        Assert.Equal(Decimal64.E, Decimal64.Exp(Decimal64.One));
        Assert.Equal("9.999999999999999E+384", Decimal64.MaxValue.ToString());
        Assert.Equal("-9.999999999999999E+384", Decimal64.MinValue.ToString());
        Assert.Equal("1E-398", Decimal64.Epsilon.ToString());
        Assert.Equal(10, Decimal64.Radix);
    }

    [Fact]
    public void EqualityIsNumericAndHashingFollowsIt()
    {
        var one = Decimal64.Parse("1");
        var alsoOne = Decimal64.Parse("1.00");
        var scaled = Decimal64.Parse("100E-2");

        Assert.True(one == alsoOne);
        Assert.True(one.Equals(alsoOne));
        Assert.True(one.Equals(scaled));
        Assert.Equal(one.GetHashCode(), alsoOne.GetHashCode());
        Assert.Equal(one.GetHashCode(), scaled.GetHashCode());
        Assert.NotEqual(one.ToBits(), alsoOne.ToBits());
        Assert.True(Decimal64.CompareTotal(one, alsoOne) > 0);

        Assert.True(Decimal64.Zero == Decimal64.NegativeZero);
        Assert.Equal(Decimal64.Zero.GetHashCode(), Decimal64.NegativeZero.GetHashCode());

        Assert.False(Decimal64.NaN == Decimal64.NaN);
        Assert.True(Decimal64.NaN.Equals(Decimal64.NaN));
        Assert.False(Decimal64.NaN < one);
        Assert.False(Decimal64.NaN >= one);
        Assert.True(Decimal64.NaN.CompareTo(one) < 0);
        Assert.Equal(0, Decimal64.NaN.CompareTo(Decimal64.Parse("sNaN3")));

        var set = new HashSet<Decimal64> { one, alsoOne, scaled, Decimal64.Zero, Decimal64.NegativeZero };
        Assert.Equal(2, set.Count);
    }

    [Fact]
    public void GenericMathWorks()
    {
        Assert.Equal("2.5", Mean<Decimal64>([Decimal64.Parse("1"), Decimal64.Parse("2"), Decimal64.Parse("3"), Decimal64.Parse("4")]).ToString());
        Assert.Equal(Decimal64.Parse("42"), Decimal64.CreateChecked(42));
        Assert.Equal(Decimal64.Parse("42"), Decimal64.CreateChecked(42.0));
        Assert.Equal(Decimal64.Parse("0.1"), Decimal64.CreateChecked(0.1));
        Assert.Equal(Decimal64.Parse("1.5"), Decimal64.CreateChecked(1.5m));
        Assert.Equal(Decimal64.Parse("1.234567890123457E+30"), Decimal64.CreateChecked(BigInteger.Parse("1234567890123456789012345678901")));
        Assert.Equal(Decimal64.Parse("123456789012345678"), Decimal64.CreateSaturating((Int128)123456789012345678));
        Assert.Equal(42, int.CreateChecked(Decimal64.Parse("42.9")));
        Assert.Equal(-42, int.CreateChecked(Decimal64.Parse("-42.9")));
        Assert.Equal(int.MaxValue, int.CreateSaturating(Decimal64.Parse("1E+30")));
        Assert.Equal(long.MinValue, long.CreateSaturating(Decimal64.NegativeInfinity));
        Assert.Equal(0, int.CreateSaturating(Decimal64.NaN));
        Assert.Equal(1.5, double.CreateChecked(Decimal64.Parse("1.5")));
        Assert.Equal(Int128.Parse("100000000000000000000"), Int128.CreateSaturating(Decimal64.Parse("1E+20")));
    }

    [Fact]
    public void IntegerConversionsRefuseWhatDoesNotFit()
    {
        Assert.Equal(42, (int)Decimal64.Parse("42.9"));
        Assert.Equal(-42, (int)Decimal64.Parse("-42.9"));
        // Nineteen and twenty digit limits round to sixteen digits on the way in, so the
        // largest values that convert are the rounded ones below the limit.
        Assert.Equal(-9223372036854775000, (long)Decimal64.Parse("-9.223372036854775E+18"));
        Assert.Equal(18446744073709550000, (ulong)Decimal64.Parse("1.844674407370955E+19"));
        Assert.Equal(0, (int)Decimal64.Parse("0.999"));
        Assert.Equal(1000, (int)Decimal64.Parse("1E+3"));

        Assert.Throws<OverflowException>(() => (int)Decimal64.Parse("3000000000"));
        Assert.Throws<OverflowException>(() => (long)Decimal64.Parse("9223372036854775808"));
        Assert.Throws<OverflowException>(() => (ulong)Decimal64.Parse("-1"));
        Assert.Throws<OverflowException>(() => (byte)Decimal64.Parse("256"));
        Assert.Throws<OverflowException>(() => (int)Decimal64.NaN);
        Assert.Throws<OverflowException>(() => (int)Decimal64.PositiveInfinity);
        Assert.Throws<OverflowException>(() => (ulong)Decimal64.Parse("1E+20"));
    }

    [Fact]
    public void BinaryConversionsHaveTwoReadings()
    {
        Assert.Equal("0.1", ((Decimal64)0.1).ToString());
        Assert.Equal("0.1", ((Decimal64)0.1f).ToString());
        Assert.Equal("0.1000000000000000", Decimal64.FromBinary(0.1, Decimal64BinaryConversion.ExactValue).ToString());
        Assert.Equal("2.5", Decimal64.FromBinary(2.5, Decimal64BinaryConversion.ExactValue).ToString());
        // 0.3 is held as 0.299999999999999988897769753748..., which rounds back up at sixteen
        // digits; a value with fewer bits shows its binary tail whole.
        Assert.Equal("0.3000000000000000", Decimal64.FromBinary(0.3, Decimal64BinaryConversion.ExactValue).ToString());
        Assert.Equal("0.0009765625", Decimal64.FromBinary(0.0009765625, Decimal64BinaryConversion.ExactValue).ToString());
        Assert.Equal("1.100000000000000", Decimal64.FromBinary(1.1, Decimal64BinaryConversion.ExactValue).ToString());
        Assert.Equal(0.1, (double)Decimal64.Parse("0.1"));
        Assert.True(double.IsNaN((double)Decimal64.NaN));
        Assert.Equal(double.PositiveInfinity, (double)Decimal64.Parse("1E+384"));
        Assert.Equal(0.0, (double)Decimal64.Parse("1E-384"));
    }

    [Fact]
    public void SpecialsHaveCanonicalForms()
    {
        var garbage = Decimal64.FromBits(0x7800000000000123);
        Assert.False(Decimal64.IsCanonical(garbage));
        Assert.True(Decimal64.IsInfinity(garbage));
        Assert.Equal(Decimal64.PositiveInfinity.ToBits(), Decimal64.Canonical(garbage).ToBits());

        // The long form carrying 10^16, one past the largest coefficient.
        var wideCoefficient = Decimal64.FromBits(0x6C7386F26FC10000);
        Assert.False(Decimal64.IsCanonical(wideCoefficient));
        Assert.True(Decimal64.IsZero(wideCoefficient));

        Assert.True(Decimal64.IsCanonical(Decimal64.Parse("1.5")));
        Assert.True(Decimal64.IsCanonical(Decimal64.Parse("sNaN12")));
        Assert.Equal(Decimal64.Parse("1.5").ToBits(), Decimal64.FromDpdBits(Decimal64.Parse("1.5").ToDpdBits()).ToBits());
    }

    [Fact]
    public void ConditionsDoNotLeakBetweenOperations()
    {
        // A subnormal result underflows only when it is itself inexact; an earlier
        // operation's inexactness in the same context must not make it so.
        var context = new Decimal64Context();
        Decimal64.Divide(Decimal64.One, Decimal64.Parse("3"), ref context);
        Assert.True(context.HasRaised(Decimal64Status.Inexact));

        var tiny = Decimal64.Add(Decimal64.Parse("1E-398"), Decimal64.Parse("1E-398"), ref context);
        Assert.Equal("2E-398", tiny.ToString());
        Assert.True(context.HasRaised(Decimal64Status.Subnormal));
        Assert.False(context.HasRaised(Decimal64Status.Underflow));
    }

    /// <summary>
    /// A base of zero or infinity makes the divisor infinite, so the quotient is a zero at
    /// an exponent far below the format; it has to come back clamped rather than packed as
    /// whatever bits that exponent makes.
    /// </summary>
    [Fact]
    public void LogarithmInADegenerateBaseIsAClampedZero()
    {
        var context = new Decimal64Context();
        Assert.Equal("-0E-398", Decimal64.Log(Decimal64.Parse("8"), Decimal64.Zero, ref context).ToString());
        Assert.True(context.HasRaised(Decimal64Status.Clamped));

        Assert.Equal("0E-398", Decimal64.Log(Decimal64.Parse("8"), Decimal64.PositiveInfinity).ToString());

        // A base of one makes the divisor zero, which is a division by zero rather than an
        // invalid operation: the quotient is an infinity.
        var byOne = new Decimal64Context();
        Assert.True(Decimal64.IsPositiveInfinity(Decimal64.Log(Decimal64.Parse("8"), Decimal64.One, ref byOne)));
        Assert.True(byOne.HasRaised(Decimal64Status.DivisionByZero));

        Assert.Equal("3", Decimal64.Log(Decimal64.Parse("8"), Decimal64.Parse("2")).ToString());
    }

    /// <summary>
    /// The integer overload of ScaleB applies the same limit as the operand form: a shift
    /// no operand could express is invalid, not an overflow.
    /// </summary>
    [Fact]
    public void ScaleByAnIntegerHonorsTheOperandLimit()
    {
        Assert.Equal("1E+10", Decimal64.ScaleB(Decimal64.One, 10).ToString());
        Assert.Equal("Infinity", Decimal64.ScaleB(Decimal64.One, 800).ToString());
        Assert.True(Decimal64.IsNaN(Decimal64.ScaleB(Decimal64.One, 801)));
        Assert.True(Decimal64.IsNaN(Decimal64.ScaleB(Decimal64.One, -801)));
        Assert.Equal("NaN7", Decimal64.ScaleB(Decimal64.Parse("NaN7"), 801).ToString());
    }

    [Fact]
    public void ExponentAndSignificandBytes()
    {
        IFloatingPoint<Decimal64> value = Decimal64.Parse("1.5");
        Span<byte> exponent = stackalloc byte[4];
        Span<byte> significand = stackalloc byte[8];

        Assert.True(value.TryWriteExponentLittleEndian(exponent, out var exponentWritten));
        Assert.Equal(4, exponentWritten);
        Assert.Equal(-1, BitConverter.ToInt32(exponent));

        Assert.True(value.TryWriteSignificandLittleEndian(significand, out var significandWritten));
        Assert.Equal(8, significandWritten);
        Assert.Equal(15UL, BitConverter.ToUInt64(significand));
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
