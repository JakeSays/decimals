// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// Divides one coefficient by another, following decNumber's <c>decDivide</c>.
/// </summary>
/// <remarks>
/// <para>
/// Schoolbook long division in base-billion. The dividend sits high in an accumulator three
/// operands wide, the divisor stays put, and each pass of the outer loop settles one
/// quotient limb by subtracting multiples of the divisor out of a window that slides one
/// limb down each time.
/// </para>
/// <para>
/// The multiplier estimate is what makes it quick. Subtracting the divisor one copy at a
/// time would be correct and hopeless; instead the leading limbs of the window and the
/// divisor give an estimate good enough that the inner loop almost never runs twice. The
/// estimate is allowed to be low -- the loop simply goes round again -- but never high,
/// which is why the divisor's estimate is rounded up wherever digits below the ones being
/// looked at might exist.
/// </para>
/// <para>
/// Digits stop at the precision plus one. Everything below that is folded into a sticky bit
/// on the last limb, which is enough for the finalizer to round correctly.
/// </para>
/// </remarks>
internal static unsafe class BcdDivider
{
    private const uint Base = 1000000000;

    /// <summary>Splitting points for the estimate when both leading limbs are small.</summary>
    private const uint EstimateLow = 1000000;

    private const uint EstimateHigh = Base / EstimateLow;

    /// <summary>Limbs in the accumulator: the dividend, plus room to keep dividing past it.</summary>
    public static int AccumulatorLength<TFormat>()
        where TFormat : IDecimalFormat
    {
        return BcdMultiplier.LimbCount<TFormat>() * 3;
    }

    /// <summary>
    /// Limbs in the quotient. One more than an operand, because the quotient's digits do
    /// not have to line up with the operands' limb boundaries.
    /// </summary>
    public static int QuotientLength<TFormat>()
        where TFormat : IDecimalFormat
    {
        return BcdMultiplier.LimbCount<TFormat>() + 1;
    }

    /// <summary>
    /// Digits the quotient can occupy, plus headroom before the leading digit.
    /// </summary>
    public static int QuotientBufferLength<TFormat>()
        where TFormat : IDecimalFormat
    {
        return (QuotientLength<TFormat>() * BcdMultiplier.DigitsPerLimb) + 2;
    }

    /// <summary>
    /// Divides two finite values. The result is the quotient to the precision plus one
    /// digit, with a sticky digit standing for the remainder, which is what the finalizer
    /// needs to round it.
    /// </summary>
    public static BcdNumber Divide<TFormat>(BcdNumber left, BcdNumber right, byte* buffer,
        uint* accumulator, uint* divisor, uint* quotient, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        var isNegative = left.IsNegative != right.IsNegative;

        if (right.IsZero)
        {
            if (left.IsZero)
            {
                status |= DecimalStatus.DivisionUndefined;
                buffer[1] = 0;
                return new BcdNumber(DecimalKind.QuietNaN, false, 0, buffer + 1, buffer + 1);
            }

            status |= DecimalStatus.DivisionByZero;
            buffer[1] = 0;
            return new BcdNumber(DecimalKind.Infinity, isNegative, 0, buffer + 1, buffer + 1);
        }

        // The ideal exponent, which is what an exact result keeps.
        var exponent = left.Exponent - right.Exponent;

        if (left.IsZero)
        {
            buffer[1] = 0;
            return new BcdNumber(DecimalKind.Finite, isNegative, exponent, buffer + 1, buffer + 1);
        }

        var limbs = BcdMultiplier.LimbCount<TFormat>();
        var accumulatorLength = limbs * 3;

        // The dividend sits in the top operand's worth of the accumulator, so the division
        // has two operands' worth of room below it to keep going into.
        var dividendBase = accumulator + accumulatorLength - limbs;
        for (var index = 0; index < accumulatorLength; index++)
        {
            accumulator[index] = 0;
        }

        PackLimbs(left, dividendBase, limbs);
        PackLimbs(right, divisor, limbs);

        var quotientLength = limbs + 1;
        for (var index = 0; index < quotientLength; index++)
        {
            quotient[index] = 0;
        }

        var mostSignificantAccumulator = accumulator + accumulatorLength - 1;
        var mostSignificantQuotient = quotient + limbs;

        var mostSignificantDivisor = divisor + limbs - 1;
        while (*mostSignificantDivisor == 0)
        {
            mostSignificantDivisor--;
        }

        var divisorLimbs = (int)(mostSignificantDivisor - divisor) + 1;

        // The window starts the same length as the divisor and slides one limb down each
        // pass of the outer loop.
        var lowestAccumulator = mostSignificantAccumulator - divisorLimbs + 1;
        var lowestQuotient = mostSignificantQuotient;

        var divisorTop = EstimateDivisorTop(divisor, mostSignificantDivisor, divisorLimbs);
        var quotientDigits = 0;

        for (; ; lowestAccumulator--)
        {
            *lowestQuotient = 0;

            while (true)
            {
                // Leading zero limbs, whether they were there to begin with or came out of
                // a subtraction below.
                while (*mostSignificantAccumulator == 0 && mostSignificantAccumulator >= lowestAccumulator)
                {
                    mostSignificantAccumulator--;
                }

                var windowLimbs = (int)(mostSignificantAccumulator - lowestAccumulator) + 1;
                if (windowLimbs < divisorLimbs)
                {
                    if (windowLimbs == 0)
                    {
                        mostSignificantAccumulator++;
                    }

                    break;
                }

                int multiplier;
                if (windowLimbs == divisorLimbs)
                {
                    // Same length, so which is larger has to be looked up rather than
                    // assumed.
                    var divisorDigit = mostSignificantDivisor;
                    var accumulatorDigit = mostSignificantAccumulator;
                    while (divisorDigit > divisor && *divisorDigit == *accumulatorDigit)
                    {
                        divisorDigit--;
                        accumulatorDigit--;
                    }

                    if (*divisorDigit > *accumulatorDigit)
                    {
                        // Nothing to subtract here; the next pass brings another limb in.
                        break;
                    }

                    if (*divisorDigit == *accumulatorDigit)
                    {
                        // They are equal, so the window divides exactly once and empties.
                        *lowestQuotient += 1;
                        mostSignificantAccumulator = lowestAccumulator;
                        *mostSignificantAccumulator = 0;
                        break;
                    }

                    multiplier = EstimateEqualLength(mostSignificantAccumulator,
                        mostSignificantDivisor, divisorLimbs);
                }
                else
                {
                    multiplier = EstimateLonger(mostSignificantAccumulator, mostSignificantDivisor,
                        divisorLimbs, divisorTop);
                }

                if (multiplier == 0)
                {
                    // The estimate can fall to zero at the margins; one subtraction is
                    // always possible here, and the loop will come back for the rest.
                    multiplier = 1;
                }

                *lowestQuotient += (uint)multiplier;
                Subtract(divisor, mostSignificantDivisor, lowestAccumulator, (uint)multiplier);
            }

            if (quotientDigits != 0)
            {
                quotientDigits += BcdMultiplier.DigitsPerLimb;
                lowestQuotient--;
                if (quotientDigits > TFormat.Precision + 1)
                {
                    break;
                }
            }
            else if (*lowestQuotient != 0)
            {
                quotientDigits = CountDigits(*lowestQuotient);
                lowestQuotient--;
            }

            if (*mostSignificantAccumulator != 0)
            {
                // A remainder is still in hand, so the division is not finished.
                continue;
            }

            // The accumulator is only empty if the whole of the original dividend has been
            // covered and everything down to the window is zero.
            if (lowestAccumulator > accumulator + accumulatorLength - limbs)
            {
                continue;
            }

            while (mostSignificantAccumulator > lowestAccumulator && *mostSignificantAccumulator == 0)
            {
                mostSignificantAccumulator--;
            }

            if (*mostSignificantAccumulator == 0 && mostSignificantAccumulator == lowestAccumulator)
            {
                break;
            }
        }

        lowestQuotient++;
        if (*mostSignificantAccumulator != 0)
        {
            // Whatever is left of the dividend cannot reach the digits being kept, but it
            // has to be visible to rounding, so it becomes a sticky bit.
            *lowestQuotient |= 1;
        }

        var limbsWritten = (int)(mostSignificantQuotient - lowestQuotient) + 1;
        var lastDigit = WriteQuotient(mostSignificantQuotient, lowestQuotient, buffer + 1);

        var result = new BcdNumber(DecimalKind.Finite, isNegative, exponent,
            buffer + 1 + (limbsWritten * BcdMultiplier.DigitsPerLimb) - quotientDigits, lastDigit);

        if (lowestAccumulator < accumulator + accumulatorLength - limbs)
        {
            // The division ran past the dividend's own digits, and each limb it ran on for
            // is nine more decades below the ideal exponent.
            var extra = (int)(accumulator + accumulatorLength - limbs - lowestAccumulator);
            result.Exponent -= extra * BcdMultiplier.DigitsPerLimb;

            if (*mostSignificantAccumulator == 0)
            {
                // An exact result can carry up to eight trailing zeros it does not need,
                // from the limb the quotient overflowed into.
                var trailing = result.Lsd;
                while (*trailing == 0 && trailing > result.Msd)
                {
                    trailing--;
                }

                result.Exponent += (int)(result.Lsd - trailing);
                result.Lsd = trailing;
            }
        }

        return result;
    }

    /// <summary>
    /// The divisor's leading limb with two bits of the one below it, rounded up if anything
    /// survives further down. The extra bits make the estimate much better when the leading
    /// limb is small, and rounding up keeps it from ever running high.
    /// </summary>
    private static uint EstimateDivisorTop(uint* divisor, uint* mostSignificant, int divisorLimbs)
    {
        var top = *mostSignificant << 2;
        if (divisorLimbs <= 1)
        {
            return top;
        }

        var below = mostSignificant - 1;
        var value = *below;

        if (value >= 750000000)
        {
            top += 3;
            value -= 750000000;
        }
        else if (value >= 500000000)
        {
            top += 2;
            value -= 500000000;
        }
        else if (value >= 250000000)
        {
            top++;
            value -= 250000000;
        }

        if (value != 0)
        {
            top++;
            return top;
        }

        for (var digit = below - 1; digit >= divisor; digit--)
        {
            if (*digit != 0)
            {
                top++;
                break;
            }
        }

        return top;
    }

    /// <summary>
    /// The estimate when the window and the divisor are the same length, which is an exact
    /// division of their two leading limbs.
    /// </summary>
    private static int EstimateEqualLength(uint* mostSignificantAccumulator,
        uint* mostSignificantDivisor, int divisorLimbs)
    {
        if (divisorLimbs <= 1)
        {
            return (int)(*mostSignificantAccumulator / *mostSignificantDivisor);
        }

        var window = ((ulong)*mostSignificantAccumulator * Base) + *(mostSignificantAccumulator - 1);
        var divisorValue = ((ulong)*mostSignificantDivisor * Base) + *(mostSignificantDivisor - 1);

        if (divisorLimbs > 2)
        {
            // There are limbs below the two being looked at, so the divisor is really a
            // little larger than this; rounding it up keeps the estimate from running high.
            divisorValue++;
        }

        return (int)(window / divisorValue);
    }

    /// <summary>
    /// The estimate when the window is longer than the divisor, so the divisor's leading
    /// limb sits one place lower than the window's.
    /// </summary>
    private static int EstimateLonger(uint* mostSignificantAccumulator, uint* mostSignificantDivisor,
        int divisorLimbs, uint divisorTop)
    {
        if (divisorLimbs > 1 && *mostSignificantAccumulator < EstimateLow
            && *mostSignificantDivisor < EstimateLow)
        {
            // Both leading limbs are small, so bringing in digits from below pays for
            // itself in the subtractions it saves.
            var window = ((ulong)*mostSignificantAccumulator * EstimateHigh * Base)
                + ((ulong)*(mostSignificantAccumulator - 1) * EstimateHigh)
                + (*(mostSignificantAccumulator - 2) / EstimateLow);

            var divisorValue = ((ulong)*mostSignificantDivisor * EstimateHigh)
                + (*(mostSignificantDivisor - 1) / EstimateLow)
                + 1;

            return (int)(window / divisorValue);
        }

        if (divisorLimbs == 1)
        {
            var window = ((ulong)*mostSignificantAccumulator * Base) + *(mostSignificantAccumulator - 1);
            return (int)(window / *mostSignificantDivisor);
        }

        var scaled = ((ulong)*mostSignificantAccumulator * (Base << 2))
            + ((ulong)*(mostSignificantAccumulator - 1) << 2);

        return (int)(scaled / divisorTop);
    }

    /// <summary>
    /// Subtracts the divisor times <paramref name="multiplier"/> out of the window, limb by
    /// limb, splitting each product back to base-billion as it goes.
    /// </summary>
    private static void Subtract(uint* divisor, uint* mostSignificantDivisor,
        uint* lowestAccumulator, uint multiplier)
    {
        var carry = 0u;
        var accumulatorDigit = lowestAccumulator;

        for (var divisorDigit = divisor; divisorDigit <= mostSignificantDivisor;
            divisorDigit++, accumulatorDigit++)
        {
            var product = ((ulong)multiplier * *divisorDigit) + carry;
            carry = (uint)(product / Base);
            var low = (uint)(product % Base);

            if (low > *accumulatorDigit)
            {
                *accumulatorDigit += Base;
                carry++;
            }

            *accumulatorDigit -= low;
        }

        if (carry != 0)
        {
            // The window was longer than the divisor, so there is a limb above to take it.
            *accumulatorDigit -= carry;
        }
    }

    /// <summary>
    /// Reads a coefficient into base-billion limbs, least significant limb first.
    /// </summary>
    private static void PackLimbs(BcdNumber value, uint* limbs, int limbCount)
    {
        var digit = value.Lsd;
        var remaining = value.DigitCount;

        for (var index = 0; index < limbCount; index++)
        {
            var limb = 0u;
            var scale = 1u;
            for (var position = 0; position < BcdMultiplier.DigitsPerLimb && remaining > 0; position++)
            {
                limb += *digit * scale;
                scale *= 10;
                digit--;
                remaining--;
            }

            limbs[index] = limb;
        }
    }

    /// <summary>
    /// Lays the quotient out as digits, most significant limb first, and hands back the
    /// last digit written.
    /// </summary>
    private static byte* WriteQuotient(uint* mostSignificant, uint* lowest, byte* digits)
    {
        var destination = digits;
        for (var limb = mostSignificant; limb >= lowest; limb--)
        {
            var value = *limb;
            Dpd.WriteTriple(value / 1000000, destination);
            Dpd.WriteTriple((value / 1000) % 1000, destination + 3);
            Dpd.WriteTriple(value % 1000, destination + 6);
            destination += BcdMultiplier.DigitsPerLimb;
        }

        return destination - 1;
    }

    private static int CountDigits(uint value)
    {
        var digits = 0;
        while (value != 0)
        {
            digits++;
            value /= 10;
        }

        return digits;
    }
}
