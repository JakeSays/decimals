// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Unicode;

namespace Decimals.Internal;

/// <summary>
/// The specification's <c>to-number</c> conversion.
/// </summary>
/// <remarks>
/// <para>
/// Syntax is stricter than .NET's numeric parsing: no leading or trailing space, no group
/// separators, no culture-specific decimal point. A leading or trailing decimal point is
/// allowed (<c>.5</c> and <c>5.</c> both parse), two are not. Infinities are <c>inf</c> or
/// <c>infinity</c> in any casing; NaNs are <c>nan</c> or <c>snan</c> with an optional
/// payload of digits, whose significant length may not exceed six.
/// </para>
/// <para>
/// A number of up to nineteen digits is read on one path: the digits are gathered four at a
/// time from a word of characters, with the rest one at a time, and the value goes straight
/// to the finalizer. Anything longer takes the general path, which gathers nineteen
/// significant digits and folds everything past them into a sticky residue -- which can
/// only tip a rounding that discards at least twelve digits above it.
/// </para>
/// </remarks>
[SkipLocalsInit]
internal static class Decimal32Parser
{
    /// <summary>
    /// An exponent past this cannot be reached by any value, so parsing clamps to it
    /// rather than overflowing the arithmetic that computes it.
    /// </summary>
    private const int ExponentLimit = 1000000000;

    /// <summary>
    /// Text up to this length is widened or rewritten on the stack; anything longer is
    /// rented. The limit covers every number the format can hold, so renting is for
    /// pathological input.
    /// </summary>
    private const int StackBufferLength = 128;

    /// <summary>The digits a word gathers without overflowing.</summary>
    private const int WordDigits = Decimal32Tables.MaxPower;

    /// <summary>
    /// The conversion a culture asks for: the text is rewritten into the specification's
    /// grammar and then read by it, so one parser still computes every value.
    /// </summary>
    public static uint Parse(ReadOnlySpan<char> text, NumberStyles styles, IFormatProvider? provider,
        Decimal32Rounding rounding, ref Decimal32Status status)
    {
        var numberFormat = NumberFormatInfo.GetInstance(provider);
        var length = Math.Max(text.Length + 1, Decimal32CultureNormalizer.MinimumBufferLength);

        char[]? rented = null;
        Span<char> buffer = length <= StackBufferLength
            ? stackalloc char[StackBufferLength]
            : (rented = ArrayPool<char>.Shared.Rent(length));

        try
        {
            if (!Decimal32CultureNormalizer.TryNormalize(text, styles, numberFormat, buffer, out var written))
            {
                return Malformed(ref status);
            }

            return Parse(buffer[..written], rounding, ref status);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }

    /// <summary>
    /// The same, over UTF-8. A culture's symbols need not be ASCII -- an infinity is
    /// <c>∞</c> in most of them -- so this decodes rather than narrowing.
    /// </summary>
    public static uint Parse(ReadOnlySpan<byte> utf8Text, NumberStyles styles, IFormatProvider? provider,
        Decimal32Rounding rounding, ref Decimal32Status status)
    {
        // Decoding never produces more UTF-16 units than there were bytes.
        char[]? rented = null;
        Span<char> buffer = utf8Text.Length <= StackBufferLength
            ? stackalloc char[StackBufferLength]
            : (rented = ArrayPool<char>.Shared.Rent(utf8Text.Length));

        try
        {
            if (Utf8.ToUtf16(utf8Text, buffer, out _, out var decoded, replaceInvalidSequences: false)
                != OperationStatus.Done)
            {
                return Malformed(ref status);
            }

            return Parse(buffer[..decoded], styles, provider, rounding, ref status);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }

    /// <summary>
    /// The specification's grammar over UTF-8 text. Every character it accepts is ASCII, so
    /// a byte outside that range cannot appear in a number and the text is rejected without
    /// being decoded.
    /// </summary>
    public static uint Parse(ReadOnlySpan<byte> utf8Text, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        char[]? rented = null;
        Span<char> buffer = utf8Text.Length <= StackBufferLength
            ? stackalloc char[StackBufferLength]
            : (rented = ArrayPool<char>.Shared.Rent(utf8Text.Length));

        try
        {
            for (var index = 0; index < utf8Text.Length; index++)
            {
                var unit = utf8Text[index];
                if (unit > 0x7F)
                {
                    return Malformed(ref status);
                }

                buffer[index] = (char)unit;
            }

            return Parse(buffer[..utf8Text.Length], rounding, ref status);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }

    public static uint Parse(ReadOnlySpan<char> text, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        var length = text.Length;
        if (length == 0)
        {
            return Malformed(ref status);
        }

        ref var start = ref MemoryMarshal.GetReference(text);
        var index = 0;
        var negative = false;

        if (start == '-')
        {
            negative = true;
            index = 1;
        }
        else if (start == '+')
        {
            index = 1;
        }

        if (index == length)
        {
            return Malformed(ref status);
        }

        // A finite number begins with a digit or a point, and every special form begins
        // with a letter, so one comparison decides which grammar applies.
        var lead = Unsafe.Add(ref start, index);
        if ((uint)(lead - '0') > 9 && lead != '.')
        {
            return ParseSpecial(text[index..], negative, ref status);
        }

        var coefficient = 0UL;
        var digits = 0;
        index = ReadDigits(ref start, index, length, ref coefficient, ref digits);

        var fractionDigits = 0;
        if (index < length && Unsafe.Add(ref start, index) == '.')
        {
            index++;
            var pointIndex = index;
            index = ReadDigits(ref start, index, length, ref coefficient, ref digits);
            fractionDigits = index - pointIndex;
        }

        if (digits > WordDigits)
        {
            // More digits than a word gathers, leading zeros included: the general path
            // counts significant digits and folds the rest into a residue.
            return ParseLong(text[(negative || start == '+' ? 1 : 0)..], negative, rounding, ref status);
        }

        if (digits == 0)
        {
            return Malformed(ref status);
        }

        var exponent = 0;
        if (index < length)
        {
            if ((Unsafe.Add(ref start, index) | 0x20) != 'e')
            {
                return Malformed(ref status);
            }

            index++;
            if (!TryReadExponent(ref start, ref index, length, out exponent))
            {
                return Malformed(ref status);
            }
        }

        var scale = Math.Clamp((long)exponent - fractionDigits, -ExponentLimit, ExponentLimit);
        return Decimal32Finalizer.Finalize(negative, coefficient, (int)scale, Decimal32Residue.Exact, rounding, ref status);
    }

    /// <summary>
    /// Gathers a run of digits into the coefficient, four at a time while four remain and
    /// the word has room for them, then one at a time. Past nineteen the digits are only
    /// counted, so the caller can see the run outgrew the word.
    /// </summary>
    private static int ReadDigits(ref char start, int index, int length, ref ulong coefficient, ref int digits)
    {
        while (index + 4 <= length && digits <= WordDigits - 4)
        {
            // Four characters as one word of four 16-bit lanes. Subtracting '0' from every
            // lane leaves each digit's value, and a lane that was not a digit -- or that
            // borrowed from a lower lane that was not -- shows a high nibble.
            var word = Unsafe.ReadUnaligned<ulong>(ref Unsafe.As<char, byte>(ref Unsafe.Add(ref start, index)));
            var lanes = word - 0x0030003000300030UL;
            if (((lanes | (lanes + 0x0006000600060006UL)) & 0xFFF0FFF0FFF0FFF0UL) != 0)
            {
                break;
            }

            // Fold the lanes: each even lane takes ten times itself plus the lane above it,
            // and the two results combine as hundreds and units.
            var pairs = ((lanes * 10) + (lanes >> 16)) & 0x0000FFFF0000FFFFUL;
            var value = ((uint)pairs * 100) + (uint)(pairs >> 32);

            coefficient = (coefficient * 10000) + value;
            digits += 4;
            index += 4;
        }

        while (index < length)
        {
            var digit = (uint)(Unsafe.Add(ref start, index) - '0');
            if (digit > 9)
            {
                break;
            }

            if (digits < WordDigits)
            {
                coefficient = (coefficient * 10) + digit;
            }

            digits++;
            index++;
        }

        return index;
    }

    /// <summary>
    /// Reads the exponent after the E through to the end of the text: an optional sign and
    /// at least one digit, clamped rather than overflowed.
    /// </summary>
    private static bool TryReadExponent(ref char start, ref int index, int length, out int exponent)
    {
        exponent = 0;
        if (index == length)
        {
            return false;
        }

        var negative = false;
        var first = Unsafe.Add(ref start, index);
        if (first == '-' || first == '+')
        {
            negative = first == '-';
            index++;
            if (index == length)
            {
                return false;
            }
        }

        var value = 0L;
        while (index < length)
        {
            var digit = (uint)(Unsafe.Add(ref start, index) - '0');
            if (digit > 9)
            {
                return false;
            }

            if (value <= ExponentLimit)
            {
                value = (value * 10) + digit;
            }

            index++;
        }

        exponent = (int)Math.Clamp(negative ? -value : value, -ExponentLimit, ExponentLimit);
        return true;
    }

    private static uint ParseSpecial(ReadOnlySpan<char> text, bool negative, ref Decimal32Status status)
    {
        if (Matches(text, "inf") || Matches(text, "infinity"))
        {
            return Decimal32Encoding.Infinity(negative);
        }

        if (StartsWith(text, "nan"))
        {
            return ParseNaN(text[3..], negative, false, ref status);
        }

        if (StartsWith(text, "snan"))
        {
            return ParseNaN(text[4..], negative, true, ref status);
        }

        return Malformed(ref status);
    }

    /// <summary>
    /// The general path, for text with more digit characters than a word gathers. Leading
    /// zeros carry no information beyond their position, and digits past the nineteenth
    /// significant one cannot change the result beyond making it inexact, so they are
    /// folded into a sticky residue rather than accumulated.
    /// </summary>
    private static uint ParseLong(ReadOnlySpan<char> text, bool negative, Decimal32Rounding rounding,
        ref Decimal32Status status)
    {
        var coefficient = 0UL;
        var significantCount = 0;
        var fractionDigits = 0;
        var sawDot = false;
        var sawDigit = false;
        var position = 0;

        var droppedNonZero = false;
        var droppedCount = 0;

        for (; position < text.Length; position++)
        {
            var character = text[position];
            if (character == '.')
            {
                if (sawDot)
                {
                    return Malformed(ref status);
                }

                sawDot = true;
                continue;
            }

            var digit = (uint)(character - '0');
            if (digit > 9)
            {
                break;
            }

            sawDigit = true;
            if (sawDot)
            {
                fractionDigits++;
            }

            if (significantCount == 0 && digit == 0)
            {
                continue;
            }

            if (significantCount < WordDigits)
            {
                coefficient = (coefficient * 10) + digit;
                significantCount++;
            }
            else
            {
                droppedCount++;
                droppedNonZero |= digit != 0;
            }
        }

        if (!sawDigit)
        {
            return Malformed(ref status);
        }

        var exponent = 0;
        if (position < text.Length)
        {
            if ((text[position] | 0x20) != 'e')
            {
                return Malformed(ref status);
            }

            position++;
            ref var start = ref MemoryMarshal.GetReference(text);
            if (!TryReadExponent(ref start, ref position, text.Length, out exponent))
            {
                return Malformed(ref status);
            }
        }

        // Digits folded into the sticky flag were dropped from the coefficient, so the
        // exponent has to account for them.
        var residue = droppedNonZero ? Decimal32Residue.BelowHalf : Decimal32Residue.Exact;
        var scale = Math.Clamp((long)exponent - fractionDigits + droppedCount, -ExponentLimit, ExponentLimit);

        return Decimal32Finalizer.Finalize(negative, coefficient, (int)scale, residue, rounding, ref status);
    }

    private static uint ParseNaN(ReadOnlySpan<char> text, bool negative, bool signaling, ref Decimal32Status status)
    {
        var payload = 0u;
        var significantCount = 0;

        foreach (var character in text)
        {
            var digit = (uint)(character - '0');
            if (digit > 9)
            {
                return Malformed(ref status);
            }

            if (significantCount == 0 && digit == 0)
            {
                continue;
            }

            significantCount++;
            if (significantCount > Decimal32Encoding.Precision - 1)
            {
                return Malformed(ref status);
            }

            payload = (payload * 10) + digit;
        }

        return Decimal32Encoding.NaN(negative, signaling, payload);
    }

    private static uint Malformed(ref Decimal32Status status)
    {
        status |= Decimal32Status.ConversionSyntax;
        return Decimal32Encoding.QuietNaN();
    }

    private static bool Matches(ReadOnlySpan<char> text, string word)
    {
        return text.Equals(word, StringComparison.OrdinalIgnoreCase);
    }

    private static bool StartsWith(ReadOnlySpan<char> text, string word)
    {
        return text.StartsWith(word, StringComparison.OrdinalIgnoreCase);
    }
}
