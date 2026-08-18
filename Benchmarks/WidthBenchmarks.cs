// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace Decimals.Benchmarks;

/// <summary>
/// The same operations at all three widths, on the wide operand set decbench uses. This is
/// the run to read against the C++ figures, and it is what says how much the 128-bit
/// coefficient costs over the 64-bit one.
/// </summary>
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class WidthBenchmarks
{
    private Decimal32[] _narrow = [];
    private Decimal64[] _middle = [];
    private Decimal128[] _wide = [];

    [GlobalSetup]
    public void Setup()
    {
        _narrow = BenchmarkValues.Wide32;
        _middle = BenchmarkValues.Wide64;
        _wide = BenchmarkValues.Wide128;
    }

    [BenchmarkCategory("add")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal32 AddDecimal32()
    {
        var result = Decimal32.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _narrow[index] + _narrow[BenchmarkValues.Second(index)];
        }

        return result;
    }

    [BenchmarkCategory("add")]
    [Benchmark(Baseline = true, OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal64 AddDecimal64()
    {
        var result = Decimal64.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _middle[index] + _middle[BenchmarkValues.Second(index)];
        }

        return result;
    }

    [BenchmarkCategory("add")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal128 AddDecimal128()
    {
        var result = Decimal128.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _wide[index] + _wide[BenchmarkValues.Second(index)];
        }

        return result;
    }

    [BenchmarkCategory("multiply")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal32 MultiplyDecimal32()
    {
        var result = Decimal32.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _narrow[index] * _narrow[BenchmarkValues.Second(index)];
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
            result = _middle[index] * _middle[BenchmarkValues.Second(index)];
        }

        return result;
    }

    [BenchmarkCategory("multiply")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal128 MultiplyDecimal128()
    {
        var result = Decimal128.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _wide[index] * _wide[BenchmarkValues.Second(index)];
        }

        return result;
    }

    [BenchmarkCategory("divide")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal32 DivideDecimal32()
    {
        var result = Decimal32.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _narrow[index] / _narrow[BenchmarkValues.Second(index)];
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
            result = _middle[index] / _middle[BenchmarkValues.Second(index)];
        }

        return result;
    }

    [BenchmarkCategory("divide")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal128 DivideDecimal128()
    {
        var result = Decimal128.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _wide[index] / _wide[BenchmarkValues.Second(index)];
        }

        return result;
    }

    [BenchmarkCategory("fma")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal32 FusedMultiplyAddDecimal32()
    {
        var result = Decimal32.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = Decimal32.FusedMultiplyAdd(_narrow[index],
                _narrow[BenchmarkValues.Second(index)], _narrow[BenchmarkValues.Third(index)]);
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
            result = Decimal64.FusedMultiplyAdd(_middle[index],
                _middle[BenchmarkValues.Second(index)], _middle[BenchmarkValues.Third(index)]);
        }

        return result;
    }

    [BenchmarkCategory("fma")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal128 FusedMultiplyAddDecimal128()
    {
        var result = Decimal128.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = Decimal128.FusedMultiplyAdd(_wide[index],
                _wide[BenchmarkValues.Second(index)], _wide[BenchmarkValues.Third(index)]);
        }

        return result;
    }

    [BenchmarkCategory("compare")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public int CompareDecimal32()
    {
        var result = 0;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _narrow[index].CompareTo(_narrow[BenchmarkValues.Second(index)]);
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
            result = _middle[index].CompareTo(_middle[BenchmarkValues.Second(index)]);
        }

        return result;
    }

    [BenchmarkCategory("compare")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public int CompareDecimal128()
    {
        var result = 0;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _wide[index].CompareTo(_wide[BenchmarkValues.Second(index)]);
        }

        return result;
    }

    [BenchmarkCategory("to string")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public string ToStringDecimal32()
    {
        var result = string.Empty;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _narrow[index].ToString();
        }

        return result;
    }

    [BenchmarkCategory("to string")]
    [Benchmark(Baseline = true, OperationsPerInvoke = BenchmarkValues.Count)]
    public string ToStringDecimal64()
    {
        var result = string.Empty;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _middle[index].ToString();
        }

        return result;
    }

    [BenchmarkCategory("to string")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public string ToStringDecimal128()
    {
        var result = string.Empty;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _wide[index].ToString();
        }

        return result;
    }

    [BenchmarkCategory("from string")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal32 ParseDecimal32()
    {
        var result = Decimal32.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = Decimal32.Parse(BenchmarkValues.WideText[index]);
        }

        return result;
    }

    [BenchmarkCategory("from string")]
    [Benchmark(Baseline = true, OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal64 ParseDecimal64()
    {
        var result = Decimal64.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = Decimal64.Parse(BenchmarkValues.WideText[index]);
        }

        return result;
    }

    [BenchmarkCategory("from string")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal128 ParseDecimal128()
    {
        var result = Decimal128.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = Decimal128.Parse(BenchmarkValues.WideText[index]);
        }

        return result;
    }
}
