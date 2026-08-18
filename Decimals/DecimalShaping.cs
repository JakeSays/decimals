// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// Operations that move a value's digits or its exponent without changing what it means
/// numerically, plus the ones that pick between two values.
/// </summary>
internal static class DecimalShaping
{
    public static UnpackedDecimal<UInt128> Max<TFormat>(UnpackedDecimal<UInt128> left,
        UnpackedDecimal<UInt128> right, DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        return Select<TFormat>(left, right, wantLarger: true, byMagnitude: false, rounding, ref status);
    }

    public static UnpackedDecimal<UInt128> Min<TFormat>(UnpackedDecimal<UInt128> left,
        UnpackedDecimal<UInt128> right, DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        return Select<TFormat>(left, right, wantLarger: false, byMagnitude: false, rounding, ref status);
    }

    public static UnpackedDecimal<UInt128> MaxMagnitude<TFormat>(UnpackedDecimal<UInt128> left,
        UnpackedDecimal<UInt128> right, DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        return Select<TFormat>(left, right, wantLarger: true, byMagnitude: true, rounding, ref status);
    }

    public static UnpackedDecimal<UInt128> MinMagnitude<TFormat>(UnpackedDecimal<UInt128> left,
        UnpackedDecimal<UInt128> right, DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        return Select<TFormat>(left, right, wantLarger: false, byMagnitude: true, rounding, ref status);
    }

    /// <summary>
    /// The adjusted exponent, as an integer. A zero has no leading digit to point at, so it
    /// reports negative infinity and signals division by zero, the way the logarithm it
    /// stands in for would.
    /// </summary>
    public static UnpackedDecimal<UInt128> LogB<TFormat>(UnpackedDecimal<UInt128> value,
        DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (value.IsNaN)
        {
            return DecimalArithmetic.PropagateNaN(value, ref status);
        }

        if (value.Kind == DecimalKind.Infinity)
        {
            return new UnpackedDecimal<UInt128>(DecimalKind.Infinity, false, 0, UInt128.Zero);
        }

        if (value.Coefficient == UInt128.Zero)
        {
            status |= DecimalStatus.DivisionByZero;
            return new UnpackedDecimal<UInt128>(DecimalKind.Infinity, true, 0, UInt128.Zero);
        }

        var adjusted = value.Exponent + DecimalRounder.CountDigits(value.Coefficient) - 1;
        return DecimalFinalizer.Finalize<TFormat>(adjusted < 0, (UInt128)(uint)Math.Abs(adjusted), 0,
            rounding, ref status);
    }

    /// <summary>Multiplies by a power of ten given as a second operand.</summary>
    public static UnpackedDecimal<UInt128> ScaleB<TFormat>(UnpackedDecimal<UInt128> value,
        UnpackedDecimal<UInt128> scale, DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (TryNaN(value, scale, ref status, out var nan))
        {
            return nan;
        }

        // The shift has to be a plain integer, and no larger than could move any value from
        // one end of the format's range to the other.
        var limit = 2 * (TFormat.MaxExponent + TFormat.Precision);
        if (!TryReadInteger(scale, limit, out var shift))
        {
            return Invalid(ref status);
        }

        if (value.Kind == DecimalKind.Infinity)
        {
            return value;
        }

        return DecimalFinalizer.Finalize<TFormat>(value.IsNegative, value.Coefficient,
            value.Exponent + shift, rounding, ref status);
    }

    /// <summary>Removes trailing zeros, leaving the shortest coefficient of the same value.</summary>
    public static UnpackedDecimal<UInt128> Reduce<TFormat>(UnpackedDecimal<UInt128> value,
        DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (value.IsNaN)
        {
            return DecimalArithmetic.PropagateNaN(value, ref status);
        }

        if (value.Kind == DecimalKind.Infinity)
        {
            return value;
        }

        if (value.Coefficient == UInt128.Zero)
        {
            return new UnpackedDecimal<UInt128>(DecimalKind.Finite, value.IsNegative, 0, UInt128.Zero);
        }

        var reduced = DecimalArithmetic.Plus<TFormat>(value, rounding, ref status);
        if (reduced.Kind != DecimalKind.Finite)
        {
            return reduced;
        }

        if (reduced.Coefficient == UInt128.Zero)
        {
            return new UnpackedDecimal<UInt128>(DecimalKind.Finite, reduced.IsNegative, 0, UInt128.Zero);
        }

        return StripZeros(reduced, TFormat.MaxQuantumExponent);
    }

    /// <summary>
    /// Like <see cref="Reduce{TFormat}"/> but stopping at a zero exponent, so an integer
    /// keeps the zeros that are part of its magnitude.
    /// </summary>
    public static UnpackedDecimal<UInt128> Trim(UnpackedDecimal<UInt128> value)
    {
        if (value.Kind != DecimalKind.Finite || value.Coefficient == UInt128.Zero)
        {
            return value;
        }

        return StripZeros(value, 0);
    }

    /// <summary>
    /// Rounds to an integer. <paramref name="exact"/> chooses between the two operations
    /// the specification offers: the exact one reports that digits were lost, the other
    /// stays quiet about it.
    /// </summary>
    public static UnpackedDecimal<UInt128> ToIntegral<TFormat>(UnpackedDecimal<UInt128> value,
        bool exact, DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (value.IsNaN)
        {
            return DecimalArithmetic.PropagateNaN(value, ref status);
        }

        if (value.Kind == DecimalKind.Infinity || value.Exponent >= 0)
        {
            return value;
        }

        var before = status;
        var drop = -value.Exponent;
        var coefficient = DecimalRounder.Round(value.Coefficient, drop, value.IsNegative,
            rounding, out var inexact);

        var result = DecimalFinalizer.Finalize<TFormat>(value.IsNegative, coefficient, 0,
            rounding, ref status);

        if (exact && value.Coefficient != UInt128.Zero)
        {
            status |= DecimalStatus.Rounded;
            if (inexact)
            {
                status |= DecimalStatus.Inexact;
            }
        }
        else
        {
            // round-to-integral-value is defined never to report that it rounded.
            status = before;
        }

        return result;
    }

    /// <summary>
    /// Rescales the first operand to the second's exponent. Needing more digits than the
    /// format holds is an invalid operation rather than a rounded result.
    /// </summary>
    public static UnpackedDecimal<UInt128> Quantize<TFormat>(UnpackedDecimal<UInt128> value,
        UnpackedDecimal<UInt128> pattern, DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (TryNaN(value, pattern, ref status, out var nan))
        {
            return nan;
        }

        var valueIsInfinite = value.Kind == DecimalKind.Infinity;
        var patternIsInfinite = pattern.Kind == DecimalKind.Infinity;
        if (valueIsInfinite || patternIsInfinite)
        {
            // Only two infinities quantize to anything; one of each is invalid.
            return valueIsInfinite && patternIsInfinite ? value : Invalid(ref status);
        }

        return Rescale<TFormat>(value, pattern.Exponent, rounding, ref status);
    }

    public static UnpackedDecimal<UInt128> Rescale<TFormat>(UnpackedDecimal<UInt128> value,
        int exponent, DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (exponent > TFormat.MaxQuantumExponent || exponent < TFormat.MinQuantumExponent)
        {
            return Invalid(ref status);
        }

        var shift = value.Exponent - exponent;
        var coefficient = value.Coefficient;
        var inexact = false;

        if (shift > 0 && coefficient != UInt128.Zero)
        {
            // Growing the coefficient past the format's width is what makes a quantize
            // invalid rather than rounded, and it rules out any shift worth scaling by.
            if (DecimalRounder.CountDigits(coefficient) + shift > TFormat.Precision)
            {
                return Invalid(ref status);
            }

            coefficient *= PowersOfTen.UInt128(shift);
        }
        else if (shift < 0)
        {
            coefficient = DecimalRounder.Round(coefficient, -shift, value.IsNegative, rounding, out inexact);
            if (DecimalRounder.CountDigits(coefficient) > TFormat.Precision)
            {
                return Invalid(ref status);
            }

            if (value.Coefficient != UInt128.Zero)
            {
                status |= DecimalStatus.Rounded;
            }

            if (inexact)
            {
                status |= DecimalStatus.Inexact;
            }
        }

        var result = DecimalFinalizer.Finalize<TFormat>(value.IsNegative, coefficient, exponent,
            rounding, ref status);

        // Quantize is defined never to signal underflow: losing precision to reach the
        // requested exponent is the operation working, not a result vanishing.
        status &= ~DecimalStatus.Underflow;
        return result;
    }

    /// <summary>
    /// Moves the coefficient's digits around within the format's full width. Rotating
    /// carries digits round the ends; shifting drops them and brings zeros in.
    /// </summary>
    public static UnpackedDecimal<UInt128> RotateOrShift<TFormat>(UnpackedDecimal<UInt128> value,
        UnpackedDecimal<UInt128> places, bool rotate, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (TryNaN(value, places, ref status, out var nan))
        {
            return nan;
        }

        if (!TryReadInteger(places, TFormat.Precision, out var count))
        {
            return Invalid(ref status);
        }

        if (value.Kind == DecimalKind.Infinity)
        {
            return value;
        }

        var width = PowersOfTen.UInt128(TFormat.Precision);
        var coefficient = value.Coefficient;

        if (rotate)
        {
            if (count < 0)
            {
                count += TFormat.Precision;
            }

            var high = coefficient / PowersOfTen.UInt128(TFormat.Precision - count);
            var low = coefficient % PowersOfTen.UInt128(TFormat.Precision - count);
            coefficient = (low * PowersOfTen.UInt128(count)) + high;
        }
        else if (count >= 0)
        {
            coefficient = coefficient % PowersOfTen.UInt128(TFormat.Precision - count)
                * PowersOfTen.UInt128(count);
        }
        else
        {
            coefficient /= PowersOfTen.UInt128(-count);
        }

        return new UnpackedDecimal<UInt128>(DecimalKind.Finite, value.IsNegative, value.Exponent,
            coefficient % width);
    }

    /// <summary>
    /// The next value toward positive infinity. Adding something smaller than the smallest
    /// subnormal under round-ceiling lands on it, whatever the value's magnitude.
    /// </summary>
    public static UnpackedDecimal<UInt128> NextPlus<TFormat>(UnpackedDecimal<UInt128> value,
        ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        return Next<TFormat>(value, toward: true, quiet: true, ref status);
    }

    public static UnpackedDecimal<UInt128> NextMinus<TFormat>(UnpackedDecimal<UInt128> value,
        ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        return Next<TFormat>(value, toward: false, quiet: true, ref status);
    }

    /// <summary>
    /// The next value from the first operand in the direction of the second. Unlike
    /// next-plus and next-minus this one reports a subnormal result.
    /// </summary>
    public static UnpackedDecimal<UInt128> NextToward<TFormat>(UnpackedDecimal<UInt128> value,
        UnpackedDecimal<UInt128> target, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (value.IsNaN || target.IsNaN)
        {
            if (value.Kind == DecimalKind.SignalingNaN || target.Kind == DecimalKind.SignalingNaN)
            {
                status |= DecimalStatus.InvalidOperation;
                return DecimalArithmetic.Quiet(
                    value.Kind == DecimalKind.SignalingNaN ? value : target);
            }

            return DecimalArithmetic.Quiet(value.IsNaN ? value : target);
        }

        var comparison = DecimalArithmetic.Compare(value, target, false, ref status, out _);
        if (comparison == 0)
        {
            // Already there. The result keeps the first operand's digits and takes the
            // second's sign, which is the only thing left to move.
            return new UnpackedDecimal<UInt128>(value.Kind, target.IsNegative, value.Exponent,
                value.Coefficient);
        }

        return Next<TFormat>(value, comparison < 0, quiet: false, ref status);
    }

    private static UnpackedDecimal<UInt128> Next<TFormat>(UnpackedDecimal<UInt128> value,
        bool toward, bool quiet, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (value.IsNaN)
        {
            return DecimalArithmetic.PropagateNaN(value, ref status);
        }

        if (value.Kind == DecimalKind.Infinity)
        {
            // An infinity steps in to the largest finite; stepping outward stays put.
            if (value.IsNegative == toward)
            {
                return new UnpackedDecimal<UInt128>(DecimalKind.Finite, value.IsNegative,
                    TFormat.MaxQuantumExponent, PowersOfTen.UInt128(TFormat.Precision) - UInt128.One);
            }

            return value;
        }

        // Smaller than the smallest subnormal, so the rounding is what moves the value and
        // the amount added never shows up in the result.
        var step = new UnpackedDecimal<UInt128>(DecimalKind.Finite, !toward,
            TFormat.MinQuantumExponent - 1, UInt128.One);

        var rounding = toward ? DecimalRounding.Ceiling : DecimalRounding.Floor;
        var raised = DecimalStatus.None;
        var result = DecimalArithmetic.Add<TFormat>(value, step, rounding, ref raised);

        // next-plus and next-minus report nothing about how they got there; next-toward is
        // an arithmetic operation and reports underflow like one.
        status |= raised & DecimalStatus.InvalidOperation;
        if (quiet)
        {
            return result;
        }

        if ((raised & DecimalStatus.Overflow) != 0)
        {
            status |= DecimalStatus.Overflow | DecimalStatus.Inexact | DecimalStatus.Rounded;
        }
        else if ((raised & DecimalStatus.Underflow) != 0 || DecimalOperations.IsSubnormal<TFormat>(result))
        {
            // Stepping into or through the subnormal range is reported; a step that lands
            // on an ordinary value says nothing, however much rounding it took to get there.
            status |= raised & (DecimalStatus.Underflow | DecimalStatus.Inexact
                | DecimalStatus.Subnormal | DecimalStatus.Rounded | DecimalStatus.Clamped);
        }

        return result;
    }

    private static UnpackedDecimal<UInt128> Select<TFormat>(UnpackedDecimal<UInt128> left,
        UnpackedDecimal<UInt128> right, bool wantLarger, bool byMagnitude, DecimalRounding rounding,
        ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (left.Kind == DecimalKind.SignalingNaN || right.Kind == DecimalKind.SignalingNaN)
        {
            status |= DecimalStatus.InvalidOperation;
            return DecimalArithmetic.Quiet(
                left.Kind == DecimalKind.SignalingNaN ? left : right);
        }

        // A quiet NaN beside a number loses: these operations hand back the number.
        if (left.IsNaN && right.IsNaN)
        {
            return DecimalArithmetic.Quiet(left);
        }

        if (left.IsNaN || right.IsNaN)
        {
            var number = left.IsNaN ? right : left;
            if (DecimalOperations.IsSubnormal<TFormat>(number))
            {
                status |= DecimalStatus.Subnormal;
            }

            return number;
        }

        var comparison = byMagnitude
            ? DecimalArithmetic.Compare(Magnitude(left), Magnitude(right), false, ref status, out _)
            : DecimalArithmetic.Compare(left, right, false, ref status, out _);

        // Equal values still differ: the total order decides which member of the pair a
        // caller gets, so -0 loses to +0 and 1.0 loses to 1 for max.
        var chooseLeft = comparison == 0
            ? DecimalOperations.CompareTotal(left, right) > 0 == wantLarger
            : comparison > 0 == wantLarger;

        var chosen = chooseLeft ? left : right;
        if (DecimalOperations.IsSubnormal<TFormat>(chosen))
        {
            status |= DecimalStatus.Subnormal;
        }

        return chosen;
    }

    /// <summary>
    /// Two-operand NaN handling: a signaling NaN wins from either side, and only then does
    /// a quiet one from the left.
    /// </summary>
    private static bool TryNaN(UnpackedDecimal<UInt128> left, UnpackedDecimal<UInt128> right,
        ref DecimalStatus status, out UnpackedDecimal<UInt128> result)
    {
        if (left.Kind == DecimalKind.SignalingNaN || right.Kind == DecimalKind.SignalingNaN)
        {
            status |= DecimalStatus.InvalidOperation;
            result = DecimalArithmetic.Quiet(left.Kind == DecimalKind.SignalingNaN ? left : right);
            return true;
        }

        if (left.IsNaN || right.IsNaN)
        {
            result = DecimalArithmetic.Quiet(left.IsNaN ? left : right);
            return true;
        }

        result = default;
        return false;
    }

    private static UnpackedDecimal<UInt128> Magnitude(UnpackedDecimal<UInt128> value)
    {
        return new UnpackedDecimal<UInt128>(value.Kind, false, value.Exponent, value.Coefficient);
    }

    private static UnpackedDecimal<UInt128> StripZeros(UnpackedDecimal<UInt128> value, int exponentLimit)
    {
        var coefficient = value.Coefficient;
        var exponent = value.Exponent;

        while (exponent < exponentLimit)
        {
            var next = coefficient / 10;
            if (next * 10 != coefficient)
            {
                break;
            }

            coefficient = next;
            exponent++;
        }

        return new UnpackedDecimal<UInt128>(value.Kind, value.IsNegative, exponent, coefficient);
    }

    /// <summary>
    /// Reads an operand that has to be a plain integer within a range: finite, with a zero
    /// exponent and no more magnitude than the operation allows.
    /// </summary>
    private static bool TryReadInteger(UnpackedDecimal<UInt128> value, int limit, out int number)
    {
        number = 0;

        if (value.Kind != DecimalKind.Finite || value.Exponent != 0)
        {
            return false;
        }

        if (value.Coefficient > (UInt128)(uint)limit)
        {
            return false;
        }

        number = (int)(uint)value.Coefficient;
        if (value.IsNegative)
        {
            number = -number;
        }

        return true;
    }

    private static UnpackedDecimal<UInt128> Invalid(ref DecimalStatus status)
    {
        status |= DecimalStatus.InvalidOperation;
        return new UnpackedDecimal<UInt128>(DecimalKind.QuietNaN, false, 0, UInt128.Zero);
    }
}
