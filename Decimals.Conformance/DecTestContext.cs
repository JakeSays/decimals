// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Conformance;

/// <summary>
/// The rounding mode for a test case and the conditions it has raised so far.
/// </summary>
/// <remarks>
/// The runner has its own context type so that it can drive every format the same way. A
/// target creates its type's context from this one and copies the conditions back.
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
