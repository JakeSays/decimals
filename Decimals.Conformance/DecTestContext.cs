// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Conformance;

/// <summary>
/// The rounding mode a testcase runs under and the conditions it has raised so far.
/// </summary>
/// <remarks>
/// The runner owns this rather than borrowing any one format's context, so that it drives
/// every format through the same seam. A target builds its type's own context from this one
/// and copies the conditions back.
/// </remarks>
public struct DecTestContext
{
    public DecTestContext()
        : this(DecTestRounding.HalfEven)
    {
    }

    public DecTestContext(DecTestRounding rounding)
    {
        Rounding = rounding;
        Status = DecTestStatus.None;
    }

    public DecTestRounding Rounding { get; set; }

    /// <summary>Conditions accumulated since this context was made or last cleared.</summary>
    public DecTestStatus Status { get; set; }

    public readonly bool HasRaised(DecTestStatus condition)
    {
        return (Status & condition) != 0;
    }

    public void ClearStatus()
    {
        Status = DecTestStatus.None;
    }
}
