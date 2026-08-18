// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text;

namespace Decimals.Conformance;

/// <summary>
/// Translates between the condition names on a testcase line and
/// <see cref="DecimalStatus"/>.
/// </summary>
public static class DecTestConditions
{
    private static readonly (string Name, DecimalStatus Flag)[] Known =
    [
        ("clamped", DecimalStatus.Clamped),
        ("conversion_syntax", DecimalStatus.ConversionSyntax),
        ("division_by_zero", DecimalStatus.DivisionByZero),
        ("division_impossible", DecimalStatus.DivisionImpossible),
        ("division_undefined", DecimalStatus.DivisionUndefined),
        ("inexact", DecimalStatus.Inexact),
        ("invalid_operation", DecimalStatus.InvalidOperation),
        ("overflow", DecimalStatus.Overflow),
        ("rounded", DecimalStatus.Rounded),
        ("subnormal", DecimalStatus.Subnormal),
        ("underflow", DecimalStatus.Underflow)
    ];

    private static readonly FrozenDictionary<string, DecimalStatus> ByName =
        Known.ToFrozenDictionary(entry => entry.Name, entry => entry.Flag, StringComparer.Ordinal);

    /// <summary>Condition names are case-independent.</summary>
    public static bool TryParse(string name, out DecimalStatus flag)
    {
        return ByName.TryGetValue(name.ToLowerInvariant(), out flag);
    }

    /// <summary>Renders a status word as its condition names, for failure reports.</summary>
    public static string Describe(DecimalStatus status)
    {
        if (status == DecimalStatus.None)
        {
            return "(none)";
        }

        var builder = new StringBuilder();
        foreach (var (name, flag) in Known)
        {
            if ((status & flag) == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(name);
        }

        return builder.ToString();
    }
}
