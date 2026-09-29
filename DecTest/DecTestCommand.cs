// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.DecTest;

/// <summary>The command a <c>dectest</c> invocation runs.</summary>
public enum DecTestCommand
{
    /// <summary>Runs testcase or vector files against the targets.</summary>
    Run,

    /// <summary>Downloads the full Sayed-Ahmed and Fahmy vector set.</summary>
    FetchHfahmy,

    /// <summary>Writes the committed sample from the full vector set.</summary>
    SampleHfahmy
}
