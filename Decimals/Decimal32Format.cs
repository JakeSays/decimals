// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// Parameters of the IEEE 754 decimal32 interchange format.
/// </summary>
internal readonly struct Decimal32Format : IDecimalFormat<uint>
{
    public const uint MaxCoefficient = 9999999;

    public static int Precision => 7;

    public static int MaxExponent => 96;

    public static int MinExponent => -95;

    public static int Bias => 101;

    public static int BitCount => 32;

    public static int ExponentContinuationBits => 6;

    public static int DecletCount => 2;

    public static int MinQuantumExponent => -Bias;

    public static int MaxQuantumExponent => MaxExponent - Precision + 1;

    public static int MaxBiasedExponent => MaxQuantumExponent + Bias;

    public static uint SignMask => 0x80000000;

    public static uint CombinationMask => 0x7C000000;

    public static uint InfinityBits => 0x78000000;

    public static uint InfinityMask => 0x7C000000;

    public static uint NaNBits => 0x7C000000;

    public static uint NaNMask => 0x7C000000;

    public static uint SignalingNaNBits => 0x7E000000;

    public static uint SignalingNaNMask => 0x7E000000;

    public static UnpackedDecimal<UInt128> Unpack(uint bits)
    {
        var value = DpdCodec.Decode32(bits);
        return new UnpackedDecimal<UInt128>(value.Kind, value.IsNegative, value.Exponent, value.Coefficient);
    }

    public static uint Pack(UnpackedDecimal<UInt128> value)
    {
        return DpdCodec.Encode32(new UnpackedDecimal<uint>(
            value.Kind, value.IsNegative, value.Exponent, (uint)value.Coefficient));
    }

    public static uint FromDpd(uint bits) => bits;

    public static uint ToDpd(uint bits) => bits;

    public static uint FromBid(uint bits) => DpdCodec.Encode32(BidCodec.Decode32(bits));

    public static uint ToBid(uint bits) => BidCodec.Encode32(DpdCodec.Decode32(bits));
}
