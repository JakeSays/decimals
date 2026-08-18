// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Numerics;

namespace Decimals.Tests;

/// <summary>
/// The 256-bit intermediate, checked against <see cref="BigInteger"/> as the oracle.
/// </summary>
public class UInt256Tests
{
    private static readonly BigInteger Modulus = BigInteger.One << 256;

    [Fact]
    public void MultiplyMatchesTheOracle()
    {
        var random = new Random(20260817);
        for (var trial = 0; trial < 20000; trial++)
        {
            var left = RandomUInt128(random);
            var right = RandomUInt128(random);

            var expected = ToBig(left) * ToBig(right);
            Assert.Equal(expected, ToBig(UInt256.Multiply(left, right)));
        }
    }

    [Fact]
    public void MultiplyHandlesTheExtremes()
    {
        Assert.Equal(BigInteger.Zero, ToBig(UInt256.Multiply(UInt128.Zero, UInt128.MaxValue)));

        var expected = ToBig(UInt128.MaxValue) * ToBig(UInt128.MaxValue);
        Assert.Equal(expected, ToBig(UInt256.Multiply(UInt128.MaxValue, UInt128.MaxValue)));
    }

    [Fact]
    public void AddAndSubtractMatchTheOracle()
    {
        var random = new Random(1861);
        for (var trial = 0; trial < 20000; trial++)
        {
            var left = RandomUInt256(random);
            var right = RandomUInt256(random);

            Assert.Equal((ToBig(left) + ToBig(right)) % Modulus, ToBig(left + right));
            Assert.Equal(((ToBig(left) - ToBig(right)) % Modulus + Modulus) % Modulus, ToBig(left - right));
        }
    }

    [Fact]
    public void ShiftsMatchTheOracle()
    {
        var random = new Random(4133);
        for (var trial = 0; trial < 5000; trial++)
        {
            var value = RandomUInt256(random);
            var shift = random.Next(0, 256);

            Assert.Equal((ToBig(value) << shift) % Modulus, ToBig(value << shift));
            Assert.Equal(ToBig(value) >> shift, ToBig(value >> shift));
        }
    }

    [Fact]
    public void DivRemMatchesTheOracle()
    {
        var random = new Random(2718);
        for (var trial = 0; trial < 20000; trial++)
        {
            var value = RandomUInt256(random);
            var divisor = (ulong)random.NextInt64(1, long.MaxValue);

            var quotient = UInt256.DivRem(value, divisor, out var remainder);

            Assert.Equal(ToBig(value) / divisor, ToBig(quotient));
            Assert.Equal(ToBig(value) % divisor, remainder);
        }
    }

    /// <summary>
    /// A divisor wider than 64 bits takes the other route through
    /// <see cref="UInt256.DivRem(UInt256, UInt128, out UInt128)"/> -- bit-at-a-time long
    /// division rather than limb-at-a-time -- and only a Decimal128 coefficient is ever
    /// that wide. The 128-bit boundary and the carry out of the running remainder are the
    /// two places that route can go wrong, so the divisors below are drawn to sit around
    /// them.
    /// </summary>
    [Fact]
    public void DivRemMatchesTheOracleForDivisorsPastSixtyFourBits()
    {
        var random = new Random(1729);
        for (var trial = 0; trial < 20000; trial++)
        {
            var value = RandomUInt256(random);

            // Anywhere from just past 64 bits to the full 128, so the shifted remainder
            // reaches the top of its width often rather than once in a blue moon.
            var width = random.Next(65, 129);
            var divisor = RandomUInt128(random) >> (128 - width);
            if (divisor <= ulong.MaxValue)
            {
                divisor |= UInt128.One << 64;
            }

            var quotient = UInt256.DivRem(value, divisor, out var remainder);

            Assert.Equal(ToBig(value) / ToBig(divisor), ToBig(quotient));
            Assert.Equal(ToBig(value) % ToBig(divisor), ToBig(remainder));
        }
    }

    [Fact]
    public void DivRemHandlesTheWideExtremes()
    {
        foreach (var divisor in new[]
        {
            UInt128.MaxValue,
            UInt128.MaxValue - UInt128.One,
            UInt128.One << 127,
            (UInt128.One << 64) + UInt128.One,
            UInt128.One << 64
        })
        {
            foreach (var value in new[]
            {
                new UInt256(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue),
                new UInt256(UInt128.MaxValue),
                UInt256.Multiply(UInt128.MaxValue, UInt128.MaxValue),
                UInt256.One,
                UInt256.Zero
            })
            {
                var quotient = UInt256.DivRem(value, divisor, out var remainder);

                Assert.Equal(ToBig(value) / ToBig(divisor), ToBig(quotient));
                Assert.Equal(ToBig(value) % ToBig(divisor), ToBig(remainder));
            }
        }
    }

    [Fact]
    public void NarrowCountDigitsMatchesTheOracle()
    {
        for (var power = 0; power <= 38; power++)
        {
            var value = BigInteger.Pow(10, power);
            Assert.Equal(power + 1, DecimalRounder.CountDigits(FromBig(value).ToUInt128()));
            if (power > 0)
            {
                Assert.Equal(power, DecimalRounder.CountDigits(FromBig(value - BigInteger.One).ToUInt128()));
            }
        }

        Assert.Equal(1, DecimalRounder.CountDigits(UInt128.Zero));
        Assert.Equal(39, DecimalRounder.CountDigits(UInt128.MaxValue));

        var random = new Random(1414213);
        for (var trial = 0; trial < 20000; trial++)
        {
            var value = RandomUInt128(random) >> random.Next(0, 128);
            Assert.Equal(ToBig(value).ToString().Length, DecimalRounder.CountDigits(value));
        }
    }

    [Fact]
    public void DivideByPowerOfTenMatchesTheOracle()
    {
        var random = new Random(3141);
        for (var trial = 0; trial < 5000; trial++)
        {
            var value = RandomUInt256(random);
            var power = random.Next(0, 78);

            var quotient = UInt256.DivideByPowerOfTen(value, power, out var hasRemainder);

            var divisor = BigInteger.Pow(10, power);
            Assert.Equal(ToBig(value) / divisor, ToBig(quotient));
            Assert.Equal(!(ToBig(value) % divisor).IsZero, hasRemainder);
        }
    }

    [Fact]
    public void MultiplyByPowerOfTenMatchesTheOracle()
    {
        var random = new Random(1618);
        for (var trial = 0; trial < 5000; trial++)
        {
            // Kept narrow enough that the product stays inside 256 bits, which is the
            // contract: decimal arithmetic only ever scales to a width it has checked.
            var value = new UInt256(RandomUInt128(random) >> 40);
            var power = random.Next(0, 34);

            var scaled = UInt256.MultiplyByPowerOfTen(value, power);
            Assert.Equal(ToBig(value) * BigInteger.Pow(10, power), ToBig(scaled));
        }
    }

    /// <summary>
    /// The count is estimated from the bit length and corrected once, so what needs
    /// checking is that one correction is always enough. The powers of ten and the values
    /// either side of them are where an off-by-one would show.
    /// </summary>
    [Fact]
    public void CountDigitsMatchesTheOracle()
    {
        Assert.Equal(1, UInt256.Zero.CountDigits());

        for (var power = 0; power <= PowersOfTen.MaxUInt256Power; power++)
        {
            var value = BigInteger.Pow(10, power);
            Assert.Equal(power + 1, FromBig(value).CountDigits());
            Assert.Equal(power + 1, FromBig(value + BigInteger.One).CountDigits());
            if (power > 0)
            {
                Assert.Equal(power, FromBig(value - BigInteger.One).CountDigits());
            }
        }

        var random = new Random(1729);
        for (var trial = 0; trial < 20000; trial++)
        {
            // Every width, so both the 128-bit path and the wider one are exercised.
            var value = RandomUInt256(random) >> random.Next(0, 256);
            var expected = ToBig(value).ToString(CultureInfo.InvariantCulture).Length;

            Assert.Equal(expected, value.CountDigits());
        }
    }

    [Fact]
    public void ComparisonMatchesTheOracle()
    {
        var random = new Random(6857);
        for (var trial = 0; trial < 20000; trial++)
        {
            var left = RandomUInt256(random) >> random.Next(0, 256);
            var right = RandomUInt256(random) >> random.Next(0, 256);

            Assert.Equal(ToBig(left).CompareTo(ToBig(right)), left.CompareTo(right));
            Assert.Equal(ToBig(left) == ToBig(right), left == right);
            Assert.Equal(ToBig(left) < ToBig(right), left < right);
            Assert.Equal(ToBig(left) >= ToBig(right), left >= right);
        }
    }

    [Fact]
    public void ToStringMatchesTheOracle()
    {
        var random = new Random(8191);
        for (var trial = 0; trial < 2000; trial++)
        {
            var value = RandomUInt256(random) >> random.Next(0, 256);
            Assert.Equal(ToBig(value).ToString(CultureInfo.InvariantCulture), value.ToString());
        }
    }

    [Fact]
    public void ToUInt128RecoversNarrowValues()
    {
        var random = new Random(5040);
        for (var trial = 0; trial < 2000; trial++)
        {
            var narrow = RandomUInt128(random);
            var value = new UInt256(narrow);

            Assert.True(value.FitsUInt128);
            Assert.Equal(narrow, value.ToUInt128());
        }

        Assert.False(UInt256.Multiply(UInt128.MaxValue, UInt128.MaxValue).FitsUInt128);
    }

    private static UInt256 FromBig(BigInteger value)
    {
        return new UInt256(
            (ulong)((value >> 192) & ulong.MaxValue),
            (ulong)((value >> 128) & ulong.MaxValue),
            (ulong)((value >> 64) & ulong.MaxValue),
            (ulong)(value & ulong.MaxValue));
    }

    private static UInt128 RandomUInt128(Random random)
    {
        return new UInt128((ulong)random.NextInt64(), (ulong)random.NextInt64());
    }

    private static UInt256 RandomUInt256(Random random)
    {
        return new UInt256(
            (ulong)random.NextInt64(),
            (ulong)random.NextInt64(),
            (ulong)random.NextInt64(),
            (ulong)random.NextInt64());
    }

    private static BigInteger ToBig(UInt128 value)
    {
        return ((BigInteger)(ulong)(value >> 64) << 64) | (ulong)value;
    }

    // Built from the limbs rather than from ToString, so the formatting test is not
    // checking itself.
    private static BigInteger ToBig(UInt256 value)
    {
        return ((BigInteger)value.Limb3 << 192)
            | ((BigInteger)value.Limb2 << 128)
            | ((BigInteger)value.Limb1 << 64)
            | value.Limb0;
    }
}
