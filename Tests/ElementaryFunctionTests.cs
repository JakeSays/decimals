// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Numerics;

namespace Decimals.Tests;

/// <summary>
/// Exp, Log, Log10, and Pow. The corpus covers them at length -- the generated groups hold
/// 4,882 cases -- so what is here is the shape of the contract: which results are exact,
/// which operands are invalid, and that the functions still invert each other once the
/// wide intermediates have been rounded away.
/// </summary>
public class ElementaryFunctionTests
{
    [Theory]
    [InlineData("0", "1")]
    [InlineData("-0", "1")]
    [InlineData("-Infinity", "0")]
    [InlineData("Infinity", "Infinity")]
    public void ExpIsExactWhereItCanBe(string input, string expected)
    {
        var context = new DecimalContext();
        var result = Decimal64.Exp(Decimal64.Parse(input), ref context);

        Assert.Equal(expected, result.ToString());
        Assert.False(context.HasRaised(DecimalStatus.Inexact));
    }

    /// <summary>
    /// A finite exp is always inexact and always full precision, trailing zeros included:
    /// e to any non-zero power is irrational, so the last digit is never the true one.
    /// </summary>
    [Theory]
    [InlineData("1", "2.718281828459045")]
    [InlineData("-1", "0.3678794411714423")]
    [InlineData("10", "22026.46579480672")]
    [InlineData("0.00001", "1.000010000050000")]
    public void ExpRoundsToSixteenDigits(string input, string expected)
    {
        var context = new DecimalContext();
        var result = Decimal64.Exp(Decimal64.Parse(input), ref context);

        Assert.Equal(expected, result.ToString());
        Assert.True(context.HasRaised(DecimalStatus.Inexact));
        Assert.True(context.HasRaised(DecimalStatus.Rounded));
    }

    [Fact]
    public void ExpOverflowsAndUnderflowsAtTheEndsOfTheFormat()
    {
        var high = new DecimalContext();
        Assert.Equal("Infinity", Decimal64.Exp(Decimal64.Parse("1000"), ref high).ToString());
        Assert.True(high.HasRaised(DecimalStatus.Overflow));

        var low = new DecimalContext();
        Assert.Equal("0E-398", Decimal64.Exp(Decimal64.Parse("-1000"), ref low).ToString());
        Assert.True(low.HasRaised(DecimalStatus.Underflow));
        Assert.True(low.HasRaised(DecimalStatus.Subnormal));
        Assert.True(low.HasRaised(DecimalStatus.Clamped));
    }

    [Theory]
    [InlineData("1", "0")]
    [InlineData("0", "-Infinity")]
    [InlineData("-0", "-Infinity")]
    [InlineData("Infinity", "Infinity")]
    public void LogIsExactWhereItCanBe(string input, string expected)
    {
        var context = new DecimalContext();
        var result = Decimal64.Log(Decimal64.Parse(input), ref context);

        Assert.Equal(expected, result.ToString());
        Assert.False(context.HasRaised(DecimalStatus.Inexact));
    }

    [Theory]
    [InlineData("2", "0.6931471805599453")]
    [InlineData("10", "2.302585092994046")]
    [InlineData("0.367879441", "-1.000000000466029")]
    public void LogRoundsToSixteenDigits(string input, string expected)
    {
        var context = new DecimalContext();
        var result = Decimal64.Log(Decimal64.Parse(input), ref context);

        Assert.Equal(expected, result.ToString());
        Assert.True(context.HasRaised(DecimalStatus.Inexact));
    }

    /// <summary>
    /// A power of ten is the one case where the base-ten logarithm is a whole number, and
    /// it comes back as one without a condition raised.
    /// </summary>
    [Theory]
    [InlineData("1", "0")]
    [InlineData("10", "1")]
    [InlineData("1000", "3")]
    [InlineData("0.001", "-3")]
    [InlineData("1E+300", "300")]
    public void Log10OfAPowerOfTenIsExact(string input, string expected)
    {
        var context = new DecimalContext();
        var result = Decimal64.Log10(Decimal64.Parse(input), ref context);

        Assert.Equal(expected, result.ToString());
        Assert.False(context.HasRaised(DecimalStatus.Inexact));
    }

    [Theory]
    [InlineData("2", "0.3010299956639812")]
    [InlineData("7", "0.8450980400142568")]
    public void Log10RoundsToSixteenDigits(string input, string expected)
    {
        var context = new DecimalContext();
        var result = Decimal64.Log10(Decimal64.Parse(input), ref context);

        Assert.Equal(expected, result.ToString());
        Assert.True(context.HasRaised(DecimalStatus.Inexact));
    }

    [Fact]
    public void LogarithmsOfNegativesAreInvalid()
    {
        foreach (var text in new[] { "-1", "-0.5", "-Infinity" })
        {
            var logContext = new DecimalContext();
            Assert.True(Decimal64.IsNaN(Decimal64.Log(Decimal64.Parse(text), ref logContext)));
            Assert.True(logContext.HasRaised(DecimalStatus.InvalidOperation));

            var log10Context = new DecimalContext();
            Assert.True(Decimal64.IsNaN(Decimal64.Log10(Decimal64.Parse(text), ref log10Context)));
            Assert.True(log10Context.HasRaised(DecimalStatus.InvalidOperation));
        }
    }

    /// <summary>
    /// An integer exponent is applied by repeated squaring rather than through exp and ln,
    /// which is what lets the result be exact.
    /// </summary>
    [Theory]
    [InlineData("2", "10", "1024")]
    [InlineData("2", "-3", "0.125")]
    [InlineData("0.5", "2", "0.25")]
    [InlineData("-2", "3", "-8")]
    [InlineData("-2", "2", "4")]
    [InlineData("7", "0", "1")]
    [InlineData("Infinity", "0", "1")]
    public void IntegerPowersAreExact(string left, string right, string expected)
    {
        var context = new DecimalContext();
        var result = Decimal64.Pow(Decimal64.Parse(left), Decimal64.Parse(right), ref context);

        Assert.Equal(expected, result.ToString());
        Assert.False(context.HasRaised(DecimalStatus.Inexact));
    }

    [Fact]
    public void ZeroToTheZeroAndNegativeToAFractionAreInvalid()
    {
        var zeroContext = new DecimalContext();
        Assert.True(Decimal64.IsNaN(Decimal64.Pow(Decimal64.Zero, Decimal64.Zero, ref zeroContext)));
        Assert.True(zeroContext.HasRaised(DecimalStatus.InvalidOperation));

        var fractionContext = new DecimalContext();
        var root = Decimal64.Pow(Decimal64.Parse("-2"), Decimal64.Parse("0.5"), ref fractionContext);
        Assert.True(Decimal64.IsNaN(root));
        Assert.True(fractionContext.HasRaised(DecimalStatus.InvalidOperation));
    }

    /// <summary>
    /// The infinite exponent cases turn on where the base sits relative to one, and one
    /// itself is deemed inexact so that it comes back padded to full precision.
    /// </summary>
    [Theory]
    [InlineData("0.5", "Infinity", "0")]
    [InlineData("0.5", "-Infinity", "Infinity")]
    [InlineData("2", "Infinity", "Infinity")]
    [InlineData("2", "-Infinity", "0")]
    [InlineData("1", "Infinity", "1.000000000000000")]
    public void InfinitePowers(string left, string right, string expected)
    {
        var context = new DecimalContext();
        var result = Decimal64.Pow(Decimal64.Parse(left), Decimal64.Parse(right), ref context);

        Assert.Equal(expected, result.ToString());
    }

    /// <summary>
    /// The three widths run the same code at three precisions; these are decNumber's
    /// results for each.
    /// </summary>
    [Fact]
    public void EveryFormatGetsItsOwnPrecision()
    {
        Assert.Equal("2.718282", Decimal32.Exp(Decimal32.One).ToString());
        Assert.Equal("2.718281828459045", Decimal64.Exp(Decimal64.One).ToString());
        Assert.Equal("2.718281828459045235360287471352662",
            Decimal128.Exp(Decimal128.One).ToString());

        Assert.Equal("2.302585092994045684017991454684364",
            Decimal128.Log(Decimal128.Parse("10")).ToString());
        Assert.Equal("0.6931471805599453094172321214581766",
            Decimal128.Log(Decimal128.Parse("2")).ToString());
    }

    /// <summary>
    /// Exp and Log invert each other, to the accuracy the round trip allows. That accuracy
    /// is not one unit in the last place: the logarithm is known to sixteen digits, so its
    /// last digit is worth an absolute error that exp turns into a relative one, and the
    /// round trip loses a factor of ln(x). Hence the tolerance below, which grows with the
    /// logarithm rather than staying fixed.
    /// </summary>
    [Fact]
    public void ExpAndLogInvertEachOther()
    {
        var random = new Random(20260817);
        for (var trial = 0; trial < 200; trial++)
        {
            var text = RandomValue(random);
            var value = Decimal64.Parse(text);
            var roundTripped = Decimal64.Exp(Decimal64.Log(value));

            Assert.True(WithinUnitsInLastPlace(value, roundTripped, ToleranceFor(text)),
                $"exp(log({text})) gave {roundTripped}");
        }
    }

    /// <summary>
    /// The same for the other pair, ten raised to the base-ten logarithm, which loses the
    /// same factor for the same reason.
    /// </summary>
    [Fact]
    public void Log10AndPowerInvertEachOther()
    {
        var ten = Decimal64.Parse("10");
        var random = new Random(20260818);
        for (var trial = 0; trial < 200; trial++)
        {
            var text = RandomValue(random);
            var value = Decimal64.Parse(text);
            var roundTripped = Decimal64.Pow(ten, Decimal64.Log10(value));

            Assert.True(WithinUnitsInLastPlace(value, roundTripped, ToleranceFor(text)),
                $"10**log10({text}) gave {roundTripped}");
        }
    }

    private static string RandomValue(Random random)
    {
        var coefficient = random.NextInt64(1, 10_000_000_000_000_000L);
        var exponent = random.Next(-12, 13);
        return coefficient.ToString(CultureInfo.InvariantCulture) + "E"
            + exponent.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Units in the last place the round trip may miss by: the amplification factor is the
    /// logarithm itself, plus a few for the two roundings either side of it.
    /// </summary>
    private static int ToleranceFor(string text)
    {
        var magnitude = Math.Abs(Math.Log(double.Parse(text, CultureInfo.InvariantCulture)));
        return 4 + (int)magnitude;
    }

    /// <summary>
    /// Whether two values agree to all but the last few digits, compared as exact integers
    /// scaled to a common exponent.
    /// </summary>
    private static bool WithinUnitsInLastPlace(Decimal64 left, Decimal64 right, int units)
    {
        var (leftDigits, leftScale) = AsFraction(left);
        var (rightDigits, rightScale) = AsFraction(right);

        var common = Math.Max(leftScale, rightScale);
        var leftScaled = leftDigits * BigInteger.Pow(10, (int)(common - leftScale));
        var rightScaled = rightDigits * BigInteger.Pow(10, (int)(common - rightScale));

        // One unit in the last place of a sixteen-digit value is the value over 10**15.
        var tolerance = (BigInteger.Abs(leftScaled) * units / BigInteger.Pow(10, 15)) + units;
        return BigInteger.Abs(leftScaled - rightScaled) <= tolerance;
    }

    private static (BigInteger Digits, long Scale) AsFraction(Decimal64 value)
    {
        var text = value.ToString();
        var exponent = 0L;

        var separator = text.IndexOf('E');
        if (separator >= 0)
        {
            exponent = long.Parse(text[(separator + 1)..], CultureInfo.InvariantCulture);
            text = text[..separator];
        }

        var point = text.IndexOf('.');
        if (point >= 0)
        {
            exponent -= text.Length - point - 1;
            text = text.Remove(point, 1);
        }

        var digits = BigInteger.Parse(text, CultureInfo.InvariantCulture);
        if (exponent > 0)
        {
            digits *= BigInteger.Pow(10, (int)exponent);
            exponent = 0;
        }

        return (digits, -exponent);
    }
}
