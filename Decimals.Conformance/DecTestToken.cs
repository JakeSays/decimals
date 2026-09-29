// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Conformance;

/// <summary>
/// One token of a test line, with quotes removed.
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
    /// True if the token was quoted. Quotes let a test case write an invalid numeric
    /// string, so a quoted token is never read as one of the # notations.
    /// </summary>
    public bool IsQuoted { get; }
}
