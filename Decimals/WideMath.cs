// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// The engine's operations shaped as expressions: each takes its result slot from the
/// arena, so a series can be written the way it reads on paper.
/// </summary>
/// <remarks>
/// These are thin. Everything they do is in <see cref="WideArithmetic"/>,
/// <see cref="WideMultiply"/>, <see cref="WideDivide"/>, <see cref="WideSquareRoot"/>, and
/// <see cref="WideRounding"/>; what they add is somewhere to put the answer.
/// </remarks>
internal static unsafe class WideMath
{
    public static WideNumber Add(ref WideArena arena, WideNumber left, WideNumber right,
        WideContext context, ref DecimalStatus status)
    {
        var mark = arena.Mark;
        var result = arena.Take();
        var work = arena.TakeUnits();

        WideArithmetic.Add(ref result, left, right, false, context, ref status, work);

        // The work slot goes back; the result's does not, since the caller holds it.
        arena.Release(mark + 1);
        return result;
    }

    public static WideNumber Subtract(ref WideArena arena, WideNumber left, WideNumber right,
        WideContext context, ref DecimalStatus status)
    {
        var mark = arena.Mark;
        var result = arena.Take();
        var work = arena.TakeUnits();

        WideArithmetic.Add(ref result, left, right, true, context, ref status, work);

        arena.Release(mark + 1);
        return result;
    }

    public static WideNumber Multiply(ref WideArena arena, WideNumber left, WideNumber right,
        WideContext context, ref DecimalStatus status)
    {
        var mark = arena.Mark;
        var result = arena.Take();

        if (!TryMultiplySpecial(ref result, left, right, ref status))
        {
            // The accumulator counts in words rather than units, so it needs twice the
            // slots the product's columns would suggest.
            var accumulator = (ulong*)arena.TakeUnits(4);
            WideMultiply.Multiply(ref result, left, right, accumulator);

            var residue = 0;
            WideRounding.SetCoefficient(ref result, context.Digits, ref residue, ref status);
            WideRounding.Finalize(ref result, residue, context, ref status);
        }

        arena.Release(mark + 1);
        return result;
    }

    public static WideNumber Divide(ref WideArena arena, WideNumber left, WideNumber right,
        WideContext context, ref DecimalStatus status)
    {
        var mark = arena.Mark;
        var result = arena.Take();
        var numerator = arena.TakeUnits();
        var denominator = arena.TakeUnits();
        var accumulator = arena.TakeUnits();

        WideDivide.Divide(ref result, left, right, context, ref status,
            numerator, denominator, accumulator);

        arena.Release(mark + 1);
        return result;
    }

    public static WideNumber SquareRoot(ref WideArena arena, WideNumber value,
        WideContext context, ref DecimalStatus status)
    {
        var mark = arena.Mark;
        var result = arena.Take();
        var work = arena.TakeUnits(WideSquareRoot.WorkBuffers);
        var accumulator = (ulong*)arena.TakeUnits(4);

        WideSquareRoot.SquareRoot(ref result, value, context, ref status,
            work, WideArena.SlotUnits, accumulator);

        arena.Release(mark + 1);
        return result;
    }

    /// <summary>
    /// Rounds a value into the context, which is decNumber's <c>decCopyFit</c> followed by
    /// <c>decFinish</c>. The series finish this way, having computed far more digits than
    /// they return.
    /// </summary>
    public static WideNumber Round(ref WideArena arena, WideNumber value, int residue,
        WideContext context, ref DecimalStatus status)
    {
        var result = arena.TakeCopy(value);

        if (!result.IsFinite)
        {
            if (residue != 0)
            {
                status |= DecimalStatus.Inexact | DecimalStatus.Rounded;
            }

            return result;
        }

        WideRounding.SetCoefficient(ref result, context.Digits, ref residue, ref status);
        WideRounding.Finalize(ref result, residue, context, ref status);
        return result;
    }

    public static int Compare(WideNumber left, WideNumber right, bool ignoreSigns)
    {
        return WideArithmetic.Compare(left, right, ignoreSigns);
    }

    public static WideNumber FromInt32(ref WideArena arena, int value)
    {
        return WideNumber.FromInt32(value, arena.TakeUnits());
    }

    public static WideNumber One(ref WideArena arena)
    {
        return FromInt32(ref arena, 1);
    }

    public static WideNumber Zero(ref WideArena arena)
    {
        return arena.Take();
    }

    public static WideNumber Infinity(ref WideArena arena, bool isNegative)
    {
        var result = arena.Take();
        result.Kind = DecimalKind.Infinity;
        result.IsNegative = isNegative;
        return result;
    }

    public static WideNumber QuietNaN(ref WideArena arena)
    {
        var result = arena.Take();
        result.Kind = DecimalKind.QuietNaN;
        return result;
    }

    /// <summary>
    /// The specials a multiplication settles before any digits are looked at.
    /// </summary>
    private static bool TryMultiplySpecial(ref WideNumber result, WideNumber left,
        WideNumber right, ref DecimalStatus status)
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
            // An infinity times a zero has no product.
            status |= DecimalStatus.InvalidOperation;
            result.SetZero();
            result.Kind = DecimalKind.QuietNaN;
            result.IsNegative = false;
            return true;
        }

        result.SetZero();
        result.Kind = DecimalKind.Infinity;
        result.IsNegative = left.IsNegative != right.IsNegative;
        return true;
    }
}
