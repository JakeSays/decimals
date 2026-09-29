// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Numerics;
using Decimals.Internal;


namespace Decimals.Tests;

/// <summary>
/// Tests the tables used by <see cref="Decimal32"/>: the reciprocals used to divide by a
/// power of ten, and the digit count. The reciprocal constants were generated and pasted
/// into the source. These tests recompute them, then check the quotients against real
/// division over the full range of numerators.
/// </summary>
public class Decimal32TableTests
{
    /// <summary>
    /// Checks the condition that makes each multiplier and shift exact. With
    /// <c>M = ceil(2^s / d)</c> and <c>e = M*d - 2^s</c>, the quotient is correct for every
    /// numerator up to <c>n_max</c> if and only if <c>n_max * e &lt; 2^s</c>. Checking this
    /// with big integers proves the result for the whole 64-bit range. Sampling could not.
    /// </summary>
    [Fact]
    public void EveryReciprocalIsExactAcrossTheWholeRange()
    {
        for (var power = 1; power <= Decimal32Tables.MaxPower; power++)
        {
            var divisor = BigInteger.Pow(5, power);
            var shift = Decimal32Tables.Shift(power);
            var multiplier = new BigInteger(Decimal32Tables.Multiplier(power));
            var scale = BigInteger.One << (64 + shift);

            Assert.Equal((int)divisor.GetBitLength() - 1, shift);
            Assert.Equal(BigInteger.Divide(scale + divisor - 1, divisor), multiplier);

            // The pre-shift divides by 2^p, which leaves a division by 5^p and a numerator
            // of 64 - p bits.
            var numeratorLimit = (BigInteger.One << (64 - power)) - 1;
            var error = (multiplier * divisor) - scale;

            Assert.True(numeratorLimit * error < scale,
                $"power {power}: n_max * e is not below 2^s, so some numerator divides wrong");
        }
    }

    [Fact]
    public void MatchesDivisionOnTheBoundaries()
    {
        for (var power = 1; power <= Decimal32Tables.MaxPower; power++)
        {
            var divisor = Decimal32Tables.PowerOfTen(power);

            foreach (var value in Boundaries(divisor))
            {
                var quotient = Decimal32Tables.DivRemPowerOfTen(value, power, out var remainder);
                Assert.Equal(value / divisor, quotient);
                Assert.Equal(value % divisor, remainder);
            }
        }
    }

    [Fact]
    public void MatchesDivisionOnRandomValues()
    {
        var random = new Random(20260922);

        for (var power = 1; power <= Decimal32Tables.MaxPower; power++)
        {
            var divisor = Decimal32Tables.PowerOfTen(power);

            for (var attempt = 0; attempt < 5000; attempt++)
            {
                var value = NextUInt64(random);
                AssertDivides(value, power, divisor);

                // Also test an exact multiple and its neighbors. An off-by-one error in the
                // multiplier shows up there first.
                var exact = (value / divisor) * divisor;
                AssertDivides(exact, power, divisor);

                if (exact > 0)
                {
                    AssertDivides(exact - 1, power, divisor);
                }

                if (exact < ulong.MaxValue)
                {
                    AssertDivides(exact + 1, power, divisor);
                }
            }
        }
    }

    [Fact]
    public void CountsDigitsAtEveryPowerBoundary()
    {
        Assert.Equal(1, Decimal32Tables.CountDigits(0));
        Assert.Equal(1, Decimal32Tables.CountDigits(1));
        Assert.Equal(1, Decimal32Tables.CountDigits(9));
        Assert.Equal(20, Decimal32Tables.CountDigits(ulong.MaxValue));

        for (var power = 1; power <= Decimal32Tables.MaxPower; power++)
        {
            var value = Decimal32Tables.PowerOfTen(power);
            Assert.Equal(power + 1, Decimal32Tables.CountDigits(value));
            Assert.Equal(power, Decimal32Tables.CountDigits(value - 1));
        }
    }

    [Fact]
    public void CountsDigitsOnRandomValues()
    {
        var random = new Random(27182);
        for (var attempt = 0; attempt < 100000; attempt++)
        {
            var value = NextUInt64(random) >> random.Next(0, 64);
            Assert.Equal(value.ToString().Length, Decimal32Tables.CountDigits(value));
        }
    }

    private static void AssertDivides(ulong value, int power, ulong divisor)
    {
        var quotient = Decimal32Tables.DivRemPowerOfTen(value, power, out var remainder);
        Assert.Equal(value / divisor, quotient);
        Assert.Equal(value % divisor, remainder);
    }

    private static IEnumerable<ulong> Boundaries(ulong divisor)
    {
        yield return 0;
        yield return 1;
        yield return divisor - 1;
        yield return divisor;
        yield return divisor + 1;
        yield return (divisor * 2) - 1;
        yield return divisor * 2;
        yield return ulong.MaxValue;
        yield return ulong.MaxValue - 1;
        yield return ulong.MaxValue / divisor * divisor;
        yield return 9999999;
        yield return 10000000;
        yield return 99999999999999;
        yield return 9999999999999999999;
    }

    private static ulong NextUInt64(Random random)
    {
        Span<byte> bytes = stackalloc byte[8];
        random.NextBytes(bytes);
        return BitConverter.ToUInt64(bytes);
    }
}
