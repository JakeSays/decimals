// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// The wide-number operations in expression form. Each takes its result slot from the
/// arena, so a series can be written the way it reads on paper.
/// </summary>
/// <remarks>
/// These methods are thin wrappers. The work is done in
/// <see cref="Decimal64WideArithmetic"/>, <see cref="Decimal64WideMultiply"/>,
/// <see cref="Decimal64WideDivide"/>, <see cref="Decimal64WideSquareRoot"/>, and
/// <see cref="Decimal64WideRounding"/>. The wrappers provide a slot for the result.
/// </remarks>
internal static unsafe class Decimal64WideMath
{
    /// <summary>Adds two values and rounds the sum to the context.</summary>
    /// <param name="arena">The arena that provides the result slot and a work buffer.</param>
    /// <param name="left">The first operand.</param>
    /// <param name="right">The second operand.</param>
    /// <param name="context">The precision, rounding, and exponent limits to apply.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The rounded sum, in a new slot the caller holds.</returns>
    public static Decimal64WideNumber Add(ref Decimal64WideArena arena, Decimal64WideNumber left, Decimal64WideNumber right,
        Decimal64WideContext context, ref Decimal64Status status)
    {
        var mark = arena.Mark;
        var result = arena.Take();
        var work = arena.TakeUnits();

        Decimal64WideArithmetic.Add(ref result, left, right, false, context, ref status, work);

        // Release the work slot. The result's slot stays taken, because the caller holds it.
        arena.Release(mark + 1);
        return result;
    }

    /// <summary>Subtracts the second value from the first and rounds the difference to the context.</summary>
    /// <param name="arena">The arena that provides the result slot and a work buffer.</param>
    /// <param name="left">The value to subtract from.</param>
    /// <param name="right">The value to subtract.</param>
    /// <param name="context">The precision, rounding, and exponent limits to apply.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The rounded difference, in a new slot the caller holds.</returns>
    public static Decimal64WideNumber Subtract(ref Decimal64WideArena arena, Decimal64WideNumber left, Decimal64WideNumber right,
        Decimal64WideContext context, ref Decimal64Status status)
    {
        var mark = arena.Mark;
        var result = arena.Take();
        var work = arena.TakeUnits();

        Decimal64WideArithmetic.Add(ref result, left, right, true, context, ref status, work);

        arena.Release(mark + 1);
        return result;
    }

    /// <summary>Multiplies two values and rounds the product to the context.</summary>
    /// <param name="arena">The arena that provides the result slot and a work buffer.</param>
    /// <param name="left">The first factor.</param>
    /// <param name="right">The second factor.</param>
    /// <param name="context">The precision, rounding, and exponent limits to apply.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The rounded product, in a new slot the caller holds.</returns>
    public static Decimal64WideNumber Multiply(ref Decimal64WideArena arena, Decimal64WideNumber left, Decimal64WideNumber right,
        Decimal64WideContext context, ref Decimal64Status status)
    {
        var mark = arena.Mark;
        var result = arena.Take();

        if (!MultiplySpecial(ref result, left, right, ref status))
        {
            // The accumulator counts 64-bit words, not 32-bit units, so it needs twice the
            // slots the product's columns would suggest.
            var accumulator = (ulong*)arena.TakeUnits(4);
            Decimal64WideMultiply.Multiply(ref result, left, right, accumulator);

            var residue = 0;
            Decimal64WideRounding.SetCoefficient(ref result, context.Digits, ref residue, ref status);
            Decimal64WideRounding.Finalize(ref result, residue, context, ref status);
        }

        arena.Release(mark + 1);
        return result;
    }

    /// <summary>Divides the first value by the second and rounds the quotient to the context.</summary>
    /// <param name="arena">The arena that provides the result slot and work buffers.</param>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <param name="context">The precision, rounding, and exponent limits to apply.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The rounded quotient, in a new slot the caller holds.</returns>
    public static Decimal64WideNumber Divide(ref Decimal64WideArena arena, Decimal64WideNumber left, Decimal64WideNumber right,
        Decimal64WideContext context, ref Decimal64Status status)
    {
        var mark = arena.Mark;
        var result = arena.Take();
        var numerator = arena.TakeUnits();
        var denominator = arena.TakeUnits();
        var accumulator = arena.TakeUnits();

        Decimal64WideDivide.Divide(ref result, left, right, context, ref status,
            numerator, denominator, accumulator);

        arena.Release(mark + 1);
        return result;
    }

    /// <summary>The square root of a value, rounded to the context.</summary>
    /// <param name="arena">The arena that provides the result slot and work buffers.</param>
    /// <param name="value">The operand.</param>
    /// <param name="context">The precision, rounding, and exponent limits to apply.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The rounded square root, in a new slot the caller holds.</returns>
    public static Decimal64WideNumber SquareRoot(ref Decimal64WideArena arena, Decimal64WideNumber value,
        Decimal64WideContext context, ref Decimal64Status status)
    {
        var mark = arena.Mark;
        var result = arena.Take();
        var work = arena.TakeUnits(Decimal64WideSquareRoot.WorkBuffers);
        var accumulator = (ulong*)arena.TakeUnits(4);

        Decimal64WideSquareRoot.SquareRoot(ref result, value, context, ref status,
            work, Decimal64WideArena.SlotUnits, accumulator);

        arena.Release(mark + 1);
        return result;
    }

    /// <summary>
    /// Rounds a value to the context: decNumber's <c>decCopyFit</c> followed by
    /// <c>decFinish</c>. The series finish this way, after computing many more digits than
    /// they return.
    /// </summary>
    /// <param name="arena">The arena that provides the result slot.</param>
    /// <param name="value">The value to round.</param>
    /// <param name="residue">
    /// The residue of the digits already discarded below <paramref name="value"/>, in the
    /// form <see cref="Decimal64WideRounding"/> describes. 0 means none were discarded.
    /// </param>
    /// <param name="context">The precision, rounding, and exponent limits to apply.</param>
    /// <param name="status">Receives the conditions the rounding raises.</param>
    /// <returns>The rounded value, in a new slot the caller holds.</returns>
    public static Decimal64WideNumber Round(ref Decimal64WideArena arena, Decimal64WideNumber value, int residue,
        Decimal64WideContext context, ref Decimal64Status status)
    {
        var result = arena.TakeCopy(value);

        if (!result.IsFinite)
        {
            if (residue != 0)
            {
                status |= Decimal64Status.Inexact | Decimal64Status.Rounded;
            }

            return result;
        }

        Decimal64WideRounding.SetCoefficient(ref result, context.Digits, ref residue, ref status);
        Decimal64WideRounding.Finalize(ref result, residue, context, ref status);
        return result;
    }

    /// <summary>Compares two values numerically. Neither may be a NaN.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <param name="ignoreSigns">True to compare magnitudes instead of signed values.</param>
    /// <returns>-1 if <paramref name="left"/> is smaller, 0 if they are equal, or 1 if it is larger.</returns>
    public static int Compare(Decimal64WideNumber left, Decimal64WideNumber right, bool ignoreSigns)
    {
        return Decimal64WideArithmetic.Compare(left, right, ignoreSigns);
    }

    /// <summary>Creates a value from an integer.</summary>
    /// <param name="arena">The arena that provides the slot.</param>
    /// <param name="value">The integer.</param>
    /// <returns>The integer as a wide number with exponent zero, in a new slot the caller holds.</returns>
    public static Decimal64WideNumber FromInt32(ref Decimal64WideArena arena, int value)
    {
        return Decimal64WideNumber.FromInt32(value, arena.TakeUnits());
    }

    /// <summary>Creates the value 1.</summary>
    /// <param name="arena">The arena that provides the slot.</param>
    /// <returns>1 with exponent zero, in a new slot the caller holds.</returns>
    public static Decimal64WideNumber One(ref Decimal64WideArena arena)
    {
        return FromInt32(ref arena, 1);
    }

    /// <summary>Creates an infinity.</summary>
    /// <param name="arena">The arena that provides the slot.</param>
    /// <param name="isNegative">True for negative infinity.</param>
    /// <returns>The infinity, in a new slot the caller holds.</returns>
    public static Decimal64WideNumber Infinity(ref Decimal64WideArena arena, bool isNegative)
    {
        var result = arena.Take();
        result.Kind = Decimal64Kind.Infinity;
        result.IsNegative = isNegative;
        return result;
    }

    /// <summary>Creates a positive quiet NaN with no payload.</summary>
    /// <param name="arena">The arena that provides the slot.</param>
    /// <returns>The NaN, in a new slot the caller holds.</returns>
    public static Decimal64WideNumber QuietNaN(ref Decimal64WideArena arena)
    {
        var result = arena.Take();
        result.Kind = Decimal64Kind.QuietNaN;
        return result;
    }

    /// <summary>
    /// Handles the special values of a multiplication before any digits are read. Returns
    /// true if <paramref name="result"/> was set.
    /// </summary>
    private static bool MultiplySpecial(ref Decimal64WideNumber result, Decimal64WideNumber left,
        Decimal64WideNumber right, ref Decimal64Status status)
    {
        if (left.IsNaN)
        {
            result.CopyFrom(left);
            return true;
        }

        if (right.IsNaN)
        {
            result.CopyFrom(right);
            return true;
        }

        if (!left.IsInfinity && !right.IsInfinity)
        {
            return false;
        }

        if ((left.IsInfinity && right.IsZero) || (right.IsInfinity && left.IsZero))
        {
            // An infinity times zero is invalid.
            status |= Decimal64Status.InvalidOperation;
            result.SetZero();
            result.Kind = Decimal64Kind.QuietNaN;
            result.IsNegative = false;
            return true;
        }

        result.SetZero();
        result.Kind = Decimal64Kind.Infinity;
        result.IsNegative = left.IsNegative != right.IsNegative;
        return true;
    }
}
