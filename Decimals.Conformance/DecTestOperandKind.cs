// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Conformance;

/// <summary>
/// How an operand or result token is to be read.
/// </summary>
public enum DecTestOperandKind
{
    /// <summary>A numeric string.</summary>
    Plain,

    /// <summary>An octothorpe and 8, 16, or 32 hexadecimal digits: an explicit encoding.</summary>
    Encoded,

    /// <summary>"32#", "64#", or "128#" and a numeric string.</summary>
    FormatPrefixed,

    /// <summary>A lone octothorpe: a null reference, which no managed value can be.</summary>
    Null,

    /// <summary>A lone question mark: the X3.274 subset leaves the result undefined.</summary>
    Undefined,

    /// <summary>An octothorpe form that matches none of the above.</summary>
    Invalid
}
