// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// The operations that move a value's digits without changing what it is worth: rescaling
/// to an exponent, rounding to an integer, stripping zeros, stepping to a neighbor, and
/// reading or shifting the exponent.
/// </summary>
internal static class Decimal64Shaping
{
    /// <summary>
    /// Rescales the value to the pattern's exponent. Needing more digits than the format
    /// holds is invalid rather than rounded: the value cannot be said at that exponent.
    /// </summary>
    public static ulong Quantize(ulong value, ulong pattern, Decimal64Rounding rounding, ref Decimal64Status status)
    {
        if (Decimal64Encoding.IsSpecial(value) || Decimal64Encoding.IsSpecial(pattern))
        {
            if (Decimal64Encoding.IsNaN(value) || Decimal64Encoding.IsNaN(pattern))
            {
                return Decimal64Arithmetic.PropagateNaN(value, pattern, ref status);
            }

            // Only two infinities quantize to anything; one of each is invalid.
            if (Decimal64Encoding.IsInfinity(value) && Decimal64Encoding.IsInfinity(pattern))
            {
                return Decimal64Encoding.Infinity(Decimal64Encoding.IsNegative(value));
            }

            return Decimal64Arithmetic.Invalid(ref status);
        }

        Decimal64Encoding.Unpack(pattern, out var exponent);
        return Rescale(value, exponent, rounding, ref status);
    }

    /// <summary>Rescales a finite value to an exponent given directly.</summary>
    public static ulong Rescale(ulong value, int exponent, Decimal64Rounding rounding, ref Decimal64Status status)
    {
        if (Decimal64Encoding.IsSpecial(value))
        {
            if (Decimal64Encoding.IsNaN(value))
            {
                return Decimal64Arithmetic.PropagateNaN(value, ref status);
            }

            return Decimal64Arithmetic.Invalid(ref status);
        }

        if (exponent > Decimal64Encoding.MaxQuantumExponent || exponent < Decimal64Encoding.MinQuantumExponent)
        {
            return Decimal64Arithmetic.Invalid(ref status);
        }

        var coefficient = Decimal64Encoding.Unpack(value, out var current);
        var negative = Decimal64Encoding.IsNegative(value);
        var shift = current - exponent;

        if (shift > 0 && coefficient != 0)
        {
            if (Decimal64Tables.CountDigits(coefficient) + shift > Decimal64Encoding.Precision)
            {
                return Decimal64Arithmetic.Invalid(ref status);
            }

            coefficient *= Decimal64Tables.PowerOfTen(shift);
        }
        else if (shift < 0)
        {
            var wasZero = coefficient == 0;
            coefficient = Decimal64Rounder.DropDigits(coefficient, -shift, Decimal64Residue.Exact, out var residue);

            if (residue != Decimal64Residue.Exact
                && Decimal64Rounder.ShouldIncrement(coefficient, residue, negative, rounding))
            {
                coefficient++;
                if (coefficient > Decimal64Encoding.MaxCoefficient)
                {
                    return Decimal64Arithmetic.Invalid(ref status);
                }
            }

            if (!wasZero)
            {
                status |= Decimal64Status.Rounded;
            }

            if (residue != Decimal64Residue.Exact)
            {
                status |= Decimal64Status.Inexact;
            }
        }

        var result = Decimal64Finalizer.Finalize(negative, coefficient, exponent, Decimal64Residue.Exact, rounding, ref status);

        // Quantize is defined never to signal underflow: losing precision to reach the
        // requested exponent is the operation working, not a result vanishing.
        status &= ~Decimal64Status.Underflow;
        return result;
    }

    /// <summary>
    /// Rounds to an integer. <paramref name="exact"/> chooses between the two operations
    /// the specification offers: the exact one reports that digits were lost, the other is
    /// defined never to say it rounded.
    /// </summary>
    public static ulong ToIntegral(ulong value, bool exact, Decimal64Rounding rounding, ref Decimal64Status status)
    {
        if (Decimal64Encoding.IsSpecial(value))
        {
            if (Decimal64Encoding.IsNaN(value))
            {
                return Decimal64Arithmetic.PropagateNaN(value, ref status);
            }

            return Decimal64Encoding.Infinity(Decimal64Encoding.IsNegative(value));
        }

        var coefficient = Decimal64Encoding.Unpack(value, out var exponent);
        var negative = Decimal64Encoding.IsNegative(value);

        if (exponent >= 0)
        {
            // Already an integer, and one whose exponent says so.
            return Decimal64Encoding.Pack(negative, exponent, coefficient);
        }

        var wasZero = coefficient == 0;
        coefficient = Decimal64Rounder.DropDigits(coefficient, -exponent, Decimal64Residue.Exact, out var residue);

        if (residue != Decimal64Residue.Exact
            && Decimal64Rounder.ShouldIncrement(coefficient, residue, negative, rounding))
        {
            coefficient++;
        }

        var local = Decimal64Status.None;
        var result = Decimal64Finalizer.Finalize(negative, coefficient, 0, Decimal64Residue.Exact, rounding, ref local);

        if (exact && !wasZero)
        {
            status |= local | Decimal64Status.Rounded;
            if (residue != Decimal64Residue.Exact)
            {
                status |= Decimal64Status.Inexact;
            }
        }

        return result;
    }

    /// <summary>
    /// Walks trailing zeros off the coefficient, raising the exponent to match, which
    /// leaves the shortest coefficient of the same value. Stops at
    /// <paramref name="exponentLimit"/> so that an integer keeps the zeros that are part of
    /// its magnitude.
    /// </summary>
    public static void StripTrailingZeros(ref ulong coefficient, ref int exponent, int exponentLimit)
    {
        if (coefficient == 0)
        {
            return;
        }

        // Eight zeros at a time, then four, two, one: a run of zeros comes off in a few
        // constant divisions rather than one per zero.
        for (var step = 8; step >= 1; step /= 2)
        {
            var power = Decimal64Tables.PowerOfTen(step);
            while (exponent + step <= exponentLimit)
            {
                var shorter = coefficient / power;
                if ((shorter * power) != coefficient)
                {
                    break;
                }

                coefficient = shorter;
                exponent += step;
            }
        }
    }

    /// <summary>
    /// Removes trailing zeros, leaving the shortest coefficient of the same value.
    /// </summary>
    public static ulong Reduce(ulong value, Decimal64Rounding rounding, ref Decimal64Status status)
    {
        if (Decimal64Encoding.IsSpecial(value))
        {
            if (Decimal64Encoding.IsNaN(value))
            {
                return Decimal64Arithmetic.PropagateNaN(value, ref status);
            }

            return Decimal64Encoding.Infinity(Decimal64Encoding.IsNegative(value));
        }

        if (Decimal64Encoding.IsZero(value))
        {
            // Every zero reduces to the same one, whatever exponent it arrived with, and it
            // keeps its sign: applying the context first would turn a negative zero
            // positive, as plus does.
            return Decimal64Encoding.Zero(Decimal64Encoding.IsNegative(value), 0);
        }

        // Applying the context first is what settles a subnormal operand's conditions
        // before its zeros are counted.
        var applied = Decimal64Arithmetic.AddToZero(value, false, rounding, ref status);
        var coefficient = Decimal64Encoding.Unpack(applied, out var exponent);

        StripTrailingZeros(ref coefficient, ref exponent, Decimal64Encoding.MaxQuantumExponent);
        return Decimal64Encoding.Pack(Decimal64Encoding.IsNegative(applied), exponent, coefficient);
    }

    /// <summary>
    /// Like <see cref="Reduce"/> but stopping at a zero exponent, so an integer keeps the
    /// zeros that are part of its magnitude.
    /// </summary>
    public static ulong Trim(ulong value)
    {
        if (Decimal64Encoding.IsSpecial(value))
        {
            return Decimal64Encoding.Canonical(value);
        }

        var coefficient = Decimal64Encoding.Unpack(value, out var exponent);
        if (coefficient == 0)
        {
            return Decimal64Encoding.Pack(Decimal64Encoding.IsNegative(value), exponent, 0);
        }

        StripTrailingZeros(ref coefficient, ref exponent, 0);
        return Decimal64Encoding.Pack(Decimal64Encoding.IsNegative(value), exponent, coefficient);
    }

    /// <summary>
    /// The adjusted exponent as an integer value. A zero has no leading digit to point at,
    /// so it reports negative infinity and signals division by zero, the way the logarithm
    /// it stands in for would.
    /// </summary>
    public static ulong LogB(ulong value, Decimal64Rounding rounding, ref Decimal64Status status)
    {
        if (Decimal64Encoding.IsSpecial(value))
        {
            if (Decimal64Encoding.IsNaN(value))
            {
                return Decimal64Arithmetic.PropagateNaN(value, ref status);
            }

            return Decimal64Encoding.Infinity(false);
        }

        var coefficient = Decimal64Encoding.Unpack(value, out var exponent);
        if (coefficient == 0)
        {
            status |= Decimal64Status.DivisionByZero;
            return Decimal64Encoding.Infinity(true);
        }

        var adjusted = exponent + Decimal64Tables.CountDigits(coefficient) - 1;
        return Decimal64Finalizer.Finalize(adjusted < 0, (ulong)Math.Abs(adjusted), 0, Decimal64Residue.Exact, rounding, ref status);
    }

    /// <summary>
    /// The adjusted exponent as a plain integer, which is what .NET's ILogB gives. The
    /// results off the end of the range are .NET's rather than the specification's: a zero
    /// gives <see cref="int.MinValue"/> and a NaN or an infinity gives
    /// <see cref="int.MaxValue"/>, where logb gives -Infinity, a NaN, and +Infinity.
    /// </summary>
    public static int ILogB(ulong value)
    {
        if (Decimal64Encoding.IsSpecial(value))
        {
            return int.MaxValue;
        }

        var coefficient = Decimal64Encoding.Unpack(value, out var exponent);
        if (coefficient == 0)
        {
            return int.MinValue;
        }

        return exponent + Decimal64Tables.CountDigits(coefficient) - 1;
    }

    /// <summary>
    /// The largest shift scaleb accepts: no larger than could move any value from one end
    /// of the format's range to the other.
    /// </summary>
    private const int ScaleLimit = 2 * (Decimal64Encoding.MaxExponent + Decimal64Encoding.Precision);

    /// <summary>Multiplies by a power of ten given as a second operand.</summary>
    public static ulong ScaleB(ulong value, ulong scale, Decimal64Rounding rounding, ref Decimal64Status status)
    {
        if (Decimal64Encoding.IsNaN(value) || Decimal64Encoding.IsNaN(scale))
        {
            return Decimal64Arithmetic.PropagateNaN(value, scale, ref status);
        }

        // The shift has to be a plain integer inside the limit; anything else is invalid
        // rather than clamped.
        if (!TryReadInteger(scale, ScaleLimit, out var shift))
        {
            return Decimal64Arithmetic.Invalid(ref status);
        }

        return ScaleB(value, shift, rounding, ref status);
    }

    /// <summary>
    /// Multiplies by ten to <paramref name="shift"/>. A shift past the limit is an invalid
    /// operation, as it is when given as an operand, rather than an overflow.
    /// </summary>
    public static ulong ScaleB(ulong value, int shift, Decimal64Rounding rounding, ref Decimal64Status status)
    {
        if (Decimal64Encoding.IsNaN(value))
        {
            return Decimal64Arithmetic.PropagateNaN(value, ref status);
        }

        if (shift > ScaleLimit || shift < -ScaleLimit)
        {
            return Decimal64Arithmetic.Invalid(ref status);
        }

        if (Decimal64Encoding.IsInfinity(value))
        {
            return Decimal64Encoding.Infinity(Decimal64Encoding.IsNegative(value));
        }

        var coefficient = Decimal64Encoding.Unpack(value, out var exponent);
        return Decimal64Finalizer.Finalize(Decimal64Encoding.IsNegative(value), coefficient, exponent + shift,
            Decimal64Residue.Exact, rounding, ref status);
    }

    /// <summary>
    /// Reads an operand as a plain integer, which the shift-like operations take rather
    /// than a general value: it has to be finite, have an exponent of zero, and fall inside
    /// the limit.
    /// </summary>
    public static bool TryReadInteger(ulong value, int limit, out int result)
    {
        result = 0;

        if (Decimal64Encoding.IsSpecial(value))
        {
            return false;
        }

        var coefficient = Decimal64Encoding.Unpack(value, out var exponent);
        if (exponent != 0 || coefficient > (ulong)limit)
        {
            return false;
        }

        var magnitude = (int)coefficient;
        result = Decimal64Encoding.IsNegative(value) ? -magnitude : magnitude;
        return true;
    }

    /// <summary>
    /// Moves the coefficient's digits within the format's full width. Rotating carries
    /// digits round the ends; shifting drops them and brings zeros in.
    /// </summary>
    public static ulong RotateOrShift(ulong value, ulong places, bool rotate, ref Decimal64Status status)
    {
        if (Decimal64Encoding.IsNaN(value) || Decimal64Encoding.IsNaN(places))
        {
            return Decimal64Arithmetic.PropagateNaN(value, places, ref status);
        }

        // The count has to be a plain integer no further than the width in either
        // direction; anything else is invalid rather than clamped.
        if (!TryReadInteger(places, Decimal64Encoding.Precision, out var count))
        {
            return Decimal64Arithmetic.Invalid(ref status);
        }

        if (Decimal64Encoding.IsInfinity(value))
        {
            return Decimal64Encoding.Infinity(Decimal64Encoding.IsNegative(value));
        }

        var coefficient = Decimal64Encoding.Unpack(value, out var exponent);
        var negative = Decimal64Encoding.IsNegative(value);

        if (count == 0)
        {
            return Decimal64Encoding.Pack(negative, exponent, coefficient);
        }

        // A positive count moves digits toward the leading end. What leaves the sixteen
        // digit field at the top comes back in at the bottom for a rotate and is lost for
        // a shift; a negative count runs the other way.
        var left = count > 0 ? count : Decimal64Encoding.Precision + count;
        var right = Decimal64Encoding.Precision - left;

        ulong movedUp;
        ulong movedDown;
        if (left == Decimal64Encoding.Precision)
        {
            movedUp = 0;
            movedDown = coefficient;
        }
        else
        {
            var wrapped = Decimal64Tables.DivRemPowerOfTen(coefficient, right, out var kept);
            movedUp = kept * Decimal64Tables.PowerOfTen(left);
            movedDown = wrapped;
        }

        ulong result;
        if (rotate)
        {
            result = movedUp + movedDown;
        }
        else
        {
            result = count > 0 ? movedUp : movedDown;
        }

        return Decimal64Encoding.Pack(negative, exponent, result);
    }

    /// <summary>The next value above or below, which is a step of one in the last place.</summary>
    public static ulong Next(ulong value, bool toward, bool quiet, ref Decimal64Status status)
    {
        if (Decimal64Encoding.IsSpecial(value))
        {
            if (Decimal64Encoding.IsNaN(value))
            {
                return Decimal64Arithmetic.PropagateNaN(value, ref status);
            }

            if (Decimal64Encoding.IsNegative(value) == toward)
            {
                // Stepping inward from an infinity lands on the largest finite.
                return Decimal64Encoding.Pack(Decimal64Encoding.IsNegative(value), Decimal64Encoding.MaxQuantumExponent,
                    Decimal64Encoding.MaxCoefficient);
            }

            // Stepping outward stays put.
            return Decimal64Encoding.Infinity(Decimal64Encoding.IsNegative(value));
        }

        // Adding a value smaller than the smallest subnormal, so the rounding is what moves
        // the value and the amount added never shows up in the result.
        var coefficient = Decimal64Encoding.Unpack(value, out var exponent);
        var rounding = toward ? Decimal64Rounding.Ceiling : Decimal64Rounding.Floor;
        var raised = Decimal64Status.None;

        var result = Decimal64Arithmetic.AddFinite(Decimal64Encoding.IsNegative(value), coefficient, exponent,
            !toward, 1, Decimal64Encoding.MinQuantumExponent - 1, rounding, ref raised);

        // next-plus and next-minus report nothing about how they got there; next-toward is
        // an arithmetic operation and reports underflow like one.
        status |= raised & Decimal64Status.InvalidOperation;
        if (quiet)
        {
            return result;
        }

        if ((raised & Decimal64Status.Overflow) != 0)
        {
            status |= Decimal64Status.Overflow | Decimal64Status.Inexact | Decimal64Status.Rounded;
        }
        else if ((raised & Decimal64Status.Underflow) != 0 || Decimal64Ordering.IsSubnormal(result))
        {
            // Stepping into or through the subnormal range is reported; a step that lands
            // on an ordinary value says nothing, however much rounding it took to get there.
            status |= raised & (Decimal64Status.Underflow | Decimal64Status.Inexact | Decimal64Status.Subnormal
                | Decimal64Status.Rounded | Decimal64Status.Clamped);
        }

        return result;
    }

    /// <summary>
    /// The next value from the first operand in the direction of the second. Unlike
    /// next-plus and next-minus this one reports a subnormal result.
    /// </summary>
    public static ulong NextToward(ulong value, ulong target, ref Decimal64Status status)
    {
        if (Decimal64Encoding.IsNaN(value) || Decimal64Encoding.IsNaN(target))
        {
            return Decimal64Arithmetic.PropagateNaN(value, target, ref status);
        }

        var comparison = Decimal64Ordering.CompareValues(value, target);
        if (comparison == 0)
        {
            // Already there. The result keeps the first operand's digits and takes the
            // second's sign, which is the only thing left to move.
            var magnitude = Decimal64Encoding.Canonical(value) & ~Decimal64Encoding.SignMask;
            return magnitude | (target & Decimal64Encoding.SignMask);
        }

        return Next(value, comparison < 0, false, ref status);
    }

    /// <summary>
    /// Rounds to a given number of fractional digits, for the .NET Round overloads. Asking
    /// for more digits than the value has is not an error, it just leaves it be: padding it
    /// out would change the quantum, which is what quantize is for.
    /// </summary>
    public static ulong Round(ulong value, int digits, Decimal64Rounding rounding)
    {
        if (Decimal64Encoding.IsSpecial(value))
        {
            return value;
        }

        Decimal64Encoding.Unpack(value, out var exponent);
        if (exponent >= -digits)
        {
            return value;
        }

        var status = Decimal64Status.None;
        var result = Rescale(value, -digits, rounding, ref status);
        return (status & Decimal64Status.InvalidOperation) != 0 ? value : result;
    }
}
