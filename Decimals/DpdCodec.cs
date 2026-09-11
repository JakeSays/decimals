// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// The densely-packed-decimal interchange encoding: the bits decNumber writes, the bits the
/// testcase corpus lists, and the bits decimal hardware consumes.
/// </summary>
/// <remarks>
/// <para>
/// The layout is a sign bit, a five-bit combination field carrying the exponent's top two
/// bits and the leading coefficient digit, an exponent continuation field, and the
/// remaining coefficient digits as ten-bit declets.
/// </para>
/// <para>
/// This produces an integer coefficient, which is what the operations still working on one
/// need. That makes it their decode rather than an interchange convenience, so the
/// gathering below is written for the cost rather than for the reading of it.
/// <see cref="BcdCodec"/> is the path that never forms an integer at all.
/// </para>
/// </remarks>
internal static class DpdCodec
{
    private const uint InfinityCombination = 0x1E;
    private const uint NaNCombination = 0x1F;

    /// <summary>Declets gathered into the low half, which is eighteen digits.</summary>
    private const int LowDeclets = 6;

    /// <summary>The power of ten separating the two halves.</summary>
    private const ulong HalfSplit = 1000000000000000000;

    public static UnpackedDecimal<uint> Decode32(uint bits)
    {
        var value = Decode<Decimal32Format>(bits);
        return new UnpackedDecimal<uint>(value.Kind, value.IsNegative, value.Exponent, (uint)value.Coefficient);
    }

    public static uint Encode32(UnpackedDecimal<uint> value)
    {
        return (uint)Encode<Decimal32Format>(new UnpackedDecimal<UInt128>(
            value.Kind, value.IsNegative, value.Exponent, value.Coefficient));
    }

    public static UnpackedDecimal<ulong> Decode64(ulong bits)
    {
        var value = Decode<Decimal64Format>(bits);
        return new UnpackedDecimal<ulong>(value.Kind, value.IsNegative, value.Exponent, (ulong)value.Coefficient);
    }

    public static ulong Encode64(UnpackedDecimal<ulong> value)
    {
        return (ulong)Encode<Decimal64Format>(new UnpackedDecimal<UInt128>(
            value.Kind, value.IsNegative, value.Exponent, value.Coefficient));
    }

    public static UnpackedDecimal<UInt128> Decode128(UInt128 bits)
    {
        return Decode<Decimal128Format>(bits);
    }

    public static UInt128 Encode128(UnpackedDecimal<UInt128> value)
    {
        return Encode<Decimal128Format>(value);
    }

    private static UnpackedDecimal<UInt128> Decode<TFormat>(UInt128 bits)
        where TFormat : IDecimalFormat
    {
        var coefficientBits = TFormat.DecletCount * 10;
        var isNegative = (bits >> (TFormat.BitCount - 1)) != UInt128.Zero;
        var combination = (uint)(bits >> (TFormat.BitCount - 6)) & 0x1F;
        var continuation = (uint)(bits >> coefficientBits) & ((1u << TFormat.ExponentContinuationBits) - 1);

        if (combination >= InfinityCombination)
        {
            if (combination == InfinityCombination)
            {
                return new UnpackedDecimal<UInt128>(DecimalKind.Infinity, isNegative, 0, UInt128.Zero);
            }

            // The bit directly below the combination field tells a quiet NaN from a
            // signaling one; the rest of the continuation field carries nothing.
            var isSignaling = (continuation >> (TFormat.ExponentContinuationBits - 1)) != 0;
            var kind = isSignaling ? DecimalKind.SignalingNaN : DecimalKind.QuietNaN;
            return new UnpackedDecimal<UInt128>(kind, isNegative, 0, ReadDeclets<TFormat>(bits, 0));
        }

        uint exponentHigh;
        uint leadingDigit;
        if ((combination >> 3) != 3)
        {
            exponentHigh = combination >> 3;
            leadingDigit = combination & 7;
        }
        else
        {
            exponentHigh = (combination >> 1) & 3;
            leadingDigit = 8 + (combination & 1);
        }

        var biasedExponent = (int)((exponentHigh << TFormat.ExponentContinuationBits) | continuation);
        var coefficient = ReadDeclets<TFormat>(bits, leadingDigit);
        return new UnpackedDecimal<UInt128>(
            DecimalKind.Finite, isNegative, biasedExponent - TFormat.Bias, coefficient);
    }

    private static UInt128 Encode<TFormat>(UnpackedDecimal<UInt128> value)
        where TFormat : IDecimalFormat
    {
        var sign = value.IsNegative ? UInt128.One << (TFormat.BitCount - 1) : UInt128.Zero;

        if (value.Kind == DecimalKind.Infinity)
        {
            return sign | ((UInt128)InfinityCombination << (TFormat.BitCount - 6));
        }

        if (value.Kind != DecimalKind.Finite)
        {
            var signaling = value.Kind == DecimalKind.SignalingNaN
                ? UInt128.One << (TFormat.BitCount - 7)
                : UInt128.Zero;

            return sign
                | ((UInt128)NaNCombination << (TFormat.BitCount - 6))
                | signaling
                | WriteDeclets<TFormat>(value.Coefficient, out _);
        }

        var biasedExponent = (uint)(value.Exponent + TFormat.Bias);
        var declets = WriteDeclets<TFormat>(value.Coefficient, out var leadingDigit);
        var exponentHigh = biasedExponent >> TFormat.ExponentContinuationBits;
        var continuation = biasedExponent & ((1u << TFormat.ExponentContinuationBits) - 1);

        var combination = leadingDigit <= 7
            ? (exponentHigh << 3) | leadingDigit
            : 0x18 | (exponentHigh << 1) | (leadingDigit & 1);

        return sign
            | ((UInt128)combination << (TFormat.BitCount - 6))
            | ((UInt128)continuation << (TFormat.DecletCount * 10))
            | declets;
    }

    /// <summary>
    /// Builds the coefficient from the declets.
    /// </summary>
    /// <remarks>
    /// The width is a compile-time constant of the instantiation, so the narrow formats
    /// fold to a machine-word accumulator and never touch 128-bit arithmetic. Decimal128
    /// gathers its two halves separately and joins them with one multiply, rather than
    /// widening the accumulator once per declet.
    /// </remarks>
    private static UInt128 ReadDeclets<TFormat>(UInt128 bits, uint leadingDigit)
        where TFormat : IDecimalFormat
    {
        if (TFormat.Precision <= 19)
        {
            var narrow = (ulong)leadingDigit;
            for (var declet = TFormat.DecletCount - 1; declet >= 0; declet--)
            {
                narrow = (narrow * 1000) + Dpd.ToBinary((uint)(bits >> (declet * 10)) & 0x3FF);
            }

            return narrow;
        }

        var high = (ulong)leadingDigit;
        for (var declet = TFormat.DecletCount - 1; declet >= LowDeclets; declet--)
        {
            high = (high * 1000) + Dpd.ToBinary((uint)(bits >> (declet * 10)) & 0x3FF);
        }

        var low = 0UL;
        for (var declet = LowDeclets - 1; declet >= 0; declet--)
        {
            low = (low * 1000) + Dpd.ToBinary((uint)(bits >> (declet * 10)) & 0x3FF);
        }

        return ((UInt128)high * HalfSplit) + low;
    }

    /// <summary>
    /// Lays the coefficient out as declets, splitting the same way the read joins: one
    /// 128-bit division rather than one per declet.
    /// </summary>
    private static UInt128 WriteDeclets<TFormat>(UInt128 coefficient, out uint leadingDigit)
        where TFormat : IDecimalFormat
    {
        var bits = UInt128.Zero;

        if (TFormat.Precision <= 19)
        {
            var narrow = (ulong)coefficient;
            for (var declet = 0; declet < TFormat.DecletCount; declet++)
            {
                bits |= (UInt128)Dpd.ToDeclet((uint)(narrow % 1000)) << (declet * 10);
                narrow /= 1000;
            }

            leadingDigit = (uint)narrow;
            return bits;
        }

        var low = (ulong)(coefficient % HalfSplit);
        var high = (ulong)(coefficient / HalfSplit);

        for (var declet = 0; declet < LowDeclets; declet++)
        {
            bits |= (UInt128)Dpd.ToDeclet((uint)(low % 1000)) << (declet * 10);
            low /= 1000;
        }

        for (var declet = LowDeclets; declet < TFormat.DecletCount; declet++)
        {
            bits |= (UInt128)Dpd.ToDeclet((uint)(high % 1000)) << (declet * 10);
            high /= 1000;
        }

        leadingDigit = (uint)high;
        return bits;
    }
}
