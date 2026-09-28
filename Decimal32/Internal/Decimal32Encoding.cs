// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace Decimals.Internal;

/// <summary>
/// The bits of a <see cref="Decimal32"/>: IEEE 754 decimal32 in its binary-integer encoding,
/// and the constants of the format.
/// </summary>
/// <remarks>
/// <para>
/// The coefficient is a plain binary integer in the trailing field, which is why this is
/// the encoding held in memory: every operation reaches it with a mask and a shift, and
/// scaling it by a power of ten is a multiply. The densely-packed interchange form is a
/// conversion away in <see cref="Decimal32Dpd"/>.
/// </para>
/// <para>
/// Two layouts, per IEEE 754-2019 section 3.5.2. When the two bits below the sign are not
/// both set, the exponent occupies the next eight bits and the coefficient the low
/// twenty-three; when they are both set, and the value is not infinite or a NaN, the
/// exponent sits two bits lower and the coefficient carries an implicit leading 100 binary,
/// which is how the coefficients from 2^23 to 10^7 - 1 are held. A coefficient above
/// 10^7 - 1 is non-canonical and reads as zero, as does a NaN payload above 10^6 - 1.
/// </para>
/// </remarks>
internal static class Decimal32Encoding
{
    public const int Precision = 7;

    /// <summary>Largest adjusted exponent, decNumber's Emax.</summary>
    public const int MaxExponent = 96;

    /// <summary>Smallest adjusted exponent of a normal value, decNumber's Emin.</summary>
    public const int MinExponent = -95;

    public const int Bias = 101;

    /// <summary>Smallest quantum exponent, which is also that of the smallest subnormal.</summary>
    public const int MinQuantumExponent = -101;

    /// <summary>Largest quantum exponent, <c>MaxExponent - Precision + 1</c>.</summary>
    public const int MaxQuantumExponent = 90;

    public const uint MaxCoefficient = 9999999;

    /// <summary>Ten to the precision, which a coefficient reaches only by rounding up.</summary>
    public const uint CoefficientLimit = 10000000;

    public const uint MaxPayload = 999999;

    public const uint SignMask = 0x80000000;

    /// <summary>The bits set in both an infinity and a NaN.</summary>
    public const uint SpecialMask = 0x78000000;

    public const uint InfinityBits = 0x78000000;

    public const uint NaNBits = 0x7C000000;

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsSpecial(uint bits)
    {
        return (bits & SpecialMask) == SpecialMask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNaN(uint bits)
    {
        return (bits & KindMask) == NaNBits;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsSignalingNaN(uint bits)
    {
        return (bits & SignalingMask) == SignalingNaNBits;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsInfinity(uint bits)
    {
        return (bits & KindMask) == InfinityBits;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNegative(uint bits)
    {
        return (int)bits < 0;
    }

    /// <summary>Whether the value is a finite zero, non-canonical coefficients included.</summary>
    public static bool IsZero(uint bits)
    {
        return !IsSpecial(bits) && Unpack(bits, out _) == 0;
    }

    /// <summary>
    /// The coefficient and quantum exponent of a finite value. Only meaningful when the
    /// bits are not special, which the caller has already established.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Unpack(uint bits, out int exponent)
    {
        // A branch on the form, deliberately. Only coefficients from about 8.4 million up
        // take the long form, which is a few percent of any mixed set, so the branch is
        // predicted; settling the shift and the fields by mask arithmetic instead put a
        // longer dependent chain on every value and cost ten to twenty percent across the
        // arithmetic.
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
    /// Lays a finite value out as bits. The coefficient must already fit the format and the
    /// exponent its range; nothing here rounds or checks, which is the finalizer's work.
    /// </summary>
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

    public static uint Zero(bool negative, int exponent)
    {
        return Pack(negative, exponent, 0);
    }

    public static uint Infinity(bool negative)
    {
        return negative ? SignMask | InfinityBits : InfinityBits;
    }

    /// <summary>A quiet NaN with no payload, which is what an operation makes rather than passes on.</summary>
    public static uint QuietNaN()
    {
        return NaNBits;
    }

    /// <summary>A NaN's diagnostic payload, read as zero when it is not one the format holds.</summary>
    public static uint Payload(uint bits)
    {
        var payload = bits & PayloadMask;
        return payload > MaxPayload ? 0 : payload;
    }

    public static uint NaN(bool negative, bool signaling, uint payload)
    {
        var sign = negative ? SignMask : 0U;
        var kind = signaling ? SignalingNaNBits : NaNBits;
        return sign | kind | (payload > MaxPayload ? 0 : payload);
    }

    /// <summary>
    /// A NaN passed through as quiet, keeping its sign and payload, which is what makes a
    /// signaling NaN's payload survive the operation that reported it.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Quiet(uint bits)
    {
        return (bits & SignMask) | NaNBits | Payload(bits);
    }

    /// <summary>
    /// The same value in its canonical encoding: an over-large coefficient or payload
    /// becomes zero, and the unused bits of a special come out clear.
    /// </summary>
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
