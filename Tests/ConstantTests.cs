// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Tests;

/// <summary>
/// The named constants are written as their encodings rather than parsed at startup, so
/// each one needs checking against the value it claims to be -- a wrong hex digit would
/// otherwise be invisible.
/// </summary>
public class ConstantTests
{
    [Fact]
    public void Decimal32Constants()
    {
        Assert.Equal("2.718282", Decimal32.E.ToString());
        Assert.Equal("3.141593", Decimal32.Pi.ToString());
        Assert.Equal("6.283185", Decimal32.Tau.ToString());
    }

    [Fact]
    public void Decimal64Constants()
    {
        Assert.Equal("2.718281828459045", Decimal64.E.ToString());
        Assert.Equal("3.141592653589793", Decimal64.Pi.ToString());
        Assert.Equal("6.283185307179586", Decimal64.Tau.ToString());
    }

    [Fact]
    public void Decimal128Constants()
    {
        Assert.Equal("2.718281828459045235360287471352662", Decimal128.E.ToString());
        Assert.Equal("3.141592653589793238462643383279503", Decimal128.Pi.ToString());
        Assert.Equal("6.283185307179586476925286766559006", Decimal128.Tau.ToString());
    }

    /// <summary>
    /// Each is the correctly rounded value at that width, which is checked by rounding the
    /// wider format's constant down to the narrower one.
    /// </summary>
    [Fact]
    public void EachWidthCarriesTheCorrectlyRoundedValue()
    {
        Assert.Equal(Decimal64.E.ToString(), ((Decimal64)Decimal128.E).ToString());
        Assert.Equal(Decimal64.Pi.ToString(), ((Decimal64)Decimal128.Pi).ToString());
        Assert.Equal(Decimal64.Tau.ToString(), ((Decimal64)Decimal128.Tau).ToString());

        Assert.Equal(Decimal32.E.ToString(), ((Decimal32)Decimal128.E).ToString());
        Assert.Equal(Decimal32.Pi.ToString(), ((Decimal32)Decimal128.Pi).ToString());
        Assert.Equal(Decimal32.Tau.ToString(), ((Decimal32)Decimal128.Tau).ToString());
    }

    /// <summary>
    /// Doubling Pi gives Tau at the two wider formats. It does not at Decimal32, and that
    /// is correct rather than a bad constant: Pi rounds up at seven digits, to 3.141593,
    /// and doubling that rounded value gives 6.283186, one unit past the 6.283185 that
    /// rounding Tau itself gives. Double rounding, the same way <c>2 * Math.PI</c> is not
    /// <c>Math.Tau</c> in binary.
    /// </summary>
    [Fact]
    public void TauIsTwicePiWhereRoundingAllowsIt()
    {
        Assert.Equal(Decimal64.Tau.ToString(), (Decimal64.Pi + Decimal64.Pi).ToString());
        Assert.Equal(Decimal128.Tau.ToString(), (Decimal128.Pi + Decimal128.Pi).ToString());

        Assert.Equal("6.283186", (Decimal32.Pi + Decimal32.Pi).ToString());
        Assert.Equal("6.283185", Decimal32.Tau.ToString());
    }

    /// <summary>
    /// And E agrees with the exponential function, which arrives at it by an entirely
    /// different route.
    /// </summary>
    [Fact]
    public void EAgreesWithExp()
    {
        Assert.Equal(Decimal32.E.ToString(), Decimal32.Exp(Decimal32.One).ToString());
        Assert.Equal(Decimal64.E.ToString(), Decimal64.Exp(Decimal64.One).ToString());
        Assert.Equal(Decimal128.E.ToString(), Decimal128.Exp(Decimal128.One).ToString());
    }
}
