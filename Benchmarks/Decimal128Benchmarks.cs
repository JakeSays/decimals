// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using BenchmarkDotNet.Attributes;

namespace Decimals.Benchmarks;

/// <summary>
/// Times <see cref="Decimal128"/> on two operand sets. The wide set is decbench's, so the
/// results can be compared with decQuad. The full-width set fills the coefficient, which
/// exercises the four-word code paths.
/// </summary>
public class Decimal128Benchmarks
{
    private Decimal128[] _values = [];
    private Decimal128[] _fullValues = [];
    private string[] _text = [];

    [GlobalSetup]
    public void Setup()
    {
        _text = BenchmarkValues.WideText;
        _values = new Decimal128[BenchmarkValues.Count];
        _fullValues = new Decimal128[BenchmarkValues.Count];
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            _values[index] = Decimal128.Parse(_text[index]);
            _fullValues[index] = Decimal128.Parse(BenchmarkValues.FullWidthText[index]);
        }
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal128 AddDecimal128()
    {
        var result = Decimal128.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _values[index] + _values[BenchmarkValues.Second(index)];
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal128 MultiplyDecimal128()
    {
        var result = Decimal128.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _values[index] * _values[BenchmarkValues.Second(index)];
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal128 DivideDecimal128()
    {
        var result = Decimal128.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _values[index] / _values[BenchmarkValues.Second(index)];
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal128 FusedMultiplyAddDecimal128()
    {
        var result = Decimal128.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = Decimal128.FusedMultiplyAdd(_values[index],
                _values[BenchmarkValues.Second(index)], _values[BenchmarkValues.Third(index)]);
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public int CompareDecimal128()
    {
        var result = 0;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _values[index].CompareTo(_values[BenchmarkValues.Second(index)]);
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal128 SqrtDecimal128()
    {
        var result = Decimal128.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = Decimal128.Sqrt(_values[index]);
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal128 QuantizeDecimal128()
    {
        var result = Decimal128.Zero;
        var context = new Decimal128Context();
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = Decimal128.Quantize(_values[index], _values[BenchmarkValues.Second(index)], ref context);
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public string ToStringDecimal128()
    {
        var result = string.Empty;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _values[index].ToString();
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public int TryFormatDecimal128()
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
    public Decimal128 ParseDecimal128()
    {
        var result = Decimal128.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = Decimal128.Parse(_text[index]);
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal128 AddFullDecimal128()
    {
        var result = Decimal128.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _fullValues[index] + _fullValues[BenchmarkValues.Second(index)];
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal128 MultiplyFullDecimal128()
    {
        var result = Decimal128.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _fullValues[index] * _fullValues[BenchmarkValues.Second(index)];
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal128 DivideFullDecimal128()
    {
        var result = Decimal128.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _fullValues[index] / _fullValues[BenchmarkValues.Second(index)];
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal128 FusedMultiplyAddFullDecimal128()
    {
        var result = Decimal128.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = Decimal128.FusedMultiplyAdd(_fullValues[index],
                _fullValues[BenchmarkValues.Second(index)], _fullValues[BenchmarkValues.Third(index)]);
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal128 SqrtFullDecimal128()
    {
        var result = Decimal128.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = Decimal128.Sqrt(_fullValues[index]);
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public string ToStringFullDecimal128()
    {
        var result = string.Empty;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = _fullValues[index].ToString();
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = BenchmarkValues.Count)]
    public Decimal128 ParseFullDecimal128()
    {
        var result = Decimal128.Zero;
        for (var index = 0; index < BenchmarkValues.Count; index++)
        {
            result = Decimal128.Parse(BenchmarkValues.FullWidthText[index]);
        }

        return result;
    }
}
