// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

namespace Decimals.Conformance;

/// <summary>
/// Splits a testcase line into tokens: space-delimited, with single- or double-quoted runs
/// held together and a doubled quote standing for one literal quote. A token beginning with
/// two hyphens starts commentary that runs to the end of the line; the same two characters
/// inside a token do not.
/// </summary>
public static class DecTestTokenizer
{
    public static bool TryTokenize(ReadOnlySpan<char> line, List<DecTestToken> tokens, out string error)
    {
        tokens.Clear();
        error = string.Empty;

        var position = 0;
        while (position < line.Length)
        {
            if (line[position] is ' ' or '\t')
            {
                position++;
                continue;
            }

            if (line[position] == '-' && position + 1 < line.Length && line[position + 1] == '-')
            {
                break;
            }

            if (line[position] is '\'' or '"')
            {
                if (!TryReadQuoted(line, ref position, out var quoted))
                {
                    error = "unterminated quoted token";
                    return false;
                }

                tokens.Add(new DecTestToken(quoted, true));
                continue;
            }

            var start = position;
            while (position < line.Length && line[position] is not (' ' or '\t'))
            {
                position++;
            }

            tokens.Add(new DecTestToken(line[start..position].ToString(), false));
        }

        return true;
    }

    private static bool TryReadQuoted(ReadOnlySpan<char> line, ref int position, out string text)
    {
        var delimiter = line[position];
        position++;

        var builder = new System.Text.StringBuilder();
        while (position < line.Length)
        {
            if (line[position] != delimiter)
            {
                builder.Append(line[position]);
                position++;
                continue;
            }

            if (position + 1 < line.Length && line[position + 1] == delimiter)
            {
                builder.Append(delimiter);
                position += 2;
                continue;
            }

            position++;
            text = builder.ToString();
            return true;
        }

        text = string.Empty;
        return false;
    }
}
