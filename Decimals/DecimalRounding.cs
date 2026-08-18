// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// The eight rounding modes of the General Decimal Arithmetic Specification.
/// </summary>
public enum DecimalRounding
{
    /// <summary>Toward positive infinity.</summary>
    Ceiling,

    /// <summary>Toward zero.</summary>
    Down,

    /// <summary>Toward negative infinity.</summary>
    Floor,

    /// <summary>To nearest, ties toward zero.</summary>
    HalfDown,

    /// <summary>To nearest, ties to the even digit. The default, and what IEEE 754 asks for.</summary>
    HalfEven,

    /// <summary>To nearest, ties away from zero.</summary>
    HalfUp,

    /// <summary>Away from zero.</summary>
    Up,

    /// <summary>
    /// Away from zero when the digit left of the discarded part is 0 or 5, toward zero
    /// otherwise. The specification calls this <c>05up</c>; it exists so a result can be
    /// rounded again later without a double-rounding error.
    /// </summary>
    ZeroFiveUp
}
