// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Buffers;
using System.Globalization;
using System.Text.Unicode;

namespace Decimals;

/// <summary>
/// The specification's <c>to-number</c> conversion.
/// </summary>
/// <remarks>
/// Syntax is stricter than .NET's numeric parsing: no leading or trailing space, no group
/// separators, no culture-specific decimal point. A leading or trailing decimal point is
/// allowed (<c>.5</c> and <c>5.</c> both parse), two are not. Infinities are <c>inf</c> or
/// <c>infinity</c> in any casing; NaNs are <c>nan</c> or <c>snan</c> with an optional
/// payload of digits, whose significant length may not exceed one less than the precision.
/// </remarks>
internal static class DecimalParser
{
    /// <summary>
    /// An exponent past this cannot be reached by any format, so parsing clamps to it
    /// rather than overflowing the arithmetic that computes it.
    /// </summary>
    private const int ExponentLimit = 1000000000;

    /// <summary>
    /// UTF-8 text up to this length is widened on the stack; anything longer is rented. The
    /// limit covers every number a format can hold, so renting is for pathological input.
    /// </summary>
    private const int StackWidenLimit = 128;

    /// <summary>
    /// The conversion a culture asks for: the text is rewritten into the specification's
    /// grammar and then read by it, so one parser still computes every value.
    /// </summary>
    public static UnpackedDecimal<UInt128> Parse<TFormat>(ReadOnlySpan<char> text,
        NumberStyles styles, IFormatProvider? provider, DecimalRounding rounding,
        ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        var numberFormat = NumberFormatInfo.GetInstance(provider);
        var length = Math.Max(text.Length + 1, CultureNormalizer.MinimumBufferLength);

        char[]? rented = null;
        Span<char> buffer = length <= StackWidenLimit
            ? stackalloc char[StackWidenLimit]
            : (rented = ArrayPool<char>.Shared.Rent(length));

        try
        {
            if (!CultureNormalizer.TryNormalize(text, styles, numberFormat, buffer, out var written))
            {
                return Malformed(ref status);
            }

            return Parse<TFormat>(buffer[..written], rounding, ref status);
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
    public static UnpackedDecimal<UInt128> Parse<TFormat>(ReadOnlySpan<byte> utf8Text,
        NumberStyles styles, IFormatProvider? provider, DecimalRounding rounding,
        ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        // Decoding never produces more UTF-16 units than there were bytes.
        char[]? rented = null;
        Span<char> buffer = utf8Text.Length <= StackWidenLimit
            ? stackalloc char[StackWidenLimit]
            : (rented = ArrayPool<char>.Shared.Rent(utf8Text.Length));

        try
        {
            if (Utf8.ToUtf16(utf8Text, buffer, out _, out var decoded, replaceInvalidSequences: false)
                != OperationStatus.Done)
            {
                return Malformed(ref status);
            }

            return Parse<TFormat>(buffer[..decoded], styles, provider, rounding, ref status);
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
    /// The same conversion over UTF-8 text. Every character the grammar accepts is ASCII,
    /// so a byte outside that range cannot appear in a number and the text is rejected
    /// without being decoded.
    /// </summary>
    public static UnpackedDecimal<UInt128> Parse<TFormat>(ReadOnlySpan<byte> utf8Text,
        DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        char[]? rented = null;
        Span<char> buffer = utf8Text.Length <= StackWidenLimit
            ? stackalloc char[StackWidenLimit]
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

            return Parse<TFormat>(buffer[..utf8Text.Length], rounding, ref status);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }

    public static UnpackedDecimal<UInt128> Parse<TFormat>(ReadOnlySpan<char> text,
        DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        var position = 0;
        var isNegative = false;
        if (position < text.Length && (text[position] == '+' || text[position] == '-'))
        {
            isNegative = text[position] == '-';
            position++;
        }

        var remainder = text[position..];

        if (Matches(remainder, "inf") || Matches(remainder, "infinity"))
        {
            return new UnpackedDecimal<UInt128>(DecimalKind.Infinity, isNegative, 0, UInt128.Zero);
        }

        if (StartsWith(remainder, "nan"))
        {
            return ParseNaN<TFormat>(remainder[3..], isNegative, DecimalKind.QuietNaN, ref status);
        }

        if (StartsWith(remainder, "snan"))
        {
            return ParseNaN<TFormat>(remainder[4..], isNegative, DecimalKind.SignalingNaN, ref status);
        }

        return ParseFinite<TFormat>(remainder, isNegative, rounding, ref status);
    }

    private static UnpackedDecimal<UInt128> ParseFinite<TFormat>(ReadOnlySpan<char> text,
        bool isNegative, DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        var coefficient = UInt128.Zero;
        var digitCount = 0;
        var significantCount = 0;
        var fractionDigits = 0;
        var sawDot = false;
        var sawDigit = false;
        var position = 0;

        // Digits past what any format can hold cannot change the result beyond making it
        // inexact, so they are folded into a sticky flag rather than accumulated.
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

            if (character is < '0' or > '9')
            {
                break;
            }

            sawDigit = true;
            digitCount++;
            if (sawDot)
            {
                fractionDigits++;
            }

            var digit = (uint)(character - '0');
            if (significantCount == 0 && digit == 0)
            {
                // Leading zeros carry no information beyond their position.
                continue;
            }

            if (significantCount < TFormat.Precision + 2)
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
        if (position < text.Length && (text[position] == 'e' || text[position] == 'E'))
        {
            position++;
            if (!TryParseExponent(text[position..], out exponent))
            {
                return Malformed(ref status);
            }

            position = text.Length;
        }

        if (position != text.Length)
        {
            return Malformed(ref status);
        }

        // Digits folded into the sticky flag were dropped from the coefficient, so the
        // exponent has to account for them; a non-zero one among them makes the value
        // inexact no matter which way it later rounds.
        exponent -= fractionDigits;
        exponent += droppedCount;
        if (droppedNonZero)
        {
            // A sticky digit below everything the coefficient holds; adding one keeps the
            // value strictly between the representable neighbours so rounding sees it.
            coefficient = (coefficient * 10) + 1;
            exponent--;
        }
        else if (droppedCount > 0)
        {
            coefficient *= 10;
            exponent--;
        }

        exponent = Math.Clamp(exponent, -ExponentLimit, ExponentLimit);
        return DecimalFinalizer.Finalize<TFormat>(isNegative, coefficient, exponent, rounding, ref status);
    }

    private static UnpackedDecimal<UInt128> ParseNaN<TFormat>(ReadOnlySpan<char> text,
        bool isNegative, DecimalKind kind, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        var payload = UInt128.Zero;
        var significantCount = 0;

        foreach (var character in text)
        {
            if (character is < '0' or > '9')
            {
                return Malformed(ref status);
            }

            var digit = (uint)(character - '0');
            if (significantCount == 0 && digit == 0)
            {
                continue;
            }

            significantCount++;
            if (significantCount > TFormat.Precision - 1)
            {
                return Malformed(ref status);
            }

            payload = (payload * 10) + digit;
        }

        return new UnpackedDecimal<UInt128>(kind, isNegative, 0, payload);
    }

    private static bool TryParseExponent(ReadOnlySpan<char> text, out int exponent)
    {
        exponent = 0;
        var position = 0;
        var isNegative = false;
        if (position < text.Length && (text[position] == '+' || text[position] == '-'))
        {
            isNegative = text[position] == '-';
            position++;
        }

        if (position == text.Length)
        {
            return false;
        }

        var value = 0L;
        for (; position < text.Length; position++)
        {
            if (text[position] is < '0' or > '9')
            {
                return false;
            }

            if (value <= ExponentLimit)
            {
                value = (value * 10) + (text[position] - '0');
            }
        }

        exponent = (int)Math.Clamp(isNegative ? -value : value, -ExponentLimit, ExponentLimit);
        return true;
    }

    private static UnpackedDecimal<UInt128> Malformed(ref DecimalStatus status)
    {
        status |= DecimalStatus.ConversionSyntax;
        return new UnpackedDecimal<UInt128>(DecimalKind.QuietNaN, false, 0, UInt128.Zero);
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
