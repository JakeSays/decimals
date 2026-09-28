// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// Multiplication on the engine's unit arrays, following decNumber's <c>decMultiplyOp</c>
/// fast path.
/// </summary>
/// <remarks>
/// The partial products accumulate lazily in 64-bit columns, but the engine's operands run
/// to many units and a column cannot absorb an unbounded number of products: each is under
/// 10^18, and 64 bits hold eighteen of them. decNumber calls that bound <c>FASTLAZY</c> and
/// spins the accumulator to settle carries whenever it is reached.
/// </remarks>
internal static unsafe class Decimal64WideMultiply
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
    public static void Multiply(ref Decimal64WideNumber result, Decimal64WideNumber left, Decimal64WideNumber right,
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
        result.Kind = Decimal64Kind.Finite;
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
            if (accumulator[index] < Decimal64WideNumber.UnitBase)
            {
                continue;
            }

            var carry = accumulator[index] / Decimal64WideNumber.UnitBase;

            if (carry >= Decimal64WideNumber.UnitBase)
            {
                // Two units' worth, which the deep accumulation makes possible though it
                // is rare.
                var upper = carry / Decimal64WideNumber.UnitBase;
                accumulator[index + 2] += upper;
                accumulator[index] -= (ulong)Decimal64WideNumber.UnitBase * Decimal64WideNumber.UnitBase * upper;
                carry -= (ulong)Decimal64WideNumber.UnitBase * upper;
            }

            accumulator[index + 1] += carry;
            accumulator[index] -= Decimal64WideNumber.UnitBase * carry;
        }
    }
}
