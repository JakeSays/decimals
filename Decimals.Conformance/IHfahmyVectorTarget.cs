// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Conformance;

/// <summary>
/// Runs vectors for one decimal format.
/// </summary>
/// <remarks>
/// This is an interface instead of a type parameter because the runner reads each line
/// once and must choose among several formats at run time.
/// </remarks>
public interface IHfahmyVectorTarget
{
    /// <summary>The target's type name, used in reports.</summary>
    string Name { get; }

    /// <summary>The format name used in vector lines: d32, d64, or d128.</summary>
    string Format { get; }

    /// <summary>Runs one vector for this format and records the result.</summary>
    void Run(HfahmyVector vector, string line, string id, DecTestTotals totals);
}
