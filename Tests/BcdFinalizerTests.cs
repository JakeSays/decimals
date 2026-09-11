// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using Decimals;

namespace Tests;

/// <summary>
/// The digit-form finalizer against the one the corpus already proves. They take the same
/// value and have to reach the same result and raise the same conditions, so any
/// disagreement is the new one's.
/// </summary>
public unsafe class BcdFinalizerTests
{
    /// <summary>
    /// Room for the widest coefficient a caller can hand over, a digit of headroom before
    /// it for a rounding carry, and room past it for a fold-down's zeros.
    /// </summary>
    private const int BufferLength = 128;

    private const int Headroom = 1;

    private static readonly DecimalRounding[] Roundings =
    [
        DecimalRounding.HalfEven,
        DecimalRounding.HalfUp,
        DecimalRounding.HalfDown,
        DecimalRounding.Up,
        DecimalRounding.Down,
        DecimalRounding.Ceiling,
        DecimalRounding.Floor,
        DecimalRounding.ZeroFiveUp
    ];

    [Fact]
    public void MatchesTheIntegerFinalizerForDecimal64()
    {
        RunDifferential<Decimal64Format>(20260822, 16);
    }

    [Fact]
    public void MatchesTheIntegerFinalizerForDecimal32()
    {
        RunDifferential<Decimal32Format>(20260823, 7);
    }

    [Fact]
    public void MatchesTheIntegerFinalizerForDecimal128()
    {
        RunDifferential<Decimal128Format>(20260824, 34);
    }

    /// <summary>
    /// The interesting exponents are the ones at the edges of the format: where a value
    /// stops being normal, where it folds down, and where it overflows.
    /// </summary>
    private static void RunDifferential<TFormat>(int seed, int precision)
        where TFormat : IDecimalFormat
    {
        var random = new Random(seed);

        // One buffer for the run: every iteration lays its own digits down and sets both
        // ends, so nothing survives from the last one.
        var buffer = stackalloc byte[BufferLength];

        for (var iteration = 0; iteration < 40000; iteration++)
        {
            var digitCount = random.Next(1, precision + 6);
            var coefficient = RandomCoefficient(random, digitCount);
            var isNegative = random.Next(2) == 1;
            var exponent = RandomExponent<TFormat>(random);
            var rounding = Roundings[random.Next(Roundings.Length)];

            var integerStatus = DecimalStatus.None;
            var expected = DecimalFinalizer.Finalize<TFormat>(isNegative, coefficient, exponent,
                rounding, ref integerStatus);

            var digitStatus = DecimalStatus.None;
            var actual = Build(coefficient, isNegative, exponent, buffer);
            BcdFinalizer.Finalize<TFormat>(ref actual, rounding, ref digitStatus);

            var context = $"coefficient {coefficient}, exponent {exponent}, {rounding}, negative {isNegative}";

            Assert.Equal(expected.Kind, actual.Kind);
            Assert.Equal(expected.IsNegative, actual.IsNegative);
            Assert.Equal(integerStatus, digitStatus);

            if (expected.Kind != DecimalKind.Finite)
            {
                continue;
            }

            Assert.True(expected.Exponent == actual.Exponent,
                $"exponent {expected.Exponent} against {actual.Exponent} for {context}");
            Assert.True(expected.Coefficient == Spell(actual),
                $"coefficient {expected.Coefficient} against {Spell(actual)} for {context}");
        }
    }

    private static UInt128 RandomCoefficient(Random random, int digitCount)
    {
        var value = UInt128.Zero;
        for (var position = 0; position < digitCount; position++)
        {
            // A leading zero would make the coefficient shorter than asked for, which the
            // caller is free to hand over but makes the digit counts less interesting.
            var digit = position == 0 ? 1 + random.Next(9) : random.Next(10);
            value = (value * 10) + (uint)digit;
        }

        return value;
    }

    private static int RandomExponent<TFormat>(Random random)
        where TFormat : IDecimalFormat
    {
        return random.Next(6) switch
        {
            0 => TFormat.MinQuantumExponent + random.Next(-4, 5),
            1 => TFormat.MaxQuantumExponent + random.Next(-4, 5),
            2 => TFormat.MinExponent + random.Next(-4, 5),
            3 => TFormat.MaxExponent + random.Next(-4, 5),
            4 => random.Next(-20, 21),
            _ => random.Next(TFormat.MinQuantumExponent, TFormat.MaxQuantumExponent + 1)
        };
    }

    /// <summary>
    /// Lays a coefficient out as digits at the buffer's headroom offset, so a rounding
    /// carry has the position before the leading digit to take.
    /// </summary>
    private static BcdNumber Build(UInt128 coefficient, bool isNegative, int exponent, byte* buffer)
    {
        var digits = stackalloc byte[48];
        var count = 0;
        do
        {
            digits[count] = (byte)(coefficient % 10);
            coefficient /= 10;
            count++;
        }
        while (coefficient != UInt128.Zero);

        var msd = buffer + Headroom;
        for (var position = 0; position < count; position++)
        {
            msd[position] = digits[count - position - 1];
        }

        return new BcdNumber(DecimalKind.Finite, isNegative, exponent, msd, msd + count - 1);
    }

    private static UInt128 Spell(BcdNumber value)
    {
        var spelled = UInt128.Zero;
        for (var position = 0; position < value.DigitCount; position++)
        {
            spelled = (spelled * 10) + value.Msd[position];
        }

        return spelled;
    }
}
