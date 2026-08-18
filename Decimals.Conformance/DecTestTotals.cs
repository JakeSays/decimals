// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Linq;

namespace Decimals.Conformance;

/// <summary>
/// Running counts for one file or for a whole run, with the reasons tests were skipped and
/// a few example ids for each so a reason can be traced back to the lines that caused it.
/// </summary>
public sealed class DecTestTotals
{
    private const int MaxExampleIds = 3;
    private const int MaxKeptFailures = 200;

    private readonly Dictionary<string, List<string>> _skipReasons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _skipCounts = new(StringComparer.Ordinal);
    private readonly List<string> _failures = [];

    public long Passed { get; private set; }

    public long Failed { get; private set; }

    public long Skipped { get; private set; }

    /// <summary>Lines that could not be read as a directive or a test at all.</summary>
    public long Errors { get; private set; }

    public IReadOnlyList<string> Failures => _failures;

    public long Total => Passed + Failed + Skipped + Errors;

    public void AddPass()
    {
        Passed++;
    }

    public void AddFailure(string description)
    {
        Failed++;
        if (_failures.Count < MaxKeptFailures)
        {
            _failures.Add(description);
        }
    }

    public void AddSkip(string reason, string id)
    {
        Skipped++;
        _skipCounts.TryGetValue(reason, out var count);
        _skipCounts[reason] = count + 1;

        if (!_skipReasons.TryGetValue(reason, out var examples))
        {
            examples = [];
            _skipReasons[reason] = examples;
        }

        if (examples.Count < MaxExampleIds)
        {
            examples.Add(id);
        }
    }

    public void AddError(string description)
    {
        Errors++;
        if (_failures.Count < MaxKeptFailures)
        {
            _failures.Add(description);
        }
    }

    public void Add(DecTestTotals other)
    {
        Passed += other.Passed;
        Failed += other.Failed;
        Skipped += other.Skipped;
        Errors += other.Errors;

        foreach (var (reason, count) in other._skipCounts)
        {
            _skipCounts.TryGetValue(reason, out var existing);
            _skipCounts[reason] = existing + count;
        }

        foreach (var (reason, examples) in other._skipReasons)
        {
            if (!_skipReasons.TryGetValue(reason, out var kept))
            {
                kept = [];
                _skipReasons[reason] = kept;
            }

            foreach (var example in examples)
            {
                if (kept.Count >= MaxExampleIds)
                {
                    break;
                }

                kept.Add(example);
            }
        }

        foreach (var failure in other._failures)
        {
            if (_failures.Count >= MaxKeptFailures)
            {
                break;
            }

            _failures.Add(failure);
        }
    }

    /// <summary>Skip reasons with their counts and example ids, most frequent first.</summary>
    public IEnumerable<(string Reason, long Count, IReadOnlyList<string> Examples)> SkipReasons()
    {
        return _skipCounts
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => (entry.Key, entry.Value, (IReadOnlyList<string>)_skipReasons[entry.Key]));
    }
}
