// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using Decimals;

namespace Tests;

/// <summary>
/// The digit-form codec against the one already proven by the corpus. Both read the same
/// interchange encoding, so wherever they disagree the new one is wrong.
/// </summary>
public unsafe class BcdCodecTests
{
    [Fact]
    public void EveryDecletUnpacksToItsDigits()
    {
        var digits = stackalloc byte[Dpd.DigitsPerDeclet];
        for (var declet = 0u; declet < Dpd.DecletCount; declet++)
        {
            Dpd.WriteDigits(declet, digits);
            var value = Dpd.ToBinary(declet);

            Assert.Equal(value / 100, (uint)digits[0]);
            Assert.Equal((value / 10) % 10, (uint)digits[1]);
            Assert.Equal(value % 10, (uint)digits[2]);
        }
    }

    [Fact]
    public void EveryDigitTriplePacksToItsDeclet()
    {
        for (var value = 0u; value < Dpd.ValueCount; value++)
        {
            var nibbles = ((value / 100) << 8) | (((value / 10) % 10) << 4) | (value % 10);
            Assert.Equal(Dpd.ToDeclet(value), Dpd.FromNibbles(nibbles));
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0")]
    [InlineData("1")]
    [InlineData("-1")]
    [InlineData("9999999999999999")]
    [InlineData("1234567890123456")]
    [InlineData("1E-383")]
    [InlineData("9.999999999999999E+384")]
    [InlineData("1E+369")]
    [InlineData("0.000001")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("NaN")]
    [InlineData("-sNaN")]
    [InlineData("NaN123")]
    public void Decimal64RoundTripsThroughDigits(string text)
    {
        var value = Decimal64.Parse(text);
        var bits = value.ToDpdBits();

        var digits = stackalloc byte[Decimal64Format.Precision];
        var number = BcdCodec.Decode<Decimal64Format>(bits, digits);

        Assert.Equal(bits, (ulong)BcdCodec.Encode<Decimal64Format>(number));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-7")]
    [InlineData("9999999")]
    [InlineData("1E-95")]
    [InlineData("9.999999E+96")]
    [InlineData("Infinity")]
    [InlineData("NaN")]
    public void Decimal32RoundTripsThroughDigits(string text)
    {
        var value = Decimal32.Parse(text);
        var bits = value.ToDpdBits();

        var digits = stackalloc byte[Decimal32Format.Precision];
        var number = BcdCodec.Decode<Decimal32Format>(bits, digits);

        Assert.Equal(bits, (uint)BcdCodec.Encode<Decimal32Format>(number));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("9999999999999999999999999999999999")]
    [InlineData("1234567890123456789012345678901234")]
    [InlineData("1E-6143")]
    [InlineData("9.999999999999999999999999999999999E+6144")]
    [InlineData("Infinity")]
    [InlineData("-NaN")]
    public void Decimal128RoundTripsThroughDigits(string text)
    {
        var value = Decimal128.Parse(text);
        var bits = value.ToDpdBits();

        var digits = stackalloc byte[Decimal128Format.Precision];
        var number = BcdCodec.Decode<Decimal128Format>(bits, digits);

        Assert.Equal(bits, BcdCodec.Encode<Decimal128Format>(number));
    }

    /// <summary>
    /// The two codecs read the same bits, so the digits one produces have to spell the
    /// coefficient the integer one produces, and the exponents and signs have to match.
    /// </summary>
    [Fact]
    public void DigitsAgreeWithTheIntegerCodecAcrossTheRange()
    {
        var random = new Random(20260822);
        var digits = stackalloc byte[Decimal64Format.Precision];

        for (var iteration = 0; iteration < 20000; iteration++)
        {
            var bits = ((ulong)(uint)random.Next() << 32) | (uint)random.Next();

            var integer = DpdCodec.Decode64(bits);
            var number = BcdCodec.Decode<Decimal64Format>(bits, digits);

            Assert.Equal(integer.Kind, number.Kind);
            Assert.Equal(integer.IsNegative, number.IsNegative);

            if (integer.Kind == DecimalKind.Infinity)
            {
                continue;
            }

            if (integer.Kind == DecimalKind.Finite)
            {
                Assert.Equal(integer.Exponent, number.Exponent);
            }

            var spelled = UInt128.Zero;
            for (var position = 0; position < number.DigitCount; position++)
            {
                spelled = (spelled * 10) + number.Msd[position];
            }

            Assert.Equal(integer.Coefficient, spelled);
        }
    }
}
