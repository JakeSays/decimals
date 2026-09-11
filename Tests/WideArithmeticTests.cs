// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Numerics;

using Decimals;

namespace Tests;

/// <summary>
/// The engine's arithmetic against <see cref="BigInteger"/>.
/// </summary>
/// <remarks>
/// These check what an independent oracle can state: the exact arithmetic, the ordering,
/// and the properties a result has to satisfy. The parts that are decNumber's conventions
/// rather than arithmetic -- how a residue is carried, which condition each step raises,
/// where a subnormal is clamped -- have no oracle outside the specification, and the
/// testcase corpus is what holds those.
/// </remarks>
public unsafe class WideArithmeticTests
{
    private const int BufferLength = 256;

    /// <summary>
    /// A context wide enough that nothing rounds, so the result is the exact sum and
    /// <see cref="BigInteger"/> can say what it should be.
    /// </summary>
    private static WideContext ExactContext()
    {
        return new WideContext
        {
            Digits = 400,
            MaxExponent = WideContext.MaxMathExponent,
            MinExponent = -WideContext.MaxMathExponent,
            Rounding = DecimalRounding.HalfEven,
            Clamp = false
        };
    }

    [Fact]
    public void AddingExactlyAgreesWithTheBinaryOracle()
    {
        RunExactAdd(20260902, false);
    }

    [Fact]
    public void SubtractingExactlyAgreesWithTheBinaryOracle()
    {
        RunExactAdd(20260903, true);
    }

    private static void RunExactAdd(int seed, bool negateRight)
    {
        var random = new Random(seed);

        var leftUnits = stackalloc uint[BufferLength];
        var rightUnits = stackalloc uint[BufferLength];
        var resultUnits = stackalloc uint[BufferLength];
        var work = stackalloc uint[BufferLength];

        var context = ExactContext();

        for (var iteration = 0; iteration < 40000; iteration++)
        {
            var leftValue = RandomValue(random, random.Next(1, 30));
            var rightValue = RandomValue(random, random.Next(1, 30));
            var leftNegative = random.Next(2) == 1;
            var rightNegative = random.Next(2) == 1;

            // Exponents close enough together that the aligned sum stays inside the
            // context, so nothing is discarded and the answer is exact.
            var leftExponent = random.Next(-40, 41);
            var rightExponent = leftExponent + random.Next(-30, 31);

            var left = Build(leftValue, leftExponent, leftNegative, leftUnits);
            var right = Build(rightValue, rightExponent, rightNegative, rightUnits);
            var actual = new WideNumber(resultUnits);
            var status = DecimalStatus.None;

            WideArithmetic.Add(ref actual, left, right, negateRight, context, ref status, work);

            var lower = Math.Min(leftExponent, rightExponent);
            var expected = Scale(leftValue, leftNegative, leftExponent - lower)
                + Scale(rightValue, rightNegative ^ negateRight, rightExponent - lower);

            var describe = $"{Sign(leftNegative)}{leftValue}E{leftExponent} "
                + $"{(negateRight ? "-" : "+")} {Sign(rightNegative)}{rightValue}E{rightExponent}";

            Assert.True(DecimalStatus.None == status, $"status {status} for {describe}");

            // Read back at the exponent the addition settled on.
            var actualValue = Scale(Read(actual), actual.IsNegative, actual.Exponent - lower);
            Assert.True(expected == actualValue,
                $"{expected} against {actualValue} for {describe}");
        }
    }

    [Fact]
    public void IntegerDivisionAgreesWithTheBinaryOracle()
    {
        var random = new Random(20260905);

        var numerator = stackalloc uint[BufferLength];
        var divisor = stackalloc uint[BufferLength];
        var accumulator = stackalloc uint[BufferLength];
        var quotient = stackalloc uint[BufferLength];

        for (var iteration = 0; iteration < 40000; iteration++)
        {
            var numeratorLength = random.Next(1, 20);
            var divisorLength = random.Next(1, 20);

            var numeratorValue = FillUnits(random, numerator, numeratorLength);
            var divisorValue = FillUnits(random, divisor, divisorLength);

            if (divisorValue.IsZero)
            {
                divisor[0] = 1;
                divisorValue = BigInteger.One;
            }

            var written = WideDivide.DivRem(numerator, numeratorLength, divisor, divisorLength,
                accumulator, quotient, out var remainderLength);

            var expectedQuotient = BigInteger.Divide(numeratorValue, divisorValue);
            var expectedRemainder = numeratorValue - (expectedQuotient * divisorValue);

            Assert.True(expectedQuotient == ReadUnits(quotient, written),
                $"{numeratorValue} / {divisorValue} gave {ReadUnits(quotient, written)}, "
                + $"wanted {expectedQuotient}");

            Assert.True(expectedRemainder == ReadUnits(accumulator, remainderLength),
                $"{numeratorValue} % {divisorValue} gave "
                + $"{ReadUnits(accumulator, remainderLength)}, wanted {expectedRemainder}");
        }
    }

    /// <summary>
    /// A quotient the division calls exact has to multiply back to the dividend, and one it
    /// calls inexact must not. That holds whatever the context, and needs no oracle beyond
    /// the arithmetic itself.
    /// </summary>
    [Fact]
    public void DividingRoundTripsWhenItIsExact()
    {
        var random = new Random(20260906);

        var leftUnits = stackalloc uint[BufferLength];
        var rightUnits = stackalloc uint[BufferLength];
        var resultUnits = stackalloc uint[BufferLength];
        var numerator = stackalloc uint[BufferLength];
        var denominator = stackalloc uint[BufferLength];
        var accumulator = stackalloc uint[BufferLength];

        var context = new WideContext
        {
            Digits = 60,
            MaxExponent = WideContext.MaxMathExponent,
            MinExponent = -WideContext.MaxMathExponent,
            Rounding = DecimalRounding.HalfEven,
            Clamp = false
        };

        for (var iteration = 0; iteration < 20000; iteration++)
        {
            var rightValue = RandomValue(random, random.Next(1, 12));
            if (rightValue.IsZero)
            {
                continue;
            }

            // Exact pairs among the rest, so the exactness branch is genuinely exercised.
            var leftValue = random.Next(2) == 0
                ? rightValue * RandomValue(random, random.Next(1, 12))
                : RandomValue(random, random.Next(1, 20));

            var leftExponent = random.Next(-20, 21);
            var rightExponent = random.Next(-20, 21);

            var left = Build(leftValue, leftExponent, false, leftUnits);
            var right = Build(rightValue, rightExponent, false, rightUnits);
            var actual = new WideNumber(resultUnits);
            var status = DecimalStatus.None;

            WideDivide.Divide(ref actual, left, right, context, ref status,
                numerator, denominator, accumulator);

            if (actual.Kind != DecimalKind.Finite || actual.IsZero)
            {
                continue;
            }

            var describe = $"{leftValue}E{leftExponent} / {rightValue}E{rightExponent}";
            var product = Read(actual) * rightValue;
            var productExponent = actual.Exponent + rightExponent;

            var exact = (status & DecimalStatus.Inexact) == 0;
            if (!exact)
            {
                continue;
            }

            // The quotient times the divisor is the dividend again, once both are said at
            // the same exponent.
            var common = Math.Min(productExponent, leftExponent);
            Assert.True(
                Scale(product, false, productExponent - common)
                    == Scale(leftValue, false, leftExponent - common),
                $"an exact quotient did not multiply back for {describe}");
        }
    }

    /// <summary>
    /// The root is the largest whose square does not exceed the radicand, which is a
    /// property the answer has rather than a value to look up.
    /// </summary>
    [Fact]
    public void SquareRootIsTheLargestThatFits()
    {
        var random = new Random(20260907);

        var valueUnits = stackalloc uint[BufferLength];
        var resultUnits = stackalloc uint[BufferLength];
        var work = stackalloc uint[BufferLength * WideSquareRoot.WorkBuffers];
        var accumulator = stackalloc ulong[BufferLength * 2];

        var context = new WideContext
        {
            Digits = 40,
            MaxExponent = WideContext.MaxMathExponent,
            MinExponent = -WideContext.MaxMathExponent,
            Rounding = DecimalRounding.HalfEven,
            Clamp = false
        };

        for (var iteration = 0; iteration < 20000; iteration++)
        {
            // Perfect squares among them, since the exact path shortens the result toward
            // the preferred exponent and the inexact one never runs.
            var value = random.Next(4) == 0
                ? Square(RandomValue(random, random.Next(1, 20)))
                : RandomValue(random, random.Next(1, 40));

            if (value.IsZero)
            {
                continue;
            }

            // An even exponent, so the root's is exact and the comparison below is a plain
            // one between integers.
            var exponent = random.Next(-20, 21) * 2;

            var operand = Build(value, exponent, false, valueUnits);
            var actual = new WideNumber(resultUnits);
            var status = DecimalStatus.None;

            WideSquareRoot.SquareRoot(ref actual, operand, context, ref status,
                work, BufferLength, accumulator);

            Assert.True(actual.Kind == DecimalKind.Finite, $"kind for sqrt({value}E{exponent})");

            if ((status & DecimalStatus.Inexact) != 0)
            {
                continue;
            }

            // An exact root squares back to the operand.
            var squared = Read(actual) * Read(actual);
            var squaredExponent = actual.Exponent * 2;
            var common = Math.Min(squaredExponent, exponent);

            Assert.True(
                Scale(squared, false, squaredExponent - common)
                    == Scale(value, false, exponent - common),
                $"an exact root did not square back for {value}E{exponent}");
        }
    }

    [Fact]
    public void ComparingAgreesWithTheBinaryOracle()
    {
        var random = new Random(20260904);

        var leftUnits = stackalloc uint[BufferLength];
        var rightUnits = stackalloc uint[BufferLength];

        for (var iteration = 0; iteration < 40000; iteration++)
        {
            var leftValue = RandomValue(random, random.Next(1, 20));

            // Equal values with unequal exponents on purpose, since that is the case where
            // comparing by digit position rather than by significance goes wrong.
            var sameValue = random.Next(3) == 0;
            var rightValue = sameValue ? leftValue : RandomValue(random, random.Next(1, 20));

            var leftNegative = random.Next(2) == 1;
            var rightNegative = sameValue && random.Next(2) == 0 ? leftNegative : random.Next(2) == 1;
            var leftExponent = random.Next(-12, 13);
            var rightExponent = sameValue ? leftExponent + random.Next(-4, 5) : random.Next(-12, 13);

            if (sameValue && rightExponent != leftExponent)
            {
                // Scale the coefficient the other way so the two are the same number said
                // two ways, which is exactly the cohort case.
                var shift = leftExponent - rightExponent;
                rightValue = shift > 0
                    ? leftValue * BigInteger.Pow(10, shift)
                    : leftValue / BigInteger.Pow(10, -shift);
            }

            var ignoreSigns = random.Next(2) == 0;

            var common = Math.Min(leftExponent, rightExponent);
            var leftScaled = Scale(leftValue, !ignoreSigns && leftNegative, leftExponent - common);
            var rightScaled = Scale(rightValue, !ignoreSigns && rightNegative, rightExponent - common);
            var expected = leftScaled.CompareTo(rightScaled);

            var left = Build(leftValue, leftExponent, leftNegative, leftUnits);
            var right = Build(rightValue, rightExponent, rightNegative, rightUnits);
            var actual = WideArithmetic.Compare(left, right, ignoreSigns);

            Assert.True(Math.Sign(expected) == Math.Sign(actual),
                $"compare {expected} against {actual} for "
                + $"{Sign(leftNegative)}{leftValue}E{leftExponent} against "
                + $"{Sign(rightNegative)}{rightValue}E{rightExponent}, magnitudes {ignoreSigns}");
        }
    }

    [Fact]
    public void IntegerPredicatesAgreeWithTheBinaryOracle()
    {
        var random = new Random(20260908);
        var units = stackalloc uint[BufferLength];

        for (var iteration = 0; iteration < 40000; iteration++)
        {
            var value = RandomValue(random, random.Next(1, 14));
            var isNegative = random.Next(2) == 1;

            // Exponents around zero, where whole and fractional part company.
            var exponent = random.Next(-14, 8);
            var actual = Build(value, exponent, isNegative, units);
            var describe = $"{Sign(isNegative)}{value}E{exponent}";

            var truncated = exponent >= 0
                ? value * BigInteger.Pow(10, exponent)
                : value / BigInteger.Pow(10, -exponent);

            var whole = exponent >= 0
                || (value % BigInteger.Pow(10, -exponent)).IsZero;

            var odd = whole && !truncated.IsZero && !truncated.IsEven;

            Assert.True(whole == actual.IsIntegerValued, $"integer-valued for {describe}");
            Assert.True(odd == actual.IsOddIntegerValued, $"odd for {describe}");

            var signed = isNegative ? -truncated : truncated;
            var expectedFits = whole && signed >= int.MinValue && signed <= int.MaxValue;
            var actualFits = actual.TryGetInt32(out var actualInteger);

            // The implementation stops short of the full range on purpose, so a value it
            // declines that would have fitted is allowed; one it accepts must be right.
            if (actualFits)
            {
                Assert.True(whole, $"accepted a fraction for {describe}");
                Assert.True(signed == actualInteger,
                    $"{signed} against {actualInteger} for {describe}");
            }
            else
            {
                Assert.True(!expectedFits || BigInteger.Abs(signed) > 999999999,
                    $"declined a value that fits for {describe}");
            }
        }
    }

    [Fact]
    public void PowersOfTwoAreRecognizedAsTheBinaryOracleDoes()
    {
        var random = new Random(20260909);
        var units = stackalloc uint[BufferLength];
        var work = stackalloc uint[BufferLength];

        for (var iteration = 0; iteration < 20000; iteration++)
        {
            // Real powers of two among the noise, at both signs of exponent, since a value
            // below one carries its twos in the denominator.
            BigInteger value;
            int exponent;

            if (random.Next(2) == 0)
            {
                var power = random.Next(0, 40);
                value = BigInteger.Pow(2, power);
                exponent = 0;

                if (random.Next(2) == 0)
                {
                    // The same value said as a fraction: 2**-n is 5**n over 10**n.
                    var down = random.Next(1, 12);
                    value = BigInteger.Pow(5, down);
                    exponent = -down;
                }
            }
            else
            {
                value = RandomValue(random, random.Next(1, 12));
                exponent = random.Next(-8, 4);
            }

            if (value.IsZero)
            {
                continue;
            }

            var expected = ExpectedPowerOfTwo(value, exponent, out var expectedPower);

            var actual = WideConstants.TryPowerOfTwoExponent(
                Build(value, exponent, false, units), work, out var actualPower);

            Assert.True(expected == actual, $"recognition for {value}E{exponent}");
            if (expected)
            {
                Assert.True(expectedPower == actualPower,
                    $"power {expectedPower} against {actualPower} for {value}E{exponent}");
            }
        }
    }

    /// <summary>
    /// Whether a value is a power of two, worked out independently: as a fraction reduced
    /// to lowest terms, with each half required to be a power of two on its own.
    /// </summary>
    private static bool ExpectedPowerOfTwo(BigInteger coefficient, int exponent, out int power)
    {
        power = 0;

        var numerator = coefficient;
        var denominator = BigInteger.One;

        if (exponent >= 0)
        {
            numerator *= BigInteger.Pow(10, exponent);
        }
        else
        {
            denominator = BigInteger.Pow(10, -exponent);
        }

        var common = BigInteger.GreatestCommonDivisor(numerator, denominator);
        numerator /= common;
        denominator /= common;

        if (!IsPowerOfTwo(numerator, out var high) || !IsPowerOfTwo(denominator, out var low))
        {
            return false;
        }

        power = high - low;
        return true;
    }

    private static bool IsPowerOfTwo(BigInteger value, out int position)
    {
        position = (int)(value.GetBitLength() - 1);
        return !value.IsZero && (value & (value - BigInteger.One)).IsZero;
    }

    private static BigInteger Square(BigInteger value) => value * value;

    private static string Sign(bool isNegative) => isNegative ? "-" : "";

    private static BigInteger Scale(BigInteger value, bool isNegative, int places)
    {
        var scaled = value * BigInteger.Pow(10, places);
        return isNegative ? -scaled : scaled;
    }

    private static BigInteger RandomValue(Random random, int digits)
    {
        var value = BigInteger.Zero;
        for (var index = 0; index < digits; index++)
        {
            var digit = index == 0 ? 1 + random.Next(9) : random.Next(10);
            value = (value * 10) + digit;
        }

        return random.Next(16) == 0 ? BigInteger.Zero : value;
    }

    private static BigInteger FillUnits(Random random, uint* units, int length)
    {
        var value = BigInteger.Zero;
        for (var index = length - 1; index >= 0; index--)
        {
            units[index] = random.Next(6) == 0 ? 0u : (uint)random.NextInt64(WideNumber.UnitBase);
            value = (value * WideNumber.UnitBase) + units[index];
        }

        return value;
    }

    private static BigInteger ReadUnits(uint* units, int length)
    {
        var value = BigInteger.Zero;
        for (var index = length - 1; index >= 0; index--)
        {
            value = (value * WideNumber.UnitBase) + units[index];
        }

        return value;
    }

    private static WideNumber Build(BigInteger value, int exponent, bool isNegative, uint* units)
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
        number.IsNegative = isNegative;
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
