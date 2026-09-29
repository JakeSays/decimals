// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace Decimals.Internal;

/// <summary>
/// The bit layout of a <see cref="Decimal32"/>, which is IEEE 754 decimal32 in the BID
/// encoding, and the constants of the format.
/// </summary>
/// <remarks>
/// <para>
/// BID stores the coefficient as a binary integer in the trailing field. That is why it is
/// the in-memory encoding: every operation extracts the coefficient with a mask and a
/// shift, and scaling by a power of ten is a multiply. <see cref="Decimal32Dpd"/> converts
/// to and from DPD.
/// </para>
/// <para>
/// There are two layouts, defined in IEEE 754-2019 section 3.5.2. If the two bits below
/// the sign are not both set, the exponent is in the next 8 bits and the coefficient in the
/// low 23 bits. If both are set, and the value is not infinite or NaN, the exponent is 2
/// bits lower and the coefficient has an implicit leading binary 100. This long form holds
/// the coefficients from 2^23 to 10^7 - 1. A coefficient above 10^7 - 1 is non-canonical
/// and reads as zero. So does a NaN payload above 10^6 - 1.
/// </para>
/// </remarks>
internal static class Decimal32Encoding
{
    /// <summary>The number of digits in the coefficient.</summary>
    public const int Precision = 7;

    /// <summary>The largest adjusted exponent, decNumber's Emax.</summary>
    public const int MaxExponent = 96;

    /// <summary>The smallest adjusted exponent of a normal value, decNumber's Emin.</summary>
    public const int MinExponent = -95;

    /// <summary>The value added to the quantum exponent to store it in the exponent field.</summary>
    public const int Bias = 101;

    /// <summary>The smallest quantum exponent, which is also the exponent of the smallest subnormal.</summary>
    public const int MinQuantumExponent = -101;

    /// <summary>The largest quantum exponent, <c>MaxExponent - Precision + 1</c>.</summary>
    public const int MaxQuantumExponent = 90;

    /// <summary>The largest coefficient: 10^7 - 1.</summary>
    public const uint MaxCoefficient = 9999999;

    /// <summary>10 to the power of the precision. A coefficient reaches it only by rounding up.</summary>
    public const uint CoefficientLimit = 10000000;

    /// <summary>The largest NaN payload: 10^6 - 1.</summary>
    public const uint MaxPayload = 999999;

    /// <summary>The sign bit.</summary>
    public const uint SignMask = 0x80000000;

    /// <summary>The bits set in both infinity and NaN.</summary>
    public const uint SpecialMask = 0x78000000;

    /// <summary>The encoding of positive infinity.</summary>
    public const uint InfinityBits = 0x78000000;

    /// <summary>The encoding of a positive quiet NaN with no payload.</summary>
    public const uint NaNBits = 0x7C000000;

    /// <summary>The encoding of a positive signaling NaN with no payload.</summary>
    public const uint SignalingNaNBits = 0x7E000000;

    private const uint KindMask = 0x7C000000;

    private const uint SignalingMask = 0x7E000000;

    private const uint LongFormMask = 0x60000000;

    private const uint ExponentFieldMask = 0xFF;

    private const int ShortExponentShift = 23;

    private const int LongExponentShift = 21;

    private const uint ShortCoefficientMask = (1U << 23) - 1;

    private const uint LongCoefficientMask = (1U << 21) - 1;

    private const uint LongCoefficientHigh = 1U << 23;

    private const uint PayloadMask = (1U << 20) - 1;

    /// <summary>Whether the value is an infinity or a NaN.</summary>
    /// <param name="bits">The encoded value.</param>
    /// <returns>True if the value is not finite.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsSpecial(uint bits)
    {
        return (bits & SpecialMask) == SpecialMask;
    }

    /// <summary>Whether the value is a quiet or signaling NaN.</summary>
    /// <param name="bits">The encoded value.</param>
    /// <returns>True if the value is a NaN.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNaN(uint bits)
    {
        return (bits & KindMask) == NaNBits;
    }

    /// <summary>Whether the value is a signaling NaN.</summary>
    /// <param name="bits">The encoded value.</param>
    /// <returns>True if the value is a signaling NaN.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsSignalingNaN(uint bits)
    {
        return (bits & SignalingMask) == SignalingNaNBits;
    }

    /// <summary>Whether the value is an infinity.</summary>
    /// <param name="bits">The encoded value.</param>
    /// <returns>True if the value is positive or negative infinity.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsInfinity(uint bits)
    {
        return (bits & KindMask) == InfinityBits;
    }

    /// <summary>Whether the sign bit is set.</summary>
    /// <param name="bits">The encoded value.</param>
    /// <returns>True if the sign bit is set.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNegative(uint bits)
    {
        return (int)bits < 0;
    }

    /// <summary>True if the value is a finite zero, including non-canonical coefficients.</summary>
    /// <param name="bits">The encoded value.</param>
    /// <returns>True if the value is finite and its coefficient reads as zero.</returns>
    public static bool IsZero(uint bits)
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
    public static uint Unpack(uint bits, out int exponent)
    {
        // This branches on the encoding form on purpose. Only coefficients of 8,388,608 and
        // above use the long form, which is a few percent of typical values, so the branch
        // predicts well. A branch-free version using masks was 10 to 20 percent slower
        // across the arithmetic, because it adds a longer chain of dependent operations to
        // every value.
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
    public static uint Pack(bool negative, int exponent, uint coefficient)
    {
        var sign = negative ? SignMask : 0U;
        var biased = (uint)(exponent + Bias);

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
    public static uint Zero(bool negative, int exponent)
    {
        return Pack(negative, exponent, 0);
    }

    /// <summary>Encodes an infinity.</summary>
    /// <param name="negative">Whether the sign is negative.</param>
    /// <returns>The encoded infinity.</returns>
    public static uint Infinity(bool negative)
    {
        return negative ? SignMask | InfinityBits : InfinityBits;
    }

    /// <summary>A quiet NaN with no payload. Operations return this when they create a NaN.</summary>
    /// <returns>The encoded positive quiet NaN.</returns>
    public static uint QuietNaN()
    {
        return NaNBits;
    }

    /// <summary>A NaN's payload, or zero if the payload is larger than the format allows.</summary>
    /// <param name="bits">The encoded NaN.</param>
    /// <returns>The payload.</returns>
    public static uint Payload(uint bits)
    {
        var payload = bits & PayloadMask;
        return payload > MaxPayload ? 0 : payload;
    }

    /// <summary>Encodes a NaN.</summary>
    /// <param name="negative">Whether the sign is negative.</param>
    /// <param name="signaling">True for a signaling NaN, false for a quiet NaN.</param>
    /// <param name="payload">The payload. A payload above <see cref="MaxPayload"/> is replaced with zero.</param>
    /// <returns>The encoded NaN.</returns>
    public static uint NaN(bool negative, bool signaling, uint payload)
    {
        var sign = negative ? SignMask : 0U;
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
    public static uint Quiet(uint bits)
    {
        return (bits & SignMask) | NaNBits | Payload(bits);
    }

    /// <summary>
    /// The same value in its canonical encoding. An oversized coefficient or payload becomes
    /// zero, and the unused bits of a special value are cleared.
    /// </summary>
    /// <param name="bits">The encoded value.</param>
    /// <returns>The canonical encoding of the same value.</returns>
    public static uint Canonical(uint bits)
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
