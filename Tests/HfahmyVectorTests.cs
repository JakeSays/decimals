// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.IO;
using System.Linq;

using Decimals.Conformance;

namespace Decimals.Tests;

/// <summary>
/// Runs the committed sample of the Sayed-Ahmed and Fahmy test vectors, with one xunit case
/// per vector set. Also tests the vector line parser.
/// </summary>
/// <remarks>
/// The sample is every hundredth vector of the full set. <c>dectest --fetch-hfahmy</c>
/// downloads the full set, and <c>dectest --vectors</c> runs it. The vectors cover only
/// decimal64 and decimal128.
/// </remarks>
public class HfahmyVectorTests
{
    private static readonly string SampleRoot =
        Path.Combine(AppContext.BaseDirectory, "TestData", "Hfahmy", "Sample");

    public static TheoryData<string> VectorSets()
    {
        var data = new TheoryData<string>();
        foreach (var directory in Directory.GetDirectories(SampleRoot).OrderBy(name => name, StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(directory));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(VectorSets))]
    public void VectorSetPasses(string set)
    {
        var path = Path.Combine(SampleRoot, set);
        IHfahmyVectorTarget[] targets =
        [
            new HfahmyVectorTarget<Decimal64Target, Decimal64>(),
            new HfahmyVectorTarget<Decimal128Target, Decimal128>()
        ];

        var totals = HfahmyVectorRunner.Run(path, targets);

        Assert.Equal(0, totals.Errors);
        Assert.True(totals.Failed == 0, Describe(set, totals));
        Assert.Equal(0, totals.Skipped);
        Assert.True(totals.Passed > 0);
    }

    [Fact]
    public void SampleCoversEveryFile()
    {
        Assert.Equal(13, Directory.GetDirectories(SampleRoot).Length);
        Assert.Equal(228, Directory.GetFiles(SampleRoot, "*.txt", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void FusedMultiplySubtractLineParses()
    {
        Assert.True(HfahmyVector.TryParse("d128*- h> +12E-3 -Inf S -> Q i", out var vector, out var error), error);

        Assert.Equal("d128", vector!.Format);
        Assert.Equal(HfahmyVectorOperation.FusedMultiplySubtract, vector.Operation);
        Assert.Equal(DecTestRounding.HalfUp, vector.Rounding);
        Assert.Equal(["+12E-3", "-Inf", "S"], vector.Operands);
        Assert.Equal("Q", vector.Result);
        Assert.Equal(DecTestStatus.InvalidOperation, vector.Flags);
    }

    [Fact]
    public void LineWithoutFlagsParses()
    {
        Assert.True(HfahmyVector.TryParse("d64V 0 +0E-398 -> +0E-199", out var vector, out var error), error);

        Assert.Equal(HfahmyVectorOperation.SquareRoot, vector!.Operation);
        Assert.Equal(DecTestRounding.Down, vector.Rounding);
        Assert.Equal(DecTestStatus.None, vector.Flags);
    }

    [Theory]
    [InlineData("d128V =0 -> Q i")]
    [InlineData("Q Q -> Q i")]
    [InlineData("d64+ ~ +1E0 +1E0 -> +2E0")]
    [InlineData("d64+ =0 +1E0 +1E0 -> +2E0 q")]
    public void DamagedLinesDoNotParse(string line)
    {
        Assert.False(HfahmyVector.TryParse(line, out _, out _));
    }

    [Fact]
    public void KnownDefectsMatchOnlyTheirOwnLine()
    {
        Assert.True(HfahmyKnownDefects.TryFind("2011_04_d128_sqrt_type1.txt", 726, "d128V =0 -> Q i", out _));
        Assert.False(HfahmyKnownDefects.TryFind("2011_04_d128_sqrt_type1.txt", 727, "d128V =0 -> Q i", out _));
        Assert.False(HfahmyKnownDefects.TryFind("2011_04_d128_sqrt_type1.txt", 726, "d128V =0 +0E0 -> +0E0", out _));
    }

    private static string Describe(string set, DecTestTotals totals)
    {
        var lines = new List<string> { $"{set}: {totals.Failed} of {totals.Total} failed" };
        lines.AddRange(totals.Failures.Take(10));
        return string.Join(Environment.NewLine, lines);
    }
}
