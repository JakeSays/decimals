// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Numerics;
using Decimals.Internal;

namespace Decimals.Tests;

/// <summary>
/// Tests the tables used by <see cref="Decimal128"/>: the powers of ten at each width, the
/// constants used to divide by them, and the digit counts. The constants were generated and
/// pasted into the source. These tests recompute them, then check the quotients against
/// big-integer division over the full range of numerators.
/// </summary>
public class Decimal128TableTests
{
    [Fact]
    public void EveryWordReciprocalIsExactAcrossTheWholeRange()
    {
        for (var power = 1; power <= Decimal128Tables.MaxPower; power++)
        {
            var divisor = BigInteger.Pow(5, power);
            var shift = Decimal128Tables.Shift(power);
            var multiplier = new BigInteger(Decimal128Tables.Multiplier(power));
            var scale = BigInteger.One << (64 + shift);

            Assert.Equal((int)divisor.GetBitLength() - 1, shift);
            Assert.Equal(BigInteger.Divide(scale + divisor - 1, divisor), multiplier);

            var numeratorLimit = (BigInteger.One << (64 - power)) - 1;
            var error = (multiplier * divisor) - scale;

            Assert.True(numeratorLimit * error < scale,
                $"power {power}: n_max * e is not below 2^s, so some numerator divides wrong");
        }
    }

    /// <summary>
    /// The 2-by-1 division constants: the divisor shifted left until its top bit is set,
    /// and its reciprocal word, <c>floor((2^128 - 1) / d') - 2^64</c>.
    /// </summary>
    [Fact]
    public void TwoByOneConstantsAreTheDerivedOnes()
    {
        for (var power = 1; power <= Decimal128Tables.MaxPower; power++)
        {
            var divisor = Decimal128Tables.PowerOfTen(power);
            var shift = BitOperations.LeadingZeroCount(divisor);
            var normalized = divisor << shift;
            var inverse = (((BigInteger.One << 128) - 1) / normalized) - (BigInteger.One << 64);

            Assert.Equal(shift, Decimal128Tables.DivisorShift(power));
            Assert.Equal(normalized, Decimal128Tables.NormalizedDivisor(power));
            Assert.Equal((ulong)inverse, Decimal128Tables.Inverse(power));
        }
    }

    [Fact]
    public void PowersOfTenAtEveryWidthAreRight()
    {
        for (var power = 0; power <= Decimal128Tables.MaxWidePower; power++)
        {
            Assert.Equal(BigInteger.Pow(10, power), ToBig(Decimal128Tables.WidePowerOfTen(power)));
            Assert.Equal(BigInteger.Pow(10, power) / 2, ToBig(Decimal128Tables.WideHalfPowerOfTen(power)));
        }

        for (var power = 0; power <= Decimal128Tables.MaxLongPower; power++)
        {
            Assert.Equal(BigInteger.Pow(10, power), ToBig(Decimal128Tables.LongPowerOfTen(power)));
        }
    }

    [Fact]
    public void TwoWordDivisionMatchesBigIntegers()
    {
        var random = new Random(20260921);

        for (var power = 1; power <= Decimal128Tables.MaxPower; power++)
        {
            var divisor = BigInteger.Pow(10, power);

            foreach (var value in TwoWordBoundaries(divisor))
            {
                AssertDivides(value, power, divisor);
            }

            for (var attempt = 0; attempt < 3000; attempt++)
            {
                var value = NextTwoWords(random);
                AssertDivides(value, power, divisor);

                var exact = value / divisor * divisor;
                AssertDivides(exact, power, divisor);
                if (exact > 0)
                {
                    AssertDivides(exact - 1, power, divisor);
                }

                if (exact + 1 < (BigInteger.One << 128))
                {
                    AssertDivides(exact + 1, power, divisor);
                }
            }
        }
    }

    [Fact]
    public void WidePowerDivisionMatchesBigIntegers()
    {
        var random = new Random(31415926);

        for (var power = 1; power <= Decimal128Tables.MaxWidePower; power++)
        {
            var divisor = BigInteger.Pow(10, power);

            for (var attempt = 0; attempt < 2000; attempt++)
            {
                var value = NextTwoWords(random);
                var quotient = Decimal128Tables.DivRemWidePowerOfTen(FromBig(value), power, out var remainder);

                Assert.Equal(value / divisor, ToBig(quotient));
                Assert.Equal(value % divisor, ToBig(remainder));
            }
        }
    }

    [Fact]
    public void FourWordDivisionMatchesBigIntegers()
    {
        var random = new Random(27182818);

        for (var power = 1; power <= Decimal128Tables.MaxPower; power++)
        {
            var divisor = BigInteger.Pow(10, power);

            for (var attempt = 0; attempt < 2000; attempt++)
            {
                var value = NextFourWords(random);
                var quotient = Decimal128Tables.DivRemPowerOfTen(FromBigLong(value), power, out var remainder);

                Assert.Equal(value / divisor, ToBig(quotient));
                Assert.Equal(value % divisor, new BigInteger(remainder));
            }
        }
    }

    [Fact]
    public void DroppingDigitsMatchesBigIntegers()
    {
        var random = new Random(16180339);

        for (var attempt = 0; attempt < 20000; attempt++)
        {
            var value = NextFourWords(random);
            var count = random.Next(1, 80);
            var divisor = BigInteger.Pow(10, count);

            var kept = Decimal128Rounder.DropDigits(FromBigLong(value), count, Decimal128Residue.Exact, out var residue);
            Assert.Equal(value / divisor, ToBig(kept));
            Assert.Equal(ResidueOf(value % divisor, divisor), residue);

            var narrow = NextTwoWords(random);
            var narrowCount = random.Next(1, 45);
            var narrowDivisor = BigInteger.Pow(10, narrowCount);

            var narrowKept = Decimal128Rounder.DropDigits(FromBig(narrow), narrowCount, Decimal128Residue.Exact,
                out var narrowResidue);

            Assert.Equal(narrow / narrowDivisor, ToBig(narrowKept));
            Assert.Equal(ResidueOf(narrow % narrowDivisor, narrowDivisor), narrowResidue);
        }
    }

    [Fact]
    public void ProductsAndScalingMatchBigIntegers()
    {
        var random = new Random(14142135);

        for (var attempt = 0; attempt < 20000; attempt++)
        {
            var left = NextTwoWords(random);
            var right = NextTwoWords(random);

            Assert.Equal(left * right, ToBig(Decimal128LongInteger.Multiply(FromBig(left), FromBig(right))));

            // The wide scaling takes an addend of up to 34 digits to at most 69 digits,
            // which fits in four words.
            var coefficient = left % BigInteger.Pow(10, 34);
            var power = random.Next(0, 70 - coefficient.ToString().Length);
            Assert.Equal(coefficient * BigInteger.Pow(10, power),
                ToBig(Decimal128Tables.ScaleLong(FromBig(coefficient), power)));

            var narrowPower = random.Next(0, 39);
            var narrow = left % BigInteger.Pow(10, 38 - narrowPower);
            Assert.Equal(narrow * BigInteger.Pow(10, narrowPower),
                ToBig(Decimal128Tables.Scale(FromBig(narrow), narrowPower)));

            // A four-word value times one word must still fit in four words.
            var word = (ulong)(right & ulong.MaxValue);
            var product = NextFourWords(random) >> random.Next(64, 128);
            Assert.Equal(product * word, ToBig(FromBigLong(product).MultiplyBy(word)));
        }
    }

    [Fact]
    public void FourWordSumsAndDifferencesMatchBigIntegers()
    {
        var random = new Random(17320508);

        for (var attempt = 0; attempt < 20000; attempt++)
        {
            var left = NextFourWords(random) >> 1;
            var right = NextFourWords(random) >> 1;
            var larger = BigInteger.Max(left, right);
            var smaller = BigInteger.Min(left, right);

            Assert.Equal(left + right, ToBig(FromBigLong(left) + FromBigLong(right)));
            Assert.Equal(larger - smaller, ToBig(FromBigLong(larger) - FromBigLong(smaller)));
            Assert.Equal(left.CompareTo(right), FromBigLong(left).CompareTo(FromBigLong(right)));

            var narrowLeft = NextTwoWords(random) >> 1;
            var narrowRight = NextTwoWords(random) >> 1;
            var narrowLarger = BigInteger.Max(narrowLeft, narrowRight);
            var narrowSmaller = BigInteger.Min(narrowLeft, narrowRight);

            Assert.Equal(narrowLeft + narrowRight, ToBig(FromBig(narrowLeft) + FromBig(narrowRight)));
            Assert.Equal(narrowLarger - narrowSmaller, ToBig(FromBig(narrowLarger) - FromBig(narrowSmaller)));
            Assert.Equal(narrowLeft.CompareTo(narrowRight), FromBig(narrowLeft).CompareTo(FromBig(narrowRight)));
        }
    }

    [Fact]
    public void CountsDigitsAtEveryWidth()
    {
        Assert.Equal(1, Decimal128Tables.CountDigits(Decimal128Integer.Zero));
        Assert.Equal(1, Decimal128Tables.CountDigits(default(Decimal128LongInteger)));

        for (var power = 0; power <= Decimal128Tables.MaxWidePower; power++)
        {
            var value = Decimal128Tables.WidePowerOfTen(power);
            Assert.Equal(power + 1, Decimal128Tables.CountDigits(value));
            if (power > 0)
            {
                Assert.Equal(power, Decimal128Tables.CountDigits(value - 1));
            }
        }

        for (var power = 0; power <= Decimal128Tables.MaxLongPower; power++)
        {
            var value = Decimal128Tables.LongPowerOfTen(power);
            Assert.Equal(power + 1, Decimal128Tables.CountDigits(value));
            if (power > 0)
            {
                Assert.Equal(power, Decimal128Tables.CountDigits(value - Decimal128LongInteger.FromInteger(Decimal128Integer.One)));
            }
        }

        var random = new Random(22360679);
        for (var attempt = 0; attempt < 50000; attempt++)
        {
            var narrow = NextTwoWords(random) >> random.Next(0, 128);
            Assert.Equal(narrow.ToString().Length, Decimal128Tables.CountDigits(FromBig(narrow)));

            var wide = NextFourWords(random) >> random.Next(0, 256);
            Assert.Equal(wide.ToString().Length, Decimal128Tables.CountDigits(FromBigLong(wide)));
        }
    }

    /// <summary>
    /// The conversions to double add the words in floating point, so the result can be off
    /// by 1 or 2 units in the last place. That is enough, because the estimates that use
    /// them need only 50 bits. The conversion back from double is exact.
    /// </summary>
    [Fact]
    public void DoubleConversionsCarryTheTopBits()
    {
        var random = new Random(26457513);
        for (var attempt = 0; attempt < 20000; attempt++)
        {
            var value = NextTwoWords(random) >> random.Next(0, 120);
            var expected = (double)value;
            AssertClose(expected, FromBig(value).ToDouble());

            var floor = new BigInteger(Math.Floor(expected));
            Assert.Equal(floor, ToBig(Decimal128Integer.FromDouble(Math.Floor(expected))));

            var wide = NextFourWords(random) >> random.Next(0, 250);
            AssertClose((double)wide, FromBigLong(wide).ToDouble());
        }
    }

    private static void AssertClose(double expected, double actual)
    {
        var tolerance = Math.ScaleB(Math.Max(Math.Abs(expected), double.Epsilon), -50);
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"{actual} is not within 2^-50 of {expected}");
    }

    private static void AssertDivides(BigInteger value, int power, BigInteger divisor)
    {
        var quotient = Decimal128Tables.DivRemPowerOfTen(FromBig(value), power, out var remainder);
        Assert.Equal(value / divisor, ToBig(quotient));
        Assert.Equal(value % divisor, new BigInteger(remainder));
    }

    private static Decimal128Residue ResidueOf(BigInteger discarded, BigInteger divisor)
    {
        if (discarded.IsZero)
        {
            return Decimal128Residue.Exact;
        }

        var half = divisor / 2;
        if (discarded < half)
        {
            return Decimal128Residue.BelowHalf;
        }

        return discarded == half ? Decimal128Residue.Half : Decimal128Residue.AboveHalf;
    }

    private static IEnumerable<BigInteger> TwoWordBoundaries(BigInteger divisor)
    {
        var limit = (BigInteger.One << 128) - 1;
        yield return 0;
        yield return 1;
        yield return divisor - 1;
        yield return divisor;
        yield return divisor + 1;
        yield return limit;
        yield return limit - 1;
        yield return limit / divisor * divisor;
        yield return BigInteger.Pow(10, 34) - 1;
        yield return BigInteger.Pow(10, 34);
        yield return BigInteger.Pow(10, 38) - 1;
        yield return ulong.MaxValue;
        yield return new BigInteger(ulong.MaxValue) + 1;
    }

    private static BigInteger NextTwoWords(Random random)
    {
        Span<byte> bytes = stackalloc byte[17];
        random.NextBytes(bytes[..16]);
        bytes[16] = 0;
        return new BigInteger(bytes);
    }

    private static BigInteger NextFourWords(Random random)
    {
        Span<byte> bytes = stackalloc byte[33];
        random.NextBytes(bytes[..32]);
        bytes[32] = 0;
        return new BigInteger(bytes);
    }

    private static Decimal128Integer FromBig(BigInteger value)
    {
        return new Decimal128Integer((ulong)(value >> 64), (ulong)(value & ulong.MaxValue));
    }

    private static Decimal128LongInteger FromBigLong(BigInteger value)
    {
        return new Decimal128LongInteger(
            (ulong)((value >> 192) & ulong.MaxValue),
            (ulong)((value >> 128) & ulong.MaxValue),
            (ulong)((value >> 64) & ulong.MaxValue),
            (ulong)(value & ulong.MaxValue));
    }

    private static BigInteger ToBig(Decimal128Integer value)
    {
        return (new BigInteger(value.High) << 64) | value.Low;
    }

    private static BigInteger ToBig(Decimal128LongInteger value)
    {
        return (new BigInteger(value.Word3) << 192) | (new BigInteger(value.Word2) << 128)
            | (new BigInteger(value.Word1) << 64) | value.Word0;
    }
}
