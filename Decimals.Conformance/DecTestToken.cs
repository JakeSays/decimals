// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Conformance;

/// <summary>
/// One space-delimited element of a testcase line, with any quoting already removed.
/// </summary>
public readonly struct DecTestToken
{
    public DecTestToken(string text, bool isQuoted)
    {
        Text = text;
        IsQuoted = isQuoted;
    }

    public string Text { get; }

    /// <summary>
    /// Quoting exists so a syntactically invalid numeric string can be written down, so a
    /// quoted token is never read as one of the octothorpe notations.
    /// </summary>
    public bool IsQuoted { get; }
}
