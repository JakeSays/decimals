// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Numerics;

namespace Decimals.Tests;

/// <summary>
/// The reciprocals that stand in for dividing by a power of ten. These constants were found
/// by a generator and pasted in, so the tests re-derive them here rather than trusting the
/// paste: one checks the algebra that makes each pair exact, the others check the quotient
/// against real division over the numerators that actually reach it.
/// </summary>
public class PowerOfTenReciprocalTests
{
    /// <summary>
    /// The condition that makes a pair exact rather than approximate. With
    /// <c>M = ceil(2^s / d)</c> and <c>e = M*d - 2^s</c>, the quotient is right for every
    /// numerator up to <c>n_max</c> exactly when <c>n_max * e &lt; 2^s</c>. Checking that is
    /// a proof for the whole 128-bit range, which no amount of sampling would give.
    /// </summary>
    [Fact]
    public void EveryReciprocalIsExactAcrossTheWholeRange()
    {
        var fullRange = (BigInteger.One << 128) - 1;

        for (var power = 1; power <= PowersOfTen.MaxUInt128Power; power++)
        {
            var reciprocal = PowersOfTen.Reciprocal(power);

            Assert.Equal(power, reciprocal.PreShift);
            Assert.True(reciprocal.Shift >= 128,
                $"power {power}: shift {reciprocal.Shift} would need the low half of the product");

            // The pre-shift takes the 2^p half, leaving 5^p to divide by.
            var divisor = BigInteger.Pow(5, power);
            var numeratorLimit = fullRange >> power;
            var multiplier = ToBigInteger(reciprocal.Multiplier);
            var scale = BigInteger.One << reciprocal.Shift;

            Assert.Equal(BigInteger.Divide(scale + divisor - 1, divisor), multiplier);

            var error = (multiplier * divisor) - scale;
            Assert.True(numeratorLimit * error < scale,
                $"power {power}: n_max * e is not below 2^s, so some numerator divides wrong");
        }
    }

    [Fact]
    public void MatchesDivisionOnTheBoundaries()
    {
        for (var power = 1; power <= PowersOfTen.MaxUInt128Power; power++)
        {
            var divisor = PowersOfTen.UInt128(power);

            foreach (var value in Boundaries(divisor))
            {
                Assert.Equal(value / divisor, PowersOfTen.DivideByPowerOfTen(value, power));
            }
        }
    }

    [Fact]
    public void MatchesDivisionOnRandomValues()
    {
        var random = new Random(20260818);

        for (var power = 1; power <= PowersOfTen.MaxUInt128Power; power++)
        {
            var divisor = PowersOfTen.UInt128(power);

            for (var attempt = 0; attempt < 2000; attempt++)
            {
                var value = NextUInt128(random);
                Assert.Equal(value / divisor, PowersOfTen.DivideByPowerOfTen(value, power));

                // And a value that divides exactly, plus its neighbours, since those are
                // where an off-by-one in the multiplier would show first.
                var exact = (value / divisor) * divisor;
                Assert.Equal(exact / divisor, PowersOfTen.DivideByPowerOfTen(exact, power));

                if (exact > UInt128.Zero)
                {
                    Assert.Equal((exact - 1) / divisor,
                        PowersOfTen.DivideByPowerOfTen(exact - 1, power));
                }

                if (exact < UInt128.MaxValue)
                {
                    Assert.Equal((exact + 1) / divisor,
                        PowersOfTen.DivideByPowerOfTen(exact + 1, power));
                }
            }
        }
    }

    /// <summary>
    /// The coefficients arithmetic actually produces: every width of value a format can
    /// carry, against every power it can be asked to drop.
    /// </summary>
    [Fact]
    public void MatchesDivisionOnCoefficientWidths()
    {
        for (var digits = 1; digits <= PowersOfTen.MaxUInt128Power; digits++)
        {
            var largest = PowersOfTen.UInt128(digits) - UInt128.One;

            for (var power = 1; power <= PowersOfTen.MaxUInt128Power; power++)
            {
                var divisor = PowersOfTen.UInt128(power);
                Assert.Equal(largest / divisor, PowersOfTen.DivideByPowerOfTen(largest, power));

                var smallest = PowersOfTen.UInt128(digits - 1);
                Assert.Equal(smallest / divisor, PowersOfTen.DivideByPowerOfTen(smallest, power));
            }
        }
    }

    private static IEnumerable<UInt128> Boundaries(UInt128 divisor)
    {
        yield return UInt128.Zero;
        yield return UInt128.One;
        yield return divisor - UInt128.One;
        yield return divisor;
        yield return divisor + UInt128.One;
        yield return (divisor * 2) - UInt128.One;
        yield return divisor * 2;
        yield return UInt128.MaxValue;
        yield return UInt128.MaxValue - UInt128.One;
        yield return UInt128.MaxValue / divisor * divisor;
    }

    private static UInt128 NextUInt128(Random random)
    {
        Span<byte> bytes = stackalloc byte[16];
        random.NextBytes(bytes);
        return new UInt128(BitConverter.ToUInt64(bytes[8..]), BitConverter.ToUInt64(bytes[..8]));
    }

    private static BigInteger ToBigInteger(UInt128 value)
    {
        return ((BigInteger)(ulong)(value >> 64) << 64) | (ulong)value;
    }
}
