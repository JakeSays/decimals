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
/// payload of digits, whose significant length may not exceed thirty-three.
/// </para>
/// <para>
/// A number of up to thirty-eight digits is read on one path: the digits are gathered into
/// two words by <see cref="Decimal128DigitRun"/>, and the value goes straight to the
/// finalizer. Anything longer takes the general path, which gathers thirty-eight significant
/// digits and folds everything past them into a sticky residue -- which can only tip a
/// rounding that discards at least four digits above it.
/// </para>
/// </remarks>
[SkipLocalsInit]
internal static class Decimal128Parser
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

    /// <summary>
    /// The conversion a culture asks for: the text is rewritten into the specification's
    /// grammar and then read by it, so one parser still computes every value.
    /// </summary>
    public static Decimal128Integer Parse(ReadOnlySpan<char> text, NumberStyles styles, IFormatProvider? provider,
        Decimal128Rounding rounding, ref Decimal128Status status)
    {
        var numberFormat = NumberFormatInfo.GetInstance(provider);
        var length = Math.Max(text.Length + 1, Decimal128CultureNormalizer.MinimumBufferLength);

        char[]? rented = null;
        Span<char> buffer = length <= StackBufferLength
            ? stackalloc char[StackBufferLength]
            : (rented = ArrayPool<char>.Shared.Rent(length));

        try
        {
            if (!Decimal128CultureNormalizer.TryNormalize(text, styles, numberFormat, buffer, out var written))
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
    public static Decimal128Integer Parse(ReadOnlySpan<byte> utf8Text, NumberStyles styles, IFormatProvider? provider,
        Decimal128Rounding rounding, ref Decimal128Status status)
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
    public static Decimal128Integer Parse(ReadOnlySpan<byte> utf8Text, Decimal128Rounding rounding,
        ref Decimal128Status status)
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

    public static Decimal128Integer Parse(ReadOnlySpan<char> text, Decimal128Rounding rounding,
        ref Decimal128Status status)
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

        var run = default(Decimal128DigitRun);
        index = run.Read(ref start, index, length);

        var fractionDigits = 0;
        if (index < length && Unsafe.Add(ref start, index) == '.')
        {
            index++;
            var pointIndex = index;
            index = run.Read(ref start, index, length);
            fractionDigits = index - pointIndex;
        }

        if (run.Digits > Decimal128DigitRun.Capacity)
        {
            // More digits than two words gather, leading zeros included: the general path
            // counts significant digits and folds the rest into a residue.
            return ParseLong(text[(negative || start == '+' ? 1 : 0)..], negative, rounding, ref status);
        }

        if (run.Digits == 0)
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
        return Decimal128Finalizer.Finalize(negative, run.ToCoefficient(), (int)scale, Decimal128Residue.Exact,
            rounding, ref status);
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

    private static Decimal128Integer ParseSpecial(ReadOnlySpan<char> text, bool negative, ref Decimal128Status status)
    {
        if (Matches(text, "inf") || Matches(text, "infinity"))
        {
            return Decimal128Encoding.Infinity(negative);
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
    /// The general path, for text with more digit characters than two words gather. Leading
    /// zeros carry no information beyond their position, and digits past the thirty-eighth
    /// significant one cannot change the result beyond making it inexact, so they are folded
    /// into a sticky residue rather than accumulated.
    /// </summary>
    private static Decimal128Integer ParseLong(ReadOnlySpan<char> text, bool negative, Decimal128Rounding rounding,
        ref Decimal128Status status)
    {
        var run = default(Decimal128DigitRun);
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

            if (run.Digits == 0 && digit == 0)
            {
                continue;
            }

            if (run.Digits < Decimal128DigitRun.Capacity)
            {
                Gather(ref run, digit);
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
        var residue = droppedNonZero ? Decimal128Residue.BelowHalf : Decimal128Residue.Exact;
        var scale = Math.Clamp((long)exponent - fractionDigits + droppedCount, -ExponentLimit, ExponentLimit);

        return Decimal128Finalizer.Finalize(negative, run.ToCoefficient(), (int)scale, residue, rounding, ref status);
    }

    /// <summary>One significant digit into the run, on the general path's own count.</summary>
    private static void Gather(ref Decimal128DigitRun run, uint digit)
    {
        if (run.Digits < Decimal128DigitRun.WordDigits)
        {
            run.High = (run.High * 10) + digit;
        }
        else
        {
            run.Low = (run.Low * 10) + digit;
            run.LowDigits++;
        }

        run.Digits++;
    }

    private static Decimal128Integer ParseNaN(ReadOnlySpan<char> text, bool negative, bool signaling,
        ref Decimal128Status status)
    {
        var payload = Decimal128Integer.Zero;
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
            if (significantCount > Decimal128Encoding.Precision - 1)
            {
                return Malformed(ref status);
            }

            payload = payload.MultiplyBy(10) + digit;
        }

        return Decimal128Encoding.NaN(negative, signaling, payload);
    }

    private static Decimal128Integer Malformed(ref Decimal128Status status)
    {
        status |= Decimal128Status.ConversionSyntax;
        return Decimal128Encoding.QuietNaN();
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
