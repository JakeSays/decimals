// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Numerics;

namespace Decimals;

/// <summary>
/// The compile-time parameters of one decimal interchange format. Arithmetic is generic
/// over this so a single algorithm serves all three formats and the JIT specializes each
/// instantiation, the way decNumber's decFloat sources are compiled once per format.
/// </summary>
internal interface IDecimalFormat
{
    /// <summary>Coefficient digits: 7, 16, or 34.</summary>
    static abstract int Precision { get; }

    /// <summary>Largest adjusted exponent: +96, +384, or +6144.</summary>
    static abstract int MaxExponent { get; }

    /// <summary>Smallest adjusted exponent of a normal value: -95, -383, or -6143.</summary>
    static abstract int MinExponent { get; }

    /// <summary>Subtracted from an encoded exponent to get the quantum exponent.</summary>
    static abstract int Bias { get; }

    /// <summary>Width of the whole encoding: 32, 64, or 128.</summary>
    static abstract int BitCount { get; }

    /// <summary>
    /// Width of the DPD exponent continuation field: 6, 8, or 12. The BID exponent field
    /// is two bits wider than this.
    /// </summary>
    static abstract int ExponentContinuationBits { get; }

    /// <summary>Ten-bit groups in the DPD coefficient continuation: 2, 5, or 11.</summary>
    static abstract int DecletCount { get; }

    /// <summary>
    /// Smallest quantum exponent, which is the negated bias. Also the exponent of the
    /// smallest positive subnormal.
    /// </summary>
    static abstract int MinQuantumExponent { get; }

    /// <summary>Largest quantum exponent, <c>MaxExponent - Precision + 1</c>.</summary>
    static abstract int MaxQuantumExponent { get; }

    /// <summary>Largest encoded exponent, <c>MaxQuantumExponent + Bias</c>.</summary>
    static abstract int MaxBiasedExponent { get; }
}

/// <summary>
/// A format together with the integer that holds it, which is what lets one body of code
/// serve all three widths. Everything here is width-specific by nature -- a mask is a
/// different constant in 32, 64, and 128 bits -- and everything that is not lives in
/// <see cref="DecimalCore{TFormat, TBits}"/>.
/// </summary>
/// <typeparam name="TBits">
/// <see cref="uint"/>, <see cref="ulong"/>, or <see cref="UInt128"/>.
/// </typeparam>
internal interface IDecimalFormat<TBits> : IDecimalFormat
    where TBits : IBinaryInteger<TBits>, IUnsignedNumber<TBits>
{
    /// <summary>The sign bit alone.</summary>
    static abstract TBits SignMask { get; }

    /// <summary>The five bits below the sign, which say whether a value is special.</summary>
    static abstract TBits CombinationMask { get; }

    /// <summary>The combination bits of an infinity, and the mask that isolates them.</summary>
    static abstract TBits InfinityBits { get; }

    static abstract TBits InfinityMask { get; }

    /// <summary>The combination bits shared by both NaNs, and the mask that isolates them.</summary>
    static abstract TBits NaNBits { get; }

    static abstract TBits NaNMask { get; }

    /// <summary>The bits set by a signaling NaN, and the mask that isolates them.</summary>
    static abstract TBits SignalingNaNBits { get; }

    static abstract TBits SignalingNaNMask { get; }

    static abstract UnpackedDecimal<UInt128> Unpack(TBits bits);

    static abstract TBits Pack(UnpackedDecimal<UInt128> value);

    /// <summary>
    /// Reads the densely-packed-decimal interchange form into the in-memory one, which is
    /// the same encoding, so this carries the bits through unchanged.
    /// </summary>
    static abstract TBits FromDpd(TBits bits);

    /// <summary>Writes the in-memory form out as the interchange form.</summary>
    static abstract TBits ToDpd(TBits bits);

    /// <summary>Reads the binary-integer-decimal form into the in-memory one.</summary>
    static abstract TBits FromBid(TBits bits);

    /// <summary>Writes the in-memory form out as binary integer decimal.</summary>
    static abstract TBits ToBid(TBits bits);
}
