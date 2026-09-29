// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Conformance;

/// <summary>
/// A line in the published vector files that cannot be used, and the reason.
/// </summary>
/// <remarks>
/// A defect is identified by file name, line number, and the text the line starts with.
/// The text check prevents a match on a different line at the same position, for example
/// in the sample.
/// </remarks>
public sealed class HfahmyDefect
{
    public HfahmyDefect(string fileName, int lineNumber, string start, string reason)
    {
        FileName = fileName;
        LineNumber = lineNumber;
        Start = start;
        Reason = reason;
    }

    public string FileName { get; }

    public int LineNumber { get; }

    public string Start { get; }

    public string Reason { get; }
}
