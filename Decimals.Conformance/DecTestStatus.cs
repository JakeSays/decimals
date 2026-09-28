// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Conformance;

/// <summary>
/// The conditions a testcase line can name, as the General Decimal Arithmetic Specification
/// names them.
/// </summary>
/// <remarks>
/// The bit for each condition is the one every decimal type in the repository uses for its
/// own status enumeration, so a target converts between the two by a cast.
/// </remarks>
[Flags]
public enum DecTestStatus
{
    None = 0,

    /// <summary>The string given to a conversion was not a number.</summary>
    ConversionSyntax = 1 << 0,

    /// <summary>A non-zero value was divided by zero.</summary>
    DivisionByZero = 1 << 1,

    /// <summary>An integer division or remainder needed more digits than the format holds.</summary>
    DivisionImpossible = 1 << 2,

    /// <summary>Zero was divided by zero.</summary>
    DivisionUndefined = 1 << 3,

    /// <summary>The result differs from the exact value.</summary>
    Inexact = 1 << 4,

    /// <summary>The operation makes no sense for its operands.</summary>
    InvalidOperation = 1 << 5,

    /// <summary>The result is larger than the format holds.</summary>
    Overflow = 1 << 6,

    /// <summary>The result's exponent was reduced to fit the encoding.</summary>
    Clamped = 1 << 7,

    /// <summary>Digits were removed from the result, whether or not they were zero.</summary>
    Rounded = 1 << 8,

    /// <summary>The result is non-zero with an adjusted exponent below the format's minimum.</summary>
    Subnormal = 1 << 9,

    /// <summary>A subnormal result also lost precision.</summary>
    Underflow = 1 << 10
}
