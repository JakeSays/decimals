// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

namespace Decimals.Conformance;

/// <summary>
/// A test line: <c>id operation operand1 operand2 operand3 -&gt; result conditions</c>.
/// There are one to three operands.
/// </summary>
public sealed class DecTestCase
{
    private DecTestCase(string id, string operationKeyword, List<DecTestToken> operands,
        DecTestToken result, List<string> conditions)
    {
        Id = id;
        OperationKeyword = operationKeyword;
        Operands = operands;
        Result = result;
        Conditions = conditions;
    }

    public string Id { get; }

    public string OperationKeyword { get; }

    public IReadOnlyList<DecTestToken> Operands { get; }

    public DecTestToken Result { get; }

    public IReadOnlyList<string> Conditions { get; }

    public static bool TryParse(List<DecTestToken> tokens, out DecTestCase? testCase, out string error)
    {
        testCase = null;
        error = string.Empty;

        if (tokens.Count < 5)
        {
            error = "a test line needs at least five tokens";
            return false;
        }

        var arrow = -1;
        for (var index = 0; index < tokens.Count; index++)
        {
            if (!tokens[index].IsQuoted && tokens[index].Text == "->")
            {
                arrow = index;
                break;
            }
        }

        if (arrow < 0)
        {
            error = "no '->' separator";
            return false;
        }

        if (arrow < 3)
        {
            error = "'->' arrives before the first operand";
            return false;
        }

        if (arrow + 1 >= tokens.Count)
        {
            error = "no result after '->'";
            return false;
        }

        var operands = new List<DecTestToken>();
        for (var index = 2; index < arrow; index++)
        {
            operands.Add(tokens[index]);
        }

        var conditions = new List<string>();
        for (var index = arrow + 2; index < tokens.Count; index++)
        {
            conditions.Add(tokens[index].Text);
        }

        testCase = new DecTestCase(tokens[0].Text, tokens[1].Text, operands, tokens[arrow + 1], conditions);
        return true;
    }
}
