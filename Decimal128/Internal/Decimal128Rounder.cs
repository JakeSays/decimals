// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Decimals.Internal;

/// <summary>
/// Discards digits into a residue, and decides for each of the eight rounding modes
/// whether a residue increments the last kept digit.
/// </summary>
/// <remarks>
/// The residue is computed from bits instead of chosen by comparisons. Which side of half
/// a discarded part falls on depends on the data, so a branch on it mispredicts about half
/// the time. Bit 0 of a residue means non-zero, bit 1 means above half, and bit 2 means at
/// least half. This gives below half the value 1, half the value 5, and above half the
/// value 7.
/// </remarks>
internal static class Decimal128Rounder
{
    private const int NonZeroBit = 0;

    private const int AboveHalfBit = 1;

    private const int AtLeastHalfBit = 2;

    /// <summary>
    /// The result of <see cref="Flip"/> for each residue value: below half and above half
    /// swap, and every other value stays the same.
    /// </summary>
    private static ReadOnlySpan<byte> Flipped => [0, 7, 2, 3, 4, 5, 6, 1];

    /// <summary>
    /// The residue of a discarded part with nothing below it. <paramref name="half"/> is
    /// half of the power of ten that was divided out, and is never zero.
    /// </summary>
    /// <param name="discarded">The discarded digits, as an integer.</param>
    /// <param name="half">Half of the power of ten the discarded digits were divided out by.</param>
    /// <returns>Where the discarded part lies relative to half a unit of the kept digits.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Residue Of(ulong discarded, ulong half)
    {
        var nonZero = Unsafe.BitCast<bool, byte>(discarded != 0);
        var atLeastHalf = Unsafe.BitCast<bool, byte>(discarded >= half);
        var aboveHalf = Unsafe.BitCast<bool, byte>(discarded > half);
        return (Decimal128Residue)((nonZero << NonZeroBit) | (atLeastHalf << AtLeastHalfBit)
            | (aboveHalf << AboveHalfBit));
    }

    /// <summary>The residue of a two-word discarded part with nothing below it.</summary>
    /// <param name="discarded">The discarded digits, as an integer.</param>
    /// <param name="half">Half of the power of ten the discarded digits were divided out by. It is never zero.</param>
    /// <returns>Where the discarded part lies relative to half a unit of the kept digits.</returns>
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
    /// The residue of a discarded part that has an earlier residue below it. The earlier
    /// residue is too small to reach half by itself. It can only turn an exact half into
    /// above half, or a zero part into below half.
    /// </summary>
    /// <param name="discarded">The discarded digits, as an integer.</param>
    /// <param name="half">Half of the power of ten the discarded digits were divided out by.</param>
    /// <param name="below">The residue of digits discarded earlier, below these.</param>
    /// <returns>Where the whole discarded part lies relative to half a unit of the kept digits.</returns>
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

    /// <summary>The residue of a two-word discarded part that has an earlier residue below it.</summary>
    /// <param name="discarded">The discarded digits, as an integer.</param>
    /// <param name="half">Half of the power of ten the discarded digits were divided out by.</param>
    /// <param name="below">The residue of digits discarded earlier, below these.</param>
    /// <returns>Where the whole discarded part lies relative to half a unit of the kept digits.</returns>
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
    /// The residue when the whole value is discarded and lies below half. It is below half
    /// if the value or the residue under it is non-zero, and exact otherwise.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Decimal128Residue Whole(bool nonZero, Decimal128Residue below)
    {
        var inexact = Unsafe.BitCast<bool, byte>(nonZero)
            | Unsafe.BitCast<bool, byte>(below != Decimal128Residue.Exact);
        return (Decimal128Residue)inexact;
    }

    /// <summary>
    /// The residue of 1 minus the fraction a residue represents. Subtracting an inexact
    /// operand leaves this: below half becomes above half, and half stays half. Only the
    /// category matters, so no arithmetic is needed.
    /// </summary>
    /// <param name="residue">The residue of the fraction.</param>
    /// <returns>The residue of 1 minus the fraction.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Residue Flip(Decimal128Residue residue)
    {
        return (Decimal128Residue)Unsafe.Add(ref MemoryMarshal.GetReference(Flipped), (int)residue);
    }

    /// <summary>
    /// Drops the last <paramref name="count"/> digits of a coefficient and folds them, with
    /// any residue already below them, into a new residue. The count is at least 1. If the
    /// count is more than the coefficient's digits, everything is discarded and the residue
    /// is below half. A count above 19 takes two divisions. The second division's residue
    /// is combined with the first's, like any later discard.
    /// </summary>
    /// <remarks>
    /// A coefficient that fits in one word is divided with a reciprocal, which is a
    /// multiply and a shift. The branch that chooses this path depends on the data, but the
    /// path saves much more than the branch costs. The second of two divisions is also a
    /// one-word division, because for every coefficient the arithmetic produces, what is
    /// left above 19 digits fits in one word.
    /// </remarks>
    /// <param name="coefficient">The coefficient to shorten.</param>
    /// <param name="count">The number of digits to drop. It must be at least 1.</param>
    /// <param name="below">The residue of digits discarded earlier, below the dropped digits.</param>
    /// <param name="residue">Receives the residue of everything discarded.</param>
    /// <returns>The kept digits.</returns>
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
    /// Drops the last <paramref name="count"/> digits of a four-word value in the same way.
    /// If the count is more than four words' digits, everything is discarded.
    /// </summary>
    /// <remarks>
    /// For a count of 20 or more, it first does one 3-by-2 division by the largest power of
    /// ten that fits the count, when the quotient fits in two words. That is true for every
    /// value the arithmetic produces. Any remaining count is then dropped from the quotient.
    /// A smaller count, or a value too wide for that division, is divided by 10^19 at a
    /// time.
    /// </remarks>
    /// <param name="value">The four-word value to shorten.</param>
    /// <param name="count">The number of digits to drop. It must be at least 1.</param>
    /// <param name="below">The residue of digits discarded earlier, below the dropped digits.</param>
    /// <param name="residue">Receives the residue of everything discarded.</param>
    /// <returns>The kept digits.</returns>
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
            if (Decimal128Tables.DivRemWidePowerOfTenIfFits(value, power, out var kept, out var discarded))
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
    /// True if the rounding mode increments the last kept digit. An exact residue never
    /// increments it, so the caller does not need to check the residue first.
    /// </summary>
    /// <remarks>
    /// The half-even case is the default and the most common. It uses bit operations on the
    /// residue instead of comparisons, because whether a residue is above half depends on
    /// the data.
    /// </remarks>
    /// <param name="coefficient">The kept digits.</param>
    /// <param name="residue">The residue of the discarded digits.</param>
    /// <param name="negative">Whether the value is negative.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <returns>True if the coefficient must be incremented.</returns>
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
                // ZeroFiveUp increments only when the last kept digit is 0 or 5. This keeps
                // the result correct if it is rounded again later.
                var last = coefficient.LastDigit();
                return inexact & ((last == 0) | (last == 5));
        }
    }
}
