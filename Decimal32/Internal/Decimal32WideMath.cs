// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// The engine's operations shaped as expressions: each takes its result slot from the
/// arena, so a series can be written the way it reads on paper.
/// </summary>
/// <remarks>
/// These are thin. Everything they do is in <see cref="Decimal32WideArithmetic"/>,
/// <see cref="Decimal32WideMultiply"/>, <see cref="Decimal32WideDivide"/>,
/// <see cref="Decimal32WideSquareRoot"/>, and <see cref="Decimal32WideRounding"/>; what they add is
/// somewhere to put the result.
/// </remarks>
internal static unsafe class Decimal32WideMath
{
    public static Decimal32WideNumber Add(ref Decimal32WideArena arena, Decimal32WideNumber left, Decimal32WideNumber right,
        Decimal32WideContext context, ref Decimal32Status status)
    {
        var mark = arena.Mark;
        var result = arena.Take();
        var work = arena.TakeUnits();

        Decimal32WideArithmetic.Add(ref result, left, right, false, context, ref status, work);

        // The work slot goes back; the result's does not, since the caller holds it.
        arena.Release(mark + 1);
        return result;
    }

    public static Decimal32WideNumber Subtract(ref Decimal32WideArena arena, Decimal32WideNumber left, Decimal32WideNumber right,
        Decimal32WideContext context, ref Decimal32Status status)
    {
        var mark = arena.Mark;
        var result = arena.Take();
        var work = arena.TakeUnits();

        Decimal32WideArithmetic.Add(ref result, left, right, true, context, ref status, work);

        arena.Release(mark + 1);
        return result;
    }

    public static Decimal32WideNumber Multiply(ref Decimal32WideArena arena, Decimal32WideNumber left, Decimal32WideNumber right,
        Decimal32WideContext context, ref Decimal32Status status)
    {
        var mark = arena.Mark;
        var result = arena.Take();

        if (!TryMultiplySpecial(ref result, left, right, ref status))
        {
            // The accumulator counts in words rather than units, so it needs twice the
            // slots the product's columns would suggest.
            var accumulator = (ulong*)arena.TakeUnits(4);
            Decimal32WideMultiply.Multiply(ref result, left, right, accumulator);

            var residue = 0;
            Decimal32WideRounding.SetCoefficient(ref result, context.Digits, ref residue, ref status);
            Decimal32WideRounding.Finalize(ref result, residue, context, ref status);
        }

        arena.Release(mark + 1);
        return result;
    }

    public static Decimal32WideNumber Divide(ref Decimal32WideArena arena, Decimal32WideNumber left, Decimal32WideNumber right,
        Decimal32WideContext context, ref Decimal32Status status)
    {
        var mark = arena.Mark;
        var result = arena.Take();
        var numerator = arena.TakeUnits();
        var denominator = arena.TakeUnits();
        var accumulator = arena.TakeUnits();

        Decimal32WideDivide.Divide(ref result, left, right, context, ref status,
            numerator, denominator, accumulator);

        arena.Release(mark + 1);
        return result;
    }

    public static Decimal32WideNumber SquareRoot(ref Decimal32WideArena arena, Decimal32WideNumber value,
        Decimal32WideContext context, ref Decimal32Status status)
    {
        var mark = arena.Mark;
        var result = arena.Take();
        var work = arena.TakeUnits(Decimal32WideSquareRoot.WorkBuffers);
        var accumulator = (ulong*)arena.TakeUnits(4);

        Decimal32WideSquareRoot.SquareRoot(ref result, value, context, ref status,
            work, Decimal32WideArena.SlotUnits, accumulator);

        arena.Release(mark + 1);
        return result;
    }

    /// <summary>
    /// Rounds a value into the context, which is decNumber's <c>decCopyFit</c> followed by
    /// <c>decFinish</c>. The series finish this way, having computed far more digits than
    /// they return.
    /// </summary>
    public static Decimal32WideNumber Round(ref Decimal32WideArena arena, Decimal32WideNumber value, int residue,
        Decimal32WideContext context, ref Decimal32Status status)
    {
        var result = arena.TakeCopy(value);

        if (!result.IsFinite)
        {
            if (residue != 0)
            {
                status |= Decimal32Status.Inexact | Decimal32Status.Rounded;
            }

            return result;
        }

        Decimal32WideRounding.SetCoefficient(ref result, context.Digits, ref residue, ref status);
        Decimal32WideRounding.Finalize(ref result, residue, context, ref status);
        return result;
    }

    public static int Compare(Decimal32WideNumber left, Decimal32WideNumber right, bool ignoreSigns)
    {
        return Decimal32WideArithmetic.Compare(left, right, ignoreSigns);
    }

    public static Decimal32WideNumber FromInt32(ref Decimal32WideArena arena, int value)
    {
        return Decimal32WideNumber.FromInt32(value, arena.TakeUnits());
    }

    public static Decimal32WideNumber One(ref Decimal32WideArena arena)
    {
        return FromInt32(ref arena, 1);
    }

    public static Decimal32WideNumber Infinity(ref Decimal32WideArena arena, bool isNegative)
    {
        var result = arena.Take();
        result.Kind = Decimal32Kind.Infinity;
        result.IsNegative = isNegative;
        return result;
    }

    public static Decimal32WideNumber QuietNaN(ref Decimal32WideArena arena)
    {
        var result = arena.Take();
        result.Kind = Decimal32Kind.QuietNaN;
        return result;
    }

    /// <summary>
    /// The specials a multiplication settles before any digits are looked at.
    /// </summary>
    private static bool TryMultiplySpecial(ref Decimal32WideNumber result, Decimal32WideNumber left,
        Decimal32WideNumber right, ref Decimal32Status status)
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
            status |= Decimal32Status.InvalidOperation;
            result.SetZero();
            result.Kind = Decimal32Kind.QuietNaN;
            result.IsNegative = false;
            return true;
        }

        result.SetZero();
        result.Kind = Decimal32Kind.Infinity;
        result.IsNegative = left.IsNegative != right.IsNegative;
        return true;
    }
}
