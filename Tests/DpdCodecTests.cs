// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Tests;

/// <summary>
/// The DPD interchange codec, checked against encodings lifted from the dsEncode, ddEncode,
/// and dqEncode testcase groups.
/// </summary>
public class DpdCodecTests
{
    [Theory]
    [InlineData(0xA23003D0u, true, 750u, -2)]
    [InlineData(0x2654D2E7u, false, 1234567u, 0)]
    [InlineData(0x00000000u, false, 0u, -101)]
    public void Decimal32DecodesKnownEncodings(uint bits, bool isNegative, uint coefficient, int exponent)
    {
        var value = DpdCodec.Decode32(bits);

        Assert.Equal(DecimalKind.Finite, value.Kind);
        Assert.Equal(isNegative, value.IsNegative);
        Assert.Equal(coefficient, value.Coefficient);
        Assert.Equal(exponent, value.Exponent);
        Assert.Equal(bits, DpdCodec.Encode32(value));
    }

    [Theory]
    [InlineData(0xA2300000000003D0UL, true, 750UL, -2)]
    [InlineData(0xA23C0000000003D0UL, true, 750UL, 1)]
    [InlineData(0xA2380000000003D0UL, true, 750UL, 0)]
    [InlineData(0xA2340000000003D0UL, true, 750UL, -1)]
    [InlineData(0xA22C0000000003D0UL, true, 750UL, -3)]
    [InlineData(0x6E38FF3FCFF3FCFFUL, false, 9999999999999999UL, 0)]
    public void Decimal64DecodesKnownEncodings(ulong bits, bool isNegative, ulong coefficient, int exponent)
    {
        var value = DpdCodec.Decode64(bits);

        Assert.Equal(DecimalKind.Finite, value.Kind);
        Assert.Equal(isNegative, value.IsNegative);
        Assert.Equal(coefficient, value.Coefficient);
        Assert.Equal(exponent, value.Exponent);
        Assert.Equal(bits, DpdCodec.Encode64(value));
    }

    [Theory]
    [InlineData(0xA2078000_00000000UL, 0x00000000_000003D0UL, true, 750UL, -2)]
    [InlineData(0x22080000_00000000UL, 0x00000000_00000001UL, false, 1UL, 0)]
    [InlineData(0x22080000_00000000UL, 0x00000000_000049C5UL, false, 12345UL, 0)]
    public void Decimal128DecodesKnownEncodings(ulong upper, ulong lower, bool isNegative,
        ulong coefficient, int exponent)
    {
        var bits = new UInt128(upper, lower);
        var value = DpdCodec.Decode128(bits);

        Assert.Equal(DecimalKind.Finite, value.Kind);
        Assert.Equal(isNegative, value.IsNegative);
        Assert.Equal((UInt128)coefficient, value.Coefficient);
        Assert.Equal(exponent, value.Exponent);
        Assert.Equal(bits, DpdCodec.Encode128(value));
    }

    [Fact]
    public void Decimal64DecodesSpecials()
    {
        (ulong Bits, DecimalKind Kind, bool IsNegative, ulong Payload)[] cases =
        [
            (0x7800000000000000UL, DecimalKind.Infinity, false, 0UL),
            (0xF800000000000000UL, DecimalKind.Infinity, true, 0UL),
            (0x7C00000000000000UL, DecimalKind.QuietNaN, false, 0UL),
            (0xFC00000000000000UL, DecimalKind.QuietNaN, true, 0UL),
            (0x7E00000000000000UL, DecimalKind.SignalingNaN, false, 0UL),
            (0x7C00FF3FCFF3FCFFUL, DecimalKind.QuietNaN, false, 999999999999999UL),
            (0x7E00FF3FCFF3FCFFUL, DecimalKind.SignalingNaN, false, 999999999999999UL)
        ];

        foreach (var (bits, kind, isNegative, payload) in cases)
        {
            var value = DpdCodec.Decode64(bits);

            Assert.Equal(kind, value.Kind);
            Assert.Equal(isNegative, value.IsNegative);
            Assert.Equal(payload, value.Coefficient);
            Assert.Equal(bits, DpdCodec.Encode64(value));
        }
    }

    [Fact]
    public void Decimal64RoundTripsEveryCoefficientLength()
    {
        var coefficient = 0UL;
        for (var digits = 0; digits <= 16; digits++)
        {
            coefficient = digits == 0 ? 0 : (coefficient * 10) + 9;
            foreach (var exponent in new[] { -398, -100, -1, 0, 1, 100, 369 })
            {
                var value = new UnpackedDecimal<ulong>(DecimalKind.Finite, digits % 2 == 0, exponent, coefficient);
                var decoded = DpdCodec.Decode64(DpdCodec.Encode64(value));

                Assert.Equal(value.Coefficient, decoded.Coefficient);
                Assert.Equal(value.Exponent, decoded.Exponent);
                Assert.Equal(value.IsNegative, decoded.IsNegative);
            }
        }
    }

    [Fact]
    public void Decimal128RoundTripsAcrossTheExponentRange()
    {
        var coefficient = Decimal128Format.MaxCoefficient;
        foreach (var exponent in new[] { -6176, -1000, -1, 0, 1, 1000, 6111 })
        {
            var value = new UnpackedDecimal<UInt128>(DecimalKind.Finite, true, exponent, coefficient);
            var decoded = DpdCodec.Decode128(DpdCodec.Encode128(value));

            Assert.Equal(value.Coefficient, decoded.Coefficient);
            Assert.Equal(value.Exponent, decoded.Exponent);
            Assert.True(decoded.IsNegative);
        }
    }

    [Fact]
    public void NonCanonicalDecletsDecodeToTheSameValue()
    {
        // 0x3FF is the redundant encoding of the declet 0x0FF, which carries 999.
        var canonical = DpdCodec.Decode64(0x6E38FF3FCFF3FCFFUL);
        var redundant = DpdCodec.Decode64(0x6E38FF3FCFF3FCFFUL | 0x300UL);

        Assert.Equal(canonical.Coefficient, redundant.Coefficient);
        Assert.Equal(canonical.Exponent, redundant.Exponent);
    }
}
