// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// The densely-packed-decimal interchange encoding: the bits decNumber writes, the bits the
/// testcase corpus lists, and the bits decimal hardware consumes. A <see cref="Decimal32"/>
/// holds the binary-integer form, so this is a conversion at the boundary rather than
/// anything arithmetic touches.
/// </summary>
/// <remarks>
/// <para>
/// The layout is a sign bit, a five-bit combination field carrying the exponent's top two
/// bits and the leading coefficient digit, a six-bit exponent continuation, and the
/// remaining six digits as two ten-bit declets.
/// </para>
/// <para>
/// A declet is packed and unpacked by the bit rules of IEEE 754-2019 section 3.5.2 rather
/// than by a table: the conversion is not on any hot path, and the rules are a few dozen
/// operations. Twenty-four of the thousand and twenty-four declets are non-canonical -- they
/// carry three digits another declet also carries -- and unpacking accepts them while
/// packing always produces the canonical one.
/// </para>
/// </remarks>
internal static class Decimal32Dpd
{
    private const uint InfinityCombination = 0x1E;

    private const uint NaNCombination = 0x1F;

    private const int DecletCount = 2;

    private const int ContinuationBits = 6;

    private const int CombinationShift = 26;

    private const uint DecletMask = 0x3FF;

    /// <summary>Reads the interchange form into the binary-integer form.</summary>
    public static uint FromDpd(uint bits)
    {
        var negative = (bits >> 31) != 0;
        var combination = (bits >> CombinationShift) & 0x1F;
        var continuation = (bits >> (DecletCount * 10)) & ((1u << ContinuationBits) - 1);

        if (combination >= InfinityCombination)
        {
            if (combination == InfinityCombination)
            {
                return Decimal32Encoding.Infinity(negative);
            }

            // The bit directly below the combination field separates the two NaNs; the rest
            // of the continuation carries nothing.
            var signaling = (continuation >> (ContinuationBits - 1)) != 0;
            return Decimal32Encoding.NaN(negative, signaling, ReadDeclets(bits, 0));
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

        var exponent = (int)((exponentHigh << ContinuationBits) | continuation) - Decimal32Encoding.Bias;
        return Decimal32Encoding.Pack(negative, exponent, ReadDeclets(bits, leadingDigit));
    }

    /// <summary>Writes the binary-integer form out as the interchange form.</summary>
    public static uint ToDpd(uint bits)
    {
        var sign = bits & Decimal32Encoding.SignMask;

        if (Decimal32Encoding.IsSpecial(bits))
        {
            if (Decimal32Encoding.IsInfinity(bits))
            {
                return sign | (InfinityCombination << CombinationShift);
            }

            var signaling = Decimal32Encoding.IsSignalingNaN(bits) ? 1u << (CombinationShift - 1) : 0u;
            return sign | (NaNCombination << CombinationShift) | signaling
                | WriteDeclets(Decimal32Encoding.Payload(bits), out _);
        }

        var coefficient = Decimal32Encoding.Unpack(bits, out var exponent);
        var biased = (uint)(exponent + Decimal32Encoding.Bias);
        var declets = WriteDeclets(coefficient, out var leadingDigit);
        var exponentHigh = biased >> ContinuationBits;
        var continuation = biased & ((1u << ContinuationBits) - 1);

        var combination = leadingDigit <= 7
            ? (exponentHigh << 3) | leadingDigit
            : 0x18 | (exponentHigh << 1) | (leadingDigit & 1);

        return sign
            | (combination << CombinationShift)
            | (continuation << (DecletCount * 10))
            | declets;
    }

    private static uint ReadDeclets(uint bits, uint leadingDigit)
    {
        var coefficient = leadingDigit;
        for (var declet = DecletCount - 1; declet >= 0; declet--)
        {
            coefficient = (coefficient * 1000) + Unpack((bits >> (declet * 10)) & DecletMask);
        }

        return coefficient;
    }

    private static uint WriteDeclets(uint coefficient, out uint leadingDigit)
    {
        var bits = 0u;
        for (var declet = 0; declet < DecletCount; declet++)
        {
            var next = coefficient / 1000;
            var group = coefficient - (next * 1000);
            bits |= Pack(group) << (declet * 10);
            coefficient = next;
        }

        leadingDigit = coefficient;
        return bits;
    }

    /// <summary>
    /// Three digits from a declet, by the rules of the standard. The declet's bits are
    /// named p through y from the top; v says whether any digit is large, and w and x say
    /// which, with s and t deciding among the three-large cases.
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
    /// The canonical declet for a value from 0 through 999. Each digit's top bit says
    /// whether it is large, and the eight combinations of those bits select the layout.
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
