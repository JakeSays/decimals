// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// The conditions a <see cref="Decimal32"/> operation can raise, as defined by the General
/// Decimal Arithmetic Specification. Operators discard them. The overloads that take a
/// <see cref="Decimal32Context"/> add them to the context.
/// </summary>
[Flags]
public enum Decimal32Status
{
    None = 0,

    /// <summary>The string passed to a conversion was not a number.</summary>
    ConversionSyntax = 1 << 0,

    /// <summary>A non-zero value was divided by zero.</summary>
    DivisionByZero = 1 << 1,

    /// <summary>An integer division or remainder needed more digits than the format has.</summary>
    DivisionImpossible = 1 << 2,

    /// <summary>Zero was divided by zero.</summary>
    DivisionUndefined = 1 << 3,

    /// <summary>The result is not exactly equal to the true result.</summary>
    Inexact = 1 << 4,

    /// <summary>The operation is not valid for its operands.</summary>
    InvalidOperation = 1 << 5,

    /// <summary>The result is too large for the format.</summary>
    Overflow = 1 << 6,

    /// <summary>The result's exponent was changed to fit the encoding.</summary>
    Clamped = 1 << 7,

    /// <summary>Digits were removed from the result, whether or not they were zero.</summary>
    Rounded = 1 << 8,

    /// <summary>The result is non-zero and its adjusted exponent is below the format's minimum.</summary>
    Subnormal = 1 << 9,

    /// <summary>The result is subnormal and inexact.</summary>
    Underflow = 1 << 10
}
