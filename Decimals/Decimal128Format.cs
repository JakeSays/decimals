// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// Parameters of the IEEE 754 decimal128 interchange format.
/// </summary>
internal readonly struct Decimal128Format : IDecimalFormat<UInt128>
{
    /// <summary>10^34 - 1, wider than any <see cref="ulong"/> reaches.</summary>
    public static UInt128 MaxCoefficient { get; } = PowersOfTen.UInt128(Precision) - UInt128.One;

    public static int Precision => 34;

    public static int MaxExponent => 6144;

    public static int MinExponent => -6143;

    public static int Bias => 6176;

    public static int BitCount => 128;

    public static int ExponentContinuationBits => 12;

    public static int DecletCount => 11;

    public static int MinQuantumExponent => -Bias;

    public static int MaxQuantumExponent => MaxExponent - Precision + 1;

    public static int MaxBiasedExponent => MaxQuantumExponent + Bias;

    public static UInt128 SignMask { get; } = new(0x8000000000000000UL, 0);

    public static UInt128 CombinationMask { get; } = new(0x7C00000000000000UL, 0);

    public static UInt128 InfinityBits { get; } = new(0x7800000000000000UL, 0);

    public static UInt128 InfinityMask { get; } = new(0x7C00000000000000UL, 0);

    public static UInt128 NaNBits { get; } = new(0x7C00000000000000UL, 0);

    public static UInt128 NaNMask { get; } = new(0x7C00000000000000UL, 0);

    public static UInt128 SignalingNaNBits { get; } = new(0x7E00000000000000UL, 0);

    public static UInt128 SignalingNaNMask { get; } = new(0x7E00000000000000UL, 0);

    public static UnpackedDecimal<UInt128> Unpack(UInt128 bits) => BidCodec.Decode128(bits);

    public static UInt128 Pack(UnpackedDecimal<UInt128> value) => BidCodec.Encode128(value);

    public static UInt128 FromDpd(UInt128 bits) => BidCodec.Encode128(DpdCodec.Decode128(bits));

    public static UInt128 ToDpd(UInt128 bits) => DpdCodec.Encode128(BidCodec.Decode128(bits));
}
