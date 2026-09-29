// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using BenchmarkDotNet.Running;

namespace Decimals.Benchmarks;

/// <summary>
/// Runs the benchmark classes. Pass <c>--filter *</c> to run all of them, or a filter such
/// as <c>--filter *Decimal64Benchmarks*</c> to run one class. With no arguments, it lists
/// the classes and asks which to run.
/// </summary>
public class Program
{
    public static void Main(string[] arguments)
    {
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(arguments);
    }
}
