// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// Multiplication on wide numbers, following the fast path of decNumber's
/// <c>decMultiplyOp</c>.
/// </summary>
/// <remarks>
/// The partial products are added into 64-bit columns without carrying. The operands can
/// have many units, and a column cannot hold an unlimited number of products: each product
/// is below 10^18, and 64 bits hold 18 of them. decNumber calls that limit <c>FASTLAZY</c>
/// and resolves the carries whenever it is reached.
/// </remarks>
internal static unsafe class Decimal32WideMultiply
{
    /// <summary>
    /// The number of rows that can be added before the carries must be resolved. A product
    /// of two units is below 10^18, and a 64-bit column holds 18 of them.
    /// </summary>
    private const int LazyLimit = 18;

    /// <summary>
    /// The extra columns an accumulator needs beyond the product's own, for the carry of up
    /// to two columns that resolving can push upward.
    /// </summary>
    public const int AccumulatorSlack = 2;

    /// <summary>
    /// Multiplies two finite values. The product is exact, and the caller rounds it.
    /// </summary>
    /// <param name="result">Receives the exact product. Its buffer must hold the units of both operands combined.</param>
    /// <param name="left">The first factor.</param>
    /// <param name="right">The second factor.</param>
    /// <param name="accumulator">
    /// A work buffer of at least the units of both operands plus
    /// <see cref="AccumulatorSlack"/> 64-bit words.
    /// </param>
    public static void Multiply(ref Decimal32WideNumber result, Decimal32WideNumber left, Decimal32WideNumber right,
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
        result.Kind = Decimal32Kind.Finite;
        result.IsNegative = left.IsNegative != right.IsNegative;
        result.Exponent = left.Exponent + right.Exponent;
        result.CountDigits();
    }

    /// <summary>
    /// Reduces each column to a single unit and carries the rest upward. A column can hold
    /// more than one unit's worth, which is what lets 18 rows be added before this runs.
    /// </summary>
    private static void ResolveCarries(ulong* accumulator, int columns)
    {
        for (var index = 0; index < columns; index++)
        {
            if (accumulator[index] < Decimal32WideNumber.UnitBase)
            {
                continue;
            }

            var carry = accumulator[index] / Decimal32WideNumber.UnitBase;

            if (carry >= Decimal32WideNumber.UnitBase)
            {
                // The carry is worth two units. This is rare, but adding many rows makes it
                // possible.
                var upper = carry / Decimal32WideNumber.UnitBase;
                accumulator[index + 2] += upper;
                accumulator[index] -= (ulong)Decimal32WideNumber.UnitBase * Decimal32WideNumber.UnitBase * upper;
                carry -= (ulong)Decimal32WideNumber.UnitBase * upper;
            }

            accumulator[index + 1] += carry;
            accumulator[index] -= Decimal32WideNumber.UnitBase * carry;
        }
    }
}
