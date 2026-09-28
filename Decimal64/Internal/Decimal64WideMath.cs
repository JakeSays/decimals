// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// The engine's operations shaped as expressions: each takes its result slot from the
/// arena, so a series can be written the way it reads on paper.
/// </summary>
/// <remarks>
/// These are thin. Everything they do is in <see cref="Decimal64WideArithmetic"/>,
/// <see cref="Decimal64WideMultiply"/>, <see cref="Decimal64WideDivide"/>,
/// <see cref="Decimal64WideSquareRoot"/>, and <see cref="Decimal64WideRounding"/>; what they add is
/// somewhere to put the result.
/// </remarks>
internal static unsafe class Decimal64WideMath
{
    public static Decimal64WideNumber Add(ref Decimal64WideArena arena, Decimal64WideNumber left, Decimal64WideNumber right,
        Decimal64WideContext context, ref Decimal64Status status)
    {
        var mark = arena.Mark;
        var result = arena.Take();
        var work = arena.TakeUnits();

        Decimal64WideArithmetic.Add(ref result, left, right, false, context, ref status, work);

        // The work slot goes back; the result's does not, since the caller holds it.
        arena.Release(mark + 1);
        return result;
    }

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

    public static Decimal64WideNumber Multiply(ref Decimal64WideArena arena, Decimal64WideNumber left, Decimal64WideNumber right,
        Decimal64WideContext context, ref Decimal64Status status)
    {
        var mark = arena.Mark;
        var result = arena.Take();

        if (!TryMultiplySpecial(ref result, left, right, ref status))
        {
            // The accumulator counts in words rather than units, so it needs twice the
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
    /// Rounds a value into the context, which is decNumber's <c>decCopyFit</c> followed by
    /// <c>decFinish</c>. The series finish this way, having computed far more digits than
    /// they return.
    /// </summary>
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

    public static int Compare(Decimal64WideNumber left, Decimal64WideNumber right, bool ignoreSigns)
    {
        return Decimal64WideArithmetic.Compare(left, right, ignoreSigns);
    }

    public static Decimal64WideNumber FromInt32(ref Decimal64WideArena arena, int value)
    {
        return Decimal64WideNumber.FromInt32(value, arena.TakeUnits());
    }

    public static Decimal64WideNumber One(ref Decimal64WideArena arena)
    {
        return FromInt32(ref arena, 1);
    }

    public static Decimal64WideNumber Infinity(ref Decimal64WideArena arena, bool isNegative)
    {
        var result = arena.Take();
        result.Kind = Decimal64Kind.Infinity;
        result.IsNegative = isNegative;
        return result;
    }

    public static Decimal64WideNumber QuietNaN(ref Decimal64WideArena arena)
    {
        var result = arena.Take();
        result.Kind = Decimal64Kind.QuietNaN;
        return result;
    }

    /// <summary>
    /// The specials a multiplication settles before any digits are looked at.
    /// </summary>
    private static bool TryMultiplySpecial(ref Decimal64WideNumber result, Decimal64WideNumber left,
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
            // An infinity times a zero has no product.
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
