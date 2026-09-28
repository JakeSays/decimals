// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Runtime.InteropServices;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Decimals.Internal;


namespace Decimals.Benchmarks;

/// <summary>
/// The pieces underneath formatting a <see cref="Decimal64"/>, timed one at a time: planning
/// the layout, writing the digits, looking up the culture, and the whole operation under
/// each provider. This is what says where a nanosecond of formatting goes.
/// </summary>
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class Decimal64TextBenchmarks
{
    private ulong[] _bits = [];
    private Decimal64[] _values = [];

    [GlobalSetup]
    public void Setup()
    {
        _values = new Decimal64[BenchmarkValues.Count];
        _bits = new ulong[BenchmarkValues.Count];
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            _values[index] = Decimal64.Parse(BenchmarkValues.WideText[index]);
            _bits[index] = _values[index].ToBits();
        }
    }

    [BenchmarkCategory("pieces")]
    [Benchmark(Baseline = true, OperationsPerInvoke = BenchmarkValues.Count)]
    public int PlanOnly()
    {
        var total = 0;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            var coefficient = Decimal64Encoding.Unpack(_bits[index], out var exponent);
            var layout = Decimal64TextLayout.Plan(coefficient, exponent, false);
            total += layout.Length(false, 1, 1, 1);
        }

        return total;
    }

    [BenchmarkCategory("pieces")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public int PlanAndWrite()
    {
        Span<char> buffer = stackalloc char[64];
        var total = 0;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            var coefficient = Decimal64Encoding.Unpack(_bits[index], out var exponent);
            var layout = Decimal64TextLayout.Plan(coefficient, exponent, false);
            total += layout.Write(buffer, Decimal64Encoding.IsNegative(_bits[index]));
        }

        return total;
    }

    [BenchmarkCategory("pieces")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public int PlanAndWriteWide()
    {
        Span<char> buffer = stackalloc char[64];
        var total = 0;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            var coefficient = Decimal64Encoding.Unpack(_bits[index], out var exponent);
            var layout = Decimal64TextLayout.Plan(coefficient, exponent, false);
            total += layout.WriteWide(ref MemoryMarshal.GetReference(buffer), Decimal64Encoding.IsNegative(_bits[index]));
        }

        return total;
    }

    [BenchmarkCategory("pieces")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public int CurrentCultureLookup()
    {
        var total = 0;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            total += NumberFormatInfo.GetInstance(null).NegativeSign.Length;
        }

        return total;
    }

    [BenchmarkCategory("whole")]
    [Benchmark(Baseline = true, OperationsPerInvoke = BenchmarkValues.Count)]
    public int TryFormatNullProvider()
    {
        Span<char> buffer = stackalloc char[64];
        var written = 0;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            _values[index].TryFormat(buffer, out written, default, null);
        }

        return written;
    }

    [BenchmarkCategory("whole")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public int TryFormatInvariantProvider()
    {
        Span<char> buffer = stackalloc char[64];
        var written = 0;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            _values[index].TryFormat(buffer, out written, default, CultureInfo.InvariantCulture);
        }

        return written;
    }

    [BenchmarkCategory("whole")]
    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public string ToStringAllocating()
    {
        var result = string.Empty;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _values[index].ToString();
        }

        return result;
    }
}
