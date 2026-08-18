// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Collections.Frozen;
using System.Collections.Generic;

namespace Decimals.Conformance;

/// <summary>
/// What the runner needs to know about an operation keyword.
/// </summary>
public static class DecTestOperations
{
    private static readonly FrozenDictionary<string, DecTestOperation> ByKeyword = BuildKeywords();

    private static readonly FrozenDictionary<DecTestOperation, int> OperandCounts = BuildOperandCounts();

    /// <summary>Operation keywords are case-independent.</summary>
    public static bool TryParse(string keyword, out DecTestOperation operation)
    {
        return ByKeyword.TryGetValue(keyword.ToLowerInvariant(), out operation);
    }

    public static int OperandCount(DecTestOperation operation)
    {
        return OperandCounts[operation];
    }

    /// <summary>
    /// toSci, toEng, and apply are the operations whose operand is converted under the
    /// format's own rules and whose conversion conditions are part of the expected result.
    /// Everything else reports only what the operation itself raised.
    /// </summary>
    public static bool ReportsConversionConditions(DecTestOperation operation)
    {
        return operation is DecTestOperation.ToSci or DecTestOperation.ToEng or DecTestOperation.Apply;
    }

    /// <summary>
    /// Operations whose result is a number the format had to round or clamp to hold. Only
    /// these keep the conditions raised getting their operands into the format: a
    /// comparison or a copy reports nothing of its own, so it reports nothing from its
    /// operands either.
    /// </summary>
    public static bool ProducesRoundedResult(DecTestOperation operation)
    {
        return operation is DecTestOperation.Add or DecTestOperation.Subtract
            or DecTestOperation.Multiply or DecTestOperation.Divide or DecTestOperation.DivideInt
            or DecTestOperation.Remainder or DecTestOperation.RemainderNear or DecTestOperation.Fma
            or DecTestOperation.Abs or DecTestOperation.Plus or DecTestOperation.Minus
            or DecTestOperation.ToSci or DecTestOperation.ToEng or DecTestOperation.Apply;
    }

    /// <summary>These produce a string rather than a number.</summary>
    public static bool ProducesText(DecTestOperation operation)
    {
        return operation is DecTestOperation.ToSci or DecTestOperation.ToEng or DecTestOperation.Class;
    }

    private static FrozenDictionary<string, DecTestOperation> BuildKeywords()
    {
        var map = new Dictionary<string, DecTestOperation>(StringComparer.Ordinal);
        foreach (var operation in Enum.GetValues<DecTestOperation>())
        {
            map[operation.ToString().ToLowerInvariant()] = operation;
        }

        // The three keywords the corpus does not spell the way the enum does.
        map["comparesig"] = DecTestOperation.CompareSig;
        map["comparetotmag"] = DecTestOperation.CompareTotalMag;
        map["tointegralx"] = DecTestOperation.ToIntegralX;
        return map.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static FrozenDictionary<DecTestOperation, int> BuildOperandCounts()
    {
        var three = new[] { DecTestOperation.Fma };

        var two = new[]
        {
            DecTestOperation.Add, DecTestOperation.And, DecTestOperation.Compare,
            DecTestOperation.CompareSig, DecTestOperation.CompareTotal,
            DecTestOperation.CompareTotalMag, DecTestOperation.CopySign, DecTestOperation.Divide,
            DecTestOperation.DivideInt, DecTestOperation.Max, DecTestOperation.MaxMag,
            DecTestOperation.Min, DecTestOperation.MinMag, DecTestOperation.Multiply,
            DecTestOperation.NextToward, DecTestOperation.Or, DecTestOperation.Power,
            DecTestOperation.Quantize, DecTestOperation.Remainder, DecTestOperation.RemainderNear,
            DecTestOperation.Rescale, DecTestOperation.Rotate, DecTestOperation.SameQuantum,
            DecTestOperation.ScaleB, DecTestOperation.Shift, DecTestOperation.Subtract,
            DecTestOperation.Xor
        };

        var map = new Dictionary<DecTestOperation, int>();
        foreach (var operation in Enum.GetValues<DecTestOperation>())
        {
            map[operation] = 1;
        }

        foreach (var operation in two)
        {
            map[operation] = 2;
        }

        foreach (var operation in three)
        {
            map[operation] = 3;
        }

        return map.ToFrozenDictionary();
    }
}
