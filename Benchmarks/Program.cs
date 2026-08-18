// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using BenchmarkDotNet.Running;

namespace Decimals.Benchmarks;

/// <summary>
/// Runs the benchmark classes. Pass <c>--filter *</c> to run everything, or a filter such
/// as <c>--filter *Arithmetic*</c> to run one class; with no arguments the switcher lists
/// what is available and waits to be told.
/// </summary>
public class Program
{
    public static void Main(string[] arguments)
    {
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(arguments);
    }
}
