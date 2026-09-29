// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace Decimals.Conformance;

/// <summary>
/// Runs vectors for one format, using that format's corpus target.
/// </summary>
/// <remarks>
/// <para>
/// Results are compared bit for bit, so the exponent must match as well as the value. NaN
/// results are compared by kind only, because the vectors give no sign or payload for a
/// NaN.
/// </para>
/// <para>
/// The vectors list only the five IEEE 754 flags. Before comparing, the target's status is
/// reduced to those five: DivisionImpossible and DivisionUndefined become InvalidOperation,
/// and Clamped, Rounded, and Subnormal are dropped.
/// </para>
/// </remarks>
public sealed class HfahmyVectorTarget<TTarget, TDecimal> : IHfahmyVectorTarget
    where TTarget : IDecTestTarget<TTarget, TDecimal>
{
    private const DecTestStatus IeeeFlags = DecTestStatus.Inexact
        | DecTestStatus.Underflow
        | DecTestStatus.Overflow
        | DecTestStatus.DivisionByZero
        | DecTestStatus.InvalidOperation;

    private const DecTestStatus InvalidConditions = DecTestStatus.InvalidOperation
        | DecTestStatus.DivisionImpossible
        | DecTestStatus.DivisionUndefined;

    public HfahmyVectorTarget()
    {
        Format = "d" + (TTarget.HexDigitCount * 4).ToString(CultureInfo.InvariantCulture);
    }

    public string Name => TTarget.Name;

    public string Format { get; }

    public void Run(HfahmyVector vector, string line, string id, DecTestTotals totals)
    {
        var operands = new TDecimal[vector.Operands.Count];
        for (var index = 0; index < operands.Length; index++)
        {
            if (!TryConvert(vector.Operands[index], out operands[index]))
            {
                totals.AddError($"error: {id}: operand '{vector.Operands[index]}' is not a {TTarget.Name} value");
                return;
            }
        }

        if (!TryConvert(vector.Result, out var expected))
        {
            totals.AddError($"error: {id}: result '{vector.Result}' is not a {TTarget.Name} value");
            return;
        }

        var context = new DecTestContext(vector.Rounding);
        var actual = Apply(vector.Operation, operands, ref context);
        var flags = ToIeeeFlags(context.Status);

        if (SameValue(vector.Result, expected, actual) && flags == vector.Flags)
        {
            totals.AddPass();
            return;
        }

        totals.AddFailure(
            $"FAIL {id}\n"
            + $"  vector:    {line}\n"
            + $"  expected:  {vector.Result}   [{DecTestConditions.Describe(vector.Flags)}]\n"
            + $"  actual:    {TTarget.ToScientificString(actual)}   [{DecTestConditions.Describe(flags)}]");
    }

    /// <summary>
    /// Converts a vector value to the target type. Every value in the vectors fits its
    /// format, so an inexact conversion means the line is bad. Subnormal and Clamped do not
    /// count, because they do not change the value.
    /// </summary>
    private static bool TryConvert(string text, out TDecimal value)
    {
        var context = new DecTestContext();
        value = TTarget.FromString(HfahmyVector.ToNumericString(text), ref context);
        return !context.HasRaised(DecTestStatus.ConversionSyntax | DecTestStatus.Inexact);
    }

    private static TDecimal Apply(HfahmyVectorOperation operation, TDecimal[] operands, ref DecTestContext context)
    {
        if (operation == HfahmyVectorOperation.FusedMultiplySubtract)
        {
            // a * b - c is computed as a * b + (-c). Negation is exact and raises no flags,
            // even for a signaling NaN.
            var scratch = new DecTestContext();
            TTarget.TryApply(DecTestOperation.CopyNegate, operands.AsSpan(2, 1), ref scratch, out operands[2]);
        }

        var applied = operation switch
        {
            HfahmyVectorOperation.Add => DecTestOperation.Add,
            HfahmyVectorOperation.Subtract => DecTestOperation.Subtract,
            HfahmyVectorOperation.Multiply => DecTestOperation.Multiply,
            HfahmyVectorOperation.Divide => DecTestOperation.Divide,
            HfahmyVectorOperation.SquareRoot => DecTestOperation.SquareRoot,
            _ => DecTestOperation.Fma
        };

        TTarget.TryApply(applied, operands, ref context, out var result);
        return result;
    }

    private static bool SameValue(string expectedText, TDecimal expected, TDecimal actual)
    {
        return expectedText switch
        {
            "Q" => TTarget.Classify(actual) == DecTestClass.QuietNaN,
            "S" => TTarget.Classify(actual) == DecTestClass.SignalingNaN,
            _ => string.Equals(TTarget.ToDpdHex(expected), TTarget.ToDpdHex(actual), StringComparison.Ordinal)
        };
    }

    private static DecTestStatus ToIeeeFlags(DecTestStatus status)
    {
        var flags = status & IeeeFlags;
        if ((status & InvalidConditions) != 0)
        {
            flags |= DecTestStatus.InvalidOperation;
        }

        return flags;
    }
}
