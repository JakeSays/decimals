// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using Decimals;

namespace Tests;

/// <summary>
/// Digit-form arithmetic against the binary path the corpus already proves. Both compute
/// the same operation on the same operands, so a disagreement is the new one's.
/// </summary>
public unsafe class BcdArithmeticTests
{
    private const int BufferLength = 256;
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
    public void AddMatchesTheBinaryPathForDecimal64()
    {
        RunAddDifferential<Decimal64Format>(20260901, 16);
    }

    [Fact]
    public void AddMatchesTheBinaryPathForDecimal32()
    {
        RunAddDifferential<Decimal32Format>(20260902, 7);
    }

    [Fact]
    public void AddMatchesTheBinaryPathForDecimal128()
    {
        RunAddDifferential<Decimal128Format>(20260903, 34);
    }

    [Fact]
    public void MultiplyMatchesTheBinaryPathForDecimal64()
    {
        RunMultiplyDifferential<Decimal64Format>(20260906, 16);
    }

    [Fact]
    public void MultiplyMatchesTheBinaryPathForDecimal32()
    {
        RunMultiplyDifferential<Decimal32Format>(20260907, 7);
    }

    [Fact]
    public void MultiplyMatchesTheBinaryPathForDecimal128()
    {
        RunMultiplyDifferential<Decimal128Format>(20260908, 34);
    }

    [Fact]
    public void FusedMultiplyAddMatchesTheBinaryPathForDecimal64()
    {
        RunFusedDifferential<Decimal64Format>(20260909, 16);
    }

    [Fact]
    public void FusedMultiplyAddMatchesTheBinaryPathForDecimal32()
    {
        RunFusedDifferential<Decimal32Format>(20260910, 7);
    }

    [Fact]
    public void FusedMultiplyAddMatchesTheBinaryPathForDecimal128()
    {
        RunFusedDifferential<Decimal128Format>(20260911, 34);
    }

    [Fact]
    public void DivideMatchesTheBinaryPathForDecimal64()
    {
        RunDivideDifferential<Decimal64Format>(20260912, 16);
    }

    [Fact]
    public void DivideMatchesTheBinaryPathForDecimal32()
    {
        RunDivideDifferential<Decimal32Format>(20260913, 7);
    }

    [Fact]
    public void DivideMatchesTheBinaryPathForDecimal128()
    {
        RunDivideDifferential<Decimal128Format>(20260914, 34);
    }

    [Fact]
    public void CompareMatchesTheBinaryPathForDecimal64()
    {
        RunCompareDifferential<Decimal64Format>(20260904, 16);
    }

    [Fact]
    public void CompareMatchesTheBinaryPathForDecimal128()
    {
        RunCompareDifferential<Decimal128Format>(20260905, 34);
    }

    private static void RunAddDifferential<TFormat>(int seed, int precision)
        where TFormat : IDecimalFormat
    {
        var random = new Random(seed);

        var leftBuffer = stackalloc byte[BufferLength];
        var rightBuffer = stackalloc byte[BufferLength];
        var leftWork = stackalloc byte[BufferLength];
        var rightWork = stackalloc byte[BufferLength];
        var resultBuffer = stackalloc byte[BufferLength];

        for (var iteration = 0; iteration < 40000; iteration++)
        {
            var leftCoefficient = RandomCoefficient(random, random.Next(1, precision + 1));
            var rightCoefficient = RandomCoefficient(random, random.Next(1, precision + 1));
            var leftNegative = random.Next(2) == 1;
            var rightNegative = random.Next(2) == 1;
            var leftExponent = RandomExponent<TFormat>(random);
            var rightExponent = RandomExponent<TFormat>(random);
            var rounding = Roundings[random.Next(Roundings.Length)];

            var expectedStatus = DecimalStatus.None;
            var expected = DecimalArithmetic.Add<TFormat>(
                new UnpackedDecimal<UInt128>(DecimalKind.Finite, leftNegative, leftExponent, leftCoefficient),
                new UnpackedDecimal<UInt128>(DecimalKind.Finite, rightNegative, rightExponent, rightCoefficient),
                rounding, ref expectedStatus);

            var left = Build(leftCoefficient, leftNegative, leftExponent, leftBuffer);
            var right = Build(rightCoefficient, rightNegative, rightExponent, rightBuffer);

            var actualStatus = DecimalStatus.None;
            var actual = BcdArithmetic.Add<TFormat>(left, right, resultBuffer, rounding,
                leftWork, rightWork);
            BcdFinalizer.Finalize<TFormat>(ref actual, rounding, ref actualStatus);

            var context = $"{Describe(leftCoefficient, leftNegative, leftExponent)} plus "
                + $"{Describe(rightCoefficient, rightNegative, rightExponent)} under {rounding}";

            Assert.True(expected.Kind == actual.Kind, $"kind for {context}");
            Assert.True(expected.IsNegative == actual.IsNegative,
                $"sign {expected.IsNegative} against {actual.IsNegative} for {context}");
            Assert.True(expectedStatus == actualStatus,
                $"status {expectedStatus} against {actualStatus} for {context}");

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

    private static void RunMultiplyDifferential<TFormat>(int seed, int precision)
        where TFormat : IDecimalFormat
    {
        var random = new Random(seed);

        var leftBuffer = stackalloc byte[BufferLength];
        var rightBuffer = stackalloc byte[BufferLength];
        var productBuffer = stackalloc byte[BufferLength];
        var leftLimbs = stackalloc uint[16];
        var rightLimbs = stackalloc uint[16];
        var accumulator = stackalloc ulong[32];

        for (var iteration = 0; iteration < 40000; iteration++)
        {
            var leftCoefficient = RandomCoefficient(random, random.Next(1, precision + 1));
            var rightCoefficient = RandomCoefficient(random, random.Next(1, precision + 1));
            var leftNegative = random.Next(2) == 1;
            var rightNegative = random.Next(2) == 1;

            // Halved, because a product's exponent is the sum of the operands' and the
            // interesting cases are the ones that land near the format's limits.
            var leftExponent = RandomExponent<TFormat>(random) / 2;
            var rightExponent = RandomExponent<TFormat>(random) / 2;
            var rounding = Roundings[random.Next(Roundings.Length)];

            var expectedStatus = DecimalStatus.None;
            var expected = DecimalArithmetic.Multiply<TFormat>(
                new UnpackedDecimal<UInt128>(DecimalKind.Finite, leftNegative, leftExponent, leftCoefficient),
                new UnpackedDecimal<UInt128>(DecimalKind.Finite, rightNegative, rightExponent, rightCoefficient),
                rounding, ref expectedStatus);

            var left = Build(leftCoefficient, leftNegative, leftExponent, leftBuffer);
            var right = Build(rightCoefficient, rightNegative, rightExponent, rightBuffer);

            var actualStatus = DecimalStatus.None;
            var actual = BcdMultiplier.Multiply<TFormat>(left, right, productBuffer,
                leftLimbs, rightLimbs, accumulator);
            BcdFinalizer.Finalize<TFormat>(ref actual, rounding, ref actualStatus);

            var context = $"{Describe(leftCoefficient, leftNegative, leftExponent)} times "
                + $"{Describe(rightCoefficient, rightNegative, rightExponent)} under {rounding}";

            Assert.True(expected.Kind == actual.Kind, $"kind for {context}");
            Assert.True(expected.IsNegative == actual.IsNegative,
                $"sign {expected.IsNegative} against {actual.IsNegative} for {context}");
            Assert.True(expectedStatus == actualStatus,
                $"status {expectedStatus} against {actualStatus} for {context}");

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

    private static void RunFusedDifferential<TFormat>(int seed, int precision)
        where TFormat : IDecimalFormat
    {
        var random = new Random(seed);

        var leftBuffer = stackalloc byte[BufferLength];
        var rightBuffer = stackalloc byte[BufferLength];
        var addendBuffer = stackalloc byte[BufferLength];
        var productBuffer = stackalloc byte[BufferLength];
        var resultBuffer = stackalloc byte[BufferLength];
        var leftWork = stackalloc byte[BufferLength];
        var rightWork = stackalloc byte[BufferLength];
        var leftLimbs = stackalloc uint[16];
        var rightLimbs = stackalloc uint[16];
        var accumulator = stackalloc ulong[32];

        for (var iteration = 0; iteration < 40000; iteration++)
        {
            var leftCoefficient = RandomCoefficient(random, random.Next(1, precision + 1));
            var rightCoefficient = RandomCoefficient(random, random.Next(1, precision + 1));
            var addendCoefficient = RandomCoefficient(random, random.Next(1, precision + 1));
            var leftNegative = random.Next(2) == 1;
            var rightNegative = random.Next(2) == 1;
            var addendNegative = random.Next(2) == 1;
            var leftExponent = RandomExponent<TFormat>(random) / 2;
            var rightExponent = RandomExponent<TFormat>(random) / 2;
            var addendExponent = RandomExponent<TFormat>(random);
            var rounding = Roundings[random.Next(Roundings.Length)];

            var expectedStatus = DecimalStatus.None;
            var expected = DecimalArithmetic.FusedMultiplyAdd<TFormat>(
                new UnpackedDecimal<UInt128>(DecimalKind.Finite, leftNegative, leftExponent, leftCoefficient),
                new UnpackedDecimal<UInt128>(DecimalKind.Finite, rightNegative, rightExponent, rightCoefficient),
                new UnpackedDecimal<UInt128>(DecimalKind.Finite, addendNegative, addendExponent, addendCoefficient),
                rounding, ref expectedStatus);

            var left = Build(leftCoefficient, leftNegative, leftExponent, leftBuffer);
            var right = Build(rightCoefficient, rightNegative, rightExponent, rightBuffer);
            var addend = Build(addendCoefficient, addendNegative, addendExponent, addendBuffer);

            var actualStatus = DecimalStatus.None;
            var actual = BcdArithmetic.FusedMultiplyAdd<TFormat>(left, right, addend, rounding,
                productBuffer, leftLimbs, rightLimbs, accumulator, resultBuffer, leftWork, rightWork);
            BcdFinalizer.Finalize<TFormat>(ref actual, rounding, ref actualStatus);

            var context = $"{Describe(leftCoefficient, leftNegative, leftExponent)} times "
                + $"{Describe(rightCoefficient, rightNegative, rightExponent)} plus "
                + $"{Describe(addendCoefficient, addendNegative, addendExponent)} under {rounding}";

            Assert.True(expected.Kind == actual.Kind, $"kind for {context}");
            Assert.True(expected.IsNegative == actual.IsNegative,
                $"sign {expected.IsNegative} against {actual.IsNegative} for {context}");
            Assert.True(expectedStatus == actualStatus,
                $"status {expectedStatus} against {actualStatus} for {context}");

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

    private static void RunDivideDifferential<TFormat>(int seed, int precision)
        where TFormat : IDecimalFormat
    {
        var random = new Random(seed);

        var leftBuffer = stackalloc byte[BufferLength];
        var rightBuffer = stackalloc byte[BufferLength];
        var quotientBuffer = stackalloc byte[BufferLength];
        var accumulator = stackalloc uint[32];
        var divisor = stackalloc uint[16];
        var quotient = stackalloc uint[16];

        for (var iteration = 0; iteration < 40000; iteration++)
        {
            var leftCoefficient = RandomCoefficient(random, random.Next(1, precision + 1));
            var rightCoefficient = RandomCoefficient(random, random.Next(1, precision + 1));
            var leftNegative = random.Next(2) == 1;
            var rightNegative = random.Next(2) == 1;
            var leftExponent = RandomExponent<TFormat>(random) / 2;
            var rightExponent = RandomExponent<TFormat>(random) / 2;
            var rounding = Roundings[random.Next(Roundings.Length)];

            var expectedStatus = DecimalStatus.None;
            var expected = DecimalArithmetic.Divide<TFormat>(
                new UnpackedDecimal<UInt128>(DecimalKind.Finite, leftNegative, leftExponent, leftCoefficient),
                new UnpackedDecimal<UInt128>(DecimalKind.Finite, rightNegative, rightExponent, rightCoefficient),
                rounding, ref expectedStatus);

            var left = Build(leftCoefficient, leftNegative, leftExponent, leftBuffer);
            var right = Build(rightCoefficient, rightNegative, rightExponent, rightBuffer);

            var actualStatus = DecimalStatus.None;
            var actual = BcdDivider.Divide<TFormat>(left, right, quotientBuffer,
                accumulator, divisor, quotient, ref actualStatus);

            if (actual.Kind == DecimalKind.Finite)
            {
                BcdFinalizer.Finalize<TFormat>(ref actual, rounding, ref actualStatus);
            }

            var context = $"{Describe(leftCoefficient, leftNegative, leftExponent)} over "
                + $"{Describe(rightCoefficient, rightNegative, rightExponent)} under {rounding}";

            Assert.True(expected.Kind == actual.Kind, $"kind for {context}");
            Assert.True(expected.IsNegative == actual.IsNegative,
                $"sign {expected.IsNegative} against {actual.IsNegative} for {context}");
            Assert.True(expectedStatus == actualStatus,
                $"status {expectedStatus} against {actualStatus} for {context}");

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

    private static void RunCompareDifferential<TFormat>(int seed, int precision)
        where TFormat : IDecimalFormat
    {
        var random = new Random(seed);

        var leftBuffer = stackalloc byte[BufferLength];
        var rightBuffer = stackalloc byte[BufferLength];

        for (var iteration = 0; iteration < 40000; iteration++)
        {
            // Operands drawn from a narrow set of exponents and digit counts, so that equal
            // values with unequal exponents come up often rather than by accident.
            var leftCoefficient = RandomCoefficient(random, random.Next(1, precision + 1));
            var rightCoefficient = random.Next(4) == 0
                ? leftCoefficient
                : RandomCoefficient(random, random.Next(1, precision + 1));

            var leftNegative = random.Next(2) == 1;
            var rightNegative = random.Next(2) == 1;
            var leftExponent = random.Next(-8, 9);
            var rightExponent = random.Next(-8, 9);

            var expected = ExpectedCompare(
                new UnpackedDecimal<UInt128>(DecimalKind.Finite, leftNegative, leftExponent, leftCoefficient),
                new UnpackedDecimal<UInt128>(DecimalKind.Finite, rightNegative, rightExponent, rightCoefficient));

            var left = Build(leftCoefficient, leftNegative, leftExponent, leftBuffer);
            var right = Build(rightCoefficient, rightNegative, rightExponent, rightBuffer);
            var actual = BcdArithmetic.Compare(left, right);

            var context = $"{Describe(leftCoefficient, leftNegative, leftExponent)} against "
                + $"{Describe(rightCoefficient, rightNegative, rightExponent)}";

            Assert.True(Math.Sign(expected) == Math.Sign(actual),
                $"compare {expected} against {actual} for {context}");
        }
    }

    /// <summary>
    /// A signed comparison built on the binary magnitude comparison, which scales one
    /// coefficient to the other's exponent where the digit form walks the digits. That
    /// magnitude step is what the differential is really checking; the sign rules around it
    /// are the same few lines either way.
    /// </summary>
    private static int ExpectedCompare(UnpackedDecimal<UInt128> left, UnpackedDecimal<UInt128> right)
    {
        var leftZero = left.Coefficient == UInt128.Zero;
        var rightZero = right.Coefficient == UInt128.Zero;

        if (leftZero && rightZero)
        {
            return 0;
        }

        if (leftZero)
        {
            return right.IsNegative ? 1 : -1;
        }

        if (rightZero)
        {
            return left.IsNegative ? -1 : 1;
        }

        if (left.IsNegative != right.IsNegative)
        {
            return left.IsNegative ? -1 : 1;
        }

        var magnitude = DecimalOperations.CompareFiniteValue(left, right);
        return left.IsNegative ? -magnitude : magnitude;
    }

    private static string Describe(UInt128 coefficient, bool isNegative, int exponent)
    {
        return $"{(isNegative ? "-" : "")}{coefficient}E{exponent}";
    }

    private static UInt128 RandomCoefficient(Random random, int digitCount)
    {
        var value = UInt128.Zero;
        for (var position = 0; position < digitCount; position++)
        {
            var digit = position == 0 ? 1 + random.Next(9) : random.Next(10);
            value = (value * 10) + (uint)digit;
        }

        // A zero operand is a case in its own right, and one the random draw above never
        // produces.
        return random.Next(16) == 0 ? UInt128.Zero : value;
    }

    private static int RandomExponent<TFormat>(Random random)
        where TFormat : IDecimalFormat
    {
        return random.Next(6) switch
        {
            0 => TFormat.MinQuantumExponent + random.Next(-2, 3),
            1 => TFormat.MaxQuantumExponent + random.Next(-2, 3),
            2 => TFormat.MinExponent + random.Next(-2, 3),
            3 => TFormat.MaxExponent + random.Next(-2, 3),
            4 => random.Next(-12, 13),
            _ => random.Next(-40, 41)
        };
    }

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
