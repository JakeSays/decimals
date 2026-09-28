// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace Decimals.Internal;

/// <summary>
/// The bits of a <see cref="Decimal128"/>: IEEE 754 decimal128 in its binary-integer
/// encoding, and the constants of the format.
/// </summary>
/// <remarks>
/// <para>
/// The coefficient is a plain binary integer in the trailing field, which is why this is
/// the encoding held in memory: every operation reaches it with a mask, and scaling it by a
/// power of ten is a multiply. The densely-packed interchange form is a conversion away in
/// <see cref="Decimal128Dpd"/>.
/// </para>
/// <para>
/// Two layouts, per IEEE 754-2019 section 3.5.2. When the two bits below the sign are not
/// both set, the exponent occupies the next fourteen bits and the coefficient the low
/// hundred and thirteen; when they are both set, and the value is not infinite or a NaN,
/// the exponent sits two bits lower and the coefficient carries an implicit leading 100
/// binary, which at this width always puts it past 10^34 - 1. Either way a coefficient
/// past that is non-canonical and reads as zero, as does a NaN payload past 10^33 - 1.
/// </para>
/// <para>
/// The bits travel as a <see cref="Decimal128Integer"/>, whose high word carries the sign,
/// the combination, and the exponent, and whose low word is the low word of the
/// coefficient.
/// </para>
/// </remarks>
internal static class Decimal128Encoding
{
    public const int Precision = 34;

    /// <summary>Largest adjusted exponent, decNumber's Emax.</summary>
    public const int MaxExponent = 6144;

    /// <summary>Smallest adjusted exponent of a normal value, decNumber's Emin.</summary>
    public const int MinExponent = -6143;

    public const int Bias = 6176;

    /// <summary>Smallest quantum exponent, which is also that of the smallest subnormal.</summary>
    public const int MinQuantumExponent = -6176;

    /// <summary>Largest quantum exponent, <c>MaxExponent - Precision + 1</c>.</summary>
    public const int MaxQuantumExponent = 6111;

    private const ulong MaxCoefficientHigh = 0x0001ED09BEAD87C0;

    private const ulong MaxCoefficientLow = 0x378D8E63FFFFFFFF;

    /// <summary>10^34 - 1, the largest coefficient.</summary>
    public static Decimal128Integer MaxCoefficient
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(MaxCoefficientHigh, MaxCoefficientLow);
    }

    /// <summary>Ten to the precision, which a coefficient reaches only by rounding up.</summary>
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

    public const ulong SignMask = 0x8000000000000000;

    /// <summary>The bits set in the high word of both an infinity and a NaN.</summary>
    public const ulong SpecialMask = 0x7800000000000000;

    public const ulong InfinityBits = 0x7800000000000000;

    public const ulong NaNBits = 0x7C00000000000000;

    public const ulong SignalingNaNBits = 0x7E00000000000000;

    private const ulong KindMask = 0x7C00000000000000;

    private const ulong SignalingMask = 0x7E00000000000000;

    private const ulong LongFormMask = 0x6000000000000000;

    private const ulong ExponentFieldMask = 0x3FFF;

    private const int ShortExponentShift = 49;

    private const int LongExponentShift = 47;

    /// <summary>The part of the coefficient that lives in the high word: bits 64 through 112.</summary>
    private const ulong CoefficientHighMask = (1UL << 49) - 1;

    /// <summary>The part of a NaN payload that lives in the high word: bits 64 through 109.</summary>
    private const ulong PayloadHighMask = (1UL << 46) - 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsSpecial(Decimal128Integer bits)
    {
        return (bits.High & SpecialMask) == SpecialMask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNaN(Decimal128Integer bits)
    {
        return (bits.High & KindMask) == NaNBits;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsSignalingNaN(Decimal128Integer bits)
    {
        return (bits.High & SignalingMask) == SignalingNaNBits;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsInfinity(Decimal128Integer bits)
    {
        return (bits.High & KindMask) == InfinityBits;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNegative(Decimal128Integer bits)
    {
        return (long)bits.High < 0;
    }

    /// <summary>Whether the value is a finite zero, non-canonical coefficients included.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsZero(Decimal128Integer bits)
    {
        return !IsSpecial(bits) && Unpack(bits, out _).IsZero;
    }

    /// <summary>
    /// The coefficient and quantum exponent of a finite value. Only meaningful when the
    /// bits are not special, which the caller has already established.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Integer Unpack(Decimal128Integer bits, out int exponent)
    {
        if ((bits.High & LongFormMask) == LongFormMask)
        {
            // The implicit leading bits put every coefficient of this form past the
            // largest the format holds, so it is a zero whatever the trailing bits say.
            exponent = (int)((bits.High >> LongExponentShift) & ExponentFieldMask) - Bias;
            return Decimal128Integer.Zero;
        }

        exponent = (int)((bits.High >> ShortExponentShift) & ExponentFieldMask) - Bias;
        var coefficient = new Decimal128Integer(bits.High & CoefficientHighMask, bits.Low);

        // Written with short-circuit operators so that it compiles to branches: a
        // coefficient past the largest is rare, so they are predicted, where the
        // flag-setting compare of the two-word operator would cost its instructions every
        // time.
        if (coefficient.High > MaxCoefficientHigh
            || (coefficient.High == MaxCoefficientHigh && coefficient.Low > MaxCoefficientLow))
        {
            return Decimal128Integer.Zero;
        }

        return coefficient;
    }

    /// <summary>
    /// Lays a finite value out as bits. The coefficient must already fit the format and the
    /// exponent its range; nothing here rounds or checks, which is the finalizer's work.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Integer Pack(bool negative, int exponent, Decimal128Integer coefficient)
    {
        var sign = negative ? SignMask : 0UL;
        var biased = (ulong)(uint)(exponent + Bias);
        return new Decimal128Integer(sign | (biased << ShortExponentShift) | coefficient.High, coefficient.Low);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Integer Zero(bool negative, int exponent)
    {
        return Pack(negative, exponent, Decimal128Integer.Zero);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Integer Infinity(bool negative)
    {
        return new Decimal128Integer(negative ? SignMask | InfinityBits : InfinityBits, 0);
    }

    /// <summary>A quiet NaN with no payload, which is what an operation makes rather than passes on.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Integer QuietNaN()
    {
        return new Decimal128Integer(NaNBits, 0);
    }

    /// <summary>A NaN's diagnostic payload, read as zero when it is not one the format holds.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Integer Payload(Decimal128Integer bits)
    {
        var payload = new Decimal128Integer(bits.High & PayloadHighMask, bits.Low);
        return payload > MaxPayload ? Decimal128Integer.Zero : payload;
    }

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
    /// A NaN passed through as quiet, keeping its sign and payload, which is what makes a
    /// signaling NaN's payload survive the operation that reported it.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Integer Quiet(Decimal128Integer bits)
    {
        return NaN(IsNegative(bits), false, Payload(bits));
    }

    /// <summary>
    /// The same value in its canonical encoding: an over-large coefficient or payload
    /// becomes zero, and the unused bits of a special come out clear.
    /// </summary>
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
