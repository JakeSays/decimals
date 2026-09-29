// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// The eight rounding modes of the General Decimal Arithmetic Specification, for
/// <see cref="Decimal128"/>.
/// </summary>
public enum Decimal128Rounding
{
    /// <summary>Toward positive infinity.</summary>
    Ceiling,

    /// <summary>Toward zero.</summary>
    Down,

    /// <summary>Toward negative infinity.</summary>
    Floor,

    /// <summary>To nearest, ties toward zero.</summary>
    HalfDown,

    /// <summary>To nearest, ties to the even digit. This is the default, as in IEEE 754.</summary>
    HalfEven,

    /// <summary>To nearest, ties away from zero.</summary>
    HalfUp,

    /// <summary>Away from zero.</summary>
    Up,

    /// <summary>
    /// Away from zero if the last kept digit is 0 or 5, otherwise toward zero. The
    /// specification calls it <c>05up</c>. A result rounded this way can be rounded again
    /// later without double-rounding errors.
    /// </summary>
    ZeroFiveUp
}
