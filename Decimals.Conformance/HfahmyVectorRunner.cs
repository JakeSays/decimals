// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Decimals.Conformance;

/// <summary>
/// Runs the Sayed-Ahmed and Fahmy test vectors against a set of decimal formats.
/// </summary>
/// <remarks>
/// Each vector line names its format, so it belongs to exactly one target. The runner reads
/// each file once and sends each line to the matching target. It skips a line when no
/// given target matches. Reading the files once per target would parse the full set three
/// times and skip most of it.
/// </remarks>
public static class HfahmyVectorRunner
{
    private const string VectorFileExtension = ".txt";

    /// <summary>Runs a vector file, or every vector file in a directory and below it.</summary>
    public static DecTestTotals Run(string path, IReadOnlyList<IHfahmyVectorTarget> targets,
        DecTestReport? report = null)
    {
        var totals = new DecTestTotals();

        if (Directory.Exists(path))
        {
            var files = Directory.GetFiles(path, "*" + VectorFileExtension, SearchOption.AllDirectories)
                .OrderBy(name => name, StringComparer.Ordinal);

            foreach (var file in files)
            {
                RunFile(file, targets, totals, report);
            }

            return totals;
        }

        if (!File.Exists(path))
        {
            totals.AddError($"cannot read {path}");
            return totals;
        }

        RunFile(path, targets, totals, report);
        return totals;
    }

    private static void RunFile(string path, IReadOnlyList<IHfahmyVectorTarget> targets, DecTestTotals totals,
        DecTestReport? report)
    {
        var name = Path.GetFileName(path);
        var fileTotals = new DecTestTotals();
        var lineNumber = 0;

        foreach (var rawLine in File.ReadLines(path))
        {
            lineNumber++;

            // Some of the files end their lines with a carriage return.
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var id = $"{name}:{lineNumber}";
            if (HfahmyKnownDefects.TryFind(name, lineNumber, line, out var reason))
            {
                fileTotals.AddSkip(reason, id);
                continue;
            }

            if (!HfahmyVector.TryParse(line, out var vector, out var error))
            {
                fileTotals.AddError($"error: {id}: {error}");
                continue;
            }

            var target = FindTarget(targets, vector!.Format);
            if (target is null)
            {
                fileTotals.AddSkip($"no selected target runs {vector.Format} vectors", id);
                continue;
            }

            target.Run(vector, line, id, fileTotals);
        }

        if (fileTotals.Total > 0)
        {
            report?.File(name, fileTotals);
        }

        totals.Add(fileTotals);
    }

    /// <summary>A linear search: there are at most three targets.</summary>
    private static IHfahmyVectorTarget? FindTarget(IReadOnlyList<IHfahmyVectorTarget> targets, string format)
    {
        foreach (var target in targets)
        {
            if (string.Equals(target.Format, format, StringComparison.Ordinal))
            {
                return target;
            }
        }

        return null;
    }
}
