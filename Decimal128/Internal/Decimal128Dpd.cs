// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// The densely-packed-decimal interchange encoding: the bits decNumber writes, the bits the
/// testcase corpus lists, and the bits decimal hardware consumes. A <see cref="Decimal128"/>
/// holds the binary-integer form, so this is a conversion at the boundary rather than
/// anything arithmetic touches.
/// </summary>
/// <remarks>
/// <para>
/// The layout is a sign bit, a five-bit combination field carrying the exponent's top two
/// bits and the leading coefficient digit, a twelve-bit exponent continuation, and the
/// remaining thirty-three digits as eleven ten-bit declets. The declets fill the low word
/// and run on into the high one, the seventh straddling the two.
/// </para>
/// <para>
/// A declet is packed and unpacked by the bit rules of IEEE 754-2019 section 3.5.2 rather
/// than by a table: the conversion is not on any hot path, and the rules are a few dozen
/// operations. Twenty-four of the thousand and twenty-four declets are non-canonical -- they
/// carry three digits another declet also carries -- and unpacking accepts them while
/// packing always produces the canonical one.
/// </para>
/// </remarks>
internal static class Decimal128Dpd
{
    private const uint InfinityCombination = 0x1E;

    private const uint NaNCombination = 0x1F;

    private const int DecletCount = 11;

    private const int ContinuationBits = 12;

    /// <summary>Where the combination field starts within the high word.</summary>
    private const int CombinationShift = 58;

    /// <summary>Where the exponent continuation starts within the high word.</summary>
    private const int ContinuationShift = 46;

    private const uint DecletMask = 0x3FF;

    /// <summary>Digits the low eighteen of a coefficient are written from, six declets' worth.</summary>
    private const int LowDigits = 18;

    /// <summary>Reads the interchange form into the binary-integer form.</summary>
    public static Decimal128Integer FromDpd(Decimal128Integer bits)
    {
        var negative = (bits.High >> 63) != 0;
        var combination = (uint)(bits.High >> CombinationShift) & 0x1F;
        var continuation = (uint)(bits.High >> ContinuationShift) & ((1u << ContinuationBits) - 1);

        if (combination >= InfinityCombination)
        {
            if (combination == InfinityCombination)
            {
                return Decimal128Encoding.Infinity(negative);
            }

            // The bit directly below the combination field separates the two NaNs; the rest
            // of the continuation carries nothing.
            var signaling = (continuation >> (ContinuationBits - 1)) != 0;
            return Decimal128Encoding.NaN(negative, signaling, ReadDeclets(bits, 0));
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

        var exponent = (int)((exponentHigh << ContinuationBits) | continuation) - Decimal128Encoding.Bias;
        return Decimal128Encoding.Pack(negative, exponent, ReadDeclets(bits, leadingDigit));
    }

    /// <summary>Writes the binary-integer form out as the interchange form.</summary>
    public static Decimal128Integer ToDpd(Decimal128Integer bits)
    {
        var sign = bits.High & Decimal128Encoding.SignMask;

        if (Decimal128Encoding.IsSpecial(bits))
        {
            if (Decimal128Encoding.IsInfinity(bits))
            {
                return new Decimal128Integer(sign | ((ulong)InfinityCombination << CombinationShift), 0);
            }

            var signaling = Decimal128Encoding.IsSignalingNaN(bits) ? 1UL << (CombinationShift - 1) : 0UL;
            var payloadDeclets = WriteDeclets(Decimal128Encoding.Payload(bits), out _);
            return new Decimal128Integer(
                sign | ((ulong)NaNCombination << CombinationShift) | signaling | payloadDeclets.High,
                payloadDeclets.Low);
        }

        var coefficient = Decimal128Encoding.Unpack(bits, out var exponent);
        var biased = (uint)(exponent + Decimal128Encoding.Bias);
        var declets = WriteDeclets(coefficient, out var leadingDigit);
        var exponentHigh = biased >> ContinuationBits;
        var continuation = biased & ((1u << ContinuationBits) - 1);

        var combination = leadingDigit <= 7
            ? (exponentHigh << 3) | leadingDigit
            : 0x18 | (exponentHigh << 1) | (leadingDigit & 1);

        var high = sign
            | ((ulong)combination << CombinationShift)
            | ((ulong)continuation << ContinuationShift)
            | declets.High;

        return new Decimal128Integer(high, declets.Low);
    }

    private static Decimal128Integer ReadDeclets(Decimal128Integer bits, uint leadingDigit)
    {
        var coefficient = Decimal128Integer.FromUInt64(leadingDigit);
        for (var declet = DecletCount - 1; declet >= 0; declet--)
        {
            coefficient = coefficient.MultiplyBy(1000) + Unpack(DecletAt(bits, declet));
        }

        return coefficient;
    }

    /// <summary>The ten bits of one declet, wherever they fall across the two words.</summary>
    private static uint DecletAt(Decimal128Integer bits, int index)
    {
        var position = index * 10;
        if (position + 10 <= 64)
        {
            return (uint)(bits.Low >> position) & DecletMask;
        }

        if (position >= 64)
        {
            return (uint)(bits.High >> (position - 64)) & DecletMask;
        }

        return (uint)((bits.Low >> position) | (bits.High << (64 - position))) & DecletMask;
    }

    /// <summary>
    /// The eleven declets of a coefficient of at most thirty-four digits, laid into their
    /// places in the two words, and the digit above them.
    /// </summary>
    private static Decimal128Integer WriteDeclets(Decimal128Integer coefficient, out uint leadingDigit)
    {
        var upper = Decimal128Tables.DivRemPowerOfTen(coefficient, LowDigits, out var lower).Low;
        var high = 0UL;
        var low = 0UL;

        for (var declet = 0; declet < DecletCount; declet++)
        {
            ulong group;
            if (declet < LowDigits / 3)
            {
                var next = lower / 1000;
                group = lower - (next * 1000);
                lower = next;
            }
            else
            {
                var next = upper / 1000;
                group = upper - (next * 1000);
                upper = next;
            }

            var packed = (ulong)Pack((uint)group);
            var position = declet * 10;
            if (position + 10 <= 64)
            {
                low |= packed << position;
            }
            else if (position >= 64)
            {
                high |= packed << (position - 64);
            }
            else
            {
                low |= packed << position;
                high |= packed >> (64 - position);
            }
        }

        leadingDigit = (uint)upper;
        return new Decimal128Integer(high, low);
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
