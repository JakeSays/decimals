// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Conformance;

/// <summary>
/// A token read as an operand or a result: which notation it uses, and what that notation
/// carries.
/// </summary>
public readonly struct DecTestOperand
{
    private DecTestOperand(DecTestOperandKind kind, string text, int hexDigitCount)
    {
        Kind = kind;
        Text = text;
        HexDigitCount = hexDigitCount;
    }

    public DecTestOperandKind Kind { get; }

    /// <summary>
    /// Hexadecimal digits for <see cref="DecTestOperandKind.Encoded"/>, the numeric string
    /// for the other numeric kinds, and empty otherwise.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Which format an octothorpe notation names, as its count of hexadecimal digits: 8,
    /// 16, or 32.
    /// </summary>
    public int HexDigitCount { get; }

    public static DecTestOperand Classify(DecTestToken token)
    {
        var text = token.Text;

        if (!token.IsQuoted)
        {
            if (text == "#")
            {
                return new DecTestOperand(DecTestOperandKind.Null, string.Empty, 0);
            }

            if (text == "?")
            {
                return new DecTestOperand(DecTestOperandKind.Undefined, string.Empty, 0);
            }
        }

        var octothorpe = token.IsQuoted ? -1 : text.IndexOf('#');
        if (octothorpe < 0)
        {
            return new DecTestOperand(DecTestOperandKind.Plain, text, 0);
        }

        var prefix = text[..octothorpe];
        var remainder = text[(octothorpe + 1)..];

        if (prefix.Length == 0)
        {
            if (remainder.Length is not (8 or 16 or 32) || !IsHex(remainder))
            {
                return new DecTestOperand(DecTestOperandKind.Invalid, text, 0);
            }

            return new DecTestOperand(DecTestOperandKind.Encoded, remainder, remainder.Length);
        }

        var digits = prefix switch
        {
            "32" => 8,
            "64" => 16,
            "128" => 32,
            _ => 0
        };

        if (digits == 0 || remainder.Length == 0)
        {
            return new DecTestOperand(DecTestOperandKind.Invalid, text, 0);
        }

        return new DecTestOperand(DecTestOperandKind.FormatPrefixed, remainder, digits);
    }

    private static bool IsHex(string text)
    {
        foreach (var character in text)
        {
            var isHex = character is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');
            if (!isHex)
            {
                return false;
            }
        }

        return true;
    }
}
