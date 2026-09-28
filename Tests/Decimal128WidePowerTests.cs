// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Numerics;
using Decimals.Internal;
using Xunit;

namespace Decimals.Tests;

/// <summary>
/// The constants behind division by a power of ten past one word, and the reciprocal
/// seeds, re-derived, and the four-word division by those powers against BigInteger.
/// </summary>
public sealed class Decimal128WidePowerTests
{
    private static readonly BigInteger WordRange = BigInteger.One << 64;

    private static readonly BigInteger WordMask = WordRange - 1;

    [Fact]
    public void ReciprocalSeedsAreTheFormula()
    {
        for (var topBits = 256; topBits < 512; topBits++)
        {
            Assert.Equal((ulong)(523520 / topBits), Decimal128Tables.ReciprocalSeed(topBits));
        }
    }

    [Fact]
    public void WideConstantsAreTheDerivedOnes()
    {
        for (var power = Decimal128Tables.MaxPower + 1; power <= Decimal128Tables.MaxWidePower; power++)
        {
            var value = BigInteger.Pow(10, power);
            var shift = 64 - (int)(value >> 64).GetBitLength();
            var normalized = value << shift;
            var divisorHigh = (ulong)(normalized >> 64);
            var divisorLow = (ulong)(normalized & WordMask);
            var inverse = (ulong)(((WordRange * WordRange * WordRange) - 1) / normalized - WordRange);

            Assert.Equal(shift, Decimal128Tables.WideDivisorShift(power));
            Assert.Equal(new Decimal128Integer(divisorHigh, divisorLow), Decimal128Tables.WideNormalizedDivisor(power));
            Assert.Equal(inverse, Decimal128Tables.WideInverse(power));
            Assert.Equal(inverse, Decimal128Divider.ReciprocalTwoWords(divisorHigh, divisorLow));
        }
    }

    [Fact]
    public void FourWordWideDivisionMatchesBigIntegers()
    {
        var random = new Random(14142135);

        for (var power = Decimal128Tables.MaxPower + 1; power <= Decimal128Tables.MaxWidePower; power++)
        {
            var divisor = BigInteger.Pow(10, power);
            var limit = divisor << 128;

            for (var attempt = 0; attempt < 3000; attempt++)
            {
                var value = NextFourWords(random) >> random.Next(0, 130);
                var fits = Decimal128Tables.TryDivRemWidePowerOfTen(FromBigLong(value), power, out var quotient,
                    out var remainder);

                Assert.Equal(value < limit, fits);
                if (fits)
                {
                    Assert.Equal(value / divisor, ToBig(quotient));
                    Assert.Equal(value % divisor, ToBig(remainder));
                }
            }

            var edge = limit - 1;
            Assert.True(Decimal128Tables.TryDivRemWidePowerOfTen(FromBigLong(edge), power, out var edgeQuotient,
                out var edgeRemainder));
            Assert.Equal(edge / divisor, ToBig(edgeQuotient));
            Assert.Equal(edge % divisor, ToBig(edgeRemainder));
            Assert.False(Decimal128Tables.TryDivRemWidePowerOfTen(FromBigLong(limit), power, out _, out _));
        }
    }

    private static BigInteger NextFourWords(Random random)
    {
        var value = BigInteger.Zero;
        for (var word = 0; word < 4; word++)
        {
            value = (value << 64) + NextWord(random);
        }

        return value;
    }

    private static ulong NextWord(Random random)
    {
        return ((ulong)(uint)random.Next() << 40) ^ ((ulong)(uint)random.Next() << 20) ^ (uint)random.Next();
    }

    private static Decimal128LongInteger FromBigLong(BigInteger value)
    {
        return new Decimal128LongInteger((ulong)((value >> 192) & WordMask), (ulong)((value >> 128) & WordMask),
            (ulong)((value >> 64) & WordMask), (ulong)(value & WordMask));
    }

    private static BigInteger ToBig(Decimal128Integer value)
    {
        return (new BigInteger(value.High) << 64) + value.Low;
    }
}
