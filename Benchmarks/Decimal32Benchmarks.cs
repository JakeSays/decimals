// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using BenchmarkDotNet.Attributes;

namespace Decimals.Benchmarks;

/// <summary>
/// <see cref="Decimal32"/> on the short operand set: decbench's shape at seven digits. The
/// two wide text rows run on decbench's own sixteen-digit strings, which round on the way
/// in, since that is the set its decSingle text figures are measured on.
/// </summary>
public class Decimal32Benchmarks
{
    private Decimal32[] _values = [];
    private string[] _text = [];
    private Decimal32[] _wideValues = [];
    private string[] _wideText = [];

    [GlobalSetup]
    public void Setup()
    {
        _text = BenchmarkValues.Short32Text;
        _values = new Decimal32[BenchmarkValues.Count];
        _wideText = BenchmarkValues.WideText;
        _wideValues = new Decimal32[BenchmarkValues.Count];
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            _values[index] = Decimal32.Parse(_text[index]);
            _wideValues[index] = Decimal32.Parse(_wideText[index]);
        }
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal32 AddDecimal32()
    {
        var result = Decimal32.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _values[index] + _values[BenchmarkValues.Second(index)];
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal32 MultiplyDecimal32()
    {
        var result = Decimal32.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _values[index] * _values[BenchmarkValues.Second(index)];
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal32 DivideDecimal32()
    {
        var result = Decimal32.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _values[index] / _values[BenchmarkValues.Second(index)];
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal32 FusedMultiplyAddDecimal32()
    {
        var result = Decimal32.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = Decimal32.FusedMultiplyAdd(_values[index],
                _values[BenchmarkValues.Second(index)], _values[BenchmarkValues.Third(index)]);
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public int CompareDecimal32()
    {
        var result = 0;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _values[index].CompareTo(_values[BenchmarkValues.Second(index)]);
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal32 SqrtDecimal32()
    {
        var result = Decimal32.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = Decimal32.Sqrt(_values[index]);
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal32 QuantizeDecimal32()
    {
        var result = Decimal32.Zero;
        var context = new Decimal32Context();
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = Decimal32.Quantize(_values[index], _values[BenchmarkValues.Second(index)], ref context);
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public string ToStringDecimal32()
    {
        var result = string.Empty;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _values[index].ToString();
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public int TryFormatDecimal32()
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
    public Decimal32 ParseDecimal32()
    {
        var result = Decimal32.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = Decimal32.Parse(_text[index]);
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public int TryFormatWideDecimal32()
    {
        Span<char> buffer = stackalloc char[64];
        var written = 0;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            _wideValues[index].TryFormat(buffer, out written, default, null);
        }

        return written;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal32 ParseWideDecimal32()
    {
        var result = Decimal32.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = Decimal32.Parse(_wideText[index]);
        }

        return result;
    }
}
