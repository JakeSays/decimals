// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Decimals.Conformance;

/// <summary>
/// Reads .decTest files and runs their test cases against one decimal format.
/// </summary>
/// <remarks>
/// This is a port of the C++ harness in <c>decNumber/harness</c>, with one difference. The
/// C++ harness drives an arbitrary-precision engine and takes the precision and exponent
/// range from the directives. This runner drives a fixed format and skips test cases whose
/// directives do not match it.
/// </remarks>
public static class DecTestRunner
{
    private const string TestFileExtension = ".decTest";
    private const int MaxIncludeDepth = 8;

    /// <summary>
    /// Runs a file, or every <c>.decTest</c> file directly inside a directory. A file that
    /// is reached more than once is run only once.
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

        // A "dectest" directive is not an include. The named file runs as if it were run on
        // its own, so it runs after this file is finished.
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

        // The format's precision and exponent range are fixed. Directives that describe a
        // different precision or range are for another format.
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

        var expected = DecTestStatus.None;
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
        var context = new DecTestContext(directives.Rounding);

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

                    // The copy operations work on the bits and must return a non-canonical
                    // operand unchanged. A type that stores BID makes the operand canonical
                    // when it reads it, so it cannot pass these cases. They test the
                    // encoding, not the operation, and are skipped for such a type.
                    if (!TTarget.PreservesNonCanonicalEncodings && IsCopyFamily(operation)
                        && !IsCanonical<TTarget, TDecimal>(operand.Text))
                    {
                        totals.AddSkip($"non-canonical encoding cannot be held by {TTarget.Name}", testCase.Id);
                        return;
                    }

                    operands[index] = TTarget.FromDpdHex(operand.Text);

                    if (DecTestOperations.ReportsConversionConditions(operation))
                    {
                        // Decoding an encoding raises no conditions. But toSci, toEng, and
                        // apply must report the conditions of converting the value to the
                        // format; for example, a subnormal operand raises Subnormal.
                        // Converting the value's string form again raises them, and keeps
                        // a signaling NaN signaling and a negative zero negative.
                        operands[index] = TTarget.FromString(
                            TTarget.ToScientificString(operands[index]), ref context);
                    }

                    break;
                default:
                    operands[index] = TTarget.FromString(operand.Text, ref context);
                    break;
            }
        }

        // If converting an operand folded its exponent, the test case's Clamped condition
        // comes from a different step here. decNumber converts operands in a context wide
        // enough to hold "1E+384" as written, so the addition folds the exponent and raises
        // Clamped. In decimal64 the operand is already 1000000000000000E+369 after
        // conversion, so the addition folds nothing and correctly raises no Clamped. The
        // result is the same; only the step that raises Clamped differs.
        var foldedOnConversion = context.HasRaised(DecTestStatus.Clamped);

        // Only toSci, toEng, and apply report the conditions raised while converting
        // operands, because those operations are conversions.
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
    /// Builds the expected encoding from a numeric string. It uses a separate context so
    /// its conditions do not mix with the test's.
    /// </summary>
    private static TDecimal ExpectedEncoding<TTarget, TDecimal>(DecTestOperand result)
        where TTarget : IDecTestTarget<TTarget, TDecimal>
    {
        var scratch = new DecTestContext();
        return TTarget.FromString(result.Text, ref scratch);
    }

    private static bool IsCopyFamily(DecTestOperation operation)
    {
        return operation is DecTestOperation.Copy or DecTestOperation.CopyAbs
            or DecTestOperation.CopyNegate or DecTestOperation.CopySign;
    }

    /// <summary>
    /// True if decoding and re-encoding gives the same bits. The corpus includes encodings
    /// for which it does not.
    /// </summary>
    private static bool IsCanonical<TTarget, TDecimal>(string hex)
        where TTarget : IDecTestTarget<TTarget, TDecimal>
    {
        return string.Equals(TTarget.ToDpdHex(TTarget.FromDpdHex(hex)), hex.ToLowerInvariant(),
            StringComparison.Ordinal);
    }

    private static string ClassText(DecTestClass value)
    {
        return value switch
        {
            DecTestClass.SignalingNaN => "sNaN",
            DecTestClass.QuietNaN => "NaN",
            DecTestClass.NegativeInfinity => "-Infinity",
            DecTestClass.NegativeNormal => "-Normal",
            DecTestClass.NegativeSubnormal => "-Subnormal",
            DecTestClass.NegativeZero => "-Zero",
            DecTestClass.PositiveZero => "+Zero",
            DecTestClass.PositiveSubnormal => "+Subnormal",
            DecTestClass.PositiveNormal => "+Normal",
            _ => "+Infinity"
        };
    }
}
