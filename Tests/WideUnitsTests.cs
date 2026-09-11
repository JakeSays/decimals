// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Numerics;

using Decimals;

namespace Tests;

/// <summary>
/// The unit-array primitives against <see cref="BigInteger"/>, which is what they are
/// replacing and so the right thing to be measured against.
/// </summary>
public unsafe class WideUnitsTests
{
    private const int BufferLength = 64;

    private static readonly BigInteger UnitBase = WideNumber.UnitBase;

    [Fact]
    public void AddingAgreesWithTheBinaryOracle()
    {
        var random = new Random(20260823);

        var left = stackalloc uint[BufferLength];
        var right = stackalloc uint[BufferLength];
        var result = stackalloc uint[BufferLength];

        for (var iteration = 0; iteration < 50000; iteration++)
        {
            var leftUnits = random.Next(1, 12);
            var rightUnits = random.Next(1, 12);
            var shift = random.Next(0, 6);

            var leftValue = Fill(random, left, leftUnits);
            var rightValue = Fill(random, right, rightUnits);

            var written = WideUnits.AddSub(left, leftUnits, right, rightUnits, shift, result, 1);

            var expected = leftValue + (rightValue * BigInteger.Pow(UnitBase, shift));
            Assert.True(written > 0, $"add gave a negative length for {leftValue} + {rightValue}");
            Assert.Equal(expected, Read(result, written));
        }
    }

    [Fact]
    public void SubtractingAgreesWithTheBinaryOracle()
    {
        var random = new Random(20260824);

        var left = stackalloc uint[BufferLength];
        var right = stackalloc uint[BufferLength];
        var result = stackalloc uint[BufferLength];

        for (var iteration = 0; iteration < 50000; iteration++)
        {
            var leftUnits = random.Next(1, 12);
            var rightUnits = random.Next(1, 12);
            var shift = random.Next(0, 6);

            var leftValue = Fill(random, left, leftUnits);
            var rightValue = Fill(random, right, rightUnits);

            var written = WideUnits.AddSub(left, leftUnits, right, rightUnits, shift, result, -1);

            var expected = leftValue - (rightValue * BigInteger.Pow(UnitBase, shift));

            // A negative result comes back complemented, with the length negated to say so.
            var magnitude = Read(result, Math.Abs(written));
            var actual = written < 0 ? -magnitude : magnitude;

            Assert.True(expected == actual,
                $"{leftValue} - {rightValue}E{shift} gave {actual}, wanted {expected}");
        }
    }

    [Fact]
    public void ShiftingUpAgreesWithTheBinaryOracle()
    {
        var random = new Random(20260825);
        var units = stackalloc uint[BufferLength];

        for (var iteration = 0; iteration < 50000; iteration++)
        {
            var length = random.Next(1, 12);
            var places = random.Next(0, 40);

            var value = Fill(random, units, length);
            var written = WideUnits.ShiftUp(units, length, places);

            Assert.Equal(value * BigInteger.Pow(10, places), Read(units, written));
        }
    }

    [Fact]
    public void ComparingAgreesWithTheBinaryOracle()
    {
        var random = new Random(20260826);

        var left = stackalloc uint[BufferLength];
        var right = stackalloc uint[BufferLength];

        for (var iteration = 0; iteration < 50000; iteration++)
        {
            var leftUnits = random.Next(1, 8);
            var rightUnits = random.Next(1, 8);

            var leftValue = Fill(random, left, leftUnits);
            var rightValue = Fill(random, right, rightUnits);

            Assert.Equal(
                leftValue.CompareTo(rightValue),
                WideUnits.Compare(left, leftUnits, right, rightUnits));
        }
    }

    /// <summary>
    /// Long operands on purpose: the lazy accumulation only needs settling once eighteen
    /// rows have gone in, so an operand shorter than that never exercises the carry
    /// resolution at all, and one longer than thirty-six never exercises it twice.
    /// </summary>
    [Fact]
    public void MultiplyingAgreesWithTheBinaryOracle()
    {
        var random = new Random(20260828);

        var left = stackalloc uint[BufferLength];
        var right = stackalloc uint[BufferLength];
        var product = stackalloc uint[BufferLength * 2];
        var accumulator = stackalloc ulong[(BufferLength * 2) + WideMultiply.AccumulatorSlack];

        for (var iteration = 0; iteration < 20000; iteration++)
        {
            var leftUnits = random.Next(1, 45);
            var rightUnits = random.Next(1, 45);

            var leftValue = Fill(random, left, leftUnits);
            var rightValue = Fill(random, right, rightUnits);

            var first = Wrap(left, leftUnits);
            var second = Wrap(right, rightUnits);
            var result = Wrap(product, 1);

            WideMultiply.Multiply(ref result, first, second, accumulator);

            Assert.Equal(leftValue * rightValue, Read(product, result.Units));
        }
    }

    /// <summary>
    /// Every unit at its maximum, which is where a column fills fastest and the two-place
    /// carry in the resolution comes up.
    /// </summary>
    [Fact]
    public void MultiplyingSaturatedUnitsCarriesCorrectly()
    {
        var left = stackalloc uint[BufferLength];
        var right = stackalloc uint[BufferLength];
        var product = stackalloc uint[BufferLength * 2];
        var accumulator = stackalloc ulong[(BufferLength * 2) + WideMultiply.AccumulatorSlack];

        for (var length = 1; length <= 40; length++)
        {
            var expected = BigInteger.Zero;
            for (var index = 0; index < length; index++)
            {
                left[index] = WideNumber.UnitBase - 1;
                right[index] = WideNumber.UnitBase - 1;
                expected = (expected * UnitBase) + (WideNumber.UnitBase - 1);
            }

            var first = Wrap(left, length);
            var second = Wrap(right, length);
            var result = Wrap(product, 1);

            WideMultiply.Multiply(ref result, first, second, accumulator);

            Assert.Equal(expected * expected, Read(product, result.Units));
        }
    }

    [Fact]
    public void ShiftingDownAgreesWithTheBinaryOracle()
    {
        var random = new Random(20260829);
        var units = stackalloc uint[BufferLength];

        for (var iteration = 0; iteration < 50000; iteration++)
        {
            var length = random.Next(1, 12);
            var places = random.Next(0, 40);

            var value = Fill(random, units, length);
            var written = WideUnits.ShiftDown(units, length, places);

            Assert.Equal(value / BigInteger.Pow(10, places), Read(units, written));
        }
    }

    [Fact]
    public void DigitReadsAndStickyChecksAgreeWithTheText()
    {
        var random = new Random(20260830);
        var units = stackalloc uint[BufferLength];

        for (var iteration = 0; iteration < 20000; iteration++)
        {
            var length = random.Next(1, 8);
            var value = Fill(random, units, length);
            var text = value.ToString();

            for (var position = 0; position < text.Length + 2; position++)
            {
                var expected = position < text.Length
                    ? (uint)(text[text.Length - position - 1] - '0')
                    : 0u;

                Assert.Equal(expected, WideUnits.DigitAt(units, length, position));

                // Anything below the position survives exactly when the value is not a
                // multiple of the power of ten that position stands for.
                var below = !(value % BigInteger.Pow(10, position)).IsZero;
                Assert.Equal(below, WideUnits.AnyBelow(units, length, position));
            }
        }
    }

    private static WideNumber Wrap(uint* units, int length)
    {
        var number = default(WideNumber);
        number.Lsu = units;
        number.Units = length;
        number.Kind = DecimalKind.Finite;
        return number;
    }

    [Fact]
    public void DigitCountsAgreeWithTheText()
    {
        var random = new Random(20260827);
        var units = stackalloc uint[BufferLength];

        for (var iteration = 0; iteration < 20000; iteration++)
        {
            var length = random.Next(1, 12);
            var value = Fill(random, units, length);

            // Wrapping units that already hold a value, rather than making a fresh zero,
            // so the constructor does not clear what was just written.
            var number = default(WideNumber);
            number.Lsu = units;
            number.Units = length;
            number.Kind = DecimalKind.Finite;
            number.CountDigits();

            var expected = value.IsZero ? 1 : value.ToString().Length;
            Assert.Equal(expected, number.Digits);
        }
    }

    /// <summary>
    /// Fills a unit array with a random value and hands back what it is worth, so the two
    /// sides of the comparison come from one draw.
    /// </summary>
    private static BigInteger Fill(Random random, uint* units, int length)
    {
        var value = BigInteger.Zero;
        for (var index = length - 1; index >= 0; index--)
        {
            // Zero units come up on purpose: they are where the carry and borrow paths
            // behave differently from the common case.
            units[index] = random.Next(6) == 0 ? 0u : (uint)random.NextInt64(WideNumber.UnitBase);
            value = (value * UnitBase) + units[index];
        }

        return value;
    }

    private static BigInteger Read(uint* units, int length)
    {
        var value = BigInteger.Zero;
        for (var index = length - 1; index >= 0; index--)
        {
            value = (value * UnitBase) + units[index];
        }

        return value;
    }
}
