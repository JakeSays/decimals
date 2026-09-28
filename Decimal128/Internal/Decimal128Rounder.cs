// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Decimals.Internal;

/// <summary>
/// Discarding digits into a residue, and deciding what a residue does to the last digit
/// kept under each of the eight rounding modes.
/// </summary>
/// <remarks>
/// The classifications here are computed from the bits of a residue rather than chosen by
/// comparisons: which side of half a discarded part falls on is decided by the data, and
/// a branch on it mispredicts as often as not. A residue's value has its non-zero bit at
/// the bottom, its at-least-half bit two above that, and its above-half bit between them,
/// which is what makes below half a one, half a five, and above half a seven.
/// </remarks>
internal static class Decimal128Rounder
{
    private const int NonZeroBit = 0;

    private const int AboveHalfBit = 1;

    private const int AtLeastHalfBit = 2;

    /// <summary>
    /// The flipped residue at each value: below half and above half exchanged, and every
    /// other value as it is.
    /// </summary>
    private static ReadOnlySpan<byte> Flipped => [0, 7, 2, 3, 4, 5, 6, 1];

    /// <summary>
    /// The residue of a discarded part that stands alone, measured against half of the
    /// power it was discarded by, which is never zero.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Residue Of(ulong discarded, ulong half)
    {
        var nonZero = Unsafe.BitCast<bool, byte>(discarded != 0);
        var atLeastHalf = Unsafe.BitCast<bool, byte>(discarded >= half);
        var aboveHalf = Unsafe.BitCast<bool, byte>(discarded > half);
        return (Decimal128Residue)((nonZero << NonZeroBit) | (atLeastHalf << AtLeastHalfBit)
            | (aboveHalf << AboveHalfBit));
    }

    /// <summary>The same, for a discarded part and a halfway point of two words.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Residue Of(Decimal128Integer discarded, Decimal128Integer half)
    {
        var nonZero = Unsafe.BitCast<bool, byte>(!discarded.IsZero);
        var atLeastHalf = Unsafe.BitCast<bool, byte>(discarded >= half);
        var aboveHalf = Unsafe.BitCast<bool, byte>(discarded > half);
        return (Decimal128Residue)((nonZero << NonZeroBit) | (atLeastHalf << AtLeastHalfBit)
            | (aboveHalf << AboveHalfBit));
    }

    /// <summary>
    /// The residue of a discarded part with an older residue already lying below it. The
    /// older one is too far down to reach halfway on its own, so it can only break a tie and
    /// make a zero part non-zero.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Residue Combine(ulong discarded, ulong half, Decimal128Residue below)
    {
        var belowInexact = Unsafe.BitCast<bool, byte>(below != Decimal128Residue.Exact);
        var nonZero = Unsafe.BitCast<bool, byte>(discarded != 0) | belowInexact;
        var atLeastHalf = Unsafe.BitCast<bool, byte>(discarded >= half);
        var aboveHalf = Unsafe.BitCast<bool, byte>(discarded > half)
            | (Unsafe.BitCast<bool, byte>(discarded == half) & belowInexact);
        return (Decimal128Residue)((nonZero << NonZeroBit) | (atLeastHalf << AtLeastHalfBit)
            | (aboveHalf << AboveHalfBit));
    }

    /// <summary>The same, for a discarded part and a halfway point of two words.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Residue Combine(Decimal128Integer discarded, Decimal128Integer half,
        Decimal128Residue below)
    {
        var belowInexact = Unsafe.BitCast<bool, byte>(below != Decimal128Residue.Exact);
        var nonZero = Unsafe.BitCast<bool, byte>(!discarded.IsZero) | belowInexact;
        var atLeastHalf = Unsafe.BitCast<bool, byte>(discarded >= half);
        var aboveHalf = Unsafe.BitCast<bool, byte>(discarded > half)
            | (Unsafe.BitCast<bool, byte>(discarded == half) & belowInexact);
        return (Decimal128Residue)((nonZero << NonZeroBit) | (atLeastHalf << AtLeastHalfBit)
            | (aboveHalf << AboveHalfBit));
    }

    /// <summary>
    /// The residue of a part discarded whole from below half: below half when there is
    /// anything in it or in the residue under it, and exact otherwise.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Decimal128Residue Whole(bool nonZero, Decimal128Residue below)
    {
        var inexact = Unsafe.BitCast<bool, byte>(nonZero)
            | Unsafe.BitCast<bool, byte>(below != Decimal128Residue.Exact);
        return (Decimal128Residue)inexact;
    }

    /// <summary>
    /// The residue of one less the fraction a residue stands for, which is what subtracting
    /// an inexact operand leaves: a part below half comes out above it, and a half stays a
    /// half. Only the category matters, so no arithmetic is needed to flip it.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Residue Flip(Decimal128Residue residue)
    {
        return (Decimal128Residue)Unsafe.Add(ref MemoryMarshal.GetReference(Flipped), (int)residue);
    }

    /// <summary>
    /// Drops the last <paramref name="count"/> digits of a coefficient, folding them and any
    /// residue already below them into a new residue. The count is at least one. A count
    /// past the width of the coefficient discards everything, and everything is then below
    /// half. Past one word's power the drop is two divisions, and the second's residue is
    /// combined with the first's the way any later discard is combined with an older
    /// residue.
    /// </summary>
    /// <remarks>
    /// A coefficient that fits one word is divided by a reciprocal, which is a multiply
    /// and a shift; the branch that picks that path is decided by the data, but the path
    /// saves far more than the branch costs. The second of the two divisions is a one-word
    /// division too, since what is left above nineteen digits fits a word for any
    /// coefficient the arithmetic forms.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Decimal128Integer DropDigits(Decimal128Integer coefficient, int count, Decimal128Residue below,
        out Decimal128Residue residue)
    {
        if (coefficient.High == 0)
        {
            if (count <= Decimal128Tables.MaxPower)
            {
                var wordKept = Decimal128Tables.DivRemPowerOfTen(coefficient.Low, count, out var wordDiscarded);
                residue = Combine(wordDiscarded, Decimal128Tables.HalfPowerOfTen(count), below);
                return Decimal128Integer.FromUInt64(wordKept);
            }

            residue = Whole(coefficient.Low != 0, below);
            return Decimal128Integer.Zero;
        }

        if (count > Decimal128Tables.MaxWidePower)
        {
            residue = Whole(true, below);
            return Decimal128Integer.Zero;
        }

        if (count <= Decimal128Tables.MaxPower)
        {
            var kept = Decimal128Tables.DivRemPowerOfTen(coefficient, count, out var discarded);
            residue = Combine(discarded, Decimal128Tables.HalfPowerOfTen(count), below);
            return kept;
        }

        var first = Decimal128Tables.DivRemPowerOfTen(coefficient, Decimal128Tables.MaxPower, out var lowDiscarded);
        var lowResidue = Combine(lowDiscarded, Decimal128Tables.HalfPowerOfTen(Decimal128Tables.MaxPower), below);

        var rest = count - Decimal128Tables.MaxPower;
        if (first.High == 0)
        {
            var secondWord = Decimal128Tables.DivRemPowerOfTen(first.Low, rest, out var wordDiscarded);
            residue = Combine(wordDiscarded, Decimal128Tables.HalfPowerOfTen(rest), lowResidue);
            return Decimal128Integer.FromUInt64(secondWord);
        }

        var second = Decimal128Tables.DivRemPowerOfTen(first, rest, out var highDiscarded);
        residue = Combine(highDiscarded, Decimal128Tables.HalfPowerOfTen(rest), lowResidue);
        return second;
    }

    /// <summary>
    /// Drops the last <paramref name="count"/> digits of a four-word value the same way. A
    /// count past the width of four words discards everything.
    /// </summary>
    /// <remarks>
    /// A count of twenty or more is one three-by-two division by the widest power that
    /// fits the count, when the quotient fits two words, which it does for every value
    /// the arithmetic forms; whatever count is left then comes off that quotient. A
    /// smaller count, or a value too wide for that, goes a word's power at a time.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Decimal128LongInteger DropDigits(Decimal128LongInteger value, int count, Decimal128Residue below,
        out Decimal128Residue residue)
    {
        if (count > Decimal128Tables.MaxLongPower)
        {
            residue = Whole(!value.IsZero, below);
            return default;
        }

        if (count > Decimal128Tables.MaxPower)
        {
            var power = Math.Min(count, Decimal128Tables.MaxWidePower);
            if (Decimal128Tables.TryDivRemWidePowerOfTen(value, power, out var kept, out var discarded))
            {
                residue = Combine(discarded, Decimal128Tables.WideHalfPowerOfTen(power), below);
                var rest = count - power;
                if (rest == 0)
                {
                    return Decimal128LongInteger.FromInteger(kept);
                }

                return Decimal128LongInteger.FromInteger(DropDigits(kept, rest, residue, out residue));
            }
        }

        residue = below;
        while (count > 0)
        {
            var step = Math.Min(count, Decimal128Tables.MaxPower);
            value = Decimal128Tables.DivRemPowerOfTen(value, step, out var discarded);
            residue = Combine(discarded, Decimal128Tables.HalfPowerOfTen(step), residue);
            count -= step;
        }

        return value;
    }

    /// <summary>
    /// Whether the last digit kept moves up under the rounding mode. An exact residue never
    /// moves it, so the caller need not look at the residue first.
    /// </summary>
    /// <remarks>
    /// The half-even case, which is the default and so the common one, is bit operations
    /// on the residue rather than comparisons: whether a residue is above half is decided
    /// by the data.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ShouldIncrement(Decimal128Integer coefficient, Decimal128Residue residue, bool negative,
        Decimal128Rounding rounding)
    {
        var inexact = residue != Decimal128Residue.Exact;

        switch (rounding)
        {
            case Decimal128Rounding.HalfEven:
                var above = Unsafe.BitCast<bool, byte>(residue == Decimal128Residue.AboveHalf);
                var half = Unsafe.BitCast<bool, byte>(residue == Decimal128Residue.Half);
                var odd = (byte)(coefficient.Low & 1);
                return (above | (half & odd)) != 0;
            case Decimal128Rounding.HalfUp:
                return residue >= Decimal128Residue.Half;
            case Decimal128Rounding.HalfDown:
                return residue == Decimal128Residue.AboveHalf;
            case Decimal128Rounding.Down:
                return false;
            case Decimal128Rounding.Up:
                return inexact;
            case Decimal128Rounding.Ceiling:
                return inexact & !negative;
            case Decimal128Rounding.Floor:
                return inexact & negative;
            default:
                // ZeroFiveUp keeps the discarded part recoverable by a later rounding: it
                // only moves the last digit when that digit is a 0 or a 5.
                var last = coefficient.LastDigit();
                return inexact & ((last == 0) | (last == 5));
        }
    }
}
