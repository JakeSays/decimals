// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

namespace Decimals.DecTest;

/// <summary>
/// The command line: which files to run, against which formats, and how much to print.
/// </summary>
public sealed class Options
{
    private static readonly string[] AllTargets =
    [
        "Decimal32", "Decimal64", "Decimal128"
    ];

    public List<string> Inputs { get; } = [];

    public List<string> Targets { get; } = [];

    public int MaxReportedFailures { get; private set; } = 25;

    public bool Quiet { get; private set; }

    public bool ShowSkips { get; private set; }

    public static bool TryParse(string[] args, out Options options, out string error)
    {
        options = new Options();
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];

            switch (argument)
            {
                case "--quiet":
                    options.Quiet = true;
                    continue;
                case "--show-skips":
                    options.ShowSkips = true;
                    continue;
                case "--max-failures":
                    if (index + 1 >= args.Length || !int.TryParse(args[index + 1], out var maximum))
                    {
                        error = "--max-failures needs a count";
                        return false;
                    }

                    options.MaxReportedFailures = maximum;
                    index++;
                    continue;
                case "--target":
                    if (index + 1 >= args.Length || Array.IndexOf(AllTargets, args[index + 1]) < 0)
                    {
                        error = "--target needs one of " + string.Join(", ", AllTargets);
                        return false;
                    }

                    options.Targets.Add(args[index + 1]);
                    index++;
                    continue;
            }

            if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                error = $"unknown option '{argument}'";
                return false;
            }

            options.Inputs.Add(argument);
        }

        if (options.Inputs.Count == 0)
        {
            error = "no testcase file or directory named";
            return false;
        }

        if (options.Targets.Count == 0)
        {
            options.Targets.AddRange(AllTargets);
        }

        return true;
    }

    public static void PrintUsage()
    {
        Console.WriteLine("usage: dectest-cs [options] <file-or-directory>...");
        Console.WriteLine();
        Console.WriteLine("  Runs the .decTest corpus against the managed decimal types. A test whose");
        Console.WriteLine("  directives do not describe the target format is skipped, so every group can");
        Console.WriteLine("  be pointed at every target.");
        Console.WriteLine();
        Console.WriteLine("options:");
        Console.WriteLine("  --target <name>       Decimal32, Decimal64, or Decimal128 (default: all three)");
        Console.WriteLine("  --max-failures <n>    describe at most <n> failures (default 25)");
        Console.WriteLine("  --quiet               omit the per-file result lines");
        Console.WriteLine("  --show-skips          tally the reasons tests were skipped");
    }
}
