// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace Decimals.Benchmarks;

/// <summary>
/// The 256-bit primitives, at the widths Decimal128 addition actually reaches. Decimal64 and
/// Decimal32 add inside 128 bits; Decimal128 cannot, because 34 digits plus three guard
/// digits plus a carry needs 39, and a <see cref="UInt128"/> holds 38. So every one of these
/// sits on the Decimal128 add path and nowhere else.
/// </summary>
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class WidePrimitiveBenchmarks
{
    private const int Count = 1024;

    /// <summary>Thirty-four digits: a Decimal128 coefficient, which still fits 128 bits.</summary>
    private UInt256[] _coefficients = [];

    /// <summary>Forty digits: an aligned intermediate, which does not.</summary>
    private UInt256[] _intermediates = [];

    private UInt256[] _addends = [];

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(20260818);
        _coefficients = new UInt256[Count];
        _intermediates = new UInt256[Count];
        _addends = new UInt256[Count];

        for (var index = 0; index < Count; index++)
        {
            var coefficient = BuildDigits(random, 34);
            _coefficients[index] = coefficient;

            // Six digits wider, which is what lining two operands up produces.
            _intermediates[index] = UInt256.MultiplyByPowerOfTen(coefficient, 6);
            _addends[index] = UInt256.MultiplyByPowerOfTen(BuildDigits(random, 34), 6);
        }
    }

    private static UInt256 BuildDigits(Random random, int digits)
    {
        var value = UInt256.Zero;
        for (var position = 0; position < digits; position++)
        {
            var digit = position == 0
                ? random.Next(1, 10)
                : random.Next(0, 10);
            value = UInt256.MultiplyByUInt64(value, 10) + new UInt256((ulong)digit);
        }

        return value;
    }

    [BenchmarkCategory("count digits")]
    [Benchmark(Baseline = true, OperationsPerInvoke = Count)]
    public int CountDigitsInsideUInt128()
    {
        var digits = 0;
        for (var index = 0; index < Count; index++)
        {
            digits = _coefficients[index].CountDigits();
        }

        return digits;
    }

    [BenchmarkCategory("count digits")]
    [Benchmark(OperationsPerInvoke = Count)]
    public int CountDigitsPastUInt128()
    {
        var digits = 0;
        for (var index = 0; index < Count; index++)
        {
            digits = _intermediates[index].CountDigits();
        }

        return digits;
    }

    /// <summary>Folding an operand into the rounding window, which add does twice.</summary>
    [BenchmarkCategory("divide")]
    [Benchmark(Baseline = true, OperationsPerInvoke = Count)]
    public ulong DivideWideByPowerOfTen()
    {
        var result = UInt256.Zero;
        for (var index = 0; index < Count; index++)
        {
            result = UInt256.DivideByPowerOfTen(_intermediates[index], 6, out _);
        }

        return result.Limb0;
    }

    /// <summary>The same, at the width the finalizer drops from.</summary>
    [BenchmarkCategory("divide")]
    [Benchmark(OperationsPerInvoke = Count)]
    public ulong DivideWideByLargePowerOfTen()
    {
        var result = UInt256.Zero;
        for (var index = 0; index < Count; index++)
        {
            result = UInt256.DivideByPowerOfTen(_intermediates[index], 22, out _);
        }

        return result.Limb0;
    }

    [BenchmarkCategory("scale")]
    [Benchmark(Baseline = true, OperationsPerInvoke = Count)]
    public ulong MultiplyWideByPowerOfTen()
    {
        var result = UInt256.Zero;
        for (var index = 0; index < Count; index++)
        {
            result = UInt256.MultiplyByPowerOfTen(_coefficients[index], 6);
        }

        return result.Limb0;
    }

    [BenchmarkCategory("scale")]
    [Benchmark(OperationsPerInvoke = Count)]
    public ulong MultiplyWideByLargePowerOfTen()
    {
        var result = UInt256.Zero;
        for (var index = 0; index < Count; index++)
        {
            result = UInt256.MultiplyByPowerOfTen(_coefficients[index], 22);
        }

        return result.Limb0;
    }

    [BenchmarkCategory("add")]
    [Benchmark(Baseline = true, OperationsPerInvoke = Count)]
    public ulong AddWide()
    {
        var result = UInt256.Zero;
        for (var index = 0; index < Count; index++)
        {
            result = _intermediates[index] + _addends[index];
        }

        return result.Limb0;
    }

    [BenchmarkCategory("add")]
    [Benchmark(OperationsPerInvoke = Count)]
    public int CompareWide()
    {
        var comparison = 0;
        for (var index = 0; index < Count; index++)
        {
            comparison = _intermediates[index].CompareTo(_addends[index]);
        }

        return comparison;
    }
}
