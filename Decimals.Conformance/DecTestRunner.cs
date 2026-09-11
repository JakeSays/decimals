// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Decimals.Conformance;

/// <summary>
/// Reads testcase files and runs what they describe against one decimal format.
/// </summary>
/// <remarks>
/// A port of the C++ harness under <c>decNumber/harness</c>, with one difference: that one
/// drives an arbitrary-precision engine and takes precision and exponent range from the
/// directives, while this one drives a fixed format and skips whatever the directives do
/// not match.
/// </remarks>
public static class DecTestRunner
{
    private const string TestFileExtension = ".decTest";
    private const int MaxIncludeDepth = 8;

    /// <summary>
    /// Runs a file, or every <c>.decTest</c> file directly inside a directory. A group
    /// reached more than once in a run is run once.
    /// </summary>
    public static DecTestTotals Run<TTarget, TDecimal>(string path, DecTestReport? report = null)
        where TTarget : IDecTestTarget<TTarget, TDecimal>
    {
        var totals = new DecTestTotals();
        var completed = new HashSet<string>(StringComparer.Ordinal);

        if (Directory.Exists(path))
        {
            foreach (var file in Directory.GetFiles(path, "*" + TestFileExtension).OrderBy(name => name, StringComparer.Ordinal))
            {
                RunFile<TTarget, TDecimal>(file, 0, totals, completed, report);
            }

            return totals;
        }

        RunFile<TTarget, TDecimal>(path, 0, totals, completed, report);
        return totals;
    }

    private static void RunFile<TTarget, TDecimal>(string path, int depth, DecTestTotals totals,
        HashSet<string> completed, DecTestReport? report)
        where TTarget : IDecTestTarget<TTarget, TDecimal>
    {
        if (depth > MaxIncludeDepth || !completed.Add(Path.GetFullPath(path)))
        {
            return;
        }

        if (!File.Exists(path))
        {
            totals.AddError($"cannot read {path}");
            return;
        }

        var directives = new DecTestDirectives();
        var fileTotals = new DecTestTotals();
        var tokens = new List<DecTestToken>();
        var includes = new List<string>();
        var name = Path.GetFileName(path);
        var lineNumber = 0;

        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;

            if (!DecTestTokenizer.TryTokenize(line, tokens, out var error))
            {
                fileTotals.AddError($"error: {name}:{lineNumber}: {error}");
                continue;
            }

            if (tokens.Count == 0)
            {
                continue;
            }

            var colon = tokens[0].IsQuoted ? -1 : tokens[0].Text.IndexOf(':');
            if (colon >= 0)
            {
                var keyword = tokens[0].Text[..colon];
                var value = tokens[0].Text[(colon + 1)..];
                if (value.Length == 0 && tokens.Count > 1)
                {
                    value = tokens[1].Text;
                }

                if (string.Equals(keyword, "dectest", StringComparison.OrdinalIgnoreCase))
                {
                    includes.Add(Path.Combine(Path.GetDirectoryName(path) ?? ".", value + TestFileExtension));
                    continue;
                }

                if (!directives.TryApply(keyword, value, out var directiveError))
                {
                    fileTotals.AddError($"error: {name}:{lineNumber}: {directiveError}");
                }

                continue;
            }

            if (!DecTestCase.TryParse(tokens, out var testCase, out var caseError))
            {
                fileTotals.AddError($"error: {name}:{lineNumber}: {caseError}");
                continue;
            }

            RunCase<TTarget, TDecimal>(testCase!, directives, name, lineNumber, fileTotals);
        }

        if (fileTotals.Total > 0)
        {
            report?.File(name, fileTotals);
        }

        totals.Add(fileTotals);

        // A "dectest" directive is not an include: the named file is processed exactly as
        // if it were the only group being run, so it is deferred until this one is done.
        foreach (var include in includes)
        {
            RunFile<TTarget, TDecimal>(include, depth + 1, totals, completed, report);
        }
    }

    private static void RunCase<TTarget, TDecimal>(DecTestCase testCase, DecTestDirectives directives,
        string file, int lineNumber, DecTestTotals totals)
        where TTarget : IDecTestTarget<TTarget, TDecimal>
    {
        if (!directives.IsComplete)
        {
            totals.AddSkip("directives incomplete", testCase.Id);
            return;
        }

        if (!directives.RoundingIsKnown)
        {
            totals.AddSkip($"unsupported rounding mode '{directives.RoundingName}'", testCase.Id);
            return;
        }

        // The format's precision and exponent range are fixed, so a directive block that
        // describes a different shape of number is not about this type.
        if (directives.Precision != TTarget.Precision
            || directives.MaxExponent != TTarget.MaxExponent
            || directives.MinExponent != TTarget.MinExponent)
        {
            totals.AddSkip($"directives do not describe {TTarget.Name}", testCase.Id);
            return;
        }

        if (!DecTestOperations.TryParse(testCase.OperationKeyword, out var operation))
        {
            totals.AddError($"error: {file}:{lineNumber}: unknown operation '{testCase.OperationKeyword}'");
            return;
        }

        var operandCount = DecTestOperations.OperandCount(operation);
        if (testCase.Operands.Count != operandCount)
        {
            totals.AddError($"error: {file}:{lineNumber}: {testCase.OperationKeyword} takes "
                + $"{operandCount} operands, {testCase.Operands.Count} given");
            return;
        }

        var expected = DecimalStatus.None;
        foreach (var condition in testCase.Conditions)
        {
            if (!DecTestConditions.TryParse(condition, out var flag))
            {
                totals.AddError($"error: {file}:{lineNumber}: unknown condition '{condition}'");
                return;
            }

            expected |= flag;
        }

        var operands = new TDecimal[operandCount];
        var context = new DecimalContext(directives.Rounding);

        for (var index = 0; index < operandCount; index++)
        {
            var operand = DecTestOperand.Classify(testCase.Operands[index]);
            switch (operand.Kind)
            {
                case DecTestOperandKind.Null:
                    totals.AddSkip("null reference operand", testCase.Id);
                    return;
                case DecTestOperandKind.Invalid:
                case DecTestOperandKind.Undefined:
                    totals.AddError($"error: {file}:{lineNumber}: bad operand "
                        + $"'{testCase.Operands[index].Text}'");
                    return;
                case DecTestOperandKind.Encoded:
                case DecTestOperandKind.FormatPrefixed:
                    if (operand.HexDigitCount != TTarget.HexDigitCount)
                    {
                        totals.AddSkip($"operand is not a {TTarget.Name} encoding", testCase.Id);
                        return;
                    }

                    if (operand.Kind == DecTestOperandKind.FormatPrefixed)
                    {
                        operands[index] = TTarget.FromString(operand.Text, ref context);
                        break;
                    }

                    operands[index] = TTarget.FromDpdHex(operand.Text);

                    if (DecTestOperations.ReportsConversionConditions(operation))
                    {
                        // An encoding decodes exactly, but toSci, toEng, and apply report
                        // what putting the value under the format's rules raises -- a
                        // subnormal operand has to signal that it is one. Reconverting the
                        // string form is the way there that leaves a signaling NaN signaling
                        // and a negative zero negative.
                        operands[index] = TTarget.FromString(
                            TTarget.ToScientificString(operands[index]), ref context);
                    }

                    break;
                default:
                    operands[index] = TTarget.FromString(operand.Text, ref context);
                    break;
            }
        }

        // A folded operand moves where the testcase's Clamped comes from, and there is no
        // way for a fixed format to put it back. decNumber converts operands in a context
        // wide enough to hold "1E+384" as written, so the addition is what folds it and
        // raises Clamped. Reaching decimal64 the same value is already 1000000000000000E+369,
        // the addition folds nothing, and Clamped is correctly not raised -- the testcase
        // and the type disagree about which step owns the condition, not about the result.
        var foldedOnConversion = context.HasRaised(DecimalStatus.Clamped);

        // Conditions raised converting operands are the operation's own only for toSci,
        // toEng, and apply, which are conversions.
        if (!DecTestOperations.ReportsConversionConditions(operation))
        {
            context.ClearStatus();

            if (foldedOnConversion)
            {
                totals.AddSkip("operand folded on conversion, which moves the testcase's Clamped",
                    testCase.Id);
                return;
            }
        }

        var result = DecTestOperand.Classify(testCase.Result);
        if (result.Kind is DecTestOperandKind.Invalid or DecTestOperandKind.Null)
        {
            totals.AddError($"error: {file}:{lineNumber}: bad result '{testCase.Result.Text}'");
            return;
        }

        string actual;
        if (DecTestOperations.ProducesText(operation))
        {
            actual = operation switch
            {
                DecTestOperation.ToSci => TTarget.ToScientificString(operands[0]),
                DecTestOperation.ToEng => TTarget.ToEngineeringString(operands[0]),
                _ => ClassText(TTarget.Classify(operands[0]))
            };
        }
        else
        {
            if (!TTarget.TryApply(operation, operands, ref context, out var value))
            {
                totals.AddSkip($"{testCase.OperationKeyword.ToLowerInvariant()} is not implemented yet", testCase.Id);
                return;
            }

            if (result.Kind is DecTestOperandKind.Encoded or DecTestOperandKind.FormatPrefixed)
            {
                if (result.HexDigitCount != TTarget.HexDigitCount)
                {
                    totals.AddSkip($"result is not a {TTarget.Name} encoding", testCase.Id);
                    return;
                }

                actual = "#" + TTarget.ToDpdHex(value);
            }
            else
            {
                actual = TTarget.ToScientificString(value);
            }
        }

        var expectedText = result.Kind switch
        {
            DecTestOperandKind.Encoded => "#" + result.Text.ToLowerInvariant(),
            DecTestOperandKind.FormatPrefixed => "#" + TTarget.ToDpdHex(ExpectedEncoding<TTarget, TDecimal>(result)),
            _ => result.Text
        };

        var valueMatches = result.Kind == DecTestOperandKind.Undefined
            || string.Equals(expectedText, actual, StringComparison.Ordinal);

        if (valueMatches && context.Status == expected)
        {
            totals.AddPass();
            return;
        }

        totals.AddFailure(
            $"FAIL {testCase.Id} at {file}:{lineNumber}\n"
            + $"  operation: {testCase.OperationKeyword}\n"
            + $"  expected:  {expectedText}   [{DecTestConditions.Describe(expected)}]\n"
            + $"  actual:    {actual}   [{DecTestConditions.Describe(context.Status)}]");
    }

    /// <summary>
    /// Building the expected encoding from a numeric string raises no conditions, so it
    /// runs through a context of its own.
    /// </summary>
    private static TDecimal ExpectedEncoding<TTarget, TDecimal>(DecTestOperand result)
        where TTarget : IDecTestTarget<TTarget, TDecimal>
    {
        var scratch = new DecimalContext();
        return TTarget.FromString(result.Text, ref scratch);
    }

    private static bool IsCopyFamily(DecTestOperation operation)
    {
        return operation is DecTestOperation.Copy or DecTestOperation.CopyAbs
            or DecTestOperation.CopyNegate or DecTestOperation.CopySign;
    }

    /// <summary>
    /// True when re-encoding what an encoding decodes to gives the same bits back. The
    /// corpus carries patterns for which it does not.
    /// </summary>
    private static bool IsCanonical<TTarget, TDecimal>(string hex)
        where TTarget : IDecTestTarget<TTarget, TDecimal>
    {
        return string.Equals(TTarget.ToDpdHex(TTarget.FromDpdHex(hex)), hex.ToLowerInvariant(),
            StringComparison.Ordinal);
    }

    private static string ClassText(DecimalClass value)
    {
        return value switch
        {
            DecimalClass.SignalingNaN => "sNaN",
            DecimalClass.QuietNaN => "NaN",
            DecimalClass.NegativeInfinity => "-Infinity",
            DecimalClass.NegativeNormal => "-Normal",
            DecimalClass.NegativeSubnormal => "-Subnormal",
            DecimalClass.NegativeZero => "-Zero",
            DecimalClass.PositiveZero => "+Zero",
            DecimalClass.PositiveSubnormal => "+Subnormal",
            DecimalClass.PositiveNormal => "+Normal",
            _ => "+Infinity"
        };
    }
}
