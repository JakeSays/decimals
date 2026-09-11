// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// Shortening a coefficient and settling it into a context: decNumber's <c>decSetCoeff</c>,
/// <c>decApplyRound</c>, <c>decFinalize</c>, and <c>decSetSubnormal</c>, on unit arrays.
/// </summary>
/// <remarks>
/// The residue is decNumber's: a small integer standing for everything discarded so far.
/// Zero means exact, 1 means something non-zero below the last digit kept, 5 means exactly
/// half, and values between say which side of half. A negative residue means the discarded
/// part was subtracted rather than added, which only the alignment in addition produces.
/// </remarks>
internal static unsafe class WideRounding
{
    /// <summary>
    /// What a discarded leading digit contributes to the residue, which is decNumber's
    /// <c>DECSTICKYTAB</c>. The residue only has to say which side of half the discarded
    /// part falls on, so the digits collapse to four outcomes: nothing, below half, exactly
    /// half, and above.
    /// </summary>
    private static ReadOnlySpan<int> ResidueMap => [0, 3, 3, 3, 3, 5, 7, 7, 7, 7];

    /// <summary>
    /// Shortens a coefficient to <paramref name="digits"/> digits, folding what goes with
    /// it into the residue. decNumber's <c>decSetCoeff</c>.
    /// </summary>
    public static void SetCoefficient(ref WideNumber value, int digits, ref int residue,
        ref DecimalStatus status)
    {
        var discard = value.Digits - digits;
        if (discard <= 0)
        {
            if (residue != 0)
            {
                status |= DecimalStatus.Inexact | DecimalStatus.Rounded;
            }

            return;
        }

        value.Exponent += discard;
        status |= DecimalStatus.Rounded;

        if (residue > 1)
        {
            // Whatever the residue described is now further to the right than the digits
            // about to go, so it can only be a sticky bit.
            residue = 1;
        }

        if (discard > value.Digits)
        {
            // Everything goes, and then some: the guard digit is a zero the value never had.
            if (residue <= 0 && !value.IsZero)
            {
                residue = 1;
            }

            if (residue != 0)
            {
                status |= DecimalStatus.Inexact;
            }

            value.SetZero();
            return;
        }

        var guard = (int)WideUnits.DigitAt(value.Lsu, value.Units, discard - 1);
        if (WideUnits.AnyBelow(value.Lsu, value.Units, discard - 1))
        {
            residue = 1;
        }

        residue += ResidueMap[guard];

        if (digits <= 0)
        {
            value.SetZero();
        }
        else
        {
            value.Units = WideUnits.ShiftDown(value.Lsu, value.Units, discard);
            value.CountDigits();
        }

        if (residue != 0)
        {
            status |= DecimalStatus.Inexact;
        }
    }

    /// <summary>
    /// Acts on a pending residue while keeping the coefficient's length, except for the two
    /// carries that cannot. decNumber's <c>decApplyRound</c>.
    /// </summary>
    public static void ApplyRound(ref WideNumber value, int residue, WideContext context,
        ref DecimalStatus status, out bool overflowed)
    {
        overflowed = false;
        if (residue == 0)
        {
            return;
        }

        var bump = Bump(value, residue, context.Rounding);
        if (bump == 0)
        {
            return;
        }

        if (bump > 0)
        {
            if (WideUnits.IsAllNines(value.Lsu, value.Units, value.Digits))
            {
                // All nines: 999 becomes 100 one decade up, rather than growing a digit.
                value.Units = WideUnits.SetPowerOfTen(value.Lsu, value.Digits - 1);
                value.Exponent++;

                if (value.Exponent + value.Digits > context.MaxExponent + 1)
                {
                    overflowed = true;
                }

                return;
            }

            value.Units = WideUnits.Increment(value.Lsu, value.Units);
            value.CountDigits();
            return;
        }

        if (WideUnits.IsPowerOfTen(value.Lsu, value.Units, value.Digits))
        {
            // The mirror case: 100 becomes 999 one decade down.
            var digits = value.Digits;
            value.Units = WideUnits.SetNines(value.Lsu, digits);
            value.Exponent--;

            if (value.Exponent + 1 == context.MinExponent - context.Digits + 1)
            {
                // The decade below was Etiny, so the value gets clamped back up and the
                // last nine drops off again.
                if (digits == 1)
                {
                    value.SetZero();
                }
                else
                {
                    value.Units = WideUnits.SetNines(value.Lsu, digits - 1);
                }

                value.Exponent++;
                status |= DecimalStatus.Clamped;
            }

            value.CountDigits();
            return;
        }

        // Stepping down by one, which keeps the length in every case the mirror above
        // does not cover.
        var one = stackalloc uint[1];
        *one = 1;
        var written = WideUnits.AddSub(value.Lsu, value.Units, one, 1, 0, value.Lsu, -1);
        value.Units = Math.Abs(written);
        value.CountDigits();
    }

    /// <summary>
    /// Which way a pending residue moves the last digit, under each rounding mode.
    /// </summary>
    private static int Bump(WideNumber value, int residue, DecimalRounding rounding)
    {
        var lastDigit = (int)WideUnits.DigitAt(value.Lsu, value.Units, 0);

        switch (rounding)
        {
            case DecimalRounding.ZeroFiveUp:
                // Down, unless the last digit is one the mode moves; a subtractive residue
                // takes it down instead, unless that would be a no-op.
                if (residue < 0 && lastDigit % 5 != 1)
                {
                    return -1;
                }

                return residue > 0 && lastDigit % 5 == 0 ? 1 : 0;
            case DecimalRounding.Down:
                return residue < 0 ? -1 : 0;
            case DecimalRounding.HalfDown:
                return residue > 5 ? 1 : 0;
            case DecimalRounding.HalfEven:
                if (residue > 5)
                {
                    return 1;
                }

                return residue == 5 && lastDigit % 2 != 0 ? 1 : 0;
            case DecimalRounding.HalfUp:
                return residue >= 5 ? 1 : 0;
            case DecimalRounding.Up:
                return residue > 0 ? 1 : 0;
            case DecimalRounding.Ceiling:
                if (value.IsNegative)
                {
                    return residue < 0 ? -1 : 0;
                }

                return residue > 0 ? 1 : 0;
            default:
                if (value.IsNegative)
                {
                    return residue > 0 ? 1 : 0;
                }

                return residue < 0 ? -1 : 0;
        }
    }

    /// <summary>
    /// Settles a coefficient of final length into the context, applying any pending round
    /// and then the exponent limits. decNumber's <c>decFinalize</c>.
    /// </summary>
    public static void Finalize(ref WideNumber value, int residue, WideContext context,
        ref DecimalStatus status)
    {
        var tinyExponent = context.MinExponent - value.Digits + 1;

        // Subnormal is decided before the pending round, since that round could carry the
        // value up to Nmin or drop it to zero and cover the fact up.
        if (value.Exponent <= tinyExponent)
        {
            if (value.Exponent < tinyExponent)
            {
                Subnormal(ref value, residue, context, ref status);
                return;
            }

            // Equal leaves one case: the value is exactly Nmin and the residue is
            // subtractive, so rounding can still take it below.
            if (residue < 0 && WideUnits.IsPowerOfTen(value.Lsu, value.Units, value.Digits))
            {
                ApplyRound(ref value, residue, context, ref status, out _);
                Subnormal(ref value, residue, context, ref status);
                return;
            }
        }

        if (residue != 0)
        {
            ApplyRound(ref value, residue, context, ref status, out var overflowed);
            if (overflowed)
            {
                Overflowed(ref value, context, ref status);
                return;
            }
        }

        if (value.Exponent <= context.MaxExponent - context.Digits + 1)
        {
            return;
        }

        if (value.Exponent > context.MaxExponent - value.Digits + 1)
        {
            Overflowed(ref value, context, ref status);
            return;
        }

        if (!context.Clamp)
        {
            return;
        }

        // In range, but only if the coefficient carries the extra magnitude as trailing
        // zeros rather than the exponent.
        var shift = value.Exponent - (context.MaxExponent - context.Digits + 1);
        if (!value.IsZero)
        {
            value.Units = WideUnits.ShiftUp(value.Lsu, value.Units, shift);
            value.CountDigits();
        }

        value.Exponent -= shift;
        status |= DecimalStatus.Clamped;
    }

    /// <summary>decNumber's <c>decSetSubnormal</c>.</summary>
    private static void Subnormal(ref WideNumber value, int residue, WideContext context,
        ref DecimalStatus status)
    {
        var tiny = context.TinyExponent;

        if (value.IsZero)
        {
            // A zero is never subnormal, whatever its exponent; it just gets clamped.
            if (value.Exponent < tiny)
            {
                value.Exponent = tiny;
                status |= DecimalStatus.Clamped;
            }

            return;
        }

        status |= DecimalStatus.Subnormal;

        var adjust = tiny - value.Exponent;
        if (adjust <= 0)
        {
            // 754's default rule: a subnormal underflows exactly when it is inexact.
            if ((status & DecimalStatus.Inexact) != 0)
            {
                status |= DecimalStatus.Underflow;
            }

            return;
        }

        var workContext = context;
        workContext.Digits = value.Digits - adjust;
        workContext.MinExponent = context.MinExponent - adjust;

        SetCoefficient(ref value, workContext.Digits, ref residue, ref status);
        ApplyRound(ref value, residue, workContext, ref status, out _);

        if ((status & DecimalStatus.Inexact) != 0)
        {
            status |= DecimalStatus.Underflow;
        }

        // Rounding a run of nines up lengthens the coefficient by one; it fits, because the
        // value was shortened a moment ago.
        if (value.Exponent > tiny)
        {
            value.Units = WideUnits.ShiftUp(value.Lsu, value.Units, 1);
            value.CountDigits();
            value.Exponent--;
        }

        if (value.IsZero)
        {
            // Rounded to nothing, which by definition means the exponent was clamped.
            status |= DecimalStatus.Clamped;
        }
    }

    /// <summary>
    /// What overflow produces, which depends on the rounding mode: the modes that round
    /// away from the value give an infinity, the ones that round toward it give the largest
    /// finite the context allows.
    /// </summary>
    private static void Overflowed(ref WideNumber value, WideContext context,
        ref DecimalStatus status)
    {
        if (value.IsZero)
        {
            // A zero has no magnitude to overflow; only its exponent needs bringing back.
            var limit = context.Clamp
                ? context.MaxExponent - (context.Digits - 1)
                : context.MaxExponent;

            if (value.Exponent > limit)
            {
                value.Exponent = limit;
                status |= DecimalStatus.Clamped;
            }

            return;
        }

        status |= DecimalStatus.Overflow | DecimalStatus.Inexact | DecimalStatus.Rounded;

        var givesLargestFinite = context.Rounding switch
        {
            DecimalRounding.Down => true,
            DecimalRounding.ZeroFiveUp => true,
            DecimalRounding.Ceiling => value.IsNegative,
            DecimalRounding.Floor => !value.IsNegative,
            _ => false
        };

        if (!givesLargestFinite)
        {
            value.Kind = DecimalKind.Infinity;
            value.SetZero();
            value.Kind = DecimalKind.Infinity;
            return;
        }

        value.Units = WideUnits.SetNines(value.Lsu, context.Digits);
        value.Exponent = context.MaxExponent - context.Digits + 1;
        value.CountDigits();
    }
}
