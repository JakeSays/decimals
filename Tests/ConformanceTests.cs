// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.IO;
using System.Linq;

using Decimals.Conformance;

namespace Decimals.Tests;

/// <summary>
/// Runs the .decTest corpus, with one xunit case per file. The dectest console runner does
/// the same work and prints a report. This class lets <c>dotnet test</c> cover the corpus
/// too.
/// </summary>
/// <remarks>
/// There are two sets of files. <c>TestData/DecTest</c> holds Cowlishaw's files as
/// distributed. <c>TestData/Generated</c> holds files written by <c>decgen</c> from the
/// general files. The general files are written for an arbitrary-precision context, so
/// they cannot run against a fixed format directly; <c>decgen</c> recomputes their results
/// for a fixed format. The generated files provide the square-root tests and the Decimal32
/// arithmetic tests, which the distributed files lack.
/// </remarks>
public class ConformanceTests
{
    private static readonly string CorpusRoot = Path.Combine(AppContext.BaseDirectory, "TestData");

    // These three files contain only "dectest" directives that name the other files.
    // Running them would repeat the other files.
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
