// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using BenchmarkDotNet.Attributes;

namespace Decimals.Benchmarks;

/// <summary>
/// Times <see cref="Decimal64"/> on decbench's wide operand set, so the results can be
/// compared with the C library.
/// </summary>
public class Decimal64Benchmarks
{
    private Decimal64[] _values = [];
    private string[] _text = [];

    [GlobalSetup]
    public void Setup()
    {
        _text = BenchmarkValues.WideText;
        _values = new Decimal64[BenchmarkValues.Count];
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            _values[index] = Decimal64.Parse(_text[index]);
        }
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal64 AddDecimal64()
    {
        var result = Decimal64.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _values[index] + _values[BenchmarkValues.Second(index)];
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal64 MultiplyDecimal64()
    {
        var result = Decimal64.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _values[index] * _values[BenchmarkValues.Second(index)];
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal64 DivideDecimal64()
    {
        var result = Decimal64.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _values[index] / _values[BenchmarkValues.Second(index)];
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal64 FusedMultiplyAddDecimal64()
    {
        var result = Decimal64.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = Decimal64.FusedMultiplyAdd(_values[index],
                _values[BenchmarkValues.Second(index)], _values[BenchmarkValues.Third(index)]);
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public int CompareDecimal64()
    {
        var result = 0;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _values[index].CompareTo(_values[BenchmarkValues.Second(index)]);
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal64 SqrtDecimal64()
    {
        var result = Decimal64.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = Decimal64.Sqrt(_values[index]);
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal64 QuantizeDecimal64()
    {
        var result = Decimal64.Zero;
        var context = new Decimal64Context();
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = Decimal64.Quantize(_values[index], _values[BenchmarkValues.Second(index)], ref context);
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public string ToStringDecimal64()
    {
        var result = string.Empty;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _values[index].ToString();
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public int TryFormatDecimal64()
    {
        Span<char> buffer = stackalloc char[64];
        var written = 0;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            _values[index].TryFormat(buffer, out written, default, null);
        }

        return written;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal64 ParseDecimal64()
    {
        var result = Decimal64.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = Decimal64.Parse(_text[index]);
        }

        return result;
    }
}
