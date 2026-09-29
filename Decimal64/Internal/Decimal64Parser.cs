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
/// The syntax is stricter than .NET's numeric parsing: no leading or trailing spaces, no
/// group separators, and no culture-specific decimal point. A leading or trailing decimal
/// point is allowed (<c>.5</c> and <c>5.</c> both parse), but two points are not. An
/// infinity is <c>inf</c> or <c>infinity</c>, in any case. A NaN is <c>nan</c> or
/// <c>snan</c>, optionally followed by a payload of digits with at most 15 significant
/// digits.
/// </para>
/// <para>
/// A number of up to 19 digits takes the fast path. Its digits are read four at a time
/// from a word of characters, then one at a time, and the value goes directly to the
/// finalizer. Longer text takes the general path. It keeps 19 significant digits and folds
/// the rest into a sticky residue. The residue only affects a rounding that discards at
/// least 3 of the kept digits.
/// </para>
/// </remarks>
[SkipLocalsInit]
internal static class Decimal64Parser
{
    /// <summary>
    /// No value can reach an exponent beyond this, so parsing clamps the exponent to it
    /// instead of letting the arithmetic overflow.
    /// </summary>
    private const int ExponentLimit = 1000000000;

    /// <summary>
    /// Text up to this length is converted or rewritten in a stack buffer. Longer text uses
    /// a rented array. The limit covers every number the format can represent, so only
    /// unusual input needs a rented array.
    /// </summary>
    private const int StackBufferLength = 128;

    /// <summary>The number of digits one word can hold without overflow.</summary>
    private const int WordDigits = Decimal64Tables.MaxPower;

    /// <summary>
    /// Parses text in a culture's format. The text is rewritten into the specification's
    /// syntax and then parsed, so one parser computes every value.
    /// </summary>
    /// <param name="text">The culture-formatted text.</param>
    /// <param name="styles">The styles allowed in the text.</param>
    /// <param name="provider">The culture's symbols, or null for the current culture.</param>
    /// <param name="rounding">The rounding mode for a value with too many digits.</param>
    /// <param name="status">Receives ConversionSyntax if the text is invalid, and the conditions the rounding raises.</param>
    /// <returns>The encoded value, or a quiet NaN if the text is invalid.</returns>
    public static ulong Parse(ReadOnlySpan<char> text, NumberStyles styles, IFormatProvider? provider,
        Decimal64Rounding rounding, ref Decimal64Status status)
    {
        var numberFormat = NumberFormatInfo.GetInstance(provider);
        var length = Math.Max(text.Length + 1, Decimal64CultureNormalizer.MinimumBufferLength);

        char[]? rented = null;
        Span<char> buffer = length <= StackBufferLength
            ? stackalloc char[StackBufferLength]
            : (rented = ArrayPool<char>.Shared.Rent(length));

        try
        {
            if (Decimal64CultureNormalizer.Normalize(text, styles, numberFormat, buffer) is not { } written)
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
    /// Parses UTF-8 text in a culture's format. A culture's symbols can be non-ASCII (most
    /// cultures use <c>∞</c> for infinity), so this decodes the text instead of narrowing
    /// each byte.
    /// </summary>
    /// <param name="utf8Text">The culture-formatted UTF-8 text.</param>
    /// <param name="styles">The styles allowed in the text.</param>
    /// <param name="provider">The culture's symbols, or null for the current culture.</param>
    /// <param name="rounding">The rounding mode for a value with too many digits.</param>
    /// <param name="status">Receives ConversionSyntax if the text is invalid, and the conditions the rounding raises.</param>
    /// <returns>The encoded value, or a quiet NaN if the text is invalid.</returns>
    public static ulong Parse(ReadOnlySpan<byte> utf8Text, NumberStyles styles, IFormatProvider? provider,
        Decimal64Rounding rounding, ref Decimal64Status status)
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
    /// Parses UTF-8 text in the specification's syntax. The syntax is all ASCII, so text
    /// with any byte above 0x7F is rejected without being decoded.
    /// </summary>
    /// <param name="utf8Text">The UTF-8 text.</param>
    /// <param name="rounding">The rounding mode for a value with too many digits.</param>
    /// <param name="status">Receives ConversionSyntax if the text is invalid, and the conditions the rounding raises.</param>
    /// <returns>The encoded value, or a quiet NaN if the text is invalid.</returns>
    public static ulong Parse(ReadOnlySpan<byte> utf8Text, Decimal64Rounding rounding, ref Decimal64Status status)
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

    /// <summary>Parses text in the specification's syntax: the to-number conversion.</summary>
    /// <param name="text">The text.</param>
    /// <param name="rounding">The rounding mode for a value with too many digits.</param>
    /// <param name="status">Receives ConversionSyntax if the text is invalid, and the conditions the rounding raises.</param>
    /// <returns>The encoded value, or a quiet NaN if the text is invalid.</returns>
    public static ulong Parse(ReadOnlySpan<char> text, Decimal64Rounding rounding, ref Decimal64Status status)
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

        // A finite number starts with a digit or a point, and every special value starts
        // with a letter, so one comparison chooses the path.
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
            // There are more digits than one word holds, counting leading zeros. The
            // general path counts only significant digits and folds the rest into a residue.
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
            if (!ReadExponent(ref start, ref index, length, out exponent))
            {
                return Malformed(ref status);
            }
        }

        var scale = Math.Clamp((long)exponent - fractionDigits, -ExponentLimit, ExponentLimit);
        return Decimal64Finalizer.Finalize(negative, coefficient, (int)scale, Decimal64Residue.Exact, rounding, ref status);
    }

    /// <summary>
    /// Reads a run of digits into the coefficient: four at a time while four remain and the
    /// word has room, then one at a time. Digits after the 19th are only counted, so the
    /// caller can see that the run did not fit in a word.
    /// </summary>
    private static int ReadDigits(ref char start, int index, int length, ref ulong coefficient, ref int digits)
    {
        while (index + 4 <= length && digits <= WordDigits - 4)
        {
            // Read four characters as one word of four 16-bit lanes, and subtract '0' from
            // each lane, which leaves each digit's value. A lane that was not a digit, or
            // that took a borrow from a lower lane that was not a digit, has a bit set above
            // its low 4 bits, either directly or after 6 is added.
            var word = Unsafe.ReadUnaligned<ulong>(ref Unsafe.As<char, byte>(ref Unsafe.Add(ref start, index)));
            var lanes = word - 0x0030003000300030UL;
            if (((lanes | (lanes + 0x0006000600060006UL)) & 0xFFF0FFF0FFF0FFF0UL) != 0)
            {
                break;
            }

            // Each even lane becomes 10 times itself plus the next lane. The two results are
            // then combined as hundreds and units.
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
    /// Reads the exponent after the E, up to the end of the text: an optional sign and at
    /// least one digit. A large exponent is clamped instead of overflowing.
    /// </summary>
    private static bool ReadExponent(ref char start, ref int index, int length, out int exponent)
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

    private static ulong ParseSpecial(ReadOnlySpan<char> text, bool negative, ref Decimal64Status status)
    {
        if (Matches(text, "inf") || Matches(text, "infinity"))
        {
            return Decimal64Encoding.Infinity(negative);
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
    /// The general path, for text with more digits than one word holds. Leading zeros only
    /// affect the position. Digits after the 19th significant digit can only make the result
    /// inexact, so they are folded into a sticky residue instead of added to the coefficient.
    /// </summary>
    private static ulong ParseLong(ReadOnlySpan<char> text, bool negative, Decimal64Rounding rounding,
        ref Decimal64Status status)
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
            if (!ReadExponent(ref start, ref position, text.Length, out exponent))
            {
                return Malformed(ref status);
            }
        }

        // The digits folded into the residue are not in the coefficient, so the exponent is
        // increased by their count.
        var residue = droppedNonZero ? Decimal64Residue.BelowHalf : Decimal64Residue.Exact;
        var scale = Math.Clamp((long)exponent - fractionDigits + droppedCount, -ExponentLimit, ExponentLimit);

        return Decimal64Finalizer.Finalize(negative, coefficient, (int)scale, residue, rounding, ref status);
    }

    private static ulong ParseNaN(ReadOnlySpan<char> text, bool negative, bool signaling, ref Decimal64Status status)
    {
        var payload = 0UL;
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
            if (significantCount > Decimal64Encoding.Precision - 1)
            {
                return Malformed(ref status);
            }

            payload = (payload * 10) + digit;
        }

        return Decimal64Encoding.NaN(negative, signaling, payload);
    }

    private static ulong Malformed(ref Decimal64Status status)
    {
        status |= Decimal64Status.ConversionSyntax;
        return Decimal64Encoding.QuietNaN();
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
