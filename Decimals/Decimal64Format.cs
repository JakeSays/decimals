// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// Parameters of the IEEE 754 decimal64 interchange format.
/// </summary>
internal readonly struct Decimal64Format : IDecimalFormat<ulong>
{
    public const ulong MaxCoefficient = 9999999999999999;

    public static int Precision => 16;

    public static int MaxExponent => 384;

    public static int MinExponent => -383;

    public static int Bias => 398;

    public static int BitCount => 64;

    public static int ExponentContinuationBits => 8;

    public static int DecletCount => 5;

    public static int MinQuantumExponent => -Bias;

    public static int MaxQuantumExponent => MaxExponent - Precision + 1;

    public static int MaxBiasedExponent => MaxQuantumExponent + Bias;

    public static ulong SignMask => 0x8000000000000000;

    public static ulong CombinationMask => 0x7C00000000000000;

    public static ulong InfinityBits => 0x7800000000000000;

    public static ulong InfinityMask => 0x7C00000000000000;

    public static ulong NaNBits => 0x7C00000000000000;

    public static ulong NaNMask => 0x7C00000000000000;

    public static ulong SignalingNaNBits => 0x7E00000000000000;

    public static ulong SignalingNaNMask => 0x7E00000000000000;

    public static UnpackedDecimal<UInt128> Unpack(ulong bits)
    {
        var value = DpdCodec.Decode64(bits);
        return new UnpackedDecimal<UInt128>(value.Kind, value.IsNegative, value.Exponent, value.Coefficient);
    }

    public static ulong Pack(UnpackedDecimal<UInt128> value)
    {
        return DpdCodec.Encode64(new UnpackedDecimal<ulong>(
            value.Kind, value.IsNegative, value.Exponent, (ulong)value.Coefficient));
    }

    public static ulong FromDpd(ulong bits) => bits;

    public static ulong ToDpd(ulong bits) => bits;

    public static ulong FromBid(ulong bits) => DpdCodec.Encode64(BidCodec.Decode64(bits));

    public static ulong ToBid(ulong bits) => BidCodec.Encode64(DpdCodec.Decode64(bits));
}
