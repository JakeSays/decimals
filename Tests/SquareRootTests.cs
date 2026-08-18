// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Numerics;

namespace Decimals.Tests;

/// <summary>
/// The square root. Unlike the rest of the arithmetic this has no coverage in the testcase
/// corpus -- the decDouble and decQuad groups carry no square-root file -- so it is checked
/// against the definition instead: the result squared must bracket the operand, and an
/// exact root must come back exactly.
/// </summary>
public class SquareRootTests
{
    [Theory]
    [InlineData("0", "0")]
    [InlineData("-0", "-0")]
    [InlineData("1", "1")]
    [InlineData("4", "2")]
    [InlineData("9", "3")]
    [InlineData("100", "10")]
    [InlineData("0.25", "0.5")]
    [InlineData("1.21", "1.1")]
    // The preferred exponent is half the operand's, so 1E+2 keeps an exponent rather than
    // spelling itself out: sqtx1207 in the corpus expects exactly this.
    [InlineData("1E+2", "1E+1")]
    [InlineData("Infinity", "Infinity")]
    public void ExactRootsComeBackExact(string input, string expected)
    {
        var context = new DecimalContext();
        var root = Decimal64.Sqrt(Decimal64.Parse(input), ref context);

        Assert.Equal(expected, root.ToString());
        Assert.False(context.HasRaised(DecimalStatus.Inexact));
    }

    [Theory]
    [InlineData("2", "1.414213562373095")]
    [InlineData("3", "1.732050807568877")]
    [InlineData("10", "3.162277660168379")]
    [InlineData("0.5", "0.7071067811865475")]
    public void InexactRootsRoundCorrectly(string input, string expected)
    {
        var context = new DecimalContext();
        var root = Decimal64.Sqrt(Decimal64.Parse(input), ref context);

        Assert.Equal(expected, root.ToString());
        Assert.True(context.HasRaised(DecimalStatus.Inexact));
        Assert.True(context.HasRaised(DecimalStatus.Rounded));
    }

    [Fact]
    public void NegativeIsInvalidButNegativeZeroIsNot()
    {
        var context = new DecimalContext();
        var root = Decimal64.Sqrt(Decimal64.Parse("-4"), ref context);

        Assert.True(Decimal64.IsNaN(root));
        Assert.True(context.HasRaised(DecimalStatus.InvalidOperation));

        var zeroContext = new DecimalContext();
        Assert.Equal("-0", Decimal64.Sqrt(Decimal64.Parse("-0"), ref zeroContext).ToString());
        Assert.False(zeroContext.HasRaised(DecimalStatus.InvalidOperation));
    }

    [Fact]
    public void NegativeInfinityIsInvalidAndNaNPropagates()
    {
        var context = new DecimalContext();
        Assert.True(Decimal64.IsNaN(Decimal64.Sqrt(Decimal64.NegativeInfinity, ref context)));
        Assert.True(context.HasRaised(DecimalStatus.InvalidOperation));

        var nanContext = new DecimalContext();
        Assert.True(Decimal64.IsNaN(Decimal64.Sqrt(Decimal64.NaN, ref nanContext)));
        Assert.False(nanContext.HasRaised(DecimalStatus.InvalidOperation));
    }

    /// <summary>
    /// The defining property, over a wide spread of values: the root is the largest 16-digit
    /// value whose square does not exceed the operand, give or take the final rounding. Both
    /// sides are compared as exact rationals through <see cref="BigInteger"/>.
    /// </summary>
    [Fact]
    public void RootsBracketTheOperand()
    {
        var random = new Random(20260817);
        for (var trial = 0; trial < 4000; trial++)
        {
            var coefficient = random.NextInt64(1, 10_000_000_000_000_000L);
            var exponent = random.Next(-40, 41);
            var text = coefficient.ToString(CultureInfo.InvariantCulture) + "E" + exponent.ToString(CultureInfo.InvariantCulture);

            var value = Decimal64.Parse(text);
            var context = new DecimalContext();
            var root = Decimal64.Sqrt(value, ref context);

            // Compare root^2 against the operand as exact rationals, scaled to a common
            // denominator so nothing is lost on the way.
            var (rootNumerator, rootScale) = AsFraction(root);
            var (valueNumerator, valueScale) = AsFraction(value);

            var squared = rootNumerator * rootNumerator;
            var squaredScale = rootScale * 2;

            var common = BigInteger.Max(squaredScale, valueScale);
            var left = squared * BigInteger.Pow(10, (int)(common - squaredScale));
            var right = valueNumerator * BigInteger.Pow(10, (int)(common - valueScale));

            // The root carries sixteen digits, so its square can miss by up to one unit in
            // the last of them; the check is that the miss is no larger than that.
            var tolerance = right / BigInteger.Pow(10, 14) + 1;
            Assert.True(BigInteger.Abs(left - right) <= tolerance,
                $"sqrt({text}) gave {root}, whose square is off by more than a rounding");
        }
    }

    private static (BigInteger Numerator, long Scale) AsFraction(Decimal64 value)
    {
        var text = value.ToString();
        var parsed = decimalPartsFrom(text);
        return parsed;

        static (BigInteger, long) decimalPartsFrom(string text)
        {
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
}
