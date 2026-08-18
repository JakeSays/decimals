// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.IO;
using System.Linq;

using Decimals.Conformance;

namespace Decimals.Tests;

/// <summary>
/// Runs the testcase corpus, one xunit case per group file. The console runner in the
/// DecTest project does the same work with a report; this is here so a plain
/// <c>dotnet test</c> covers conformance too.
/// </summary>
/// <remarks>
/// Two sets of groups. <c>TestData/DecTest</c> holds Cowlishaw's own, as distributed.
/// <c>TestData/Generated</c> holds groups written by <c>decgen</c> from the general groups:
/// those were written for an arbitrary-precision context and cannot be run against a fixed
/// format directly, so their operands are re-evaluated under one. That is where the square
/// root and the Decimal32 arithmetic coverage comes from, neither of which the distributed
/// groups have a file for.
/// </remarks>
public class ConformanceTests
{
    private static readonly string CorpusRoot = Path.Combine(AppContext.BaseDirectory, "TestData");

    // The three group files hold nothing but "dectest" directives naming the others, so
    // running them as well would only repeat work.
    private static readonly string[] GroupFiles = ["decSingle", "decDouble", "decQuad"];

    public static TheoryData<string, string> CorpusFiles()
    {
        var data = new TheoryData<string, string>();
        foreach (var directory in new[] { "DecTest", "Generated" })
        {
            var path = Path.Combine(CorpusRoot, directory);
            foreach (var file in Directory.GetFiles(path, "*.decTest").OrderBy(name => name, StringComparer.Ordinal))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!GroupFiles.Contains(name))
                {
                    data.Add(directory, name);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void GroupPassesAgainstEveryFormat(string directory, string group)
    {
        var path = Path.Combine(CorpusRoot, directory, group + ".decTest");

        var totals = new DecTestTotals();
        totals.Add(DecTestRunner.Run<Decimal32Target, Decimal32>(path));
        totals.Add(DecTestRunner.Run<Decimal64Target, Decimal64>(path));
        totals.Add(DecTestRunner.Run<Decimal128Target, Decimal128>(path));

        Assert.Equal(0, totals.Errors);
        Assert.True(totals.Failed == 0, Describe(group, totals));
    }

    [Fact]
    public void BothCorpusSetsArePresent()
    {
        Assert.Equal(89, Directory.GetFiles(Path.Combine(CorpusRoot, "DecTest"), "*.decTest").Length);
        Assert.Equal(18, Directory.GetFiles(Path.Combine(CorpusRoot, "Generated"), "*.decTest").Length);
    }

    private static string Describe(string group, DecTestTotals totals)
    {
        var lines = new List<string> { $"{group}: {totals.Failed} of {totals.Total} failed" };
        lines.AddRange(totals.Failures.Take(10));
        return string.Join(Environment.NewLine, lines);
    }
}
