// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// A rounding mode and the conditions raised so far. Passed by reference to the
/// <see cref="Decimal32"/> overloads that need either, so a caller can ask for a rounding mode
/// other than the default and read back what happened.
/// </summary>
/// <remarks>
/// Precision and exponent range are not here: those are fixed by the format. An operation
/// decides its own conditions from a clean slate and then adds them to
/// <see cref="Status"/>, so what one operation raised never changes what the next reports.
/// </remarks>
public struct Decimal32Context
{
    public Decimal32Context()
        : this(Decimal32Rounding.HalfEven)
    {
    }

    public Decimal32Context(Decimal32Rounding rounding)
    {
        Rounding = rounding;
        Status = Decimal32Status.None;
    }

    public Decimal32Rounding Rounding { get; set; }

    /// <summary>
    /// Conditions accumulated since this context was made or last cleared. Sticky: an
    /// operation only ever adds to it.
    /// </summary>
    public Decimal32Status Status { get; set; }

    public readonly bool HasRaised(Decimal32Status condition)
    {
        return (Status & condition) != 0;
    }

    public void ClearStatus()
    {
        Status = Decimal32Status.None;
    }
}
