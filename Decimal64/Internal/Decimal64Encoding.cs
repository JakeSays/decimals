// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;


namespace Decimals.Internal;

/// <summary>
/// The bits of a <see cref="Decimal64"/>: IEEE 754 decimal64 in its binary-integer encoding,
/// and the constants of the format.
/// </summary>
/// <remarks>
/// <para>
/// The coefficient is a plain binary integer in the trailing field, which is why this is
/// the encoding held in memory: every operation reaches it with a mask and a shift, and
/// scaling it by a power of ten is a multiply. The densely-packed interchange form is a
/// conversion away in <see cref="Decimal64Dpd"/>.
/// </para>
/// <para>
/// Two layouts, per IEEE 754-2019 section 3.5.2. When the two bits below the sign are not
/// both set, the exponent occupies the next ten bits and the coefficient the low
/// fifty-three; when they are both set, and the value is not infinite or a NaN, the
/// exponent sits two bits lower and the coefficient carries an implicit leading 100 binary.
/// A coefficient above 10^16 - 1 is non-canonical and reads as zero, as does a NaN payload
/// above 10^15 - 1.
/// </para>
/// </remarks>
internal static class Decimal64Encoding
{
    public const int Precision = 16;

    /// <summary>Largest adjusted exponent, decNumber's Emax.</summary>
    public const int MaxExponent = 384;

    /// <summary>Smallest adjusted exponent of a normal value, decNumber's Emin.</summary>
    public const int MinExponent = -383;

    public const int Bias = 398;

    /// <summary>Smallest quantum exponent, which is also that of the smallest subnormal.</summary>
    public const int MinQuantumExponent = -398;

    /// <summary>Largest quantum exponent, <c>MaxExponent - Precision + 1</c>.</summary>
    public const int MaxQuantumExponent = 369;

    public const ulong MaxCoefficient = 9999999999999999;

    /// <summary>Ten to the precision, which a coefficient reaches only by rounding up.</summary>
    public const ulong CoefficientLimit = 10000000000000000;

    public const ulong MaxPayload = 999999999999999;

    public const ulong SignMask = 0x8000000000000000;

    /// <summary>The bits set in both an infinity and a NaN.</summary>
    public const ulong SpecialMask = 0x7800000000000000;

    public const ulong InfinityBits = 0x7800000000000000;

    public const ulong NaNBits = 0x7C00000000000000;

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsSpecial(ulong bits)
    {
        return (bits & SpecialMask) == SpecialMask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNaN(ulong bits)
    {
        return (bits & KindMask) == NaNBits;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsSignalingNaN(ulong bits)
    {
        return (bits & SignalingMask) == SignalingNaNBits;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsInfinity(ulong bits)
    {
        return (bits & KindMask) == InfinityBits;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNegative(ulong bits)
    {
        return (long)bits < 0;
    }

    /// <summary>Whether the value is a finite zero, non-canonical coefficients included.</summary>
    public static bool IsZero(ulong bits)
    {
        return !IsSpecial(bits) && Unpack(bits, out _) == 0;
    }

    /// <summary>
    /// The coefficient and quantum exponent of a finite value. Only meaningful when the
    /// bits are not special, which the caller has already established.
    /// </summary>
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
    /// Lays a finite value out as bits. The coefficient must already fit the format and the
    /// exponent its range; nothing here rounds or checks, which is the finalizer's work.
    /// </summary>
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

    public static ulong Zero(bool negative, int exponent)
    {
        return Pack(negative, exponent, 0);
    }

    public static ulong Infinity(bool negative)
    {
        return negative ? SignMask | InfinityBits : InfinityBits;
    }

    /// <summary>A quiet NaN with no payload, which is what an operation makes rather than passes on.</summary>
    public static ulong QuietNaN()
    {
        return NaNBits;
    }

    /// <summary>A NaN's diagnostic payload, read as zero when it is not one the format holds.</summary>
    public static ulong Payload(ulong bits)
    {
        var payload = bits & PayloadMask;
        return payload > MaxPayload ? 0 : payload;
    }

    public static ulong NaN(bool negative, bool signaling, ulong payload)
    {
        var sign = negative ? SignMask : 0UL;
        var kind = signaling ? SignalingNaNBits : NaNBits;
        return sign | kind | (payload > MaxPayload ? 0 : payload);
    }

    /// <summary>
    /// A NaN passed through as quiet, keeping its sign and payload, which is what makes a
    /// signaling NaN's payload survive the operation that reported it.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Quiet(ulong bits)
    {
        return (bits & SignMask) | NaNBits | Payload(bits);
    }

    /// <summary>
    /// The same value in its canonical encoding: an over-large coefficient or payload
    /// becomes zero, and the unused bits of a special come out clear.
    /// </summary>
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
