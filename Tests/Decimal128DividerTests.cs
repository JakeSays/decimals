// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Numerics;
using Decimals.Internal;
using Xunit;

namespace Decimals.Tests;

/// <summary>
/// The divider's reciprocals and its quotients and remainders, against BigInteger.
/// </summary>
public sealed class Decimal128DividerTests
{
    private static readonly BigInteger WordRange = BigInteger.One << 64;

    private static readonly BigInteger WordMask = WordRange - 1;

    [Fact]
    public void ReciprocalOfAWordIsExact()
    {
        AssertReciprocal(1UL << 63);
        AssertReciprocal((1UL << 63) + 1);
        AssertReciprocal(ulong.MaxValue - 1);
        AssertReciprocal(ulong.MaxValue);

        var random = new Random(1414213);
        for (var attempt = 0; attempt < 200000; attempt++)
        {
            AssertReciprocal(NextWord(random) | (1UL << 63));
        }

        // Divisors with long runs of ones and zeros, where a double's rounding lands the
        // seed on a boundary.
        for (var ones = 1; ones < 64; ones++)
        {
            var divisor = ulong.MaxValue << (64 - ones);
            AssertReciprocal(divisor);
            AssertReciprocal(divisor | 1);
            AssertReciprocal(divisor + 1);
        }
    }

    [Fact]
    public void ReciprocalOfTwoWordsIsExact()
    {
        AssertReciprocal(1UL << 63, 0);
        AssertReciprocal(1UL << 63, 1);
        AssertReciprocal(1UL << 63, ulong.MaxValue);
        AssertReciprocal(ulong.MaxValue, 0);
        AssertReciprocal(ulong.MaxValue, ulong.MaxValue);
        AssertReciprocal(ulong.MaxValue - 1, ulong.MaxValue);

        var random = new Random(1732050);
        for (var attempt = 0; attempt < 200000; attempt++)
        {
            AssertReciprocal(NextWord(random) | (1UL << 63), NextWord(random));
        }

        for (var ones = 1; ones < 64; ones++)
        {
            var high = ulong.MaxValue << (64 - ones);
            AssertReciprocal(high, 0);
            AssertReciprocal(high, ulong.MaxValue);
            AssertReciprocal(high + 1, 0);
            AssertReciprocal(high + 1, ulong.MaxValue);
        }
    }

    [Fact]
    public void QuotientsAndRemaindersAreExact()
    {
        var random = new Random(2236067);
        for (var attempt = 0; attempt < 100000; attempt++)
        {
            var dividend = NextDecimal(random, random.Next(1, 35));
            var divisor = NextDecimal(random, random.Next(1, 35));
            var dividendDigits = dividend.ToString(CultureInfo.InvariantCulture).Length;
            var divisorDigits = divisor.ToString(CultureInfo.InvariantCulture).Length;

            // The quotient has to stay below 10^35, which the scale the arithmetic uses
            // guarantees, as does anything less, down to none.
            var zeros = attempt % 5 == 0
                ? 0
                : Math.Max(0, Decimal128Encoding.Precision + divisorDigits - dividendDigits - random.Next(0, 3));

            AssertDivides(dividend, zeros, divisor);
        }
    }

    [Fact]
    public void QuotientsAtTheEdgesAreExact()
    {
        var largest = BigInteger.Pow(10, 34) - 1;
        AssertDivides(largest, 34, largest);
        AssertDivides(largest, 34, BigInteger.Pow(10, 33));
        AssertDivides(BigInteger.One, 67, largest);
        AssertDivides(BigInteger.One, 34, BigInteger.One);
        AssertDivides(largest, 1, BigInteger.One);
        AssertDivides(largest, 0, BigInteger.One);
        AssertDivides(largest, 0, largest);
        AssertDivides(largest, 20, (BigInteger.One << 64) - 1);
        AssertDivides(largest, 20, BigInteger.One << 64);
        AssertDivides(largest, 20, (BigInteger.One << 64) + 1);
        AssertDivides(BigInteger.Pow(10, 33), 35, BigInteger.Pow(10, 34) - 1);
        AssertDivides(BigInteger.Pow(10, 20), 33, BigInteger.Pow(10, 19) + 1);
    }

    private static void AssertReciprocal(ulong divisor)
    {
        var expected = ((WordRange * WordRange) - 1) / divisor - WordRange;
        Assert.Equal(expected, new BigInteger(Decimal128Divider.ReciprocalWord(divisor)));
    }

    private static void AssertReciprocal(ulong high, ulong low)
    {
        var divisor = (new BigInteger(high) << 64) + low;
        var expected = ((WordRange * WordRange * WordRange) - 1) / divisor - WordRange;
        Assert.Equal(expected, new BigInteger(Decimal128Divider.ReciprocalTwoWords(high, low)));
    }

    private static void AssertDivides(BigInteger dividend, int zeros, BigInteger divisor)
    {
        var scaled = dividend * BigInteger.Pow(10, zeros);
        var quotient = Decimal128Divider.Divide(FromBig(dividend), zeros, FromBig(divisor), out var remainder);
        Assert.Equal(scaled / divisor, ToBig(quotient));
        Assert.Equal(scaled % divisor, ToBig(remainder));
    }

    private static ulong NextWord(Random random)
    {
        return ((ulong)(uint)random.Next() << 40) ^ ((ulong)(uint)random.Next() << 20) ^ (uint)random.Next();
    }

    private static BigInteger NextDecimal(Random random, int digits)
    {
        var text = new char[digits];
        for (var position = 0; position < digits; position++)
        {
            text[position] = position == 0
                ? (char)('1' + random.Next(0, 9))
                : (char)('0' + random.Next(0, 10));
        }

        return BigInteger.Parse(new string(text), CultureInfo.InvariantCulture);
    }

    private static Decimal128Integer FromBig(BigInteger value)
    {
        return new Decimal128Integer((ulong)(value >> 64), (ulong)(value & WordMask));
    }

    private static BigInteger ToBig(Decimal128Integer value)
    {
        return (new BigInteger(value.High) << 64) + value.Low;
    }
}
