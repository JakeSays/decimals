// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace Decimals.Benchmarks;

/// <summary>
/// Decimal128 on coefficients that fill it. The wide set decbench builds tops out at
/// sixteen digits, and two of those multiply to thirty-two, which a Decimal128 holds
/// exactly -- so that set never asks the widest format to round, and the multiply it times
/// is the one that skips the rounding. These operands are thirty-four digits, so the
/// products do not fit and the work the other set skips gets measured.
/// </summary>
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class FullWidthBenchmarks
{
    private Decimal128[] _values = [];

    [GlobalSetup]
    public void Setup()
    {
        _values = BenchmarkValues.FullWidth128;
    }

    [BenchmarkCategory("add")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal128 AddFullWidth()
    {
        var result = Decimal128.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _values[index] + _values[BenchmarkValues.Second(index)];
        }

        return result;
    }

    [BenchmarkCategory("multiply")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal128 MultiplyFullWidth()
    {
        var result = Decimal128.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _values[index] * _values[BenchmarkValues.Second(index)];
        }

        return result;
    }

    [BenchmarkCategory("divide")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal128 DivideFullWidth()
    {
        var result = Decimal128.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _values[index] / _values[BenchmarkValues.Second(index)];
        }

        return result;
    }

    [BenchmarkCategory("fma")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal128 FusedMultiplyAddFullWidth()
    {
        var result = Decimal128.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = Decimal128.FusedMultiplyAdd(_values[index],
                _values[BenchmarkValues.Second(index)],
                _values[BenchmarkValues.Third(index)]);
        }

        return result;
    }

    [BenchmarkCategory("compare")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public int CompareFullWidth()
    {
        var result = 0;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _values[index].CompareTo(_values[BenchmarkValues.Second(index)]);
        }

        return result;
    }
}
