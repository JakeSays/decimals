// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Numerics;

using Decimals;

namespace Tests;

/// <summary>
/// The engine's shortening against <see cref="BigInteger"/>.
/// </summary>
/// <remarks>
/// What an independent oracle can state about <c>decSetCoeff</c> is where the digits go:
/// the kept coefficient is the value divided by the power of ten discarded, and the result
/// is inexact exactly when that division leaves something behind. The residue's own scale
/// is decNumber's convention rather than arithmetic, and the corpus is what holds it.
/// </remarks>
public unsafe class WideRoundingTests
{
    private const int BufferLength = 128;

    [Fact]
    public void ShorteningKeepsTheDigitsTheOracleKeeps()
    {
        var random = new Random(20260831);
        var units = stackalloc uint[BufferLength];

        for (var iteration = 0; iteration < 40000; iteration++)
        {
            var digits = random.Next(1, 60);
            var value = RandomValue(random, digits);
            var target = random.Next(1, 40);
            var exponent = random.Next(-50, 51);

            var actual = Build(value, exponent, units);
            var residue = 0;
            var status = DecimalStatus.None;

            WideRounding.SetCoefficient(ref actual, target, ref residue, ref status);

            var length = value.IsZero ? 1 : value.ToString().Length;
            var discard = length - target;
            var describe = $"{value}E{exponent} to {target} digits";

            if (discard <= 0)
            {
                Assert.True(value == Read(actual), $"untouched for {describe}");
                Assert.True(exponent == actual.Exponent, $"exponent for {describe}");
                Assert.True(DecimalStatus.None == status, $"status for {describe}");
                continue;
            }

            var power = BigInteger.Pow(10, discard);
            var expected = discard > length ? BigInteger.Zero : value / power;

            Assert.True(expected == Read(actual),
                $"{expected} against {Read(actual)} for {describe}");

            Assert.True(exponent + discard == actual.Exponent,
                $"exponent {exponent + discard} against {actual.Exponent} for {describe}");

            // Inexact exactly when something was left behind, and rounded whenever digits
            // were asked to go at all.
            var lost = !(value % power).IsZero;
            Assert.True((status & DecimalStatus.Rounded) != 0, $"rounded for {describe}");
            Assert.True(lost == ((status & DecimalStatus.Inexact) != 0),
                $"inexact {lost} for {describe}");

            Assert.True(lost == (residue != 0), $"residue {residue} for {describe}");
        }
    }

    /// <summary>
    /// A run of nines rounded up shortens to a one followed by zeros one decade higher,
    /// rather than growing a digit. That is the case the length bookkeeping turns on.
    /// </summary>
    [Fact]
    public void RoundingAllNinesUpCarriesIntoTheExponent()
    {
        var units = stackalloc uint[BufferLength];

        for (var digits = 1; digits <= 60; digits++)
        {
            var nines = BigInteger.Pow(10, digits) - BigInteger.One;
            var value = Build(nines, 0, units);

            var context = new WideContext
            {
                Digits = digits,
                MaxExponent = WideContext.MaxMathExponent,
                MinExponent = -WideContext.MaxMathExponent,
                Rounding = DecimalRounding.HalfUp,
                Clamp = false
            };

            var status = DecimalStatus.None;
            WideRounding.ApplyRound(ref value, 7, context, ref status, out var overflowed);

            Assert.False(overflowed, $"overflow at {digits} nines");
            Assert.True(BigInteger.Pow(10, digits - 1) == Read(value),
                $"{digits} nines rounded to {Read(value)}");

            Assert.True(value.Exponent == 1, $"exponent for {digits} nines");
            Assert.True(value.Digits == digits, $"digit count for {digits} nines");
        }
    }

    private static BigInteger RandomValue(Random random, int digits)
    {
        var value = BigInteger.Zero;
        for (var index = 0; index < digits; index++)
        {
            var digit = index == 0 ? 1 + random.Next(9) : random.Next(10);
            value = (value * 10) + digit;
        }

        return random.Next(20) == 0 ? BigInteger.Zero : value;
    }

    private static WideNumber Build(BigInteger value, int exponent, uint* units)
    {
        var number = new WideNumber(units);
        var length = 0;
        var remaining = value;

        do
        {
            units[length] = (uint)(remaining % WideNumber.UnitBase);
            remaining /= WideNumber.UnitBase;
            length++;
        }
        while (!remaining.IsZero);

        number.Units = length;
        number.Exponent = exponent;
        number.CountDigits();
        return number;
    }

    private static BigInteger Read(WideNumber value)
    {
        var result = BigInteger.Zero;
        for (var index = value.Units - 1; index >= 0; index--)
        {
            result = (result * WideNumber.UnitBase) + value.Lsu[index];
        }

        return result;
    }
}
