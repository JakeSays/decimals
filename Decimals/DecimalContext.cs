// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// A rounding mode and the conditions raised so far. Passed by reference to the operation
/// overloads that need either, so a caller can ask for a rounding mode other than the
/// default and read back what happened.
/// </summary>
/// <remarks>
/// Precision and exponent range are not here: those are fixed by the format, unlike the
/// arbitrary-precision engine the specification's testcases were written against.
/// </remarks>
public struct DecimalContext
{
    public DecimalContext()
        : this(DecimalRounding.HalfEven)
    {
    }

    public DecimalContext(DecimalRounding rounding)
    {
        Rounding = rounding;
        Status = DecimalStatus.None;
    }

    public DecimalRounding Rounding { get; set; }

    /// <summary>
    /// Conditions accumulated since this context was made or last cleared. Sticky: an
    /// operation only ever adds to it.
    /// </summary>
    public DecimalStatus Status { get; set; }

    public readonly bool HasRaised(DecimalStatus condition)
    {
        return (Status & condition) != 0;
    }

    public void ClearStatus()
    {
        Status = DecimalStatus.None;
    }
}
