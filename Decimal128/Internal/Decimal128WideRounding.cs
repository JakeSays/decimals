// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// Shortens a coefficient and fits it to a context: decNumber's <c>decSetCoeff</c>,
/// <c>decApplyRound</c>, <c>decFinalize</c>, and <c>decSetSubnormal</c>, on wide numbers.
/// </summary>
/// <remarks>
/// The residue is decNumber's: a small integer that represents all the digits discarded so
/// far. 0 means nothing non-zero was discarded. 1 means a non-zero amount below the digit
/// after the last one kept. 5 means exactly half. Other values below 5 mean below half, and
/// values above 5 mean above half. A negative residue means the discarded part was
/// subtracted instead of added, which only alignment in addition produces.
/// </remarks>
internal static unsafe class Decimal128WideRounding
{
    /// <summary>
    /// The residue contribution of the first discarded digit: decNumber's
    /// <c>DECSTICKYTAB</c>. The residue only needs to show where the discarded part is
    /// relative to half, so the digits map to four results: zero, below half, exactly half,
    /// and above half.
    /// </summary>
    private static ReadOnlySpan<int> ResidueMap => [0, 3, 3, 3, 3, 5, 7, 7, 7, 7];

    /// <summary>
    /// Shortens a coefficient to <paramref name="digits"/> digits and adds the discarded
    /// digits to the residue: decNumber's <c>decSetCoeff</c>. Sets Rounded when digits are
    /// discarded and Inexact when the residue is not zero.
    /// </summary>
    /// <param name="value">The value to shorten. Its exponent is raised by the number of digits discarded.</param>
    /// <param name="digits">The number of digits to keep.</param>
    /// <param name="residue">The residue so far. Receives the residue after the discarded digits are added.</param>
    /// <param name="status">Receives the conditions the shortening raises.</param>
    public static void SetCoefficient(ref Decimal128WideNumber value, int digits, ref int residue,
        ref Decimal128Status status)
    {
        var discard = value.Digits - digits;
        if (discard <= 0)
        {
            if (residue != 0)
            {
                status |= Decimal128Status.Inexact | Decimal128Status.Rounded;
            }

            return;
        }

        value.Exponent += discard;
        status |= Decimal128Status.Rounded;

        if (residue > 1)
        {
            // The old residue is now below all the digits being discarded, so it only
            // matters as a sticky bit.
            residue = 1;
        }

        if (discard > value.Digits)
        {
            // Every digit is discarded, and the guard digit is a zero above the coefficient.
            if (residue <= 0 && !value.IsZero)
            {
                residue = 1;
            }

            if (residue != 0)
            {
                status |= Decimal128Status.Inexact;
            }

            value.SetZero();
            return;
        }

        var guard = (int)Decimal128WideUnits.DigitAt(value.Lsu, value.Units, discard - 1);
        if (Decimal128WideUnits.AnyBelow(value.Lsu, value.Units, discard - 1))
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
            value.Units = Decimal128WideUnits.ShiftDown(value.Lsu, value.Units, discard);
            value.CountDigits();
        }

        if (residue != 0)
        {
            status |= Decimal128Status.Inexact;
        }
    }

    /// <summary>
    /// Applies a pending residue to the last digit, keeping the coefficient's length:
    /// decNumber's <c>decApplyRound</c>. An increment of all nines and a decrement of a power
    /// of ten change the exponent instead of the length.
    /// </summary>
    /// <param name="value">The value to round. It already has its final number of digits.</param>
    /// <param name="residue">The residue of the digits discarded below <paramref name="value"/>.</param>
    /// <param name="context">The rounding mode and exponent limits to apply.</param>
    /// <param name="status">Receives Clamped if a decrement at the bottom of the range is clamped.</param>
    /// <param name="overflowed">Receives true if an increment raised the exponent beyond the context's limit.</param>
    public static void ApplyRound(ref Decimal128WideNumber value, int residue, Decimal128WideContext context,
        ref Decimal128Status status, out bool overflowed)
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
            if (Decimal128WideUnits.IsAllNines(value.Lsu, value.Units, value.Digits))
            {
                // All nines: 999 becomes 100 with the exponent raised by one, instead of
                // gaining a digit.
                value.Units = Decimal128WideUnits.SetPowerOfTen(value.Lsu, value.Digits - 1);
                value.Exponent++;

                if (value.Exponent + value.Digits > context.MaxExponent + 1)
                {
                    overflowed = true;
                }

                return;
            }

            value.Units = Decimal128WideUnits.Increment(value.Lsu, value.Units);
            value.CountDigits();
            return;
        }

        if (Decimal128WideUnits.IsPowerOfTen(value.Lsu, value.Units, value.Digits))
        {
            // The opposite case: 100 becomes 999 with the exponent lowered by one.
            var digits = value.Digits;
            value.Units = Decimal128WideUnits.SetNines(value.Lsu, digits);
            value.Exponent--;

            if (value.Exponent + 1 == context.MinExponent - context.Digits + 1)
            {
                // The new exponent is below Etiny, so the exponent is clamped back up and the
                // last nine is removed.
                if (digits == 1)
                {
                    value.SetZero();
                }
                else
                {
                    value.Units = Decimal128WideUnits.SetNines(value.Lsu, digits - 1);
                }

                value.Exponent++;
                status |= Decimal128Status.Clamped;
            }

            value.CountDigits();
            return;
        }

        // Subtract one. This keeps the length in every case the power-of-ten case above
        // does not cover.
        var one = stackalloc uint[1];
        *one = 1;
        var written = Decimal128WideUnits.AddSub(value.Lsu, value.Units, one, 1, 0, value.Lsu, -1);
        value.Units = Math.Abs(written);
        value.CountDigits();
    }

    /// <summary>
    /// The direction a pending residue moves the last digit under the rounding mode: 1 up,
    /// -1 down, or 0 unchanged.
    /// </summary>
    private static int Bump(Decimal128WideNumber value, int residue, Decimal128Rounding rounding)
    {
        var lastDigit = (int)Decimal128WideUnits.DigitAt(value.Lsu, value.Units, 0);

        switch (rounding)
        {
            case Decimal128Rounding.ZeroFiveUp:
                // Round toward zero, but increment if the last digit is 0 or 5. A negative
                // residue decrements, unless the last digit is 1 or 6.
                if (residue < 0 && lastDigit % 5 != 1)
                {
                    return -1;
                }

                return residue > 0 && lastDigit % 5 == 0 ? 1 : 0;
            case Decimal128Rounding.Down:
                return residue < 0 ? -1 : 0;
            case Decimal128Rounding.HalfDown:
                return residue > 5 ? 1 : 0;
            case Decimal128Rounding.HalfEven:
                if (residue > 5)
                {
                    return 1;
                }

                return residue == 5 && lastDigit % 2 != 0 ? 1 : 0;
            case Decimal128Rounding.HalfUp:
                return residue >= 5 ? 1 : 0;
            case Decimal128Rounding.Up:
                return residue > 0 ? 1 : 0;
            case Decimal128Rounding.Ceiling:
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
    /// Fits a coefficient of final length to the context. Applies any pending rounding, then
    /// the exponent limits: decNumber's <c>decFinalize</c>.
    /// </summary>
    /// <param name="value">The value to fit. It already has at most the context's number of digits.</param>
    /// <param name="residue">The residue of the digits discarded below <paramref name="value"/>.</param>
    /// <param name="context">The precision, rounding, and exponent limits to apply.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    public static void Finalize(ref Decimal128WideNumber value, int residue, Decimal128WideContext context,
        ref Decimal128Status status)
    {
        var tinyExponent = context.MinExponent - value.Digits + 1;

        // Subnormal is decided before the pending rounding, because that rounding could
        // carry the value up to Nmin or reduce it to zero and hide that it was subnormal.
        if (value.Exponent <= tinyExponent)
        {
            if (value.Exponent < tinyExponent)
            {
                Subnormal(ref value, residue, context, ref status);
                return;
            }

            // When the exponents are equal, one case remains: the value is exactly Nmin and
            // the residue is negative, so rounding can still take it below Nmin.
            if (residue < 0 && Decimal128WideUnits.IsPowerOfTen(value.Lsu, value.Units, value.Digits))
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

        // The value is in range only if the coefficient holds the extra magnitude as
        // trailing zeros instead of the exponent.
        var shift = value.Exponent - (context.MaxExponent - context.Digits + 1);
        if (!value.IsZero)
        {
            value.Units = Decimal128WideUnits.ShiftUp(value.Lsu, value.Units, shift);
            value.CountDigits();
        }

        value.Exponent -= shift;
        status |= Decimal128Status.Clamped;
    }

    /// <summary>decNumber's <c>decSetSubnormal</c>.</summary>
    private static void Subnormal(ref Decimal128WideNumber value, int residue, Decimal128WideContext context,
        ref Decimal128Status status)
    {
        var tiny = context.TinyExponent;

        if (value.IsZero)
        {
            // A zero is never subnormal, whatever its exponent. Its exponent is only clamped.
            if (value.Exponent < tiny)
            {
                value.Exponent = tiny;
                status |= Decimal128Status.Clamped;
            }

            return;
        }

        status |= Decimal128Status.Subnormal;

        var adjust = tiny - value.Exponent;
        if (adjust <= 0)
        {
            // IEEE 754's default rule: a subnormal result underflows if and only if it is
            // inexact.
            if ((status & Decimal128Status.Inexact) != 0)
            {
                status |= Decimal128Status.Underflow;
            }

            return;
        }

        var workContext = context;
        workContext.Digits = value.Digits - adjust;
        workContext.MinExponent = context.MinExponent - adjust;

        SetCoefficient(ref value, workContext.Digits, ref residue, ref status);
        ApplyRound(ref value, residue, workContext, ref status, out _);

        if ((status & Decimal128Status.Inexact) != 0)
        {
            status |= Decimal128Status.Underflow;
        }

        // Rounding up a run of nines raises the exponent by one. The coefficient is shifted
        // back, which fits, because the value was just shortened.
        if (value.Exponent > tiny)
        {
            value.Units = Decimal128WideUnits.ShiftUp(value.Lsu, value.Units, 1);
            value.CountDigits();
            value.Exponent--;
        }

        if (value.IsZero)
        {
            // The value rounded to zero, which by definition means the exponent was clamped.
            status |= Decimal128Status.Clamped;
        }
    }

    /// <summary>
    /// Sets the overflow result, which depends on the rounding mode. Modes that round away
    /// from zero for this sign give an infinity. Modes that round toward zero give the
    /// largest finite value the context allows.
    /// </summary>
    private static void Overflowed(ref Decimal128WideNumber value, Decimal128WideContext context,
        ref Decimal128Status status)
    {
        if (value.IsZero)
        {
            // A zero has no magnitude to overflow. Only its exponent is brought into range.
            var limit = context.Clamp
                ? context.MaxExponent - (context.Digits - 1)
                : context.MaxExponent;

            if (value.Exponent > limit)
            {
                value.Exponent = limit;
                status |= Decimal128Status.Clamped;
            }

            return;
        }

        status |= Decimal128Status.Overflow | Decimal128Status.Inexact | Decimal128Status.Rounded;

        var givesLargestFinite = context.Rounding switch
        {
            Decimal128Rounding.Down => true,
            Decimal128Rounding.ZeroFiveUp => true,
            Decimal128Rounding.Ceiling => value.IsNegative,
            Decimal128Rounding.Floor => !value.IsNegative,
            _ => false
        };

        if (!givesLargestFinite)
        {
            value.SetZero();
            value.Kind = Decimal128Kind.Infinity;
            return;
        }

        value.Units = Decimal128WideUnits.SetNines(value.Lsu, context.Digits);
        value.Exponent = context.MaxExponent - context.Digits + 1;
        value.CountDigits();
    }
}
