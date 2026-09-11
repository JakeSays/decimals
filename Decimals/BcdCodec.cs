// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// The DPD interchange encoding read straight into digits and written straight back out.
/// </summary>
/// <remarks>
/// <para>
/// This is where the representation pays. <see cref="DpdCodec"/> reaches a coefficient by
/// multiplying each declet into a running integer and dividing back out again; here a
/// declet is three digits, so a whole coefficient is one table read per three digits and no
/// arithmetic at all.
/// </para>
/// <para>
/// The digit buffer a caller supplies must hold <c>TFormat.Precision</c> bytes, and the
/// caller owns it: everything here borrows.
/// </para>
/// </remarks>
internal static unsafe class BcdCodec
{
    private const uint InfinityCombination = 0x1E;
    private const uint NaNCombination = 0x1F;

    /// <summary>
    /// Takes an encoding apart into digits. The result addresses the buffer, so it stays
    /// valid only as long as the buffer does.
    /// </summary>
    public static BcdNumber Decode<TFormat>(UInt128 bits, byte* digits)
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
                digits[0] = 0;
                return new BcdNumber(DecimalKind.Infinity, isNegative, 0, digits, digits);
            }

            // The bit directly below the combination field separates the two NaNs; the rest
            // of the continuation carries nothing.
            var isSignaling = (continuation >> (TFormat.ExponentContinuationBits - 1)) != 0;
            var kind = isSignaling ? DecimalKind.SignalingNaN : DecimalKind.QuietNaN;
            ReadDeclets<TFormat>(bits, 0, digits);

            var payload = new BcdNumber(kind, isNegative, 0, digits, digits + TFormat.Precision - 1);
            payload.Trim();
            return payload;
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
        ReadDeclets<TFormat>(bits, leadingDigit, digits);

        var value = new BcdNumber(DecimalKind.Finite, isNegative, biasedExponent - TFormat.Bias,
            digits, digits + TFormat.Precision - 1);
        value.Trim();
        return value;
    }

    /// <summary>
    /// Lays a value out as an encoding. The coefficient must already fit the format: this
    /// rounds nothing and checks no range, which is the finalizer's work.
    /// </summary>
    public static UInt128 Encode<TFormat>(BcdNumber value)
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
                | WriteDeclets<TFormat>(value, out _);
        }

        var biasedExponent = (uint)(value.Exponent + TFormat.Bias);
        var declets = WriteDeclets<TFormat>(value, out var leadingDigit);
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
    /// Fills the buffer with the format's full precision: the combination field's digit,
    /// then three digits per declet. Leading zeros are written and the caller trims them,
    /// so the loop stays straight-line.
    /// </summary>
    private static void ReadDeclets<TFormat>(UInt128 bits, uint leadingDigit, byte* digits)
        where TFormat : IDecimalFormat
    {
        digits[0] = (byte)leadingDigit;

        var low = (ulong)bits;
        var upper = UpperDeclets<TFormat>(bits, low);

        var destination = digits + 1;
        for (var declet = TFormat.DecletCount - 1; declet >= 0; declet--)
        {
            var packed = declet < LowDecletCount<TFormat>()
                ? (uint)(low >> (declet * 10)) & 0x3FF
                : (uint)(upper >> ((declet - LowDecletCount<TFormat>()) * 10)) & 0x3FF;

            Dpd.WriteDigits(packed, destination);
            destination += Dpd.DigitsPerDeclet;
        }
    }

    /// <summary>
    /// Declets carried by the encoding's low word. decNumber reaches the coefficient
    /// through <c>DFWORD</c>, a word at a time, and never forms an integer as wide as the
    /// format: a shift of a 128-bit value by a count the compiler cannot see is a branch
    /// and two shifts, and there is one per declet.
    /// </summary>
    private static int LowDecletCount<TFormat>()
        where TFormat : IDecimalFormat
    {
        return Math.Min(TFormat.DecletCount, 6);
    }

    /// <summary>
    /// The declets above the low word, brought down so they can be read with the same
    /// 64-bit shifts. Only the widest format has any.
    /// </summary>
    private static ulong UpperDeclets<TFormat>(UInt128 bits, ulong low)
        where TFormat : IDecimalFormat
    {
        if (TFormat.DecletCount <= 6)
        {
            return 0;
        }

        var lowBits = LowDecletCount<TFormat>() * 10;
        return ((ulong)(bits >> 64) << (64 - lowBits)) | (low >> lowBits);
    }

    /// <summary>
    /// Writes a coefficient out as declets. The digits are right-aligned against the
    /// format's precision, so a coefficient shorter than that leaves the leading declets
    /// zero and the combination field's digit zero with it.
    /// </summary>
    private static UInt128 WriteDeclets<TFormat>(BcdNumber value, out uint leadingDigit)
        where TFormat : IDecimalFormat
    {
        // Walking up from the least significant digit right-aligns the coefficient without
        // having to work out where it starts: a declet the digits do not reach is a zero.
        var digit = value.Lsd;
        var msd = value.Msd;

        // Built in words, the way it is read: nothing here shifts a value wider than 64
        // bits, however wide the format is.
        var low = 0UL;
        var upper = 0UL;

        for (var declet = 0; declet < TFormat.DecletCount; declet++)
        {
            var nibbles = 0u;
            if (digit >= msd)
            {
                nibbles = *digit;
                digit--;
            }

            if (digit >= msd)
            {
                nibbles |= (uint)*digit << 4;
                digit--;
            }

            if (digit >= msd)
            {
                nibbles |= (uint)*digit << 8;
                digit--;
            }

            var packed = (ulong)Dpd.FromNibbles(nibbles);
            if (declet < LowDecletCount<TFormat>())
            {
                low |= packed << (declet * 10);
            }
            else
            {
                upper |= packed << ((declet - LowDecletCount<TFormat>()) * 10);
            }
        }

        // The precision is three digits per declet plus one, so at most one digit is left
        // over and it is the one the combination field carries.
        leadingDigit = digit >= msd ? *digit : 0u;

        if (TFormat.DecletCount <= 6)
        {
            return low;
        }

        var lowBits = LowDecletCount<TFormat>() * 10;
        return new UInt128(upper >> (64 - lowBits), low | (upper << lowBits));
    }
}
