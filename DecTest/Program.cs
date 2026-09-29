// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Diagnostics;

using Decimals.Conformance;

namespace Decimals.DecTest;

/// <summary>
/// Runs the .decTest corpus or the test vectors against the decimal types, or fetches and
/// samples the vector set. The counts match the format of the C++ harness.
/// </summary>
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (!Options.TryParse(args, out var options, out var error))
        {
            Console.WriteLine($"error: {error}");
            Console.WriteLine();
            Options.PrintUsage();
            return 2;
        }

        switch (options.Command)
        {
            case DecTestCommand.FetchHfahmy:
                return await HfahmyFetcher.Fetch(options.FullDirectory);
            case DecTestCommand.SampleHfahmy:
                return HfahmySampler.Write(options.FullDirectory, options.SampleDirectory);
        }

        var report = options.RunQuietly
            ? null
            : new DecTestReport(Console.WriteLine);
        var totals = options.RunVectorTests
            ? RunVectors(options, report)
            : RunCorpus(options, report);

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

        if (options.ShowSkippedTests)
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

        return totals.Failed > 0 || totals.Errors > 0
            ? 1
            : 0;
    }

    /// <summary>
    /// Reads each input once per target. The directives in a .decTest file set the format
    /// for the cases that follow, so each target reads the whole file and skips cases for
    /// other formats.
    /// </summary>
    private static DecTestTotals RunCorpus(Options options, DecTestReport? report)
    {
        var totals = new DecTestTotals();
        foreach (var input in options.Inputs)
        {
            foreach (var target in options.Targets)
            {
                if (!options.RunQuietly)
                {
                    Console.WriteLine($"=== {target} over {input}");
                }

                totals.Add(RunCorpusTarget(target, input, report));
            }
        }

        return totals;
    }

    private static DecTestTotals RunCorpusTarget(Target target, string input, DecTestReport? report)
    {
        return target switch
        {
            Target.Decimal32 => DecTestRunner.Run<Decimal32Target, Decimal32>(input, report),
            Target.Decimal64 => DecTestRunner.Run<Decimal64Target, Decimal64>(input, report),
            Target.Decimal128 => DecTestRunner.Run<Decimal128Target, Decimal128>(input, report),
            _ => throw new UnreachableException($"no runner for {target}")
        };
    }

    /// <summary>
    /// Reads each input once for all targets. Each vector line names its format, so the
    /// runner sends each line to the matching target.
    /// </summary>
    private static DecTestTotals RunVectors(Options options, DecTestReport? report)
    {
        var targets = options.Targets.Select(VectorTarget).ToArray();

        var totals = new DecTestTotals();
        foreach (var input in options.Inputs)
        {
            if (!options.RunQuietly)
            {
                Console.WriteLine($"=== {string.Join(", ", options.Targets)} over {input}");
            }

            totals.Add(HfahmyVectorRunner.Run(input, targets, report));
        }

        return totals;
    }

    private static IHfahmyVectorTarget VectorTarget(Target target)
    {
        return target switch
        {
            Target.Decimal32 => new HfahmyVectorTarget<Decimal32Target, Decimal32>(),
            Target.Decimal64 => new HfahmyVectorTarget<Decimal64Target, Decimal64>(),
            Target.Decimal128 => new HfahmyVectorTarget<Decimal128Target, Decimal128>(),
            _ => throw new UnreachableException($"no vector target for {target}")
        };
    }
}
