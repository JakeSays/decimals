// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Conformance;

/// <summary>
/// The seam between the corpus reader and a decimal format.
/// </summary>
/// <remarks>
/// This is the part that is not a port of the C++ harness. decNumber takes its precision
/// and exponent range from the directives; these types have theirs fixed by the format, so
/// the runner reads <see cref="Precision"/>, <see cref="MaxExponent"/>, and
/// <see cref="MinExponent"/> and skips any test whose directives ask for something else.
/// That rule is what makes a corpus written for an arbitrary-precision engine usable
/// against fixed formats.
/// </remarks>
/// <typeparam name="TSelf">The implementing type, so the members can be static.</typeparam>
/// <typeparam name="TDecimal">The decimal type being driven.</typeparam>
public interface IDecTestTarget<TSelf, TDecimal>
    where TSelf : IDecTestTarget<TSelf, TDecimal>
{
    static abstract string Name { get; }

    static abstract int Precision { get; }

    static abstract int MaxExponent { get; }

    static abstract int MinExponent { get; }

    /// <summary>The width of this format's octothorpe notation: 8, 16, or 32 digits.</summary>
    static abstract int HexDigitCount { get; }

    /// <summary>
    /// Whether a non-canonical DPD encoding survives being held by the type. The corpus
    /// expects the copy family to hand such an operand back untouched, which only a type
    /// storing the interchange form can do; one that stores the binary-integer form
    /// canonicalizes on the way in, and those cases are skipped for it.
    /// </summary>
    static abstract bool PreservesNonCanonicalEncodings { get; }

    static abstract TDecimal FromString(ReadOnlySpan<char> text, ref DecTestContext context);

    static abstract TDecimal FromDpdHex(ReadOnlySpan<char> hex);

    static abstract string ToDpdHex(TDecimal value);

    static abstract string ToScientificString(TDecimal value);

    static abstract string ToEngineeringString(TDecimal value);

    static abstract DecTestClass Classify(TDecimal value);

    /// <summary>
    /// Runs the operation. Returns false when the type does not have it, which the runner
    /// counts as a skip rather than a failure.
    /// </summary>
    static abstract bool TryApply(DecTestOperation operation, ReadOnlySpan<TDecimal> operands,
        ref DecTestContext context, out TDecimal result);
}
