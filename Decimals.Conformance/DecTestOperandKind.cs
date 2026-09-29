// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Conformance;

/// <summary>
/// The notation of an operand or result token.
/// </summary>
public enum DecTestOperandKind
{
    /// <summary>A numeric string.</summary>
    Plain,

    /// <summary>A # followed by 8, 16, or 32 hex digits: an explicit encoding.</summary>
    Encoded,

    /// <summary>"32#", "64#", or "128#" followed by a numeric string.</summary>
    FormatPrefixed,

    /// <summary>A # by itself: a null reference. No managed value can be null here.</summary>
    Null,

    /// <summary>A ? by itself: the result is undefined in the X3.274 subset.</summary>
    Undefined,

    /// <summary>A # form that matches none of the above.</summary>
    Invalid
}
