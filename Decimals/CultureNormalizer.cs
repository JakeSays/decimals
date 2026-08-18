// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace Decimals;

/// <summary>
/// Rewrites culture-formatted text into the grammar the specification's <c>to-number</c>
/// reads, so that one parser computes every value.
/// </summary>
/// <remarks>
/// The scan knows about whitespace, signs, separators, and the currency symbol. Everything
/// else it copies through for the parser to judge, which keeps the numeric semantics -- what
/// counts as significant, how the coefficient rounds, where the exponent lands -- in the one
/// place they are written. The specification's own spellings survive that copy, so
/// <c>Infinity</c>, <c>sNaN</c>, and a NaN payload still parse under any culture.
/// </remarks>
internal static class CultureNormalizer
{
    /// <summary>
    /// The output is never longer than the input plus a sign, except for the infinity and
    /// NaN symbols, which are written out in full. No destination shorter than this.
    /// </summary>
    public const int MinimumBufferLength = 16;

    /// <summary>The styles .NET reads a floating-point type with when none are named.</summary>
    public const NumberStyles DefaultStyles = NumberStyles.Float | NumberStyles.AllowThousands;

    /// <summary>
    /// The styles that mean something for a decimal format. The two integer specifiers are
    /// not among them, which is what the built-in floating-point types also refuse.
    /// </summary>
    private const NumberStyles SupportedStyles =
        NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite
        | NumberStyles.AllowLeadingSign | NumberStyles.AllowTrailingSign
        | NumberStyles.AllowParentheses | NumberStyles.AllowDecimalPoint
        | NumberStyles.AllowThousands | NumberStyles.AllowExponent
        | NumberStyles.AllowCurrencySymbol;

    /// <summary>
    /// Rejects styles a decimal format cannot honor, before any text is looked at. This
    /// throws rather than reporting a failed parse, because a bad style is the caller's
    /// mistake and not the input's.
    /// </summary>
    public static void ValidateStyles(NumberStyles styles, string parameterName)
    {
        if ((styles & ~SupportedStyles) != 0)
        {
            throw new ArgumentException(
                $"'{styles}' includes a style a decimal format cannot parse.", parameterName);
        }
    }

    public static bool TryNormalize(ReadOnlySpan<char> text, NumberStyles styles,
        NumberFormatInfo numberFormat, Span<char> destination, out int written)
    {
        written = 0;

        if (destination.Length < MinimumBufferLength || destination.Length < text.Length + 1)
        {
            return false;
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

        if (TryWriteSpecial(body, numberFormat, destination, out written))
        {
            return true;
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

        // A currency symbol can sit on either side of the sign, so the two are taken in
        // both orders: "-$5" and "$-5" both read.
        TakeCurrencySymbol(ref body, styles, numberFormat);
        TakeSign(ref body, styles, numberFormat, ref isNegative, ref hasSign);
        TakeCurrencySymbol(ref body, styles, numberFormat);

        if (body.IsEmpty)
        {
            return false;
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
                    return false;
                }

                sawDecimalSeparator = true;
                destination[index++] = '.';
                position += decimalSeparator.Length;
                continue;
            }

            if (StartsWith(rest, groupSeparator))
            {
                // A group separator belongs to the integer part and needs a digit ahead of
                // it. Dropping it wherever it fell would read a German ".5" -- where the
                // period is the group separator -- as 5. What follows it is not checked,
                // which is also where the built-in types stop: "1,,5" and "5," both read.
                if ((styles & NumberStyles.AllowThousands) == 0
                    || sawDecimalSeparator || sawExponent || !sawDigit)
                {
                    return false;
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
                    return false;
                }

                sawExponent = true;
                destination[index++] = 'E';
                position++;

                // The exponent carries the culture's sign, which the specification writes
                // as plain ASCII.
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

            // Only the specification's own words can still be standing here: Infinity, inf,
            // NaN, and sNaN, none of which a culture spells for itself. Everything else is
            // rejected rather than copied through, because the parser downstream has rules
            // of its own -- it takes a leading sign unconditionally, and a period as a
            // point. Neither is authorized here: a sign the styles allow was taken off the
            // ends already, and a period means nothing in a culture that does not use one.
            if (!char.IsAsciiLetter(character))
            {
                return false;
            }

            destination[index++] = character;
            position++;
        }

        written = index;
        return true;
    }

    /// <summary>
    /// The three symbols a culture spells for itself. Anything else -- including the
    /// specification's own words, a signaling NaN, and a NaN payload -- goes through the
    /// scan instead.
    /// </summary>
    private static bool TryWriteSpecial(ReadOnlySpan<char> text, NumberFormatInfo numberFormat,
        Span<char> destination, out int written)
    {
        if (Matches(text, numberFormat.NaNSymbol))
        {
            return Write("NaN", destination, out written);
        }

        if (Matches(text, numberFormat.PositiveInfinitySymbol))
        {
            return Write("Infinity", destination, out written);
        }

        if (Matches(text, numberFormat.NegativeInfinitySymbol))
        {
            return Write("-Infinity", destination, out written);
        }

        written = 0;
        return false;
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

    private static bool Write(ReadOnlySpan<char> text, Span<char> destination, out int written)
    {
        text.CopyTo(destination);
        written = text.Length;
        return true;
    }

    // An empty separator or symbol matches nothing. A culture is free to leave one blank,
    // and a match on it would consume no input and never end.

    private static bool StartsWith(ReadOnlySpan<char> text, string word) =>
        word.Length > 0 && text.StartsWith(word, StringComparison.Ordinal);

    private static bool EndsWith(ReadOnlySpan<char> text, string word) =>
        word.Length > 0 && text.EndsWith(word, StringComparison.Ordinal);

    private static bool Matches(ReadOnlySpan<char> text, string word) =>
        word.Length > 0 && text.Equals(word, StringComparison.OrdinalIgnoreCase);
}
