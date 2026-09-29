// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Conformance;

/// <summary>
/// Writes one result line per file.
/// </summary>
/// <remarks>
/// The console runner prints the lines in the same layout as the C++ harness, so the two
/// outputs can be compared with diff. The xunit tests pass no report.
/// </remarks>
public sealed class DecTestReport
{
    private readonly Action<string> _write;

    public DecTestReport(Action<string> write)
    {
        _write = write;
    }

    public void File(string name, DecTestTotals totals)
    {
        _write($"{name,-44} {totals.Passed,7} pass {totals.Failed,6} fail "
            + $"{totals.Skipped,6} skip {totals.Errors,4} error");
    }
}
