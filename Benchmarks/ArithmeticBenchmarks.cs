// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace Decimals.Benchmarks;

/// <summary>
/// What decimal arithmetic costs against the two types a .NET caller would otherwise reach
/// for. <see cref="Decimal64"/> is the baseline in each group, so the ratio column reads as
/// "how much cheaper the alternative is".
/// </summary>
/// <remarks>
/// The operands are the narrow set, which all three types hold exactly; see
/// <see cref="BenchmarkValues"/> for why that matters. Each method runs a loop of
/// <see cref="BenchmarkValues.Count"/> operations and declares as much through
/// <c>OperationsPerInvoke</c>, so the reported time is per operation and can be read
/// against the C++ decbench figures directly.
/// </remarks>
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ArithmeticBenchmarks
{
    private Decimal64[] _decimals = [];
    private decimal[] _systemDecimals = [];
    private double[] _doubles = [];

    [GlobalSetup]
    public void Setup()
    {
        _decimals = BenchmarkValues.Narrow64;
        _systemDecimals = BenchmarkValues.NarrowDecimal;
        _doubles = BenchmarkValues.NarrowDouble;
    }

    [BenchmarkCategory("add")]
    [Benchmark(Baseline = true, OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal64 AddDecimal64()
    {
        var result = Decimal64.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _decimals[index] + _decimals[BenchmarkValues.Second(index)];
        }

        return result;
    }

    [BenchmarkCategory("add")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public decimal AddSystemDecimal()
    {
        var result = decimal.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _systemDecimals[index] + _systemDecimals[BenchmarkValues.Second(index)];
        }

        return result;
    }

    [BenchmarkCategory("add")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public double AddDouble()
    {
        var result = 0.0;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _doubles[index] + _doubles[BenchmarkValues.Second(index)];
        }

        return result;
    }

    [BenchmarkCategory("multiply")]
    [Benchmark(Baseline = true, OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal64 MultiplyDecimal64()
    {
        var result = Decimal64.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _decimals[index] * _decimals[BenchmarkValues.Second(index)];
        }

        return result;
    }

    [BenchmarkCategory("multiply")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public decimal MultiplySystemDecimal()
    {
        var result = decimal.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _systemDecimals[index] * _systemDecimals[BenchmarkValues.Second(index)];
        }

        return result;
    }

    [BenchmarkCategory("multiply")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public double MultiplyDouble()
    {
        var result = 0.0;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _doubles[index] * _doubles[BenchmarkValues.Second(index)];
        }

        return result;
    }

    [BenchmarkCategory("divide")]
    [Benchmark(Baseline = true, OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal64 DivideDecimal64()
    {
        var result = Decimal64.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _decimals[index] / _decimals[BenchmarkValues.Second(index)];
        }

        return result;
    }

    [BenchmarkCategory("divide")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public decimal DivideSystemDecimal()
    {
        var result = decimal.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _systemDecimals[index] / _systemDecimals[BenchmarkValues.Second(index)];
        }

        return result;
    }

    [BenchmarkCategory("divide")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public double DivideDouble()
    {
        var result = 0.0;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _doubles[index] / _doubles[BenchmarkValues.Second(index)];
        }

        return result;
    }

    [BenchmarkCategory("fma")]
    [Benchmark(Baseline = true, OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal64 FusedMultiplyAddDecimal64()
    {
        var result = Decimal64.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = Decimal64.FusedMultiplyAdd(_decimals[index],
                _decimals[BenchmarkValues.Second(index)], _decimals[BenchmarkValues.Third(index)]);
        }

        return result;
    }

    /// <summary>
    /// System.Decimal has no fused form, so this is the two operations it would take -- the
    /// comparison is against what a caller can actually write.
    /// </summary>
    [BenchmarkCategory("fma")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public decimal FusedMultiplyAddSystemDecimal()
    {
        var result = decimal.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = (_systemDecimals[index] * _systemDecimals[BenchmarkValues.Second(index)])
                + _systemDecimals[BenchmarkValues.Third(index)];
        }

        return result;
    }

    [BenchmarkCategory("fma")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public double FusedMultiplyAddDouble()
    {
        var result = 0.0;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = Math.FusedMultiplyAdd(_doubles[index], _doubles[BenchmarkValues.Second(index)],
                _doubles[BenchmarkValues.Third(index)]);
        }

        return result;
    }

    [BenchmarkCategory("compare")]
    [Benchmark(Baseline = true, OperationsPerInvoke = BenchmarkValues.Count)]
    public int CompareDecimal64()
    {
        var result = 0;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _decimals[index].CompareTo(_decimals[BenchmarkValues.Second(index)]);
        }

        return result;
    }

    [BenchmarkCategory("compare")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public int CompareSystemDecimal()
    {
        var result = 0;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _systemDecimals[index].CompareTo(_systemDecimals[BenchmarkValues.Second(index)]);
        }

        return result;
    }

    [BenchmarkCategory("compare")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public int CompareDouble()
    {
        var result = 0;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _doubles[index].CompareTo(_doubles[BenchmarkValues.Second(index)]);
        }

        return result;
    }
}
