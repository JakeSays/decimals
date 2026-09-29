// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace Decimals.Internal;

/// <summary>
/// The bit layout of a <see cref="Decimal64"/>, which is IEEE 754 decimal64 in the BID
/// encoding, and the constants of the format.
/// </summary>
/// <remarks>
/// <para>
/// BID stores the coefficient as a binary integer in the trailing field. That is why it is
/// the in-memory encoding: every operation extracts the coefficient with a mask and a
/// shift, and scaling by a power of ten is a multiply. <see cref="Decimal64Dpd"/> converts
/// to and from DPD.
/// </para>
/// <para>
/// There are two layouts, defined in IEEE 754-2019 section 3.5.2. If the two bits below
/// the sign are not both set, the exponent is in the next 10 bits and the coefficient in
/// the low 53 bits. If both are set, and the value is not infinite or NaN, the exponent is
/// 2 bits lower and the coefficient has an implicit leading binary 100. A coefficient
/// above 10^16 - 1 is non-canonical and reads as zero. So does a NaN payload above
/// 10^15 - 1.
/// </para>
/// </remarks>
internal static class Decimal64Encoding
{
    /// <summary>The number of digits in the coefficient.</summary>
    public const int Precision = 16;

    /// <summary>The largest adjusted exponent, decNumber's Emax.</summary>
    public const int MaxExponent = 384;

    /// <summary>The smallest adjusted exponent of a normal value, decNumber's Emin.</summary>
    public const int MinExponent = -383;

    /// <summary>The value added to the quantum exponent to store it in the exponent field.</summary>
    public const int Bias = 398;

    /// <summary>The smallest quantum exponent, which is also the exponent of the smallest subnormal.</summary>
    public const int MinQuantumExponent = -398;

    /// <summary>The largest quantum exponent, <c>MaxExponent - Precision + 1</c>.</summary>
    public const int MaxQuantumExponent = 369;

    /// <summary>The largest coefficient: 10^16 - 1.</summary>
    public const ulong MaxCoefficient = 9999999999999999;

    /// <summary>10 to the power of the precision. A coefficient reaches it only by rounding up.</summary>
    public const ulong CoefficientLimit = 10000000000000000;

    /// <summary>The largest NaN payload: 10^15 - 1.</summary>
    public const ulong MaxPayload = 999999999999999;

    /// <summary>The sign bit.</summary>
    public const ulong SignMask = 0x8000000000000000;

    /// <summary>The bits set in both infinity and NaN.</summary>
    public const ulong SpecialMask = 0x7800000000000000;

    /// <summary>The encoding of positive infinity.</summary>
    public const ulong InfinityBits = 0x7800000000000000;

    /// <summary>The encoding of a positive quiet NaN with no payload.</summary>
    public const ulong NaNBits = 0x7C00000000000000;

    /// <summary>The encoding of a positive signaling NaN with no payload.</summary>
    public const ulong SignalingNaNBits = 0x7E00000000000000;

    private const ulong KindMask = 0x7C00000000000000;

    private const ulong SignalingMask = 0x7E00000000000000;

    private const ulong LongFormMask = 0x6000000000000000;

    private const ulong ExponentFieldMask = 0x3FF;

    private const int ShortExponentShift = 53;

    private const int LongExponentShift = 51;

    private const ulong ShortCoefficientMask = (1UL << 53) - 1;

    private const ulong LongCoefficientMask = (1UL << 51) - 1;

    private const ulong LongCoefficientHigh = 1UL << 53;

    private const ulong PayloadMask = (1UL << 50) - 1;

    /// <summary>Whether the value is an infinity or a NaN.</summary>
    /// <param name="bits">The encoded value.</param>
    /// <returns>True if the value is not finite.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsSpecial(ulong bits)
    {
        return (bits & SpecialMask) == SpecialMask;
    }

    /// <summary>Whether the value is a quiet or signaling NaN.</summary>
    /// <param name="bits">The encoded value.</param>
    /// <returns>True if the value is a NaN.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNaN(ulong bits)
    {
        return (bits & KindMask) == NaNBits;
    }

    /// <summary>Whether the value is a signaling NaN.</summary>
    /// <param name="bits">The encoded value.</param>
    /// <returns>True if the value is a signaling NaN.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsSignalingNaN(ulong bits)
    {
        return (bits & SignalingMask) == SignalingNaNBits;
    }

    /// <summary>Whether the value is an infinity.</summary>
    /// <param name="bits">The encoded value.</param>
    /// <returns>True if the value is positive or negative infinity.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsInfinity(ulong bits)
    {
        return (bits & KindMask) == InfinityBits;
    }

    /// <summary>Whether the sign bit is set.</summary>
    /// <param name="bits">The encoded value.</param>
    /// <returns>True if the sign bit is set.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNegative(ulong bits)
    {
        return (long)bits < 0;
    }

    /// <summary>True if the value is a finite zero, including non-canonical coefficients.</summary>
    /// <param name="bits">The encoded value.</param>
    /// <returns>True if the value is finite and its coefficient reads as zero.</returns>
    public static bool IsZero(ulong bits)
    {
        return !IsSpecial(bits) && Unpack(bits, out _) == 0;
    }

    /// <summary>
    /// The coefficient and quantum exponent of a finite value. The caller must already have
    /// checked that the value is not special.
    /// </summary>
    /// <param name="bits">The encoded finite value.</param>
    /// <param name="exponent">Receives the quantum exponent.</param>
    /// <returns>The coefficient, or zero if it is non-canonical.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Unpack(ulong bits, out int exponent)
    {
        if ((bits & LongFormMask) == LongFormMask)
        {
            exponent = (int)((bits >> LongExponentShift) & ExponentFieldMask) - Bias;
            var coefficient = (bits & LongCoefficientMask) | LongCoefficientHigh;
            return coefficient > MaxCoefficient ? 0 : coefficient;
        }

        exponent = (int)((bits >> ShortExponentShift) & ExponentFieldMask) - Bias;
        return bits & ShortCoefficientMask;
    }

    /// <summary>
    /// Packs a finite value into bits. The coefficient and exponent must already be in
    /// range. This method does not round or check; the finalizer does that.
    /// </summary>
    /// <param name="negative">Whether the sign is negative.</param>
    /// <param name="exponent">The quantum exponent, from <see cref="MinQuantumExponent"/> to <see cref="MaxQuantumExponent"/>.</param>
    /// <param name="coefficient">The coefficient, at most <see cref="MaxCoefficient"/>.</param>
    /// <returns>The encoded value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Pack(bool negative, int exponent, ulong coefficient)
    {
        var sign = negative ? SignMask : 0UL;
        var biased = (ulong)(uint)(exponent + Bias);

        if (coefficient < LongCoefficientHigh)
        {
            return sign | (biased << ShortExponentShift) | coefficient;
        }

        return sign | LongFormMask | (biased << LongExponentShift) | (coefficient & LongCoefficientMask);
    }

    /// <summary>Encodes a zero.</summary>
    /// <param name="negative">Whether the sign is negative.</param>
    /// <param name="exponent">The quantum exponent, from <see cref="MinQuantumExponent"/> to <see cref="MaxQuantumExponent"/>.</param>
    /// <returns>The encoded zero.</returns>
    public static ulong Zero(bool negative, int exponent)
    {
        return Pack(negative, exponent, 0);
    }

    /// <summary>Encodes an infinity.</summary>
    /// <param name="negative">Whether the sign is negative.</param>
    /// <returns>The encoded infinity.</returns>
    public static ulong Infinity(bool negative)
    {
        return negative ? SignMask | InfinityBits : InfinityBits;
    }

    /// <summary>A quiet NaN with no payload. Operations return this when they create a NaN.</summary>
    /// <returns>The encoded positive quiet NaN.</returns>
    public static ulong QuietNaN()
    {
        return NaNBits;
    }

    /// <summary>A NaN's payload, or zero if the payload is larger than the format allows.</summary>
    /// <param name="bits">The encoded NaN.</param>
    /// <returns>The payload.</returns>
    public static ulong Payload(ulong bits)
    {
        var payload = bits & PayloadMask;
        return payload > MaxPayload ? 0 : payload;
    }

    /// <summary>Encodes a NaN.</summary>
    /// <param name="negative">Whether the sign is negative.</param>
    /// <param name="signaling">True for a signaling NaN, false for a quiet NaN.</param>
    /// <param name="payload">The payload. A payload above <see cref="MaxPayload"/> is replaced with zero.</param>
    /// <returns>The encoded NaN.</returns>
    public static ulong NaN(bool negative, bool signaling, ulong payload)
    {
        var sign = negative ? SignMask : 0UL;
        var kind = signaling ? SignalingNaNBits : NaNBits;
        return sign | kind | (payload > MaxPayload ? 0 : payload);
    }

    /// <summary>
    /// The NaN made quiet, with its sign and payload kept. This is how a signaling NaN's
    /// payload survives the operation that reported it.
    /// </summary>
    /// <param name="bits">The encoded NaN.</param>
    /// <returns>The encoded quiet NaN.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Quiet(ulong bits)
    {
        return (bits & SignMask) | NaNBits | Payload(bits);
    }

    /// <summary>
    /// The same value in its canonical encoding. An oversized coefficient or payload becomes
    /// zero, and the unused bits of a special value are cleared.
    /// </summary>
    /// <param name="bits">The encoded value.</param>
    /// <returns>The canonical encoding of the same value.</returns>
    public static ulong Canonical(ulong bits)
    {
        if (IsSpecial(bits))
        {
            if (IsInfinity(bits))
            {
                return Infinity(IsNegative(bits));
            }

            return NaN(IsNegative(bits), IsSignalingNaN(bits), Payload(bits));
        }

        var coefficient = Unpack(bits, out var exponent);
        return Pack(IsNegative(bits), exponent, coefficient);
    }
}
