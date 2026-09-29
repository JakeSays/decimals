// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Conformance;

/// <summary>
/// Connects the corpus runner to one decimal format.
/// </summary>
/// <remarks>
/// decNumber takes its precision and exponent range from the directives. The types here
/// have a fixed precision and exponent range. The runner compares <see cref="Precision"/>,
/// <see cref="MaxExponent"/>, and <see cref="MinExponent"/> with the directives and skips
/// any test case that does not match. This lets a corpus written for an arbitrary-precision
/// engine run against fixed formats.
/// </remarks>
/// <typeparam name="TSelf">The implementing type. It lets the members be static.</typeparam>
/// <typeparam name="TDecimal">The decimal type under test.</typeparam>
public interface IDecTestTarget<TSelf, TDecimal>
    where TSelf : IDecTestTarget<TSelf, TDecimal>
{
    static abstract string Name { get; }

    static abstract int Precision { get; }

    static abstract int MaxExponent { get; }

    static abstract int MinExponent { get; }

    /// <summary>The number of hex digits in this format's # notation: 8, 16, or 32.</summary>
    static abstract int HexDigitCount { get; }

    /// <summary>
    /// True if the type keeps a non-canonical DPD encoding unchanged. The corpus expects the
    /// copy operations to return such an operand unchanged. Only a type that stores DPD can
    /// do that. A type that stores BID makes the encoding canonical when it reads it, so
    /// the runner skips those cases for it.
    /// </summary>
    static abstract bool PreservesNonCanonicalEncodings { get; }

    static abstract TDecimal FromString(ReadOnlySpan<char> text, ref DecTestContext context);

    static abstract TDecimal FromDpdHex(ReadOnlySpan<char> hex);

    static abstract string ToDpdHex(TDecimal value);

    static abstract string ToScientificString(TDecimal value);

    static abstract string ToEngineeringString(TDecimal value);

    static abstract DecTestClass Classify(TDecimal value);

    /// <summary>
    /// Runs the operation. Returns false if the type does not support it. The runner counts
    /// that as a skip, not a failure.
    /// </summary>
    static abstract bool TryApply(DecTestOperation operation, ReadOnlySpan<TDecimal> operands,
        ref DecTestContext context, out TDecimal result);
}
