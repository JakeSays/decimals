// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using Decimals.Conformance;

namespace Decimals.DecTest;

/// <summary>
/// Runs the testcase corpus against the managed decimal types and reports the same counts
/// the C++ harness does.
/// </summary>
public class Program
{
    public static int Main(string[] args)
    {
        if (!Options.TryParse(args, out var options, out var error))
        {
            Console.WriteLine($"error: {error}");
            Console.WriteLine();
            Options.PrintUsage();
            return 2;
        }

        var report = options.Quiet ? null : new DecTestReport(Console.WriteLine);
        var totals = new DecTestTotals();

        foreach (var input in options.Inputs)
        {
            foreach (var target in options.Targets)
            {
                if (!options.Quiet)
                {
                    Console.WriteLine($"=== {target} over {input}");
                }

                totals.Add(RunTarget(target, input, report));
            }
        }

        var described = 0;
        foreach (var failure in totals.Failures)
        {
            if (described >= options.MaxReportedFailures)
            {
                Console.WriteLine("(further failures counted but not described)");
                break;
            }

            Console.WriteLine(failure);
            described++;
        }

        if (options.ShowSkips)
        {
            Console.WriteLine();
            Console.WriteLine("skipped:");
            foreach (var (reason, count, examples) in totals.SkipReasons())
            {
                Console.WriteLine($"  {count,7}  {reason} (for example {string.Join(", ", examples)})");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"total: {totals.Passed} pass, {totals.Failed} fail, "
            + $"{totals.Skipped} skip, {totals.Errors} error");

        return totals.Failed > 0 || totals.Errors > 0 ? 1 : 0;
    }

    private static DecTestTotals RunTarget(string target, string input, DecTestReport? report)
    {
        return target switch
        {
            "Decimal32" => DecTestRunner.Run<Decimal32Target, Decimal32>(input, report),
            "Decimal64" => DecTestRunner.Run<Decimal64Target, Decimal64>(input, report),
            _ => DecTestRunner.Run<Decimal128Target, Decimal128>(input, report)
        };
    }
}
