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

    static abstract TDecimal FromString(ReadOnlySpan<char> text, ref DecimalContext context);

    static abstract TDecimal FromDpdHex(ReadOnlySpan<char> hex);

    static abstract string ToDpdHex(TDecimal value);

    static abstract string ToScientificString(TDecimal value);

    static abstract string ToEngineeringString(TDecimal value);

    static abstract DecimalClass Classify(TDecimal value);

    /// <summary>
    /// Runs the operation. Returns false when it is not implemented yet, which is how the
    /// runner reports honest progress while the arithmetic is still being written.
    /// </summary>
    static abstract bool TryApply(DecTestOperation operation, ReadOnlySpan<TDecimal> operands,
        ref DecimalContext context, out TDecimal result);
}
