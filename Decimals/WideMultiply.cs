// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// Multiplication on the engine's unit arrays, following decNumber's <c>decMultiplyOp</c>
/// fast path.
/// </summary>
/// <remarks>
/// <para>
/// The partial products accumulate lazily in 64-bit columns, as they do in
/// <see cref="BcdMultiplier"/>, but the engine's operands run to far more units than a
/// format's do and a column cannot absorb an unbounded number of products: each is under
/// 10^18, and 64 bits hold eighteen of them. decNumber calls that bound <c>FASTLAZY</c> and
/// spins the accumulator to settle carries whenever it is reached.
/// </para>
/// <para>
/// Where decNumber has to chunk its operands into base-billion items first, because its
/// units may be narrower, ours already are: the copy in and the re-split out both vanish.
/// </para>
/// </remarks>
internal static unsafe class WideMultiply
{
    /// <summary>
    /// Rows that may accumulate before the carries have to be settled. A product of two
    /// units is under 10^18 and a 64-bit column holds eighteen of them.
    /// </summary>
    private const int LazyLimit = 18;

    /// <summary>
    /// Columns an accumulator needs beyond the product's own, for the two-place carry the
    /// resolution below can push upward.
    /// </summary>
    public const int AccumulatorSlack = 2;

    /// <summary>
    /// Multiplies two finite values. The product is exact, so the caller rounds it.
    /// </summary>
    public static void Multiply(ref WideNumber result, WideNumber left, WideNumber right,
        ulong* accumulator)
    {
        var columns = left.Units + right.Units;

        for (var index = 0; index < columns + AccumulatorSlack; index++)
        {
            accumulator[index] = 0;
        }

        var lazy = LazyLimit;

        for (var rightIndex = 0; rightIndex < right.Units; rightIndex++)
        {
            var multiplier = right.Lsu[rightIndex];
            if (multiplier != 0)
            {
                var column = accumulator + rightIndex;
                for (var leftIndex = 0; leftIndex < left.Units; leftIndex++)
                {
                    column[leftIndex] += (ulong)multiplier * left.Lsu[leftIndex];
                }
            }

            lazy--;
            if (lazy > 0 && rightIndex != right.Units - 1)
            {
                continue;
            }

            lazy = LazyLimit;
            ResolveCarries(accumulator, columns);
        }

        for (var index = 0; index < columns; index++)
        {
            result.Lsu[index] = (uint)accumulator[index];
        }

        result.Units = columns;
        result.Kind = DecimalKind.Finite;
        result.IsNegative = left.IsNegative != right.IsNegative;
        result.Exponent = left.Exponent + right.Exponent;
        result.CountDigits();
    }

    /// <summary>
    /// Settles the columns back into units. A column can carry more than one unit's worth,
    /// which is what lets the accumulation run eighteen rows deep before this is needed.
    /// </summary>
    private static void ResolveCarries(ulong* accumulator, int columns)
    {
        for (var index = 0; index < columns; index++)
        {
            if (accumulator[index] < WideNumber.UnitBase)
            {
                continue;
            }

            var carry = accumulator[index] / WideNumber.UnitBase;

            if (carry >= WideNumber.UnitBase)
            {
                // Two units' worth, which the deep accumulation makes possible though it
                // is rare.
                var upper = carry / WideNumber.UnitBase;
                accumulator[index + 2] += upper;
                accumulator[index] -= (ulong)WideNumber.UnitBase * WideNumber.UnitBase * upper;
                carry -= (ulong)WideNumber.UnitBase * upper;
            }

            accumulator[index + 1] += carry;
            accumulator[index] -= WideNumber.UnitBase * carry;
        }
    }
}
