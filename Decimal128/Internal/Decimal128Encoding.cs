// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace Decimals.Internal;

/// <summary>
/// The bit layout of a <see cref="Decimal128"/>, which is IEEE 754 decimal128 in the BID
/// encoding, and the constants of the format.
/// </summary>
/// <remarks>
/// <para>
/// BID stores the coefficient as a binary integer in the trailing field. That is why it is
/// the in-memory encoding: every operation extracts the coefficient with a mask, and
/// scaling by a power of ten is a multiply. <see cref="Decimal128Dpd"/> converts to and
/// from DPD.
/// </para>
/// <para>
/// There are two layouts, defined in IEEE 754-2019 section 3.5.2. If the two bits below
/// the sign are not both set, the exponent is in the next 14 bits and the coefficient in
/// the low 113 bits. If both are set, and the value is not infinite or NaN, the exponent is
/// 2 bits lower and the coefficient has an implicit leading binary 100. In this format that
/// always makes the coefficient larger than 10^34 - 1. In either layout, a coefficient
/// above 10^34 - 1 is non-canonical and reads as zero. So does a NaN payload above
/// 10^33 - 1.
/// </para>
/// <para>
/// The bits are passed around as a <see cref="Decimal128Integer"/>. Its high word holds the
/// sign, the combination field, the exponent, and the top of the coefficient. Its low word
/// is the low word of the coefficient.
/// </para>
/// </remarks>
internal static class Decimal128Encoding
{
    /// <summary>The number of digits in the coefficient.</summary>
    public const int Precision = 34;

    /// <summary>The largest adjusted exponent, decNumber's Emax.</summary>
    public const int MaxExponent = 6144;

    /// <summary>The smallest adjusted exponent of a normal value, decNumber's Emin.</summary>
    public const int MinExponent = -6143;

    /// <summary>The value added to the quantum exponent to store it in the exponent field.</summary>
    public const int Bias = 6176;

    /// <summary>The smallest quantum exponent, which is also the exponent of the smallest subnormal.</summary>
    public const int MinQuantumExponent = -6176;

    /// <summary>The largest quantum exponent, <c>MaxExponent - Precision + 1</c>.</summary>
    public const int MaxQuantumExponent = 6111;

    private const ulong MaxCoefficientHigh = 0x0001ED09BEAD87C0;

    private const ulong MaxCoefficientLow = 0x378D8E63FFFFFFFF;

    /// <summary>10^34 - 1, the largest coefficient.</summary>
    public static Decimal128Integer MaxCoefficient
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(MaxCoefficientHigh, MaxCoefficientLow);
    }

    /// <summary>10 to the power of the precision. A coefficient reaches it only by rounding up.</summary>
    public static Decimal128Integer CoefficientLimit
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(0x0001ED09BEAD87C0, 0x378D8E6400000000);
    }

    /// <summary>10^33 - 1, the largest NaN payload.</summary>
    public static Decimal128Integer MaxPayload
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(0x0000314DC6448D93, 0x38C15B09FFFFFFFF);
    }

    /// <summary>The sign bit, in the high word.</summary>
    public const ulong SignMask = 0x8000000000000000;

    /// <summary>The bits set in the high word of both infinity and NaN.</summary>
    public const ulong SpecialMask = 0x7800000000000000;

    /// <summary>The high word of positive infinity. Its low word is zero.</summary>
    public const ulong InfinityBits = 0x7800000000000000;

    /// <summary>The high word of a positive quiet NaN with no payload. Its low word is zero.</summary>
    public const ulong NaNBits = 0x7C00000000000000;

    /// <summary>The high word of a positive signaling NaN with no payload. Its low word is zero.</summary>
    public const ulong SignalingNaNBits = 0x7E00000000000000;

    private const ulong KindMask = 0x7C00000000000000;

    private const ulong SignalingMask = 0x7E00000000000000;

    private const ulong LongFormMask = 0x6000000000000000;

    private const ulong ExponentFieldMask = 0x3FFF;

    private const int ShortExponentShift = 49;

    private const int LongExponentShift = 47;

    /// <summary>The part of the coefficient in the high word: bits 64 to 112.</summary>
    private const ulong CoefficientHighMask = (1UL << 49) - 1;

    /// <summary>The part of a NaN payload in the high word: bits 64 to 109.</summary>
    private const ulong PayloadHighMask = (1UL << 46) - 1;

    /// <summary>Whether the value is an infinity or a NaN.</summary>
    /// <param name="bits">The encoded value.</param>
    /// <returns>True if the value is not finite.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsSpecial(Decimal128Integer bits)
    {
        return (bits.High & SpecialMask) == SpecialMask;
    }

    /// <summary>Whether the value is a quiet or signaling NaN.</summary>
    /// <param name="bits">The encoded value.</param>
    /// <returns>True if the value is a NaN.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNaN(Decimal128Integer bits)
    {
        return (bits.High & KindMask) == NaNBits;
    }

    /// <summary>Whether the value is a signaling NaN.</summary>
    /// <param name="bits">The encoded value.</param>
    /// <returns>True if the value is a signaling NaN.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsSignalingNaN(Decimal128Integer bits)
    {
        return (bits.High & SignalingMask) == SignalingNaNBits;
    }

    /// <summary>Whether the value is an infinity.</summary>
    /// <param name="bits">The encoded value.</param>
    /// <returns>True if the value is positive or negative infinity.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsInfinity(Decimal128Integer bits)
    {
        return (bits.High & KindMask) == InfinityBits;
    }

    /// <summary>Whether the sign bit is set.</summary>
    /// <param name="bits">The encoded value.</param>
    /// <returns>True if the sign bit is set.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNegative(Decimal128Integer bits)
    {
        return (long)bits.High < 0;
    }

    /// <summary>True if the value is a finite zero, including non-canonical coefficients.</summary>
    /// <param name="bits">The encoded value.</param>
    /// <returns>True if the value is finite and its coefficient reads as zero.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsZero(Decimal128Integer bits)
    {
        return !IsSpecial(bits) && Unpack(bits, out _).IsZero;
    }

    /// <summary>
    /// The coefficient and quantum exponent of a finite value. The caller must already have
    /// checked that the value is not special.
    /// </summary>
    /// <param name="bits">The encoded finite value.</param>
    /// <param name="exponent">Receives the quantum exponent.</param>
    /// <returns>The coefficient, or zero if it is non-canonical.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Integer Unpack(Decimal128Integer bits, out int exponent)
    {
        if ((bits.High & LongFormMask) == LongFormMask)
        {
            // The implicit leading bits make every long-form coefficient larger than the
            // largest valid one, so the value is zero whatever the trailing bits are.
            exponent = (int)((bits.High >> LongExponentShift) & ExponentFieldMask) - Bias;
            return Decimal128Integer.Zero;
        }

        exponent = (int)((bits.High >> ShortExponentShift) & ExponentFieldMask) - Bias;
        var coefficient = new Decimal128Integer(bits.High & CoefficientHighMask, bits.Low);

        // Written with short-circuit operators so it compiles to branches. An oversized
        // coefficient is rare, so the branches predict well. The two-word comparison
        // operator would spend its flag-setting instructions every time.
        if (coefficient.High > MaxCoefficientHigh
            || (coefficient.High == MaxCoefficientHigh && coefficient.Low > MaxCoefficientLow))
        {
            return Decimal128Integer.Zero;
        }

        return coefficient;
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
    public static Decimal128Integer Pack(bool negative, int exponent, Decimal128Integer coefficient)
    {
        var sign = negative ? SignMask : 0UL;
        var biased = (ulong)(uint)(exponent + Bias);
        return new Decimal128Integer(sign | (biased << ShortExponentShift) | coefficient.High, coefficient.Low);
    }

    /// <summary>Encodes a zero.</summary>
    /// <param name="negative">Whether the sign is negative.</param>
    /// <param name="exponent">The quantum exponent, from <see cref="MinQuantumExponent"/> to <see cref="MaxQuantumExponent"/>.</param>
    /// <returns>The encoded zero.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Integer Zero(bool negative, int exponent)
    {
        return Pack(negative, exponent, Decimal128Integer.Zero);
    }

    /// <summary>Encodes an infinity.</summary>
    /// <param name="negative">Whether the sign is negative.</param>
    /// <returns>The encoded infinity.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Integer Infinity(bool negative)
    {
        return new Decimal128Integer(negative ? SignMask | InfinityBits : InfinityBits, 0);
    }

    /// <summary>A quiet NaN with no payload. Operations return this when they create a NaN.</summary>
    /// <returns>The encoded positive quiet NaN.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Integer QuietNaN()
    {
        return new Decimal128Integer(NaNBits, 0);
    }

    /// <summary>A NaN's payload, or zero if the payload is larger than the format allows.</summary>
    /// <param name="bits">The encoded NaN.</param>
    /// <returns>The payload.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Integer Payload(Decimal128Integer bits)
    {
        var payload = new Decimal128Integer(bits.High & PayloadHighMask, bits.Low);
        return payload > MaxPayload ? Decimal128Integer.Zero : payload;
    }

    /// <summary>Encodes a NaN.</summary>
    /// <param name="negative">Whether the sign is negative.</param>
    /// <param name="signaling">True for a signaling NaN, false for a quiet NaN.</param>
    /// <param name="payload">The payload. A payload above <see cref="MaxPayload"/> is replaced with zero.</param>
    /// <returns>The encoded NaN.</returns>
    public static Decimal128Integer NaN(bool negative, bool signaling, Decimal128Integer payload)
    {
        var sign = negative ? SignMask : 0UL;
        var kind = signaling ? SignalingNaNBits : NaNBits;
        if (payload > MaxPayload)
        {
            payload = Decimal128Integer.Zero;
        }

        return new Decimal128Integer(sign | kind | payload.High, payload.Low);
    }

    /// <summary>
    /// The NaN made quiet, with its sign and payload kept. This is how a signaling NaN's
    /// payload survives the operation that reported it.
    /// </summary>
    /// <param name="bits">The encoded NaN.</param>
    /// <returns>The encoded quiet NaN.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Integer Quiet(Decimal128Integer bits)
    {
        return NaN(IsNegative(bits), false, Payload(bits));
    }

    /// <summary>
    /// The same value in its canonical encoding. An oversized coefficient or payload becomes
    /// zero, and the unused bits of a special value are cleared.
    /// </summary>
    /// <param name="bits">The encoded value.</param>
    /// <returns>The canonical encoding of the same value.</returns>
    public static Decimal128Integer Canonical(Decimal128Integer bits)
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
