// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Conformance;

/// <summary>
/// Where a run's per-file lines go. The console runner prints them in the same shape as the
/// C++ harness so the two runs can be diffed; the xunit theory leaves it null.
/// </summary>
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
