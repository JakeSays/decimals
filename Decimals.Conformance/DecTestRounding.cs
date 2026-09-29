// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Conformance;

/// <summary>
/// The eight rounding modes a <c>rounding</c> directive can set.
/// </summary>
/// <remarks>
/// Each decimal type's own rounding enum uses the same order, so a target converts between
/// the two with a cast.
/// </remarks>
public enum DecTestRounding
{
    /// <summary>Toward positive infinity.</summary>
    Ceiling,

    /// <summary>Toward zero.</summary>
    Down,

    /// <summary>Toward negative infinity.</summary>
    Floor,

    /// <summary>To nearest, ties toward zero.</summary>
    HalfDown,

    /// <summary>To nearest, ties to the even digit.</summary>
    HalfEven,

    /// <summary>To nearest, ties away from zero.</summary>
    HalfUp,

    /// <summary>Away from zero.</summary>
    Up,

    /// <summary>
    /// Away from zero if the last kept digit is 0 or 5, otherwise toward zero. The corpus
    /// calls it <c>05up</c>.
    /// </summary>
    ZeroFiveUp
}
