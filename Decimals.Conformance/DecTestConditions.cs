// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text;

namespace Decimals.Conformance;

/// <summary>
/// Converts between the condition names in test lines and <see cref="DecTestStatus"/>.
/// </summary>
public static class DecTestConditions
{
    private static readonly (string Name, DecTestStatus Flag)[] Known =
    [
        ("clamped", DecTestStatus.Clamped),
        ("conversion_syntax", DecTestStatus.ConversionSyntax),
        ("division_by_zero", DecTestStatus.DivisionByZero),
        ("division_impossible", DecTestStatus.DivisionImpossible),
        ("division_undefined", DecTestStatus.DivisionUndefined),
        ("inexact", DecTestStatus.Inexact),
        ("invalid_operation", DecTestStatus.InvalidOperation),
        ("overflow", DecTestStatus.Overflow),
        ("rounded", DecTestStatus.Rounded),
        ("subnormal", DecTestStatus.Subnormal),
        ("underflow", DecTestStatus.Underflow)
    ];

    private static readonly FrozenDictionary<string, DecTestStatus> ByName =
        Known.ToFrozenDictionary(entry => entry.Name, entry => entry.Flag, StringComparer.Ordinal);

    /// <summary>Parses a condition name. Names are not case-sensitive.</summary>
    public static bool TryParse(string name, out DecTestStatus flag)
    {
        return ByName.TryGetValue(name.ToLowerInvariant(), out flag);
    }

    /// <summary>Lists the condition names in a status, for failure reports.</summary>
    public static string Describe(DecTestStatus status)
    {
        if (status == DecTestStatus.None)
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
