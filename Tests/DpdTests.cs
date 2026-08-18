// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Tests;

/// <summary>
/// The declet tables. A round trip alone would pass for any bijection, so the absolute
/// encodings are pinned against values taken from the testcase corpus.
/// </summary>
public class DpdTests
{
    [Fact]
    public void EveryValueRoundTrips()
    {
        for (var value = 0u; value < Dpd.ValueCount; value++)
        {
            Assert.Equal(value, Dpd.ToBinary(Dpd.ToDeclet(value)));
        }
    }

    [Fact]
    public void EveryDecletDecodesInRange()
    {
        for (var declet = 0u; declet < Dpd.DecletCount; declet++)
        {
            Assert.InRange(Dpd.ToBinary(declet), 0u, 999u);
        }
    }

    [Fact]
    public void TwentyFourDecletsAreNonCanonical()
    {
        var nonCanonical = 0;
        for (var declet = 0u; declet < Dpd.DecletCount; declet++)
        {
            if (Dpd.ToDeclet(Dpd.ToBinary(declet)) != declet)
            {
                nonCanonical++;
            }
        }

        Assert.Equal(24, nonCanonical);
    }

    [Fact]
    public void NonCanonicalDecletsAgreeWithTheirCanonicalSpelling()
    {
        for (var declet = 0u; declet < Dpd.DecletCount; declet++)
        {
            var canonical = Dpd.ToDeclet(Dpd.ToBinary(declet));
            Assert.Equal(Dpd.ToBinary(canonical), Dpd.ToBinary(declet));
        }
    }

    // 750 is the low declet of the -7.50 encodings throughout the corpus; 999 repeats
    // through 9999999999999999, whose encoding ends ff3fcff3fcff.
    [Theory]
    [InlineData(0u, 0x000u)]
    [InlineData(9u, 0x009u)]
    [InlineData(99u, 0x05Fu)]
    [InlineData(750u, 0x3D0u)]
    [InlineData(999u, 0x0FFu)]
    public void KnownValuesEncodeAsExpected(uint value, uint declet)
    {
        Assert.Equal(declet, Dpd.ToDeclet(value));
        Assert.Equal(value, Dpd.ToBinary(declet));
    }

    // The four encodings of 999 differ only in the two bits the decode ignores.
    [Theory]
    [InlineData(0x0FFu)]
    [InlineData(0x1FFu)]
    [InlineData(0x2FFu)]
    [InlineData(0x3FFu)]
    public void RedundantSpellingsOfNineNineNineAllDecode(uint declet)
    {
        Assert.Equal(999u, Dpd.ToBinary(declet));
    }
}
