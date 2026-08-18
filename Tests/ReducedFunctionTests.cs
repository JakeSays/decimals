// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Tests;

/// <summary>
/// The functions that reduce to exp, ln, and the square root: the other bases, the M1 and
/// P1 variants, the roots, and Hypot. None of them appear in the testcase corpus, so these
/// check the two things a reduction can get wrong -- losing the exact cases, and losing all
/// significance where the identity cancels.
/// </summary>
public class ReducedFunctionTests
{
    [Theory]
    [InlineData("0", "1")]
    [InlineData("10", "1024")]
    [InlineData("-2", "0.25")]
    [InlineData("16", "65536")]
    public void Exp2OfAWholeNumberIsExact(string input, string expected)
    {
        var context = new DecimalContext();
        var result = Decimal64.Exp2(Decimal64.Parse(input), ref context);

        Assert.Equal(expected, result.ToString());
        Assert.False(context.HasRaised(DecimalStatus.Inexact));
    }

    [Theory]
    [InlineData("0", "1")]
    [InlineData("3", "1000")]
    [InlineData("-2", "0.01")]
    public void Exp10OfAWholeNumberIsExact(string input, string expected)
    {
        var context = new DecimalContext();
        var result = Decimal64.Exp10(Decimal64.Parse(input), ref context);

        Assert.Equal(expected, result.ToString());
        Assert.False(context.HasRaised(DecimalStatus.Inexact));
    }

    /// <summary>Two to the half is the square root of two, to the last digit.</summary>
    [Fact]
    public void Exp2AgreesWithTheSquareRoot()
    {
        Assert.Equal(Decimal64.Sqrt(Decimal64.Parse("2")).ToString(),
            Decimal64.Exp2(Decimal64.Parse("0.5")).ToString());
    }

    [Theory]
    [InlineData("1", "3", "0")]
    [InlineData("8", "2", "3")]
    [InlineData("1024", "2", "10")]
    [InlineData("0.5", "2", "-1")]
    [InlineData("0.001", "10", "-3")]
    [InlineData("1000", "10", "3")]
    public void LogarithmsOfExactPowersAreExact(string input, string logBase, string expected)
    {
        var context = new DecimalContext();
        var result = Decimal64.Log(Decimal64.Parse(input), Decimal64.Parse(logBase), ref context);

        Assert.Equal(expected, result.ToString());
        Assert.False(context.HasRaised(DecimalStatus.Inexact));
    }

    [Theory]
    [InlineData("8", "3")]
    [InlineData("1024", "10")]
    [InlineData("0.5", "-1")]
    [InlineData("0.125", "-3")]
    [InlineData("1", "0")]
    public void Log2OfAPowerOfTwoIsExact(string input, string expected)
    {
        var context = new DecimalContext();
        var result = Decimal64.Log2(Decimal64.Parse(input), ref context);

        Assert.Equal(expected, result.ToString());
        Assert.False(context.HasRaised(DecimalStatus.Inexact));
    }

    [Fact]
    public void Log2OfSomethingElseRounds()
    {
        var context = new DecimalContext();
        var result = Decimal64.Log2(Decimal64.Parse("10"), ref context);

        Assert.Equal("3.321928094887362", result.ToString());
        Assert.True(context.HasRaised(DecimalStatus.Inexact));
    }

    /// <summary>
    /// The whole point of the M1 form: for a small operand the answer is about the operand
    /// itself, which a plain <c>Exp(x) - 1</c> would have thrown away.
    /// </summary>
    [Theory]
    [InlineData("1E-20", "1E-20")]
    [InlineData("1E-300", "1E-300")]
    [InlineData("-1E-20", "-1E-20")]
    public void ExpMinusOneKeepsSmallOperands(string input, string expected)
    {
        var context = new DecimalContext();
        var result = Decimal64.ExpM1(Decimal64.Parse(input), ref context);

        Assert.Equal(expected, result.ToString());
        Assert.True(context.HasRaised(DecimalStatus.Inexact));

        // The naive form gives a flat zero, which is what makes the variant worth having.
        Assert.True(Decimal64.IsZero(Decimal64.Subtract(Decimal64.Exp(Decimal64.Parse(input)),
            Decimal64.One)));
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("-0", "-0")]
    [InlineData("-Infinity", "-1")]
    public void ExpMinusOneIsExactWhereItCanBe(string input, string expected)
    {
        var context = new DecimalContext();
        var result = Decimal64.ExpM1(Decimal64.Parse(input), ref context);

        Assert.Equal(expected, result.ToString());
        Assert.False(context.HasRaised(DecimalStatus.Inexact));
    }

    [Fact]
    public void ExpMinusOneAgreesWithExpWhereNothingCancels()
    {
        // e - 1, where the subtraction costs a single digit and both forms should agree.
        Assert.Equal("1.718281828459045", Decimal64.ExpM1(Decimal64.One).ToString());
    }

    [Theory]
    [InlineData("1E-20", "1E-20")]
    [InlineData("-1E-20", "-1E-20")]
    public void LogPlusOneKeepsSmallOperands(string input, string expected)
    {
        var context = new DecimalContext();
        var result = Decimal64.LogP1(Decimal64.Parse(input), ref context);

        Assert.Equal(expected, result.ToString());
        Assert.True(context.HasRaised(DecimalStatus.Inexact));

        // Again the naive form has nothing left to work with.
        Assert.True(Decimal64.IsZero(Decimal64.Log(Decimal64.Add(Decimal64.One,
            Decimal64.Parse(input)))));
    }

    [Fact]
    public void LogPlusOneAtModerateOperands()
    {
        // ln(1+9) is ln(10).
        Assert.Equal(Decimal64.Log(Decimal64.Parse("10")).ToString(),
            Decimal64.LogP1(Decimal64.Parse("9")).ToString());

        // And the boundaries: 1+x of zero, and of a negative one.
        Assert.Equal("-Infinity", Decimal64.LogP1(Decimal64.NegativeOne).ToString());

        var context = new DecimalContext();
        Assert.True(Decimal64.IsNaN(Decimal64.LogP1(Decimal64.Parse("-2"), ref context)));
        Assert.True(context.HasRaised(DecimalStatus.InvalidOperation));
    }

    /// <summary>
    /// The P1 forms in the other bases keep their exact cases, which they only can by going
    /// through the same routine the plain logarithm uses rather than dividing.
    /// </summary>
    [Fact]
    public void LogPlusOneInOtherBasesKeepsItsExactCases()
    {
        var tenContext = new DecimalContext();
        Assert.Equal("1", Decimal64.Log10P1(Decimal64.Parse("9"), ref tenContext).ToString());
        Assert.False(tenContext.HasRaised(DecimalStatus.Inexact));

        var twoContext = new DecimalContext();
        Assert.Equal("3", Decimal64.Log2P1(Decimal64.Parse("7"), ref twoContext).ToString());
        Assert.False(twoContext.HasRaised(DecimalStatus.Inexact));
    }

    [Theory]
    [InlineData("8", "2")]
    [InlineData("-8", "-2")]
    [InlineData("0.008", "0.2")]
    [InlineData("1000", "10")]
    [InlineData("0", "0")]
    [InlineData("-0", "-0")]
    public void CubeRootsThatComeOutExactAreExact(string input, string expected)
    {
        var context = new DecimalContext();
        var result = Decimal64.Cbrt(Decimal64.Parse(input), ref context);

        Assert.Equal(expected, result.ToString());
        Assert.False(context.HasRaised(DecimalStatus.Inexact));
    }

    [Fact]
    public void CubeRootOfTwoRounds()
    {
        var context = new DecimalContext();
        var result = Decimal64.Cbrt(Decimal64.Parse("2"), ref context);

        Assert.Equal("1.259921049894873", result.ToString());
        Assert.True(context.HasRaised(DecimalStatus.Inexact));
    }

    [Theory]
    [InlineData("16", 4, "2")]
    [InlineData("32", 5, "2")]
    [InlineData("100", 2, "10")]
    [InlineData("7", 1, "7")]
    [InlineData("8", -3, "0.5")]
    public void RootsOfTheGivenDegree(string input, int degree, string expected)
    {
        var context = new DecimalContext();
        var result = Decimal64.RootN(Decimal64.Parse(input), degree, ref context);

        Assert.Equal(expected, result.ToString());
        Assert.False(context.HasRaised(DecimalStatus.Inexact));
    }

    [Fact]
    public void RootsWithNoRealValueAreInvalid()
    {
        var degreeContext = new DecimalContext();
        Assert.True(Decimal64.IsNaN(Decimal64.RootN(Decimal64.Parse("8"), 0, ref degreeContext)));
        Assert.True(degreeContext.HasRaised(DecimalStatus.InvalidOperation));

        var evenContext = new DecimalContext();
        Assert.True(Decimal64.IsNaN(Decimal64.RootN(Decimal64.Parse("-8"), 4, ref evenContext)));
        Assert.True(evenContext.HasRaised(DecimalStatus.InvalidOperation));
    }

    [Theory]
    [InlineData("3", "4", "5")]
    [InlineData("0", "0", "0")]
    [InlineData("5", "0", "5")]
    [InlineData("-3", "-4", "5")]
    public void HypotenusesThatComeOutExactAreExact(string left, string right, string expected)
    {
        var context = new DecimalContext();
        var result = Decimal64.Hypot(Decimal64.Parse(left), Decimal64.Parse(right), ref context);

        Assert.Equal(expected, result.ToString());
        Assert.False(context.HasRaised(DecimalStatus.Inexact));
    }

    /// <summary>
    /// Squaring either operand would overflow the format long before the hypotenuse does,
    /// which is the reason the sum of squares is formed at the wider precision.
    /// </summary>
    [Fact]
    public void HypotDoesNotOverflowOnTheWay()
    {
        var context = new DecimalContext();
        var result = Decimal64.Hypot(Decimal64.Parse("1E+200"), Decimal64.Parse("1E+200"),
            ref context);

        Assert.Equal("1.414213562373095E+200", result.ToString());
        Assert.False(context.HasRaised(DecimalStatus.Overflow));

        var tinyContext = new DecimalContext();
        var tiny = Decimal64.Hypot(Decimal64.Parse("3E-200"), Decimal64.Parse("4E-200"),
            ref tinyContext);
        Assert.Equal("5E-200", tiny.ToString());
    }

    [Fact]
    public void HypotOfAnInfinityIsInfiniteEvenBesideANaN()
    {
        var context = new DecimalContext();
        var result = Decimal64.Hypot(Decimal64.PositiveInfinity, Decimal64.NaN, ref context);

        Assert.Equal("Infinity", result.ToString());
        Assert.False(context.HasRaised(DecimalStatus.InvalidOperation));
    }

    /// <summary>The reductions run at every width, not only at sixteen digits.</summary>
    [Fact]
    public void EveryFormatGetsTheReductions()
    {
        Assert.Equal("1024", Decimal32.Exp2(Decimal32.Parse("10")).ToString());
        Assert.Equal("1024", Decimal128.Exp2(Decimal128.Parse("10")).ToString());

        Assert.Equal("2", Decimal32.Cbrt(Decimal32.Parse("8")).ToString());
        Assert.Equal("2", Decimal128.Cbrt(Decimal128.Parse("8")).ToString());

        Assert.Equal("5", Decimal32.Hypot(Decimal32.Parse("3"), Decimal32.Parse("4")).ToString());
        Assert.Equal("5", Decimal128.Hypot(Decimal128.Parse("3"), Decimal128.Parse("4")).ToString());

        Assert.Equal("3", Decimal128.Log2(Decimal128.Parse("8")).ToString());
        Assert.Equal("1.259921049894873164767210607278228",
            Decimal128.Cbrt(Decimal128.Parse("2")).ToString());
    }
}
