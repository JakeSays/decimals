// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Numerics;
using Decimals.Internal;


namespace Decimals.Tests;

/// <summary>
/// The tables underneath <see cref="Decimal64"/>: the reciprocals that stand in for dividing
/// by a power of ten, and the digit count. The reciprocal constants were found by a
/// generator and pasted in, so these re-derive them rather than trusting the paste, and
/// then check the quotient against real division over the numerators that reach it.
/// </summary>
public class Decimal64TableTests
{
    /// <summary>
    /// The condition that makes a pair exact rather than approximate. With
    /// <c>M = ceil(2^s / d)</c> and <c>e = M*d - 2^s</c>, the quotient is right for every
    /// numerator up to <c>n_max</c> exactly when <c>n_max * e &lt; 2^s</c>. Checking that
    /// with big integers is a proof for the whole 64-bit range, which no amount of sampling
    /// would give.
    /// </summary>
    [Fact]
    public void EveryReciprocalIsExactAcrossTheWholeRange()
    {
        for (var power = 1; power <= Decimal64Tables.MaxPower; power++)
        {
            var divisor = BigInteger.Pow(5, power);
            var shift = Decimal64Tables.Shift(power);
            var multiplier = new BigInteger(Decimal64Tables.Multiplier(power));
            var scale = BigInteger.One << (64 + shift);

            Assert.Equal((int)divisor.GetBitLength() - 1, shift);
            Assert.Equal(BigInteger.Divide(scale + divisor - 1, divisor), multiplier);

            // The pre-shift takes the 2^p half, leaving 5^p to divide by and a numerator of
            // 64 - p bits.
            var numeratorLimit = (BigInteger.One << (64 - power)) - 1;
            var error = (multiplier * divisor) - scale;

            Assert.True(numeratorLimit * error < scale,
                $"power {power}: n_max * e is not below 2^s, so some numerator divides wrong");
        }
    }

    [Fact]
    public void MatchesDivisionOnTheBoundaries()
    {
        for (var power = 1; power <= Decimal64Tables.MaxPower; power++)
        {
            var divisor = Decimal64Tables.PowerOfTen(power);

            foreach (var value in Boundaries(divisor))
            {
                var quotient = Decimal64Tables.DivRemPowerOfTen(value, power, out var remainder);
                Assert.Equal(value / divisor, quotient);
                Assert.Equal(value % divisor, remainder);
            }
        }
    }

    [Fact]
    public void MatchesDivisionOnRandomValues()
    {
        var random = new Random(20260911);

        for (var power = 1; power <= Decimal64Tables.MaxPower; power++)
        {
            var divisor = Decimal64Tables.PowerOfTen(power);

            for (var attempt = 0; attempt < 5000; attempt++)
            {
                var value = NextUInt64(random);
                AssertDivides(value, power, divisor);

                // And a value that divides exactly, plus its neighbors, since those are
                // where an off-by-one in the multiplier would show first.
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
        Assert.Equal(1, Decimal64Tables.CountDigits(0));
        Assert.Equal(1, Decimal64Tables.CountDigits(1));
        Assert.Equal(1, Decimal64Tables.CountDigits(9));
        Assert.Equal(20, Decimal64Tables.CountDigits(ulong.MaxValue));

        for (var power = 1; power <= Decimal64Tables.MaxPower; power++)
        {
            var value = Decimal64Tables.PowerOfTen(power);
            Assert.Equal(power + 1, Decimal64Tables.CountDigits(value));
            Assert.Equal(power, Decimal64Tables.CountDigits(value - 1));
        }
    }

    [Fact]
    public void CountsDigitsOnRandomValues()
    {
        var random = new Random(31415);
        for (var attempt = 0; attempt < 100000; attempt++)
        {
            var value = NextUInt64(random) >> random.Next(0, 64);
            Assert.Equal(value.ToString().Length, Decimal64Tables.CountDigits(value));
        }
    }

    private static void AssertDivides(ulong value, int power, ulong divisor)
    {
        var quotient = Decimal64Tables.DivRemPowerOfTen(value, power, out var remainder);
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
        yield return 9999999999999999;
        yield return 10000000000000000;
        yield return 9999999999999999999;
    }

    private static ulong NextUInt64(Random random)
    {
        Span<byte> bytes = stackalloc byte[8];
        random.NextBytes(bytes);
        return BitConverter.ToUInt64(bytes);
    }
}
