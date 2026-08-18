// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace Decimals.Benchmarks;

/// <summary>
/// The square root and the four functions that go through the arbitrary-precision engine.
/// This is the run that says what that engine costs: the square root works on the format's
/// own coefficient, while exp, log, and pow evaluate on <c>BigDecimal</c> and allocate.
/// </summary>
/// <remarks>
/// <para>
/// The loop is short here. These are microseconds-per-operation territory rather than
/// nanoseconds, and a thousand of them per invocation would make each measurement take
/// longer than it is worth.
/// </para>
/// <para>
/// <see cref="double"/> is alongside for scale, not as a fair fight: its results carry
/// fifteen digits to a decimal64's sixteen, and they are not correctly rounded.
/// </para>
/// </remarks>
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[MemoryDiagnoser]
public class ElementaryFunctionBenchmarks
{
    private const int Operations = 64;

    private Decimal64[] _decimals = [];
    private double[] _doubles = [];
    private Decimal64 _power;
    private Decimal64 _integerPower;

    [GlobalSetup]
    public void Setup()
    {
        _decimals = BenchmarkValues.Moderate64;
        _doubles = BenchmarkValues.ModerateDouble;

        // A non-integer exponent, so pow takes the exp(ln(x)*y) route rather than the
        // repeated-squaring one.
        _power = Decimal64.Parse("1.5");
        _integerPower = Decimal64.Parse("7");
    }

    [BenchmarkCategory("sqrt")]
    [Benchmark(Baseline = true, OperationsPerInvoke = Operations)]
    public Decimal64 SqrtDecimal64()
    {
        var result = Decimal64.Zero;
        for (var index = 0; index < Operations; index++)
        {
            result = Decimal64.Sqrt(_decimals[index]);
        }

        return result;
    }

    [BenchmarkCategory("sqrt")]
    [Benchmark(OperationsPerInvoke = Operations)]
    public double SqrtDouble()
    {
        var result = 0.0;
        for (var index = 0; index < Operations; index++)
        {
            result = Math.Sqrt(_doubles[index]);
        }

        return result;
    }

    [BenchmarkCategory("exp")]
    [Benchmark(Baseline = true, OperationsPerInvoke = Operations)]
    public Decimal64 ExpDecimal64()
    {
        var result = Decimal64.Zero;
        for (var index = 0; index < Operations; index++)
        {
            result = Decimal64.Exp(_decimals[index]);
        }

        return result;
    }

    [BenchmarkCategory("exp")]
    [Benchmark(OperationsPerInvoke = Operations)]
    public double ExpDouble()
    {
        var result = 0.0;
        for (var index = 0; index < Operations; index++)
        {
            result = Math.Exp(_doubles[index]);
        }

        return result;
    }

    [BenchmarkCategory("log")]
    [Benchmark(Baseline = true, OperationsPerInvoke = Operations)]
    public Decimal64 LogDecimal64()
    {
        var result = Decimal64.Zero;
        for (var index = 0; index < Operations; index++)
        {
            result = Decimal64.Log(_decimals[index]);
        }

        return result;
    }

    [BenchmarkCategory("log")]
    [Benchmark(OperationsPerInvoke = Operations)]
    public double LogDouble()
    {
        var result = 0.0;
        for (var index = 0; index < Operations; index++)
        {
            result = Math.Log(_doubles[index]);
        }

        return result;
    }

    [BenchmarkCategory("pow")]
    [Benchmark(Baseline = true, OperationsPerInvoke = Operations)]
    public Decimal64 PowDecimal64()
    {
        var result = Decimal64.Zero;
        for (var index = 0; index < Operations; index++)
        {
            result = Decimal64.Pow(_decimals[index], _power);
        }

        return result;
    }

    [BenchmarkCategory("pow")]
    [Benchmark(OperationsPerInvoke = Operations)]
    public double PowDouble()
    {
        var result = 0.0;
        for (var index = 0; index < Operations; index++)
        {
            result = Math.Pow(_doubles[index], 1.5);
        }

        return result;
    }

    /// <summary>
    /// An integer exponent takes the repeated-squaring path instead, which is both faster
    /// and able to give an exact result; worth seeing next to the general case.
    /// </summary>
    [BenchmarkCategory("pow integer")]
    [Benchmark(Baseline = true, OperationsPerInvoke = Operations)]
    public Decimal64 PowIntegerDecimal64()
    {
        var result = Decimal64.Zero;
        for (var index = 0; index < Operations; index++)
        {
            result = Decimal64.Pow(_decimals[index], _integerPower);
        }

        return result;
    }

    [BenchmarkCategory("pow integer")]
    [Benchmark(OperationsPerInvoke = Operations)]
    public double PowIntegerDouble()
    {
        var result = 0.0;
        for (var index = 0; index < Operations; index++)
        {
            result = Math.Pow(_doubles[index], 7.0);
        }

        return result;
    }
}
