// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// Which of the four operations built on division is being carried out. They share a loop
/// and differ in what they keep from it, and in what the special values mean to them.
/// </summary>
internal enum BcdDivisionKind
{
    /// <summary>The full quotient, rounded to the format.</summary>
    Divide,

    /// <summary>The quotient's integer part, with the remainder discarded.</summary>
    DivideInteger,

    /// <summary>What is left after the integer part is taken out.</summary>
    Remainder,

    /// <summary>The same, with the quotient rounded to nearest rather than truncated.</summary>
    RemainderNear
}
