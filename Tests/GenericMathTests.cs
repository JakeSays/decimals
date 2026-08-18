// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Numerics;

namespace Decimals.Tests;

/// <summary>
/// The generic math surface: that the contracts are satisfied, and that equality and
/// ordering mean what .NET expects them to rather than what the bits say.
/// </summary>
public class GenericMathTests
{
    /// <summary>Adds through the constraint, which only compiles if the contract is met.</summary>
    private static T SumThroughConstraint<T>(params T[] values)
        where T : INumberBase<T>
    {
        var total = T.Zero;
        foreach (var value in values)
        {
            total += value;
        }

        return total;
    }

    [Fact]
    public void TheTypesSatisfyTheNumericContracts()
    {
        Assert.Equal(Decimal32.Parse("6"), SumThroughConstraint(Decimal32.Parse("1"), Decimal32.Parse("2"), Decimal32.Parse("3")));
        Assert.Equal(Decimal64.Parse("6"), SumThroughConstraint(Decimal64.Parse("1"), Decimal64.Parse("2"), Decimal64.Parse("3")));
        Assert.Equal(Decimal128.Parse("6"), SumThroughConstraint(Decimal128.Parse("1"), Decimal128.Parse("2"), Decimal128.Parse("3")));
    }

    [Fact]
    public void RadixIsTen()
    {
        Assert.Equal(10, Decimal32.Radix);
        Assert.Equal(10, Decimal64.Radix);
        Assert.Equal(10, Decimal128.Radix);
    }

    /// <summary>
    /// Cohort members are the same number, so they compare equal, hash the same, and sort
    /// together -- even though their encodings differ and CompareTotal separates them.
    /// </summary>
    [Fact]
    public void OneCohortIsOneValue()
    {
        var one = Decimal64.Parse("1");
        var onePointZero = Decimal64.Parse("1.0");
        var onePointZeroZero = Decimal64.Parse("1.00");

        Assert.True(one == onePointZero);
        Assert.True(onePointZero == onePointZeroZero);
        Assert.True(one.Equals(onePointZeroZero));
        Assert.Equal(one.GetHashCode(), onePointZeroZero.GetHashCode());
        Assert.Equal(0, one.CompareTo(onePointZeroZero));

        // The bits still differ, and the total order still separates them.
        Assert.NotEqual(one.ToBits(), onePointZeroZero.ToBits());
        Assert.True(Decimal64.CompareTotal(one, onePointZeroZero) > 0);
    }

    [Fact]
    public void BothZerosAreOneValue()
    {
        var positive = Decimal64.Zero;
        var negative = Decimal64.NegativeZero;

        Assert.True(positive == negative);
        Assert.True(positive.Equals(negative));
        Assert.Equal(positive.GetHashCode(), negative.GetHashCode());

        // Only the total order tells them apart, which is what it is for.
        Assert.True(Decimal64.CompareTotal(negative, positive) < 0);
    }

    [Fact]
    public void NaNIsUnorderedByOperatorsButEqualToItselfForCollections()
    {
        var nan = Decimal64.NaN;
        var one = Decimal64.One;

        var alsoNaN = Decimal64.NaN;
        Assert.False(nan == alsoNaN);
        Assert.True(nan != alsoNaN);
        Assert.False(nan < one);
        Assert.False(nan > one);
        Assert.False(nan <= one);
        Assert.False(nan >= one);

        // Equals and CompareTo have to place it somewhere, as they do for double.
        Assert.True(nan.Equals(Decimal64.NaN));
        Assert.Equal(0, nan.CompareTo(Decimal64.NaN));
        Assert.True(nan.CompareTo(one) < 0);
    }

    [Fact]
    public void ComparisonOperatorsAreNumeric()
    {
        var small = Decimal64.Parse("1.5");
        var large = Decimal64.Parse("2.5");

        Assert.True(small < large);
        Assert.True(large > small);
        Assert.True(small <= Decimal64.Parse("1.50"));
        Assert.True(small >= Decimal64.Parse("1.500"));
    }

    [Fact]
    public void ArithmeticOperatorsWork()
    {
        var a = Decimal64.Parse("1.5");
        var b = Decimal64.Parse("0.25");

        Assert.Equal(Decimal64.Parse("1.75"), a + b);
        Assert.Equal(Decimal64.Parse("1.25"), a - b);
        Assert.Equal(Decimal64.Parse("0.375"), a * b);
        Assert.Equal(Decimal64.Parse("6"), a / b);
        Assert.Equal(Decimal64.Parse("0"), a % b);
        Assert.Equal(Decimal64.Parse("-1.5"), -a);

        var counter = Decimal64.Parse("41");
        counter++;
        Assert.Equal(Decimal64.Parse("42"), counter);
        counter--;
        Assert.Equal(Decimal64.Parse("41"), counter);
    }

    [Theory]
    [InlineData("0", true, true, false)]
    [InlineData("2", true, true, false)]
    [InlineData("3", true, false, true)]
    [InlineData("2.0", true, true, false)]
    [InlineData("3.0", true, false, true)]
    [InlineData("2.5", false, false, false)]
    [InlineData("1E+3", true, true, false)]
    [InlineData("NaN", false, false, false)]
    [InlineData("Infinity", false, false, false)]
    public void IntegerPredicates(string text, bool isInteger, bool isEven, bool isOdd)
    {
        var value = Decimal64.Parse(text);

        Assert.Equal(isInteger, Decimal64.IsInteger(value));
        Assert.Equal(isEven, Decimal64.IsEvenInteger(value));
        Assert.Equal(isOdd, Decimal64.IsOddInteger(value));
    }

    [Fact]
    public void ConversionsFromTheBuiltInTypes()
    {
        Assert.Equal(Decimal64.Parse("42"), Decimal64.CreateChecked(42));
        Assert.Equal(Decimal64.Parse("-42"), Decimal64.CreateChecked(-42L));
        Assert.Equal(Decimal64.Parse("255"), Decimal64.CreateChecked((byte)255));
        Assert.Equal(Decimal64.Parse("1.5"), Decimal64.CreateChecked(1.5));

        // A double reads as the decimal its text says, not the binary fraction beneath it.
        Assert.Equal(Decimal64.Parse("0.1"), Decimal64.CreateChecked(0.1));
        Assert.Equal(Decimal64.Parse("0.1"), Decimal64.CreateChecked(0.1m));
    }

    [Fact]
    public void ConversionsToTheBuiltInTypes()
    {
        var value = Decimal64.Parse("42.75");

        Assert.Equal(42, int.CreateTruncating(value));
        Assert.Equal(42L, long.CreateTruncating(value));
        Assert.Equal(42.75, double.CreateSaturating(value), 12);

        Assert.Equal(-42, int.CreateTruncating(Decimal64.Parse("-42.75")));
    }

    [Fact]
    public void RoundToDigits()
    {
        var value = Decimal64.Parse("2.345");

        Assert.Equal(Decimal64.Parse("2.35"), Decimal64.Round(value, 2, MidpointRounding.AwayFromZero));
        Assert.Equal(Decimal64.Parse("2.34"), Decimal64.Round(value, 2, MidpointRounding.ToEven));
        Assert.Equal(Decimal64.Parse("2.3"), Decimal64.Round(value, 1, MidpointRounding.ToZero));
    }

    [Fact]
    public void FormatStrings()
    {
        var value = Decimal64.Parse("1234.5");

        // The default is the specification's scientific form, cohort and all.
        Assert.Equal("1234.5", value.ToString());
        Assert.Equal("1234.5", value.ToString(null, CultureInfo.InvariantCulture));
        Assert.Equal("1234.50", value.ToString("F2", CultureInfo.InvariantCulture));

        // Half to even, matching what the framework does for fixed-point output.
        Assert.Equal("1234", value.ToString("F0", CultureInfo.InvariantCulture));
        Assert.Equal("1,234.50", value.ToString("N2", CultureInfo.InvariantCulture));

        // Fixed-point keeps every digit, which is the point of a 34-digit format; routing
        // it through double would have stopped at the seventeenth.
        var wide = Decimal128.Parse("1.234567890123456789012345678901234");
        Assert.Equal("1.2345678901234567890123456789012", wide.ToString("F31", CultureInfo.InvariantCulture));

        Span<char> buffer = stackalloc char[32];
        Assert.True(value.TryFormat(buffer, out var written, "F2", CultureInfo.InvariantCulture));
        Assert.Equal("1234.50", buffer[..written].ToString());

        Assert.False(value.TryFormat(stackalloc char[2], out _, default, null));
    }

    [Fact]
    public void ParseThroughTheInterfaces()
    {
        Assert.Equal(Decimal64.Parse("1.5"), Decimal64.Parse("1.5", CultureInfo.InvariantCulture));
        Assert.True(Decimal64.TryParse("1.5", CultureInfo.InvariantCulture, out var parsed));
        Assert.Equal(Decimal64.Parse("1.5"), parsed);
        Assert.False(Decimal64.TryParse("1..5", CultureInfo.InvariantCulture, out _));
    }

    [Fact]
    public void ExponentAndSignificandCanBeWritten()
    {
        IFloatingPoint<Decimal64> value = Decimal64.Parse("7.50");

        Span<byte> exponent = stackalloc byte[8];
        Assert.True(value.TryWriteExponentBigEndian(exponent, out var exponentBytes));
        Assert.Equal(4, exponentBytes);

        Span<byte> significand = stackalloc byte[16];
        Assert.True(value.TryWriteSignificandLittleEndian(significand, out var significandBytes));
        Assert.Equal(8, significandBytes);

        // 7.50 is a coefficient of 750 at an exponent of -2.
        Assert.Equal(750UL, BitConverter.ToUInt64(significand[..significandBytes]));
    }

    [Fact]
    public void ConstantsAreAtTheFormatsPrecision()
    {
        Assert.Equal("2.718281828459045", Decimal64.E.ToString());
        Assert.Equal("3.141592653589793", Decimal64.Pi.ToString());
        Assert.Equal("6.283185307179586", Decimal64.Tau.ToString());
        Assert.Equal("3.141593", Decimal32.Pi.ToString());
        Assert.Equal("3.141592653589793238462643383279503", Decimal128.Pi.ToString());
    }

    [Fact]
    public void MinMaxAndMagnitude()
    {
        var small = Decimal64.Parse("-5");
        var large = Decimal64.Parse("3");

        Assert.Equal(large, Decimal64.Max(small, large, ref Unsafe()));
        Assert.Equal(small, Decimal64.Min(small, large, ref Unsafe()));
        Assert.Equal(small, Decimal64.MaxMagnitude(small, large));
        Assert.Equal(large, Decimal64.MinMagnitude(small, large));
        Assert.Equal(Decimal64.Parse("5"), Decimal64.Abs(small));
    }

    private static DecimalContext _context;

    private static ref DecimalContext Unsafe()
    {
        _context = new DecimalContext();
        return ref _context;
    }
}
