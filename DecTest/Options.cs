// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

namespace Decimals.DecTest;

/// <summary>
/// The parsed command line: the command, the input files, the targets, and the output
/// settings.
/// </summary>
public sealed class Options
{
    private static readonly Target[] AllTargets =
    [
        Target.Decimal32, Target.Decimal64, Target.Decimal128
    ];

    public DecTestCommand Command { get; private set; } = DecTestCommand.Run;

    public List<string> Inputs { get; } = [];

    public List<Target> Targets { get; } = [];

    /// <summary>True when the inputs are Sayed-Ahmed and Fahmy vector files instead of .decTest files.</summary>
    public bool RunVectorTests { get; private set; }

    /// <summary>The directory that holds the full vector set.</summary>
    public string FullDirectory { get; private set; } = string.Empty;

    /// <summary>The directory the sample is written to.</summary>
    public string SampleDirectory { get; private set; } = string.Empty;

    public int MaxReportedFailures { get; private set; } = 25;

    public bool RunQuietly { get; private set; }

    public bool ShowSkippedTests { get; private set; }

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
                    options.RunQuietly = true;
                    continue;
                case "--show-skips":
                    options.ShowSkippedTests = true;
                    continue;
                case "--vectors":
                    options.RunVectorTests = true;
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
                    if (index + 1 >= args.Length || !TryParseTarget(args[index + 1], out var target))
                    {
                        error = "--target needs one of " + string.Join(", ", AllTargets);
                        return false;
                    }

                    options.Targets.Add(target);
                    index++;
                    continue;
                case "--fetch-hfahmy":
                    if (index + 1 >= args.Length)
                    {
                        error = "--fetch-hfahmy needs a directory";
                        return false;
                    }

                    options.Command = DecTestCommand.FetchHfahmy;
                    options.FullDirectory = args[index + 1];
                    index++;
                    continue;
                case "--sample-hfahmy":
                    if (index + 2 >= args.Length)
                    {
                        error = "--sample-hfahmy needs the full-set directory and the sample directory";
                        return false;
                    }

                    options.Command = DecTestCommand.SampleHfahmy;
                    options.FullDirectory = args[index + 1];
                    options.SampleDirectory = args[index + 2];
                    index += 2;
                    continue;
            }

            if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                error = $"unknown option '{argument}'";
                return false;
            }

            options.Inputs.Add(argument);
        }

        if (options.Command == DecTestCommand.Run && options.Inputs.Count == 0)
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

    /// <summary>
    /// Parses a target name. Enum.TryParse is not used because it also accepts numbers,
    /// so "--target 7" would pass.
    /// </summary>
    private static bool TryParseTarget(string name, out Target target)
    {
        foreach (var candidate in AllTargets)
        {
            if (string.Equals(candidate.ToString(), name, StringComparison.Ordinal))
            {
                target = candidate;
                return true;
            }
        }

        target = Target.Decimal64;
        return false;
    }

    public static void PrintUsage()
    {
        Console.WriteLine("usage: dectest [options] <file-or-directory>...");
        Console.WriteLine("       dectest --fetch-hfahmy <full-directory>");
        Console.WriteLine("       dectest --sample-hfahmy <full-directory> <sample-directory>");
        Console.WriteLine();
        Console.WriteLine("  Runs the .decTest corpus, or with --vectors the Sayed-Ahmed and Fahmy test");
        Console.WriteLine("  vectors, against the managed decimal types. A test for another format is");
        Console.WriteLine("  skipped, so every file can be pointed at every target.");
        Console.WriteLine();
        Console.WriteLine("  --fetch-hfahmy downloads the full vector set, checks it against the published");
        Console.WriteLine("  md5 sums, and unpacks it. --sample-hfahmy writes every hundredth vector of the");
        Console.WriteLine("  full set to the sample directory.");
        Console.WriteLine();
        Console.WriteLine("options:");
        Console.WriteLine("  --target <name>       Decimal32, Decimal64, or Decimal128 (default: all three)");
        Console.WriteLine("  --vectors             the inputs are vector files or directories of them");
        Console.WriteLine("  --max-failures <n>    describe at most <n> failures (default 25)");
        Console.WriteLine("  --quiet               omit the per-file result lines");
        Console.WriteLine("  --show-skips          tally the reasons tests were skipped");
    }
}
