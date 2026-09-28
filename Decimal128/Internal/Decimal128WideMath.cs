// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// The engine's operations shaped as expressions: each takes its result slot from the
/// arena, so a series can be written the way it reads on paper.
/// </summary>
/// <remarks>
/// These are thin. Everything they do is in <see cref="Decimal128WideArithmetic"/>,
/// <see cref="Decimal128WideMultiply"/>, <see cref="Decimal128WideDivide"/>,
/// <see cref="Decimal128WideSquareRoot"/>, and <see cref="Decimal128WideRounding"/>; what
/// they add is somewhere to put the result.
/// </remarks>
internal static unsafe class Decimal128WideMath
{
    public static Decimal128WideNumber Add(ref Decimal128WideArena arena, Decimal128WideNumber left,
        Decimal128WideNumber right, Decimal128WideContext context, ref Decimal128Status status)
    {
        var mark = arena.Mark;
        var result = arena.Take();
        var work = arena.TakeUnits();

        Decimal128WideArithmetic.Add(ref result, left, right, false, context, ref status, work);

        // The work slot goes back; the result's does not, since the caller holds it.
        arena.Release(mark + 1);
        return result;
    }

    public static Decimal128WideNumber Subtract(ref Decimal128WideArena arena, Decimal128WideNumber left,
        Decimal128WideNumber right, Decimal128WideContext context, ref Decimal128Status status)
    {
        var mark = arena.Mark;
        var result = arena.Take();
        var work = arena.TakeUnits();

        Decimal128WideArithmetic.Add(ref result, left, right, true, context, ref status, work);

        arena.Release(mark + 1);
        return result;
    }

    public static Decimal128WideNumber Multiply(ref Decimal128WideArena arena, Decimal128WideNumber left,
        Decimal128WideNumber right, Decimal128WideContext context, ref Decimal128Status status)
    {
        var mark = arena.Mark;
        var result = arena.Take();

        if (!TryMultiplySpecial(ref result, left, right, ref status))
        {
            // The accumulator counts in words rather than units, so it needs twice the
            // slots the product's columns would suggest.
            var accumulator = (ulong*)arena.TakeUnits(4);
            Decimal128WideMultiply.Multiply(ref result, left, right, accumulator);

            var residue = 0;
            Decimal128WideRounding.SetCoefficient(ref result, context.Digits, ref residue, ref status);
            Decimal128WideRounding.Finalize(ref result, residue, context, ref status);
        }

        arena.Release(mark + 1);
        return result;
    }

    public static Decimal128WideNumber Divide(ref Decimal128WideArena arena, Decimal128WideNumber left,
        Decimal128WideNumber right, Decimal128WideContext context, ref Decimal128Status status)
    {
        var mark = arena.Mark;
        var result = arena.Take();
        var numerator = arena.TakeUnits();
        var denominator = arena.TakeUnits();
        var accumulator = arena.TakeUnits();

        Decimal128WideDivide.Divide(ref result, left, right, context, ref status,
            numerator, denominator, accumulator);

        arena.Release(mark + 1);
        return result;
    }

    public static Decimal128WideNumber SquareRoot(ref Decimal128WideArena arena, Decimal128WideNumber value,
        Decimal128WideContext context, ref Decimal128Status status)
    {
        var mark = arena.Mark;
        var result = arena.Take();
        var work = arena.TakeUnits(Decimal128WideSquareRoot.WorkBuffers);
        var accumulator = (ulong*)arena.TakeUnits(4);

        Decimal128WideSquareRoot.SquareRoot(ref result, value, context, ref status,
            work, Decimal128WideArena.SlotUnits, accumulator);

        arena.Release(mark + 1);
        return result;
    }

    /// <summary>
    /// Rounds a value into the context, which is decNumber's <c>decCopyFit</c> followed by
    /// <c>decFinish</c>. The series finish this way, having computed far more digits than
    /// they return.
    /// </summary>
    public static Decimal128WideNumber Round(ref Decimal128WideArena arena, Decimal128WideNumber value, int residue,
        Decimal128WideContext context, ref Decimal128Status status)
    {
        var result = arena.TakeCopy(value);

        if (!result.IsFinite)
        {
            if (residue != 0)
            {
                status |= Decimal128Status.Inexact | Decimal128Status.Rounded;
            }

            return result;
        }

        Decimal128WideRounding.SetCoefficient(ref result, context.Digits, ref residue, ref status);
        Decimal128WideRounding.Finalize(ref result, residue, context, ref status);
        return result;
    }

    public static int Compare(Decimal128WideNumber left, Decimal128WideNumber right, bool ignoreSigns)
    {
        return Decimal128WideArithmetic.Compare(left, right, ignoreSigns);
    }

    public static Decimal128WideNumber FromInt32(ref Decimal128WideArena arena, int value)
    {
        return Decimal128WideNumber.FromInt32(value, arena.TakeUnits());
    }

    public static Decimal128WideNumber One(ref Decimal128WideArena arena)
    {
        return FromInt32(ref arena, 1);
    }

    public static Decimal128WideNumber Infinity(ref Decimal128WideArena arena, bool isNegative)
    {
        var result = arena.Take();
        result.Kind = Decimal128Kind.Infinity;
        result.IsNegative = isNegative;
        return result;
    }

    public static Decimal128WideNumber QuietNaN(ref Decimal128WideArena arena)
    {
        var result = arena.Take();
        result.Kind = Decimal128Kind.QuietNaN;
        return result;
    }

    /// <summary>
    /// The specials a multiplication settles before any digits are looked at.
    /// </summary>
    private static bool TryMultiplySpecial(ref Decimal128WideNumber result, Decimal128WideNumber left,
        Decimal128WideNumber right, ref Decimal128Status status)
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
            status |= Decimal128Status.InvalidOperation;
            result.SetZero();
            result.Kind = Decimal128Kind.QuietNaN;
            result.IsNegative = false;
            return true;
        }

        result.SetZero();
        result.Kind = Decimal128Kind.Infinity;
        result.IsNegative = left.IsNegative != right.IsNegative;
        return true;
    }
}
