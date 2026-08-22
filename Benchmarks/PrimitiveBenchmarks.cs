// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace Decimals.Benchmarks;

/// <summary>
/// The pieces underneath an operation, timed one at a time. A profile says which frames an
/// operation sits in; this says what each of those frames costs on its own, which is what
/// makes it obvious whether a frame is expensive or merely popular.
/// </summary>
/// <remarks>
/// The inputs are what a Decimal64 add actually hands these routines: sixteen-digit
/// coefficients, and the thirty-two digit intermediates that come of lining two of them up.
/// </remarks>
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class PrimitiveBenchmarks
{
    private const int Count = 1024;

    private UInt128[] _coefficients = [];
    private ulong[] _encoded = [];
    private UInt256[] _narrowIntermediates = [];
    private UInt256[] _wideIntermediates = [];

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(20260817);
        _coefficients = new UInt128[Count];
        _encoded = new ulong[Count];
        _narrowIntermediates = new UInt256[Count];
        _wideIntermediates = new UInt256[Count];

        for (var index = 0; index < Count; index++)
        {
            // Sixteen digits, the width of a decimal64 coefficient.
            var coefficient = (UInt128)random.NextInt64(1_000_000_000_000_000L, 9_999_999_999_999_999L);
            _coefficients[index] = coefficient;
            _encoded[index] = Decimal64Format.Pack(new UnpackedDecimal<UInt128>(
                DecimalKind.Finite, false, -5, coefficient));
            _narrowIntermediates[index] = new UInt256(coefficient);

            // Thirty-two digits, which is what two of them aligned or multiplied comes to.
            _wideIntermediates[index] = UInt256.Multiply(coefficient, coefficient);
        }
    }

    /// <summary>
    /// The digit count an add takes twice, once per operand, to find where each operand's
    /// leading digit sits.
    /// </summary>
    [BenchmarkCategory("count digits")]
    [Benchmark(Baseline = true, OperationsPerInvoke = Count)]
    public int CountDigitsOnWideSixteen()
    {
        var digits = 0;
        for (var index = 0; index < Count; index++)
        {
            digits = _narrowIntermediates[index].CountDigits();
        }

        return digits;
    }

    [BenchmarkCategory("count digits")]
    [Benchmark(OperationsPerInvoke = Count)]
    public int CountDigitsOnWideThirtyTwo()
    {
        var digits = 0;
        for (var index = 0; index < Count; index++)
        {
            digits = _wideIntermediates[index].CountDigits();
        }

        return digits;
    }

    /// <summary>
    /// The other digit count, the one the finalizer uses. Same question, different routine:
    /// this one compares against a table of powers instead of dividing.
    /// </summary>
    [BenchmarkCategory("count digits")]
    [Benchmark(OperationsPerInvoke = Count)]
    public int CountDigitsOnNarrow()
    {
        var digits = 0;
        for (var index = 0; index < Count; index++)
        {
            digits = DecimalRounder.CountDigits(_coefficients[index]);
        }

        return digits;
    }

    [BenchmarkCategory("scale")]
    [Benchmark(Baseline = true, OperationsPerInvoke = Count)]
    public ulong MultiplyByPowerOfTen()
    {
        var result = UInt256.Zero;
        for (var index = 0; index < Count; index++)
        {
            result = UInt256.MultiplyByPowerOfTen(_narrowIntermediates[index], 20);
        }

        // The limb rather than the value itself: a benchmark method has to be public, and
        // the wide type is not.
        return result.Limb0;
    }

    [BenchmarkCategory("scale")]
    [Benchmark(OperationsPerInvoke = Count)]
    public ulong DivideByPowerOfTen()
    {
        var result = UInt256.Zero;
        for (var index = 0; index < Count; index++)
        {
            result = UInt256.DivideByPowerOfTen(_wideIntermediates[index], 20, out _);
        }

        return result.Limb0;
    }

    /// <summary>
    /// Dividing a 128-bit value by a power of ten, which is what folding an operand into
    /// the rounding window and rounding a result both come down to.
    /// </summary>
    [BenchmarkCategory("divide")]
    [Benchmark(Baseline = true, OperationsPerInvoke = Count)]
    public UInt128 DivideUInt128ByPowerOfTen()
    {
        var result = UInt128.Zero;
        for (var index = 0; index < Count; index++)
        {
            result = _coefficients[index] / PowersOfTen.UInt128(8);
        }

        return result;
    }

    /// <summary>The same division as a multiply, which is what the rounding path now does.</summary>
    [BenchmarkCategory("divide")]
    [Benchmark(OperationsPerInvoke = Count)]
    public UInt128 DivideUInt128ByReciprocal()
    {
        var result = UInt128.Zero;
        for (var index = 0; index < Count; index++)
        {
            result = PowersOfTen.DivideByPowerOfTen(_coefficients[index], 8);
        }

        return result;
    }

    /// <summary>
    /// And at a power past 64 bits, where the divisor no longer fits a machine word and the
    /// software division has the furthest to fall.
    /// </summary>
    [BenchmarkCategory("divide")]
    [Benchmark(OperationsPerInvoke = Count)]
    public UInt128 DivideUInt128ByWidePowerOfTen()
    {
        var result = UInt128.Zero;
        for (var index = 0; index < Count; index++)
        {
            result = _wideIntermediates[index].ToUInt128() / PowersOfTen.UInt128(20);
        }

        return result;
    }

    [BenchmarkCategory("divide")]
    [Benchmark(OperationsPerInvoke = Count)]
    public UInt128 DivideUInt128ByWideReciprocal()
    {
        var result = UInt128.Zero;
        for (var index = 0; index < Count; index++)
        {
            result = PowersOfTen.DivideByPowerOfTen(_wideIntermediates[index].ToUInt128(), 20);
        }

        return result;
    }

    /// <summary>The same division at 64 bits, where the processor has an instruction.</summary>
    [BenchmarkCategory("divide")]
    [Benchmark(OperationsPerInvoke = Count)]
    public ulong DivideUInt64ByPowerOfTen()
    {
        var result = 0UL;
        for (var index = 0; index < Count; index++)
        {
            result = (ulong)_coefficients[index] / PowersOfTen.UInt64(8);
        }

        return result;
    }

    /// <summary>And the multiply, for the division to be read against.</summary>
    [BenchmarkCategory("divide")]
    [Benchmark(OperationsPerInvoke = Count)]
    public UInt128 MultiplyUInt128ByPowerOfTen()
    {
        var result = UInt128.Zero;
        for (var index = 0; index < Count; index++)
        {
            result = _coefficients[index] * PowersOfTen.UInt128(8);
        }

        return result;
    }

    /// <summary>Taking the encoding apart, which every operation does to both operands.</summary>
    [BenchmarkCategory("encoding")]
    [Benchmark(Baseline = true, OperationsPerInvoke = Count)]
    public int Unpack()
    {
        var exponent = 0;
        for (var index = 0; index < Count; index++)
        {
            exponent = Decimal64Format.Unpack(_encoded[index]).Exponent;
        }

        return exponent;
    }

    /// <summary>And putting one back together, which every operation does once.</summary>
    [BenchmarkCategory("encoding")]
    [Benchmark(OperationsPerInvoke = Count)]
    public ulong Pack()
    {
        var bits = 0UL;
        for (var index = 0; index < Count; index++)
        {
            bits = Decimal64Format.Pack(Decimal64Format.Unpack(_encoded[index]));
        }

        return bits;
    }

    /// <summary>
    /// Rounding a result into the format and settling its exponent: the tail of every
    /// arithmetic operation.
    /// </summary>
    [BenchmarkCategory("encoding")]
    [Benchmark(OperationsPerInvoke = Count)]
    public int FinalizeValue()
    {
        var exponent = 0;
        for (var index = 0; index < Count; index++)
        {
            var status = DecimalStatus.None;
            exponent = DecimalFinalizer.Finalize<Decimal64Format>(false, _coefficients[index],
                -5, DecimalRounding.HalfEven, ref status).Exponent;
        }

        return exponent;
    }

    [BenchmarkCategory("round")]
    [Benchmark(Baseline = true, OperationsPerInvoke = Count)]
    public UInt128 RoundSixteenDigitsAway()
    {
        var result = UInt128.Zero;
        for (var index = 0; index < Count; index++)
        {
            result = DecimalRounder.Round(_coefficients[index], 8, false,
                DecimalRounding.HalfEven, out _);
        }

        return result;
    }

    /// <summary>The whole operation, for the pieces above to be read against.</summary>
    [BenchmarkCategory("round")]
    [Benchmark(OperationsPerInvoke = Count)]
    public Decimal64 WholeAdd()
    {
        var values = BenchmarkValues.Wide64;
        var result = Decimal64.Zero;
        for (var index = 0; index < Count; index++)
        {
            result = values[index] + values[BenchmarkValues.Second(index)];
        }

        return result;
    }
}
