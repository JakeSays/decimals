// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// Converts to and from the DPD encoding. DPD is what decNumber writes, what the test
/// corpus lists, and what decimal hardware uses. A <see cref="Decimal64"/> stores BID, so
/// DPD is only a conversion at the boundary. The arithmetic never uses it.
/// </summary>
/// <remarks>
/// <para>
/// The layout is: a sign bit; a 5-bit combination field holding the exponent's top 2 bits
/// and the leading coefficient digit; an 8-bit exponent continuation; and the remaining 15
/// digits as five 10-bit declets.
/// </para>
/// <para>
/// Declets are packed and unpacked with the bit rules of IEEE 754-2019 section 3.5.2, not
/// with a table. The conversion is not on a hot path, and the rules take a few dozen
/// operations. 24 of the 1024 declets are non-canonical: they encode the same three digits
/// as another declet. Unpacking accepts them, and packing always produces the canonical
/// one.
/// </para>
/// </remarks>
internal static class Decimal64Dpd
{
    private const uint InfinityCombination = 0x1E;

    private const uint NaNCombination = 0x1F;

    private const int DecletCount = 5;

    private const int ContinuationBits = 8;

    private const int CombinationShift = 58;

    private const ulong DecletMask = 0x3FF;

    /// <summary>Converts DPD bits to BID bits.</summary>
    /// <param name="bits">The DPD encoding.</param>
    /// <returns>The canonical BID encoding of the same value.</returns>
    public static ulong FromDpd(ulong bits)
    {
        var negative = (bits >> 63) != 0;
        var combination = (uint)(bits >> CombinationShift) & 0x1F;
        var continuation = (uint)(bits >> (DecletCount * 10)) & ((1u << ContinuationBits) - 1);

        if (combination >= InfinityCombination)
        {
            if (combination == InfinityCombination)
            {
                return Decimal64Encoding.Infinity(negative);
            }

            // The bit just below the combination field tells the two NaNs apart. The rest of
            // the continuation is unused.
            var signaling = (continuation >> (ContinuationBits - 1)) != 0;
            return Decimal64Encoding.NaN(negative, signaling, ReadDeclets(bits, 0));
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

        var exponent = (int)((exponentHigh << ContinuationBits) | continuation) - Decimal64Encoding.Bias;
        return Decimal64Encoding.Pack(negative, exponent, ReadDeclets(bits, leadingDigit));
    }

    /// <summary>Converts BID bits to DPD bits.</summary>
    /// <param name="bits">The BID encoding.</param>
    /// <returns>The canonical DPD encoding of the same value.</returns>
    public static ulong ToDpd(ulong bits)
    {
        var sign = bits & Decimal64Encoding.SignMask;

        if (Decimal64Encoding.IsSpecial(bits))
        {
            if (Decimal64Encoding.IsInfinity(bits))
            {
                return sign | ((ulong)InfinityCombination << CombinationShift);
            }

            var signaling = Decimal64Encoding.IsSignalingNaN(bits) ? 1UL << (CombinationShift - 1) : 0UL;
            return sign | ((ulong)NaNCombination << CombinationShift) | signaling
                | WriteDeclets(Decimal64Encoding.Payload(bits), out _);
        }

        var coefficient = Decimal64Encoding.Unpack(bits, out var exponent);
        var biased = (uint)(exponent + Decimal64Encoding.Bias);
        var declets = WriteDeclets(coefficient, out var leadingDigit);
        var exponentHigh = biased >> ContinuationBits;
        var continuation = biased & ((1u << ContinuationBits) - 1);

        var combination = leadingDigit <= 7
            ? (exponentHigh << 3) | leadingDigit
            : 0x18 | (exponentHigh << 1) | (leadingDigit & 1);

        return sign
            | ((ulong)combination << CombinationShift)
            | ((ulong)continuation << (DecletCount * 10))
            | declets;
    }

    private static ulong ReadDeclets(ulong bits, uint leadingDigit)
    {
        var coefficient = (ulong)leadingDigit;
        for (var declet = DecletCount - 1; declet >= 0; declet--)
        {
            coefficient = (coefficient * 1000) + Unpack((uint)(bits >> (declet * 10)) & (uint)DecletMask);
        }

        return coefficient;
    }

    private static ulong WriteDeclets(ulong coefficient, out uint leadingDigit)
    {
        var bits = 0UL;
        for (var declet = 0; declet < DecletCount; declet++)
        {
            var next = coefficient / 1000;
            var group = (uint)(coefficient - (next * 1000));
            bits |= (ulong)Pack(group) << (declet * 10);
            coefficient = next;
        }

        leadingDigit = (uint)coefficient;
        return bits;
    }

    /// <summary>
    /// Decodes a declet to three digits, using the rules in the standard. The declet's bits
    /// are named p through y from the top. v says whether any digit is large (8 or 9). w and
    /// x say which one. s and t decide among the cases with more than one large digit.
    /// </summary>
    private static uint Unpack(uint declet)
    {
        var p = (declet >> 9) & 1;
        var q = (declet >> 8) & 1;
        var r = (declet >> 7) & 1;
        var s = (declet >> 6) & 1;
        var t = (declet >> 5) & 1;
        var u = (declet >> 4) & 1;
        var v = (declet >> 3) & 1;
        var w = (declet >> 2) & 1;
        var x = (declet >> 1) & 1;
        var y = declet & 1;

        uint hundreds;
        uint tens;
        uint units;

        if (v == 0)
        {
            hundreds = (p << 2) | (q << 1) | r;
            tens = (s << 2) | (t << 1) | u;
            units = (w << 2) | (x << 1) | y;
        }
        else if (w == 0 && x == 0)
        {
            hundreds = (p << 2) | (q << 1) | r;
            tens = (s << 2) | (t << 1) | u;
            units = 8 | y;
        }
        else if (w == 0 && x == 1)
        {
            hundreds = (p << 2) | (q << 1) | r;
            tens = 8 | u;
            units = (s << 2) | (t << 1) | y;
        }
        else if (w == 1 && x == 0)
        {
            hundreds = 8 | r;
            tens = (s << 2) | (t << 1) | u;
            units = (p << 2) | (q << 1) | y;
        }
        else if (s == 0 && t == 0)
        {
            hundreds = 8 | r;
            tens = 8 | u;
            units = (p << 2) | (q << 1) | y;
        }
        else if (s == 0 && t == 1)
        {
            hundreds = 8 | r;
            tens = (p << 2) | (q << 1) | u;
            units = 8 | y;
        }
        else if (s == 1 && t == 0)
        {
            hundreds = (p << 2) | (q << 1) | r;
            tens = 8 | u;
            units = 8 | y;
        }
        else
        {
            hundreds = 8 | r;
            tens = 8 | u;
            units = 8 | y;
        }

        return (hundreds * 100) + (tens * 10) + units;
    }

    /// <summary>
    /// The canonical declet for a value from 0 to 999. Each digit's top bit says whether it
    /// is large (8 or 9). The eight combinations of those bits select the layout.
    /// </summary>
    private static uint Pack(uint value)
    {
        var hundreds = value / 100;
        var rest = value - (hundreds * 100);
        var tens = rest / 10;
        var units = rest - (tens * 10);

        var a = (hundreds >> 3) & 1;
        var b = (hundreds >> 2) & 1;
        var c = (hundreds >> 1) & 1;
        var d = hundreds & 1;
        var e = (tens >> 3) & 1;
        var f = (tens >> 2) & 1;
        var g = (tens >> 1) & 1;
        var h = tens & 1;
        var i = (units >> 3) & 1;
        var j = (units >> 2) & 1;
        var k = (units >> 1) & 1;
        var m = units & 1;

        var large = (a << 2) | (e << 1) | i;
        return large switch
        {
            0 => (b << 9) | (c << 8) | (d << 7) | (f << 6) | (g << 5) | (h << 4) | (j << 2) | (k << 1) | m,
            1 => (b << 9) | (c << 8) | (d << 7) | (f << 6) | (g << 5) | (h << 4) | (1 << 3) | m,
            2 => (b << 9) | (c << 8) | (d << 7) | (j << 6) | (k << 5) | (h << 4) | (1 << 3) | (1 << 1) | m,
            4 => (j << 9) | (k << 8) | (d << 7) | (f << 6) | (g << 5) | (h << 4) | (1 << 3) | (1 << 2) | m,
            6 => (j << 9) | (k << 8) | (d << 7) | (h << 4) | (1 << 3) | (1 << 2) | (1 << 1) | m,
            5 => (f << 9) | (g << 8) | (d << 7) | (1 << 5) | (h << 4) | (1 << 3) | (1 << 2) | (1 << 1) | m,
            3 => (b << 9) | (c << 8) | (d << 7) | (1 << 6) | (h << 4) | (1 << 3) | (1 << 2) | (1 << 1) | m,
            _ => (d << 7) | (1 << 6) | (1 << 5) | (h << 4) | (1 << 3) | (1 << 2) | (1 << 1) | m
        };
    }
}
