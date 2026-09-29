// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace Decimals.Internal;

/// <summary>
/// Converts culture-formatted text into the syntax that the specification's
/// <c>to-number</c> operation reads, so one parser computes every value.
/// </summary>
/// <remarks>
/// The scan handles whitespace, signs, separators, and the currency symbol. It copies
/// digits, the exponent, and ASCII letters to the parser, which decides whether they are
/// valid. This keeps the numeric rules (which digits are significant, how the coefficient
/// rounds, what the exponent is) in one place. The specification's own spellings are
/// copied too, so <c>Infinity</c>, <c>sNaN</c>, and a NaN payload parse under any culture.
/// </remarks>
internal static class Decimal64CultureNormalizer
{
    /// <summary>
    /// The output is never longer than the input plus a sign, except for the infinity and
    /// NaN symbols, which are written in full. The destination must be at least this long.
    /// </summary>
    public const int MinimumBufferLength = 16;

    /// <summary>The styles .NET uses to parse a floating-point type when none are given.</summary>
    public const NumberStyles DefaultStyles = NumberStyles.Float | NumberStyles.AllowThousands;

    /// <summary>
    /// The styles that apply to a decimal format. The hex and binary specifiers are
    /// excluded, as they are for the built-in floating-point types.
    /// </summary>
    private const NumberStyles SupportedStyles =
        NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite
        | NumberStyles.AllowLeadingSign | NumberStyles.AllowTrailingSign
        | NumberStyles.AllowParentheses | NumberStyles.AllowDecimalPoint
        | NumberStyles.AllowThousands | NumberStyles.AllowExponent
        | NumberStyles.AllowCurrencySymbol;

    /// <summary>
    /// Rejects styles that a decimal format cannot support, before any text is read. It
    /// throws instead of returning a failed parse, because a bad style is the caller's
    /// error, not the input's.
    /// </summary>
    /// <param name="styles">The styles to check.</param>
    /// <param name="parameterName">The caller's parameter name, reported in the exception.</param>
    /// <exception cref="ArgumentException"><paramref name="styles"/> includes a style a decimal format does not support.</exception>
    public static void ValidateStyles(NumberStyles styles, string parameterName)
    {
        if ((styles & ~SupportedStyles) != 0)
        {
            throw new ArgumentException(
                $"'{styles}' includes a style a decimal format cannot parse.", parameterName);
        }
    }

    /// <summary>
    /// Rewrites culture-formatted text into the specification's syntax. The result is only
    /// rewritten, not validated: the parser decides whether it is a number.
    /// </summary>
    /// <param name="text">The culture-formatted text.</param>
    /// <param name="styles">The styles allowed in the text.</param>
    /// <param name="numberFormat">The culture's symbols.</param>
    /// <param name="destination">
    /// Receives the rewritten text. It must hold at least <see cref="MinimumBufferLength"/>
    /// characters and one more character than <paramref name="text"/>.
    /// </param>
    /// <returns>
    /// The number of characters written, or null if the destination is too short or the
    /// text uses a symbol the styles do not allow.
    /// </returns>
    public static int? Normalize(ReadOnlySpan<char> text, NumberStyles styles, NumberFormatInfo numberFormat,
        Span<char> destination)
    {
        if (destination.Length < MinimumBufferLength || destination.Length < text.Length + 1)
        {
            return null;
        }

        var body = text;
        if ((styles & NumberStyles.AllowLeadingWhite) != 0)
        {
            body = body.TrimStart();
        }

        if ((styles & NumberStyles.AllowTrailingWhite) != 0)
        {
            body = body.TrimEnd();
        }

        if (WriteCultureSymbol(body, numberFormat, destination) is { } symbolLength)
        {
            return symbolLength;
        }

        var isNegative = false;
        var hasSign = false;

        if ((styles & NumberStyles.AllowParentheses) != 0 && body.Length >= 2
            && body[0] == '(' && body[^1] == ')')
        {
            isNegative = true;
            hasSign = true;
            body = body[1..^1].Trim();
        }

        // A currency symbol can come before or after the sign, so both orders are accepted:
        // "-$5" and "$-5" both parse.
        TakeCurrencySymbol(ref body, styles, numberFormat);
        TakeSign(ref body, styles, numberFormat, ref isNegative, ref hasSign);
        TakeCurrencySymbol(ref body, styles, numberFormat);

        if (body.IsEmpty)
        {
            return null;
        }

        var decimalSeparator = (styles & NumberStyles.AllowCurrencySymbol) != 0
            ? numberFormat.CurrencyDecimalSeparator
            : numberFormat.NumberDecimalSeparator;
        var groupSeparator = (styles & NumberStyles.AllowCurrencySymbol) != 0
            ? numberFormat.CurrencyGroupSeparator
            : numberFormat.NumberGroupSeparator;

        var index = 0;
        if (isNegative)
        {
            destination[index++] = '-';
        }

        var sawDigit = false;
        var sawDecimalSeparator = false;
        var sawExponent = false;

        var position = 0;
        while (position < body.Length)
        {
            var rest = body[position..];

            if (StartsWith(rest, decimalSeparator))
            {
                if ((styles & NumberStyles.AllowDecimalPoint) == 0)
                {
                    return null;
                }

                sawDecimalSeparator = true;
                destination[index++] = '.';
                position += decimalSeparator.Length;
                continue;
            }

            if (StartsWith(rest, groupSeparator))
            {
                // A group separator belongs to the integer part and must follow a digit.
                // Otherwise German ".5", where the period is the group separator, would
                // parse as 5. The character after the separator is not checked, which
                // matches the built-in types: "1,,5" and "5," both parse.
                if ((styles & NumberStyles.AllowThousands) == 0
                    || sawDecimalSeparator || sawExponent || !sawDigit)
                {
                    return null;
                }

                position += groupSeparator.Length;
                continue;
            }

            var character = body[position];
            if (character is >= '0' and <= '9')
            {
                sawDigit = true;
                destination[index++] = character;
                position++;
                continue;
            }

            if (character is 'e' or 'E')
            {
                if ((styles & NumberStyles.AllowExponent) == 0)
                {
                    return null;
                }

                sawExponent = true;
                destination[index++] = 'E';
                position++;

                // The exponent uses the culture's sign. The specification uses an ASCII sign.
                var exponent = body[position..];
                if (StartsWith(exponent, numberFormat.NegativeSign))
                {
                    destination[index++] = '-';
                    position += numberFormat.NegativeSign.Length;
                }
                else if (StartsWith(exponent, numberFormat.PositiveSign))
                {
                    destination[index++] = '+';
                    position += numberFormat.PositiveSign.Length;
                }

                continue;
            }

            // The only valid text left here is the specification's own words: Infinity,
            // inf, NaN, and sNaN. No culture has its own spelling for them. Any other
            // character is rejected instead of copied, because the parser has its own
            // rules: it always accepts a leading sign, and it treats a period as the
            // decimal point. Neither is allowed here. A sign the styles allow was already
            // removed from the ends, and a period means nothing in a culture that does not
            // use one.
            if (!char.IsAsciiLetter(character))
            {
                return null;
            }

            destination[index++] = character;
            position++;
        }

        return index;
    }

    /// <summary>
    /// Writes the three symbols that a culture defines: NaN, positive infinity, and
    /// negative infinity. Everything else, including the specification's own words, a
    /// signaling NaN, and a NaN payload, goes through the scan. The result is the number
    /// of characters written, or null if the text is none of the three symbols.
    /// </summary>
    private static int? WriteCultureSymbol(ReadOnlySpan<char> text, NumberFormatInfo numberFormat,
        Span<char> destination)
    {
        if (Matches(text, numberFormat.NaNSymbol))
        {
            return Write("NaN", destination);
        }

        if (Matches(text, numberFormat.PositiveInfinitySymbol))
        {
            return Write("Infinity", destination);
        }

        if (Matches(text, numberFormat.NegativeInfinitySymbol))
        {
            return Write("-Infinity", destination);
        }

        return null;
    }

    private static void TakeSign(ref ReadOnlySpan<char> body, NumberStyles styles,
        NumberFormatInfo numberFormat, ref bool isNegative, ref bool hasSign)
    {
        if (hasSign)
        {
            return;
        }

        if ((styles & NumberStyles.AllowLeadingSign) != 0)
        {
            if (StartsWith(body, numberFormat.NegativeSign))
            {
                isNegative = true;
                hasSign = true;
                body = body[numberFormat.NegativeSign.Length..];
                return;
            }

            if (StartsWith(body, numberFormat.PositiveSign))
            {
                hasSign = true;
                body = body[numberFormat.PositiveSign.Length..];
                return;
            }
        }

        if ((styles & NumberStyles.AllowTrailingSign) != 0)
        {
            if (EndsWith(body, numberFormat.NegativeSign))
            {
                isNegative = true;
                hasSign = true;
                body = body[..^numberFormat.NegativeSign.Length];
                return;
            }

            if (EndsWith(body, numberFormat.PositiveSign))
            {
                hasSign = true;
                body = body[..^numberFormat.PositiveSign.Length];
            }
        }
    }

    private static void TakeCurrencySymbol(ref ReadOnlySpan<char> body, NumberStyles styles,
        NumberFormatInfo numberFormat)
    {
        if ((styles & NumberStyles.AllowCurrencySymbol) == 0)
        {
            return;
        }

        var symbol = numberFormat.CurrencySymbol;
        if (StartsWith(body, symbol))
        {
            body = body[symbol.Length..].TrimStart();
            return;
        }

        if (EndsWith(body, symbol))
        {
            body = body[..^symbol.Length].TrimEnd();
        }
    }

    private static int Write(ReadOnlySpan<char> text, Span<char> destination)
    {
        text.CopyTo(destination);
        return text.Length;
    }

    // An empty separator or symbol matches nothing. A culture can leave one empty, and
    // matching it would consume no input, so the scan would never end.

    private static bool StartsWith(ReadOnlySpan<char> text, string word) =>
        word.Length > 0 && text.StartsWith(word, StringComparison.Ordinal);

    private static bool EndsWith(ReadOnlySpan<char> text, string word) =>
        word.Length > 0 && text.EndsWith(word, StringComparison.Ordinal);

    private static bool Matches(ReadOnlySpan<char> text, string word) =>
        word.Length > 0 && text.Equals(word, StringComparison.OrdinalIgnoreCase);
}
