// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Tests;

/// <summary>
/// The in-memory codec. The interesting cases are the two coefficient layouts, the values
/// that are non-canonical because their coefficient overruns the format, and the specials,
/// whose bit patterns the standard requires to match DPD.
/// </summary>
public class BidCodecTests
{
    [Fact]
    public void Decimal32RoundTripsBothCoefficientLayouts()
    {
        // 2^23 is where the short trailing field runs out and the implicit-leading-bit
        // layout takes over.
        foreach (var coefficient in new uint[] { 0, 1, 750, 1234567, (1u << 23) - 1, 1u << 23, 9999999 })
        {
            foreach (var exponent in new[] { -101, -2, 0, 5, 90 })
            {
                var value = new UnpackedDecimal<uint>(DecimalKind.Finite, coefficient % 3 == 0, exponent, coefficient);
                var decoded = BidCodec.Decode32(BidCodec.Encode32(value));

                Assert.Equal(DecimalKind.Finite, decoded.Kind);
                Assert.Equal(value.Coefficient, decoded.Coefficient);
                Assert.Equal(value.Exponent, decoded.Exponent);
                Assert.Equal(value.IsNegative, decoded.IsNegative);
            }
        }
    }

    [Fact]
    public void Decimal64RoundTripsBothCoefficientLayouts()
    {
        foreach (var coefficient in new ulong[]
        {
            0, 1, 750, 9999999999999999, (1UL << 53) - 1, 1UL << 53, 1234567890123456
        })
        {
            foreach (var exponent in new[] { -398, -16, 0, 100, 369 })
            {
                var value = new UnpackedDecimal<ulong>(DecimalKind.Finite, exponent < 0, exponent, coefficient);
                var decoded = BidCodec.Decode64(BidCodec.Encode64(value));

                Assert.Equal(DecimalKind.Finite, decoded.Kind);
                Assert.Equal(value.Coefficient, decoded.Coefficient);
                Assert.Equal(value.Exponent, decoded.Exponent);
                Assert.Equal(value.IsNegative, decoded.IsNegative);
            }
        }
    }

    [Fact]
    public void Decimal128RoundTripsAcrossTheRange()
    {
        foreach (var coefficient in new[]
        {
            UInt128.Zero, UInt128.One, (UInt128)750, (UInt128)ulong.MaxValue, Decimal128Format.MaxCoefficient
        })
        {
            foreach (var exponent in new[] { -6176, -34, 0, 1000, 6111 })
            {
                var value = new UnpackedDecimal<UInt128>(DecimalKind.Finite, true, exponent, coefficient);
                var decoded = BidCodec.Decode128(BidCodec.Encode128(value));

                Assert.Equal(DecimalKind.Finite, decoded.Kind);
                Assert.Equal(value.Coefficient, decoded.Coefficient);
                Assert.Equal(value.Exponent, decoded.Exponent);
                Assert.True(decoded.IsNegative);
            }
        }
    }

    [Fact]
    public void Decimal64CoefficientPastTheFormatReadsAsZero()
    {
        // The long layout can encode coefficients well past 10^16 - 1. Those are
        // non-canonical, and the standard says they are a zero of the given sign and
        // exponent rather than an error.
        var exponent = (ulong)(0 + Decimal64Format.Bias);
        var bits = (3UL << 61) | (exponent << 51) | ((1UL << 51) - 1);
        var decoded = BidCodec.Decode64(bits);

        Assert.Equal(DecimalKind.Finite, decoded.Kind);
        Assert.Equal(0UL, decoded.Coefficient);
        Assert.Equal(0, decoded.Exponent);
    }

    [Fact]
    public void Decimal32CoefficientPastTheFormatReadsAsZero()
    {
        var exponent = (uint)(0 + Decimal32Format.Bias);
        var bits = (3u << 29) | (exponent << 21) | ((1u << 21) - 1);
        var decoded = BidCodec.Decode32(bits);

        Assert.Equal(DecimalKind.Finite, decoded.Kind);
        Assert.Equal(0u, decoded.Coefficient);
    }

    [Fact]
    public void Decimal64SpecialsMatchTheInterchangePatterns()
    {
        // Infinity and NaN carry the same bits in both encodings, so these are the DPD
        // patterns straight out of the corpus.
        (ulong Bits, DecimalKind Kind, bool IsNegative)[] cases =
        [
            (0x7800000000000000UL, DecimalKind.Infinity, false),
            (0xF800000000000000UL, DecimalKind.Infinity, true),
            (0x7C00000000000000UL, DecimalKind.QuietNaN, false),
            (0x7E00000000000000UL, DecimalKind.SignalingNaN, false),
            (0xFE00000000000000UL, DecimalKind.SignalingNaN, true)
        ];

        foreach (var (bits, kind, isNegative) in cases)
        {
            var decoded = BidCodec.Decode64(bits);

            Assert.Equal(kind, decoded.Kind);
            Assert.Equal(isNegative, decoded.IsNegative);
            Assert.Equal(bits, BidCodec.Encode64(decoded));
        }
    }

    [Fact]
    public void NaNPayloadsRoundTrip()
    {
        foreach (var payload in new ulong[] { 0, 1, 12345, 999999999999999 })
        {
            var value = new UnpackedDecimal<ulong>(DecimalKind.QuietNaN, false, 0, payload);
            var decoded = BidCodec.Decode64(BidCodec.Encode64(value));

            Assert.Equal(DecimalKind.QuietNaN, decoded.Kind);
            Assert.Equal(payload, decoded.Coefficient);
        }
    }

    [Fact]
    public void NaNPayloadPastTheFormatReadsAsZero()
    {
        var bits = (0x1FUL << 58) | 1000000000000000UL;
        var decoded = BidCodec.Decode64(bits);

        Assert.Equal(DecimalKind.QuietNaN, decoded.Kind);
        Assert.Equal(0UL, decoded.Coefficient);
    }

    /// <summary>
    /// The two encodings have to agree about the value, which is the whole basis of the
    /// interchange conversion.
    /// </summary>
    [Fact]
    public void BidAndDpdAgreeOnEveryValueTried()
    {
        var random = new Random(20260817);
        for (var trial = 0; trial < 20000; trial++)
        {
            var coefficient = (ulong)random.NextInt64(0, (long)Decimal64Format.MaxCoefficient + 1);
            var exponent = random.Next(Decimal64Format.MinQuantumExponent, Decimal64Format.MaxQuantumExponent + 1);
            var isNegative = random.Next(2) == 0;
            var value = new UnpackedDecimal<ulong>(DecimalKind.Finite, isNegative, exponent, coefficient);

            var throughBid = BidCodec.Decode64(BidCodec.Encode64(value));
            var throughDpd = DpdCodec.Decode64(DpdCodec.Encode64(value));

            Assert.Equal(throughDpd.Coefficient, throughBid.Coefficient);
            Assert.Equal(throughDpd.Exponent, throughBid.Exponent);
            Assert.Equal(throughDpd.IsNegative, throughBid.IsNegative);
        }
    }
}
