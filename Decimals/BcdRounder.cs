// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// Discards digits from a coefficient under one of the eight rounding modes. This is
/// <see cref="DecimalRounder"/>'s work on the digit form.
/// </summary>
/// <remarks>
/// <para>
/// The modes are defined against the discarded part's size relative to half the discarded
/// power. On a binary coefficient that means a division to get quotient and remainder; on
/// digits it is already there. The first discarded digit against 5 settles which side of
/// halfway the part falls on, and whether anything below it is non-zero settles the tie:
/// </para>
/// <list type="bullet">
/// <item>above half: the first discarded digit is above 5, or it is 5 with something under it</item>
/// <item>exactly half: the first discarded digit is 5 with nothing but zeros under it</item>
/// <item>non-zero at all: any discarded digit is non-zero</item>
/// </list>
/// <para>
/// Shortening itself moves <see cref="BcdNumber.Lsd"/> and nothing else. Only an increment
/// touches digits, and only as far as the carry reaches.
/// </para>
/// </remarks>
internal static unsafe class BcdRounder
{
    /// <summary>
    /// Shortens <paramref name="value"/> by <paramref name="discardedDigits"/> digits,
    /// raising its exponent to match. The coefficient can gain a digit -- 999 rounded up by
    /// one digit is 100 with the exponent one higher -- so the buffer needs a digit of
    /// headroom before the leading one, and the caller has to look at the length again.
    /// </summary>
    public static void Round(ref BcdNumber value, int discardedDigits, DecimalRounding rounding,
        out bool inexact)
    {
        if (discardedDigits <= 0)
        {
            inexact = false;
            return;
        }

        if (discardedDigits >= value.DigitCount)
        {
            // Everything goes. What is left is a zero, but the discarded part still decides
            // whether that zero is exact and whether it rounds up to a one.
            RoundAway(ref value, discardedDigits, rounding, out inexact);
            return;
        }

        var first = value.Lsd[-(discardedDigits - 1)];
        var restNonZero = discardedDigits > 1 && value.AnyNonZeroInLast(discardedDigits - 1);
        inexact = first != 0 || restNonZero;

        value.DropDigits(discardedDigits);
        if (!inexact)
        {
            return;
        }

        if (ShouldIncrement(value, first, restNonZero, rounding))
        {
            value.Increment();
        }
    }

    /// <summary>
    /// The case where the shortening consumes the whole coefficient. The kept part is a
    /// zero, and the leading discarded digit is the coefficient's own leading digit only
    /// when the counts line up exactly; past that the discarded part sits below a zero
    /// digit, so it cannot reach halfway.
    /// </summary>
    private static void RoundAway(ref BcdNumber value, int discardedDigits,
        DecimalRounding rounding, out bool inexact)
    {
        inexact = !value.IsZero;

        var first = discardedDigits == value.DigitCount ? *value.Msd : (byte)0;
        var restNonZero = discardedDigits == value.DigitCount
            ? value.DigitCount > 1 && value.AnyNonZeroInLast(value.DigitCount - 1)
            : inexact;

        var exponent = value.Exponent + discardedDigits;
        value.SetZero(exponent);

        if (!inexact)
        {
            return;
        }

        if (ShouldIncrement(value, first, restNonZero, rounding))
        {
            value.Increment();
        }
    }

    /// <summary>
    /// Whether the kept part moves up. <paramref name="first"/> is the leading discarded
    /// digit and <paramref name="restNonZero"/> says whether anything below it survives;
    /// together they place the discarded part against halfway.
    /// </summary>
    private static bool ShouldIncrement(BcdNumber value, byte first, bool restNonZero,
        DecimalRounding rounding)
    {
        switch (rounding)
        {
            case DecimalRounding.Ceiling:
                return !value.IsNegative;
            case DecimalRounding.Floor:
                return value.IsNegative;
            case DecimalRounding.Down:
                return false;
            case DecimalRounding.Up:
                return true;
            case DecimalRounding.HalfUp:
                return first >= 5;
            case DecimalRounding.HalfDown:
                return first > 5 || (first == 5 && restNonZero);
            case DecimalRounding.HalfEven:
                if (first > 5 || (first == 5 && restNonZero))
                {
                    return true;
                }

                return first == 5 && (*value.Lsd & 1) != 0;
            default:
                // ZeroFiveUp keeps the discarded part recoverable by a later rounding: it
                // only moves the last digit when that digit is a 0 or a 5.
                return *value.Lsd == 0 || *value.Lsd == 5;
        }
    }
}
