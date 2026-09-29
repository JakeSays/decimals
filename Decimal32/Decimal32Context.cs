// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// A rounding mode and the conditions raised so far. Pass it by reference to the
/// <see cref="Decimal32"/> overloads that take it, to choose a rounding mode other than
/// the default or to see which conditions an operation raised.
/// </summary>
/// <remarks>
/// The context does not hold a precision or exponent range, because the format fixes those.
/// Each operation works out its own conditions from scratch and then adds them to
/// <see cref="Status"/>. So conditions raised by one operation never change what the next
/// operation reports.
/// </remarks>
public struct Decimal32Context
{
    /// <summary>Creates a context that rounds half to even, with no conditions raised.</summary>
    public Decimal32Context()
        : this(Decimal32Rounding.HalfEven)
    {
    }

    /// <summary>Creates a context with the given rounding mode, with no conditions raised.</summary>
    /// <param name="rounding">The rounding mode for operations that use this context.</param>
    public Decimal32Context(Decimal32Rounding rounding)
    {
        Rounding = rounding;
        Status = Decimal32Status.None;
    }

    /// <summary>The rounding mode for operations that use this context.</summary>
    public Decimal32Rounding Rounding { get; set; }

    /// <summary>
    /// The conditions raised since the context was created or last cleared. Operations only
    /// add conditions; they never remove them.
    /// </summary>
    public Decimal32Status Status { get; set; }

    /// <summary>Whether any of the given conditions has been raised.</summary>
    /// <param name="condition">One condition, or several combined with <c>|</c>.</param>
    /// <returns>True if <see cref="Status"/> contains at least one of the conditions.</returns>
    public readonly bool HasRaised(Decimal32Status condition)
    {
        return (Status & condition) != 0;
    }

    /// <summary>Clears all raised conditions. The rounding mode is unchanged.</summary>
    public void ClearStatus()
    {
        Status = Decimal32Status.None;
    }
}
