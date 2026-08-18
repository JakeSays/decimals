// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Globalization;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace Decimals.Benchmarks;

/// <summary>
/// Conversion to and from text, which decbench measures too and which tends to dominate any
/// workload that reads or writes numbers rather than only computing with them.
/// </summary>
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class TextBenchmarks
{
    private Decimal64[] _decimals = [];
    private decimal[] _systemDecimals = [];
    private double[] _doubles = [];
    private string[] _text = [];

    [GlobalSetup]
    public void Setup()
    {
        _decimals = BenchmarkValues.Narrow64;
        _systemDecimals = BenchmarkValues.NarrowDecimal;
        _doubles = BenchmarkValues.NarrowDouble;
        _text = BenchmarkValues.NarrowText;
    }

    [BenchmarkCategory("to string")]
    [Benchmark(Baseline = true, OperationsPerInvoke = BenchmarkValues.Count)]
    public string ToStringDecimal64()
    {
        var result = string.Empty;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _decimals[index].ToString();
        }

        return result;
    }

    [BenchmarkCategory("to string")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public string ToStringSystemDecimal()
    {
        var result = string.Empty;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _systemDecimals[index].ToString(CultureInfo.InvariantCulture);
        }

        return result;
    }

    [BenchmarkCategory("to string")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public string ToStringDouble()
    {
        var result = string.Empty;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _doubles[index].ToString(CultureInfo.InvariantCulture);
        }

        return result;
    }

    /// <summary>
    /// The allocation-free path, which is the one worth having: the same conversion into a
    /// caller's buffer.
    /// </summary>
    [BenchmarkCategory("format")]
    [Benchmark(Baseline = true, OperationsPerInvoke = BenchmarkValues.Count)]
    public int TryFormatDecimal64()
    {
        Span<char> buffer = stackalloc char[64];
        var written = 0;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            _decimals[index].TryFormat(buffer, out written, default, CultureInfo.InvariantCulture);
        }

        return written;
    }

    [BenchmarkCategory("format")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public int TryFormatSystemDecimal()
    {
        Span<char> buffer = stackalloc char[64];
        var written = 0;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            _systemDecimals[index].TryFormat(buffer, out written, default,
                CultureInfo.InvariantCulture);
        }

        return written;
    }

    [BenchmarkCategory("from string")]
    [Benchmark(Baseline = true, OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal64 ParseDecimal64()
    {
        var result = Decimal64.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = Decimal64.Parse(_text[index]);
        }

        return result;
    }

    [BenchmarkCategory("from string")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public decimal ParseSystemDecimal()
    {
        var result = decimal.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = decimal.Parse(_text[index], NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        return result;
    }

    [BenchmarkCategory("from string")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public double ParseDouble()
    {
        var result = 0.0;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = double.Parse(_text[index], NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        return result;
    }
}
