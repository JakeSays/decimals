// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// The binary-integer-decimal encoding, which is how the three types hold a value in
/// memory. The coefficient sits in the trailing field as a plain binary integer, so every
/// operation reaches it with a shift and a mask instead of eleven declet lookups.
/// </summary>
/// <remarks>
/// <para>
/// Two layouts, per IEEE 754-2019 section 3.5.2. When the two bits below the sign are not
/// both set, the exponent occupies the next E bits and the coefficient the rest; when they
/// are both set (and the value is not infinite or NaN) the exponent is displaced two bits
/// lower and the coefficient carries an implicit leading one. Infinity and NaN use the same
/// bit patterns here as they do in DPD, which the standard requires.
/// </para>
/// <para>
/// A coefficient larger than the format holds is non-canonical, and the value is a zero of
/// the given sign and exponent. A NaN payload larger than the format holds reads as zero
/// the same way. The payload occupies the same number of bits as the DPD coefficient
/// continuation, which is exactly what the largest payload needs.
/// </para>
/// <para>
/// These routines are on every arithmetic path, so each format is written out rather than
/// shared: the shifts and masks are format-specific constants and want to be immediate.
/// </para>
/// </remarks>
internal static class BidCodec
{
    private const uint InfinityCombination = 0x1E;
    private const uint NaNCombination = 0x1F;

    private const uint Decimal32MaxPayload = 999999;
    private const ulong Decimal64MaxPayload = 999999999999999;

    private const int Decimal32ExponentBits = 8;
    private const int Decimal32ShortTrailingBits = 23;
    private const int Decimal32LongTrailingBits = 21;
    private const int Decimal32PayloadBits = 20;

    private const int Decimal64ExponentBits = 10;
    private const int Decimal64ShortTrailingBits = 53;
    private const int Decimal64LongTrailingBits = 51;
    private const int Decimal64PayloadBits = 50;

    private const int Decimal128ExponentBits = 14;
    private const int Decimal128ShortTrailingBits = 113;
    private const int Decimal128PayloadBits = 110;

    /// <summary>10^33 - 1, the largest decimal128 NaN payload.</summary>
    private static UInt128 Decimal128MaxPayload { get; } =
        PowersOfTen.UInt128(Decimal128Format.Precision - 1) - UInt128.One;

    public static UnpackedDecimal<uint> Decode32(uint bits)
    {
        var isNegative = (bits >> 31) != 0;
        var combination = (bits >> 26) & 0x1F;

        if (combination >= InfinityCombination)
        {
            return DecodeSpecial32(bits, isNegative, combination);
        }

        uint biasedExponent;
        uint coefficient;
        if (((bits >> 29) & 3) == 3)
        {
            biasedExponent = (bits >> Decimal32LongTrailingBits) & ((1u << Decimal32ExponentBits) - 1);
            coefficient = (1u << Decimal32ShortTrailingBits)
                | (bits & ((1u << Decimal32LongTrailingBits) - 1));
        }
        else
        {
            biasedExponent = (bits >> Decimal32ShortTrailingBits) & ((1u << Decimal32ExponentBits) - 1);
            coefficient = bits & ((1u << Decimal32ShortTrailingBits) - 1);
        }

        if (coefficient > Decimal32Format.MaxCoefficient)
        {
            coefficient = 0;
        }

        return new UnpackedDecimal<uint>(
            DecimalKind.Finite, isNegative, (int)biasedExponent - Decimal32Format.Bias, coefficient);
    }

    public static uint Encode32(UnpackedDecimal<uint> value)
    {
        var sign = value.IsNegative ? 1u << 31 : 0u;

        if (value.Kind == DecimalKind.Infinity)
        {
            return sign | (InfinityCombination << 26);
        }

        if (value.Kind != DecimalKind.Finite)
        {
            var signaling = value.Kind == DecimalKind.SignalingNaN ? 1u << 25 : 0u;
            var payload = value.Coefficient > Decimal32MaxPayload ? 0u : value.Coefficient;
            return sign | (NaNCombination << 26) | signaling | payload;
        }

        var biasedExponent = (uint)(value.Exponent + Decimal32Format.Bias);
        if (value.Coefficient < (1u << Decimal32ShortTrailingBits))
        {
            return sign | (biasedExponent << Decimal32ShortTrailingBits) | value.Coefficient;
        }

        return sign
            | (3u << 29)
            | (biasedExponent << Decimal32LongTrailingBits)
            | (value.Coefficient - (1u << Decimal32ShortTrailingBits));
    }

    public static UnpackedDecimal<ulong> Decode64(ulong bits)
    {
        var isNegative = (bits >> 63) != 0;
        var combination = (uint)(bits >> 58) & 0x1F;

        if (combination >= InfinityCombination)
        {
            return DecodeSpecial64(bits, isNegative, combination);
        }

        ulong biasedExponent;
        ulong coefficient;
        if (((bits >> 61) & 3) == 3)
        {
            biasedExponent = (bits >> Decimal64LongTrailingBits) & ((1UL << Decimal64ExponentBits) - 1);
            coefficient = (1UL << Decimal64ShortTrailingBits)
                | (bits & ((1UL << Decimal64LongTrailingBits) - 1));
        }
        else
        {
            biasedExponent = (bits >> Decimal64ShortTrailingBits) & ((1UL << Decimal64ExponentBits) - 1);
            coefficient = bits & ((1UL << Decimal64ShortTrailingBits) - 1);
        }

        if (coefficient > Decimal64Format.MaxCoefficient)
        {
            coefficient = 0;
        }

        return new UnpackedDecimal<ulong>(
            DecimalKind.Finite, isNegative, (int)biasedExponent - Decimal64Format.Bias, coefficient);
    }

    public static ulong Encode64(UnpackedDecimal<ulong> value)
    {
        var sign = value.IsNegative ? 1UL << 63 : 0UL;

        if (value.Kind == DecimalKind.Infinity)
        {
            return sign | ((ulong)InfinityCombination << 58);
        }

        if (value.Kind != DecimalKind.Finite)
        {
            var signaling = value.Kind == DecimalKind.SignalingNaN ? 1UL << 57 : 0UL;
            var payload = value.Coefficient > Decimal64MaxPayload ? 0UL : value.Coefficient;
            return sign | ((ulong)NaNCombination << 58) | signaling | payload;
        }

        var biasedExponent = (ulong)(value.Exponent + Decimal64Format.Bias);
        if (value.Coefficient < (1UL << Decimal64ShortTrailingBits))
        {
            return sign | (biasedExponent << Decimal64ShortTrailingBits) | value.Coefficient;
        }

        return sign
            | (3UL << 61)
            | (biasedExponent << Decimal64LongTrailingBits)
            | (value.Coefficient - (1UL << Decimal64ShortTrailingBits));
    }

    public static UnpackedDecimal<UInt128> Decode128(UInt128 bits)
    {
        var isNegative = (bits >> 127) != UInt128.Zero;
        var combination = (uint)(bits >> 122) & 0x1F;

        if (combination >= InfinityCombination)
        {
            return DecodeSpecial128(bits, isNegative, combination);
        }

        UInt128 biasedExponent;
        UInt128 coefficient;
        if (((uint)(bits >> 125) & 3) == 3)
        {
            // The long form cannot hold a canonical decimal128 coefficient, because 2^113
            // already exceeds 10^34 - 1. Decoding it is still required, and everything it
            // can produce is non-canonical.
            coefficient = Decimal128Format.MaxCoefficient + UInt128.One;
            biasedExponent = (bits >> 111) & ((UInt128.One << Decimal128ExponentBits) - UInt128.One);
        }
        else
        {
            biasedExponent = (bits >> Decimal128ShortTrailingBits)
                & ((UInt128.One << Decimal128ExponentBits) - UInt128.One);
            coefficient = bits & ((UInt128.One << Decimal128ShortTrailingBits) - UInt128.One);
        }

        if (coefficient > Decimal128Format.MaxCoefficient)
        {
            coefficient = UInt128.Zero;
        }

        return new UnpackedDecimal<UInt128>(
            DecimalKind.Finite, isNegative, (int)biasedExponent - Decimal128Format.Bias, coefficient);
    }

    public static UInt128 Encode128(UnpackedDecimal<UInt128> value)
    {
        var sign = value.IsNegative ? UInt128.One << 127 : UInt128.Zero;

        if (value.Kind == DecimalKind.Infinity)
        {
            return sign | ((UInt128)InfinityCombination << 122);
        }

        if (value.Kind != DecimalKind.Finite)
        {
            var signaling = value.Kind == DecimalKind.SignalingNaN ? UInt128.One << 121 : UInt128.Zero;
            var payload = value.Coefficient > Decimal128MaxPayload ? UInt128.Zero : value.Coefficient;
            return sign | ((UInt128)NaNCombination << 122) | signaling | payload;
        }

        var biasedExponent = (UInt128)(uint)(value.Exponent + Decimal128Format.Bias);
        return sign | (biasedExponent << Decimal128ShortTrailingBits) | value.Coefficient;
    }

    private static UnpackedDecimal<uint> DecodeSpecial32(uint bits, bool isNegative, uint combination)
    {
        if (combination == InfinityCombination)
        {
            return new UnpackedDecimal<uint>(DecimalKind.Infinity, isNegative, 0, 0);
        }

        var kind = ((bits >> 25) & 1) != 0 ? DecimalKind.SignalingNaN : DecimalKind.QuietNaN;
        var payload = bits & ((1u << Decimal32PayloadBits) - 1);
        return new UnpackedDecimal<uint>(kind, isNegative, 0, payload > Decimal32MaxPayload ? 0 : payload);
    }

    private static UnpackedDecimal<ulong> DecodeSpecial64(ulong bits, bool isNegative, uint combination)
    {
        if (combination == InfinityCombination)
        {
            return new UnpackedDecimal<ulong>(DecimalKind.Infinity, isNegative, 0, 0);
        }

        var kind = ((bits >> 57) & 1) != 0 ? DecimalKind.SignalingNaN : DecimalKind.QuietNaN;
        var payload = bits & ((1UL << Decimal64PayloadBits) - 1);
        return new UnpackedDecimal<ulong>(kind, isNegative, 0, payload > Decimal64MaxPayload ? 0 : payload);
    }

    private static UnpackedDecimal<UInt128> DecodeSpecial128(UInt128 bits, bool isNegative, uint combination)
    {
        if (combination == InfinityCombination)
        {
            return new UnpackedDecimal<UInt128>(DecimalKind.Infinity, isNegative, 0, UInt128.Zero);
        }

        var kind = ((uint)(bits >> 121) & 1) != 0 ? DecimalKind.SignalingNaN : DecimalKind.QuietNaN;
        var payload = bits & ((UInt128.One << Decimal128PayloadBits) - UInt128.One);
        return new UnpackedDecimal<UInt128>(
            kind, isNegative, 0, payload > Decimal128MaxPayload ? UInt128.Zero : payload);
    }
}
