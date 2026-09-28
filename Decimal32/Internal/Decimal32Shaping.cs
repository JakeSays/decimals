// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// The operations that move a value's digits without changing what it is worth: rescaling
/// to an exponent, rounding to an integer, stripping zeros, stepping to a neighbor, and
/// reading or shifting the exponent.
/// </summary>
internal static class Decimal32Shaping
{
    /// <summary>
    /// Rescales the value to the pattern's exponent. Needing more digits than the format
    /// holds is invalid rather than rounded: the value cannot be said at that exponent.
    /// </summary>
    public static uint Quantize(uint value, uint pattern, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsSpecial(value) || Decimal32Encoding.IsSpecial(pattern))
        {
            if (Decimal32Encoding.IsNaN(value) || Decimal32Encoding.IsNaN(pattern))
            {
                return Decimal32Arithmetic.PropagateNaN(value, pattern, ref status);
            }

            // Only two infinities quantize to anything; one of each is invalid.
            if (Decimal32Encoding.IsInfinity(value) && Decimal32Encoding.IsInfinity(pattern))
            {
                return Decimal32Encoding.Infinity(Decimal32Encoding.IsNegative(value));
            }

            return Decimal32Arithmetic.Invalid(ref status);
        }

        Decimal32Encoding.Unpack(pattern, out var exponent);
        return Rescale(value, exponent, rounding, ref status);
    }

    /// <summary>Rescales a finite value to an exponent given directly.</summary>
    public static uint Rescale(uint value, int exponent, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsSpecial(value))
        {
            if (Decimal32Encoding.IsNaN(value))
            {
                return Decimal32Arithmetic.PropagateNaN(value, ref status);
            }

            return Decimal32Arithmetic.Invalid(ref status);
        }

        if (exponent > Decimal32Encoding.MaxQuantumExponent || exponent < Decimal32Encoding.MinQuantumExponent)
        {
            return Decimal32Arithmetic.Invalid(ref status);
        }

        ulong coefficient = Decimal32Encoding.Unpack(value, out var current);
        var negative = Decimal32Encoding.IsNegative(value);
        var shift = current - exponent;

        if (shift > 0 && coefficient != 0)
        {
            if (Decimal32Tables.CountDigits(coefficient) + shift > Decimal32Encoding.Precision)
            {
                return Decimal32Arithmetic.Invalid(ref status);
            }

            coefficient *= Decimal32Tables.PowerOfTen(shift);
        }
        else if (shift < 0)
        {
            var wasZero = coefficient == 0;
            coefficient = Decimal32Rounder.DropDigits(coefficient, -shift, Decimal32Residue.Exact, out var residue);

            if (residue != Decimal32Residue.Exact
                && Decimal32Rounder.ShouldIncrement(coefficient, residue, negative, rounding))
            {
                coefficient++;
                if (coefficient > Decimal32Encoding.MaxCoefficient)
                {
                    return Decimal32Arithmetic.Invalid(ref status);
                }
            }

            if (!wasZero)
            {
                status |= Decimal32Status.Rounded;
            }

            if (residue != Decimal32Residue.Exact)
            {
                status |= Decimal32Status.Inexact;
            }
        }

        var result = Decimal32Finalizer.Finalize(negative, coefficient, exponent, Decimal32Residue.Exact, rounding, ref status);

        // Quantize is defined never to signal underflow: losing precision to reach the
        // requested exponent is the operation working, not a result vanishing.
        status &= ~Decimal32Status.Underflow;
        return result;
    }

    /// <summary>
    /// Rounds to an integer. <paramref name="exact"/> chooses between the two operations
    /// the specification offers: the exact one reports that digits were lost, the other is
    /// defined never to say it rounded.
    /// </summary>
    public static uint ToIntegral(uint value, bool exact, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsSpecial(value))
        {
            if (Decimal32Encoding.IsNaN(value))
            {
                return Decimal32Arithmetic.PropagateNaN(value, ref status);
            }

            return Decimal32Encoding.Infinity(Decimal32Encoding.IsNegative(value));
        }

        ulong coefficient = Decimal32Encoding.Unpack(value, out var exponent);
        var negative = Decimal32Encoding.IsNegative(value);

        if (exponent >= 0)
        {
            // Already an integer, and one whose exponent says so.
            return Decimal32Encoding.Pack(negative, exponent, (uint)coefficient);
        }

        var wasZero = coefficient == 0;
        coefficient = Decimal32Rounder.DropDigits(coefficient, -exponent, Decimal32Residue.Exact, out var residue);

        if (residue != Decimal32Residue.Exact
            && Decimal32Rounder.ShouldIncrement(coefficient, residue, negative, rounding))
        {
            coefficient++;
        }

        var local = Decimal32Status.None;
        var result = Decimal32Finalizer.Finalize(negative, coefficient, 0, Decimal32Residue.Exact, rounding, ref local);

        if (exact && !wasZero)
        {
            status |= local | Decimal32Status.Rounded;
            if (residue != Decimal32Residue.Exact)
            {
                status |= Decimal32Status.Inexact;
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

        // Four zeros at a time, then two, one: a run of zeros comes off in a few constant
        // divisions rather than one per zero.
        for (var step = 4; step >= 1; step /= 2)
        {
            var power = Decimal32Tables.PowerOfTen(step);
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
    public static uint Reduce(uint value, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsSpecial(value))
        {
            if (Decimal32Encoding.IsNaN(value))
            {
                return Decimal32Arithmetic.PropagateNaN(value, ref status);
            }

            return Decimal32Encoding.Infinity(Decimal32Encoding.IsNegative(value));
        }

        if (Decimal32Encoding.IsZero(value))
        {
            // Every zero reduces to the same one, whatever exponent it arrived with, and it
            // keeps its sign: applying the context first would turn a negative zero
            // positive, as plus does.
            return Decimal32Encoding.Zero(Decimal32Encoding.IsNegative(value), 0);
        }

        // Applying the context first is what settles a subnormal operand's conditions
        // before its zeros are counted.
        var applied = Decimal32Arithmetic.AddToZero(value, false, rounding, ref status);
        ulong coefficient = Decimal32Encoding.Unpack(applied, out var exponent);

        StripTrailingZeros(ref coefficient, ref exponent, Decimal32Encoding.MaxQuantumExponent);
        return Decimal32Encoding.Pack(Decimal32Encoding.IsNegative(applied), exponent, (uint)coefficient);
    }

    /// <summary>
    /// Like <see cref="Reduce"/> but stopping at a zero exponent, so an integer keeps the
    /// zeros that are part of its magnitude.
    /// </summary>
    public static uint Trim(uint value)
    {
        if (Decimal32Encoding.IsSpecial(value))
        {
            return Decimal32Encoding.Canonical(value);
        }

        ulong coefficient = Decimal32Encoding.Unpack(value, out var exponent);
        if (coefficient == 0)
        {
            return Decimal32Encoding.Pack(Decimal32Encoding.IsNegative(value), exponent, 0);
        }

        StripTrailingZeros(ref coefficient, ref exponent, 0);
        return Decimal32Encoding.Pack(Decimal32Encoding.IsNegative(value), exponent, (uint)coefficient);
    }

    /// <summary>
    /// The adjusted exponent as an integer value. A zero has no leading digit to point at,
    /// so it reports negative infinity and signals division by zero, the way the logarithm
    /// it stands in for would.
    /// </summary>
    public static uint LogB(uint value, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsSpecial(value))
        {
            if (Decimal32Encoding.IsNaN(value))
            {
                return Decimal32Arithmetic.PropagateNaN(value, ref status);
            }

            return Decimal32Encoding.Infinity(false);
        }

        var coefficient = Decimal32Encoding.Unpack(value, out var exponent);
        if (coefficient == 0)
        {
            status |= Decimal32Status.DivisionByZero;
            return Decimal32Encoding.Infinity(true);
        }

        var adjusted = exponent + Decimal32Tables.CountDigits(coefficient) - 1;
        return Decimal32Finalizer.Finalize(adjusted < 0, (ulong)Math.Abs(adjusted), 0, Decimal32Residue.Exact, rounding, ref status);
    }

    /// <summary>
    /// The adjusted exponent as a plain integer, which is what .NET's ILogB gives. The
    /// results off the end of the range are .NET's rather than the specification's: a zero
    /// gives <see cref="int.MinValue"/> and a NaN or an infinity gives
    /// <see cref="int.MaxValue"/>, where logb gives -Infinity, a NaN, and +Infinity.
    /// </summary>
    public static int ILogB(uint value)
    {
        if (Decimal32Encoding.IsSpecial(value))
        {
            return int.MaxValue;
        }

        var coefficient = Decimal32Encoding.Unpack(value, out var exponent);
        if (coefficient == 0)
        {
            return int.MinValue;
        }

        return exponent + Decimal32Tables.CountDigits(coefficient) - 1;
    }

    /// <summary>
    /// The largest shift scaleb accepts: no larger than could move any value from one end
    /// of the format's range to the other.
    /// </summary>
    private const int ScaleLimit = 2 * (Decimal32Encoding.MaxExponent + Decimal32Encoding.Precision);

    /// <summary>Multiplies by a power of ten given as a second operand.</summary>
    public static uint ScaleB(uint value, uint scale, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsNaN(value) || Decimal32Encoding.IsNaN(scale))
        {
            return Decimal32Arithmetic.PropagateNaN(value, scale, ref status);
        }

        // The shift has to be a plain integer inside the limit; anything else is invalid
        // rather than clamped.
        if (!TryReadInteger(scale, ScaleLimit, out var shift))
        {
            return Decimal32Arithmetic.Invalid(ref status);
        }

        return ScaleB(value, shift, rounding, ref status);
    }

    /// <summary>
    /// Multiplies by ten to <paramref name="shift"/>. A shift past the limit is an invalid
    /// operation, as it is when given as an operand, rather than an overflow.
    /// </summary>
    public static uint ScaleB(uint value, int shift, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsNaN(value))
        {
            return Decimal32Arithmetic.PropagateNaN(value, ref status);
        }

        if (shift > ScaleLimit || shift < -ScaleLimit)
        {
            return Decimal32Arithmetic.Invalid(ref status);
        }

        if (Decimal32Encoding.IsInfinity(value))
        {
            return Decimal32Encoding.Infinity(Decimal32Encoding.IsNegative(value));
        }

        var coefficient = Decimal32Encoding.Unpack(value, out var exponent);
        return Decimal32Finalizer.Finalize(Decimal32Encoding.IsNegative(value), coefficient, exponent + shift,
            Decimal32Residue.Exact, rounding, ref status);
    }

    /// <summary>
    /// Reads an operand as a plain integer, which the shift-like operations take rather
    /// than a general value: it has to be finite, have an exponent of zero, and fall inside
    /// the limit.
    /// </summary>
    public static bool TryReadInteger(uint value, int limit, out int result)
    {
        result = 0;

        if (Decimal32Encoding.IsSpecial(value))
        {
            return false;
        }

        var coefficient = Decimal32Encoding.Unpack(value, out var exponent);
        if (exponent != 0 || coefficient > (uint)limit)
        {
            return false;
        }

        var magnitude = (int)coefficient;
        result = Decimal32Encoding.IsNegative(value) ? -magnitude : magnitude;
        return true;
    }

    /// <summary>
    /// Moves the coefficient's digits within the format's full width. Rotating carries
    /// digits round the ends; shifting drops them and brings zeros in.
    /// </summary>
    public static uint RotateOrShift(uint value, uint places, bool rotate, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsNaN(value) || Decimal32Encoding.IsNaN(places))
        {
            return Decimal32Arithmetic.PropagateNaN(value, places, ref status);
        }

        // The count has to be a plain integer no further than the width in either
        // direction; anything else is invalid rather than clamped.
        if (!TryReadInteger(places, Decimal32Encoding.Precision, out var count))
        {
            return Decimal32Arithmetic.Invalid(ref status);
        }

        if (Decimal32Encoding.IsInfinity(value))
        {
            return Decimal32Encoding.Infinity(Decimal32Encoding.IsNegative(value));
        }

        ulong coefficient = Decimal32Encoding.Unpack(value, out var exponent);
        var negative = Decimal32Encoding.IsNegative(value);

        if (count == 0)
        {
            return Decimal32Encoding.Pack(negative, exponent, (uint)coefficient);
        }

        // A positive count moves digits toward the leading end. What leaves the seven
        // digit field at the top comes back in at the bottom for a rotate and is lost for
        // a shift; a negative count runs the other way.
        var left = count > 0 ? count : Decimal32Encoding.Precision + count;
        var right = Decimal32Encoding.Precision - left;

        ulong movedUp;
        ulong movedDown;
        if (left == Decimal32Encoding.Precision)
        {
            movedUp = 0;
            movedDown = coefficient;
        }
        else
        {
            var wrapped = Decimal32Tables.DivRemPowerOfTen(coefficient, right, out var kept);
            movedUp = kept * Decimal32Tables.PowerOfTen(left);
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

        return Decimal32Encoding.Pack(negative, exponent, (uint)result);
    }

    /// <summary>The next value above or below, which is a step of one in the last place.</summary>
    public static uint Next(uint value, bool toward, bool quiet, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsSpecial(value))
        {
            if (Decimal32Encoding.IsNaN(value))
            {
                return Decimal32Arithmetic.PropagateNaN(value, ref status);
            }

            if (Decimal32Encoding.IsNegative(value) == toward)
            {
                // Stepping inward from an infinity lands on the largest finite.
                return Decimal32Encoding.Pack(Decimal32Encoding.IsNegative(value), Decimal32Encoding.MaxQuantumExponent,
                    Decimal32Encoding.MaxCoefficient);
            }

            // Stepping outward stays put.
            return Decimal32Encoding.Infinity(Decimal32Encoding.IsNegative(value));
        }

        // Adding a value smaller than the smallest subnormal, so the rounding is what moves
        // the value and the amount added never shows up in the result.
        var coefficient = Decimal32Encoding.Unpack(value, out var exponent);
        var rounding = toward ? Decimal32Rounding.Ceiling : Decimal32Rounding.Floor;
        var raised = Decimal32Status.None;

        var result = Decimal32Arithmetic.AddFinite(Decimal32Encoding.IsNegative(value), coefficient, exponent,
            !toward, 1, Decimal32Encoding.MinQuantumExponent - 1, rounding, ref raised);

        // next-plus and next-minus report nothing about how they got there; next-toward is
        // an arithmetic operation and reports underflow like one.
        status |= raised & Decimal32Status.InvalidOperation;
        if (quiet)
        {
            return result;
        }

        if ((raised & Decimal32Status.Overflow) != 0)
        {
            status |= Decimal32Status.Overflow | Decimal32Status.Inexact | Decimal32Status.Rounded;
        }
        else if ((raised & Decimal32Status.Underflow) != 0 || Decimal32Ordering.IsSubnormal(result))
        {
            // Stepping into or through the subnormal range is reported; a step that lands
            // on an ordinary value says nothing, however much rounding it took to get there.
            status |= raised & (Decimal32Status.Underflow | Decimal32Status.Inexact | Decimal32Status.Subnormal
                | Decimal32Status.Rounded | Decimal32Status.Clamped);
        }

        return result;
    }

    /// <summary>
    /// The next value from the first operand in the direction of the second. Unlike
    /// next-plus and next-minus this one reports a subnormal result.
    /// </summary>
    public static uint NextToward(uint value, uint target, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsNaN(value) || Decimal32Encoding.IsNaN(target))
        {
            return Decimal32Arithmetic.PropagateNaN(value, target, ref status);
        }

        var comparison = Decimal32Ordering.CompareValues(value, target);
        if (comparison == 0)
        {
            // Already there. The result keeps the first operand's digits and takes the
            // second's sign, which is the only thing left to move.
            var magnitude = Decimal32Encoding.Canonical(value) & ~Decimal32Encoding.SignMask;
            return magnitude | (target & Decimal32Encoding.SignMask);
        }

        return Next(value, comparison < 0, false, ref status);
    }

    /// <summary>
    /// Rounds to a given number of fractional digits, for the .NET Round overloads. Asking
    /// for more digits than the value has is not an error, it just leaves it be: padding it
    /// out would change the quantum, which is what quantize is for.
    /// </summary>
    public static uint Round(uint value, int digits, Decimal32Rounding rounding)
    {
        if (Decimal32Encoding.IsSpecial(value))
        {
            return value;
        }

        Decimal32Encoding.Unpack(value, out var exponent);
        if (exponent >= -digits)
        {
            return value;
        }

        var status = Decimal32Status.None;
        var result = Rescale(value, -digits, rounding, ref status);
        return (status & Decimal32Status.InvalidOperation) != 0 ? value : result;
    }
}
