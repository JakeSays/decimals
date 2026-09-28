// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace Decimals.Internal;

/// <summary>
/// The operations that move a value's digits without changing what it is worth: rescaling
/// to an exponent, rounding to an integer, stripping zeros, stepping to a neighbor, and
/// reading or shifting the exponent.
/// </summary>
internal static class Decimal128Shaping
{
    /// <summary>
    /// Rescales the value to the pattern's exponent. Needing more digits than the format
    /// holds is invalid rather than rounded: the value cannot be said at that exponent.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Decimal128Integer Quantize(Decimal128Integer value, Decimal128Integer pattern,
        Decimal128Rounding rounding, ref Decimal128Status status)
    {
        if (Decimal128Encoding.IsSpecial(value) || Decimal128Encoding.IsSpecial(pattern))
        {
            if (Decimal128Encoding.IsNaN(value) || Decimal128Encoding.IsNaN(pattern))
            {
                return Decimal128Arithmetic.PropagateNaN(value, pattern, ref status);
            }

            // Only two infinities quantize to anything; one of each is invalid.
            if (Decimal128Encoding.IsInfinity(value) && Decimal128Encoding.IsInfinity(pattern))
            {
                return Decimal128Encoding.Infinity(Decimal128Encoding.IsNegative(value));
            }

            return Decimal128Arithmetic.Invalid(ref status);
        }

        Decimal128Encoding.Unpack(pattern, out var exponent);
        return Rescale(value, exponent, rounding, ref status);
    }

    /// <summary>Rescales a finite value to an exponent given directly.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Decimal128Integer Rescale(Decimal128Integer value, int exponent, Decimal128Rounding rounding,
        ref Decimal128Status status)
    {
        if (Decimal128Encoding.IsSpecial(value))
        {
            if (Decimal128Encoding.IsNaN(value))
            {
                return Decimal128Arithmetic.PropagateNaN(value, ref status);
            }

            return Decimal128Arithmetic.Invalid(ref status);
        }

        if (exponent > Decimal128Encoding.MaxQuantumExponent || exponent < Decimal128Encoding.MinQuantumExponent)
        {
            return Decimal128Arithmetic.Invalid(ref status);
        }

        var coefficient = Decimal128Encoding.Unpack(value, out var current);
        var negative = Decimal128Encoding.IsNegative(value);
        var shift = current - exponent;

        if (shift > 0 && !coefficient.IsZero)
        {
            if (Decimal128Tables.CountDigits(coefficient) + shift > Decimal128Encoding.Precision)
            {
                return Decimal128Arithmetic.Invalid(ref status);
            }

            coefficient = Decimal128Tables.Scale(coefficient, shift);
        }
        else if (shift < 0)
        {
            var wasZero = coefficient.IsZero;
            coefficient = Decimal128Rounder.DropDigits(coefficient, -shift, Decimal128Residue.Exact, out var residue);

            if (residue != Decimal128Residue.Exact
                && Decimal128Rounder.ShouldIncrement(coefficient, residue, negative, rounding))
            {
                coefficient += 1;
                if (coefficient > Decimal128Encoding.MaxCoefficient)
                {
                    return Decimal128Arithmetic.Invalid(ref status);
                }
            }

            if (!wasZero)
            {
                status |= Decimal128Status.Rounded;
            }

            if (residue != Decimal128Residue.Exact)
            {
                status |= Decimal128Status.Inexact;
            }
        }

        var result = Decimal128Finalizer.Finalize(negative, coefficient, exponent, Decimal128Residue.Exact, rounding,
            ref status);

        // Quantize is defined never to signal underflow: losing precision to reach the
        // requested exponent is the operation working, not a result vanishing.
        status &= ~Decimal128Status.Underflow;
        return result;
    }

    /// <summary>
    /// Rounds to an integer. <paramref name="exact"/> chooses between the two operations
    /// the specification offers: the exact one reports that digits were lost, the other is
    /// defined never to say it rounded.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Decimal128Integer ToIntegral(Decimal128Integer value, bool exact, Decimal128Rounding rounding,
        ref Decimal128Status status)
    {
        if (Decimal128Encoding.IsSpecial(value))
        {
            if (Decimal128Encoding.IsNaN(value))
            {
                return Decimal128Arithmetic.PropagateNaN(value, ref status);
            }

            return Decimal128Encoding.Infinity(Decimal128Encoding.IsNegative(value));
        }

        var coefficient = Decimal128Encoding.Unpack(value, out var exponent);
        var negative = Decimal128Encoding.IsNegative(value);

        if (exponent >= 0)
        {
            // Already an integer, and one whose exponent says so.
            return Decimal128Encoding.Pack(negative, exponent, coefficient);
        }

        var wasZero = coefficient.IsZero;
        coefficient = Decimal128Rounder.DropDigits(coefficient, -exponent, Decimal128Residue.Exact, out var residue);

        if (residue != Decimal128Residue.Exact
            && Decimal128Rounder.ShouldIncrement(coefficient, residue, negative, rounding))
        {
            coefficient += 1;
        }

        var local = Decimal128Status.None;
        var result = Decimal128Finalizer.Finalize(negative, coefficient, 0, Decimal128Residue.Exact, rounding,
            ref local);

        if (exact && !wasZero)
        {
            status |= local | Decimal128Status.Rounded;
            if (residue != Decimal128Residue.Exact)
            {
                status |= Decimal128Status.Inexact;
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
    public static void StripTrailingZeros(ref Decimal128Integer coefficient, ref int exponent, int exponentLimit)
    {
        if (coefficient.IsZero)
        {
            return;
        }

        // Sixteen zeros at a time, then eight, four, two, one: a run of zeros comes off in
        // a few divisions rather than one per zero.
        for (var step = 16; step >= 1; step /= 2)
        {
            while (exponent + step <= exponentLimit)
            {
                var shorter = Decimal128Tables.DivRemPowerOfTen(coefficient, step, out var rest);
                if (rest != 0)
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
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Decimal128Integer Reduce(Decimal128Integer value, Decimal128Rounding rounding,
        ref Decimal128Status status)
    {
        if (Decimal128Encoding.IsSpecial(value))
        {
            if (Decimal128Encoding.IsNaN(value))
            {
                return Decimal128Arithmetic.PropagateNaN(value, ref status);
            }

            return Decimal128Encoding.Infinity(Decimal128Encoding.IsNegative(value));
        }

        if (Decimal128Encoding.IsZero(value))
        {
            // Every zero reduces to the same one, whatever exponent it arrived with, and it
            // keeps its sign: applying the context first would turn a negative zero
            // positive, as plus does.
            return Decimal128Encoding.Zero(Decimal128Encoding.IsNegative(value), 0);
        }

        // Applying the context first is what settles a subnormal operand's conditions
        // before its zeros are counted.
        var applied = Decimal128Arithmetic.AddToZero(value, false, rounding, ref status);
        var coefficient = Decimal128Encoding.Unpack(applied, out var exponent);

        StripTrailingZeros(ref coefficient, ref exponent, Decimal128Encoding.MaxQuantumExponent);
        return Decimal128Encoding.Pack(Decimal128Encoding.IsNegative(applied), exponent, coefficient);
    }

    /// <summary>
    /// Like <see cref="Reduce"/> but stopping at a zero exponent, so an integer keeps the
    /// zeros that are part of its magnitude.
    /// </summary>
    public static Decimal128Integer Trim(Decimal128Integer value)
    {
        if (Decimal128Encoding.IsSpecial(value))
        {
            return Decimal128Encoding.Canonical(value);
        }

        var coefficient = Decimal128Encoding.Unpack(value, out var exponent);
        if (coefficient.IsZero)
        {
            return Decimal128Encoding.Pack(Decimal128Encoding.IsNegative(value), exponent, Decimal128Integer.Zero);
        }

        StripTrailingZeros(ref coefficient, ref exponent, 0);
        return Decimal128Encoding.Pack(Decimal128Encoding.IsNegative(value), exponent, coefficient);
    }

    /// <summary>
    /// The adjusted exponent as an integer value. A zero has no leading digit to point at,
    /// so it reports negative infinity and signals division by zero, the way the logarithm
    /// it stands in for would.
    /// </summary>
    public static Decimal128Integer LogB(Decimal128Integer value, Decimal128Rounding rounding,
        ref Decimal128Status status)
    {
        if (Decimal128Encoding.IsSpecial(value))
        {
            if (Decimal128Encoding.IsNaN(value))
            {
                return Decimal128Arithmetic.PropagateNaN(value, ref status);
            }

            return Decimal128Encoding.Infinity(false);
        }

        var coefficient = Decimal128Encoding.Unpack(value, out var exponent);
        if (coefficient.IsZero)
        {
            status |= Decimal128Status.DivisionByZero;
            return Decimal128Encoding.Infinity(true);
        }

        var adjusted = exponent + Decimal128Tables.CountDigits(coefficient) - 1;
        return Decimal128Finalizer.Finalize(adjusted < 0, Decimal128Integer.FromUInt64((ulong)Math.Abs(adjusted)), 0,
            Decimal128Residue.Exact, rounding, ref status);
    }

    /// <summary>
    /// The adjusted exponent as a plain integer, which is what .NET's ILogB gives. The
    /// results off the end of the range are .NET's rather than the specification's: a zero
    /// gives <see cref="int.MinValue"/> and a NaN or an infinity gives
    /// <see cref="int.MaxValue"/>, where logb gives -Infinity, a NaN, and +Infinity.
    /// </summary>
    public static int ILogB(Decimal128Integer value)
    {
        if (Decimal128Encoding.IsSpecial(value))
        {
            return int.MaxValue;
        }

        var coefficient = Decimal128Encoding.Unpack(value, out var exponent);
        if (coefficient.IsZero)
        {
            return int.MinValue;
        }

        return exponent + Decimal128Tables.CountDigits(coefficient) - 1;
    }

    /// <summary>
    /// The largest shift scaleb accepts: no larger than could move any value from one end
    /// of the format's range to the other.
    /// </summary>
    private const int ScaleLimit = 2 * (Decimal128Encoding.MaxExponent + Decimal128Encoding.Precision);

    /// <summary>Multiplies by a power of ten given as a second operand.</summary>
    public static Decimal128Integer ScaleB(Decimal128Integer value, Decimal128Integer scale, Decimal128Rounding rounding,
        ref Decimal128Status status)
    {
        if (Decimal128Encoding.IsNaN(value) || Decimal128Encoding.IsNaN(scale))
        {
            return Decimal128Arithmetic.PropagateNaN(value, scale, ref status);
        }

        // The shift has to be a plain integer inside the limit; anything else is invalid
        // rather than clamped.
        if (!TryReadInteger(scale, ScaleLimit, out var shift))
        {
            return Decimal128Arithmetic.Invalid(ref status);
        }

        return ScaleB(value, shift, rounding, ref status);
    }

    /// <summary>
    /// Multiplies by ten to <paramref name="shift"/>. A shift past the limit is an invalid
    /// operation, as it is when given as an operand, rather than an overflow.
    /// </summary>
    public static Decimal128Integer ScaleB(Decimal128Integer value, int shift, Decimal128Rounding rounding,
        ref Decimal128Status status)
    {
        if (Decimal128Encoding.IsNaN(value))
        {
            return Decimal128Arithmetic.PropagateNaN(value, ref status);
        }

        if (shift > ScaleLimit || shift < -ScaleLimit)
        {
            return Decimal128Arithmetic.Invalid(ref status);
        }

        if (Decimal128Encoding.IsInfinity(value))
        {
            return Decimal128Encoding.Infinity(Decimal128Encoding.IsNegative(value));
        }

        var coefficient = Decimal128Encoding.Unpack(value, out var exponent);
        return Decimal128Finalizer.Finalize(Decimal128Encoding.IsNegative(value), coefficient, exponent + shift,
            Decimal128Residue.Exact, rounding, ref status);
    }

    /// <summary>
    /// Reads an operand as a plain integer, which the shift-like operations take rather
    /// than a general value: it has to be finite, have an exponent of zero, and fall inside
    /// the limit.
    /// </summary>
    public static bool TryReadInteger(Decimal128Integer value, int limit, out int result)
    {
        result = 0;

        if (Decimal128Encoding.IsSpecial(value))
        {
            return false;
        }

        var coefficient = Decimal128Encoding.Unpack(value, out var exponent);
        if (exponent != 0 || !coefficient.IsWord || coefficient.Low > (ulong)limit)
        {
            return false;
        }

        var magnitude = (int)coefficient.Low;
        result = Decimal128Encoding.IsNegative(value) ? -magnitude : magnitude;
        return true;
    }

    /// <summary>
    /// Moves the coefficient's digits within the format's full width. Rotating carries
    /// digits round the ends; shifting drops them and brings zeros in.
    /// </summary>
    public static Decimal128Integer RotateOrShift(Decimal128Integer value, Decimal128Integer places, bool rotate,
        ref Decimal128Status status)
    {
        if (Decimal128Encoding.IsNaN(value) || Decimal128Encoding.IsNaN(places))
        {
            return Decimal128Arithmetic.PropagateNaN(value, places, ref status);
        }

        // The count has to be a plain integer no further than the width in either
        // direction; anything else is invalid rather than clamped.
        if (!TryReadInteger(places, Decimal128Encoding.Precision, out var count))
        {
            return Decimal128Arithmetic.Invalid(ref status);
        }

        if (Decimal128Encoding.IsInfinity(value))
        {
            return Decimal128Encoding.Infinity(Decimal128Encoding.IsNegative(value));
        }

        var coefficient = Decimal128Encoding.Unpack(value, out var exponent);
        var negative = Decimal128Encoding.IsNegative(value);

        if (count == 0)
        {
            return Decimal128Encoding.Pack(negative, exponent, coefficient);
        }

        // A positive count moves digits toward the leading end. What leaves the
        // thirty-four digit field at the top comes back in at the bottom for a rotate and
        // is lost for a shift; a negative count runs the other way.
        var left = count > 0 ? count : Decimal128Encoding.Precision + count;
        var right = Decimal128Encoding.Precision - left;

        Decimal128Integer movedUp;
        Decimal128Integer movedDown;
        if (left == Decimal128Encoding.Precision)
        {
            movedUp = Decimal128Integer.Zero;
            movedDown = coefficient;
        }
        else
        {
            var wrapped = Decimal128Tables.DivRemWidePowerOfTen(coefficient, right, out var kept);
            movedUp = Decimal128Tables.Scale(kept, left);
            movedDown = wrapped;
        }

        Decimal128Integer result;
        if (rotate)
        {
            result = movedUp + movedDown;
        }
        else
        {
            result = count > 0 ? movedUp : movedDown;
        }

        return Decimal128Encoding.Pack(negative, exponent, result);
    }

    /// <summary>The next value above or below, which is a step of one in the last place.</summary>
    public static Decimal128Integer Next(Decimal128Integer value, bool toward, bool quiet, ref Decimal128Status status)
    {
        if (Decimal128Encoding.IsSpecial(value))
        {
            if (Decimal128Encoding.IsNaN(value))
            {
                return Decimal128Arithmetic.PropagateNaN(value, ref status);
            }

            if (Decimal128Encoding.IsNegative(value) == toward)
            {
                // Stepping inward from an infinity lands on the largest finite.
                return Decimal128Encoding.Pack(Decimal128Encoding.IsNegative(value),
                    Decimal128Encoding.MaxQuantumExponent, Decimal128Encoding.MaxCoefficient);
            }

            // Stepping outward stays put.
            return Decimal128Encoding.Infinity(Decimal128Encoding.IsNegative(value));
        }

        // Adding a value smaller than the smallest subnormal, so the rounding is what moves
        // the value and the amount added never shows up in the result.
        var coefficient = Decimal128Encoding.Unpack(value, out var exponent);
        var rounding = toward ? Decimal128Rounding.Ceiling : Decimal128Rounding.Floor;
        var raised = Decimal128Status.None;

        var result = Decimal128Arithmetic.AddFinite(Decimal128Encoding.IsNegative(value), coefficient, exponent,
            !toward, Decimal128Integer.One, Decimal128Encoding.MinQuantumExponent - 1, rounding, ref raised);

        // next-plus and next-minus report nothing about how they got there; next-toward is
        // an arithmetic operation and reports underflow like one.
        status |= raised & Decimal128Status.InvalidOperation;
        if (quiet)
        {
            return result;
        }

        if ((raised & Decimal128Status.Overflow) != 0)
        {
            status |= Decimal128Status.Overflow | Decimal128Status.Inexact | Decimal128Status.Rounded;
        }
        else if ((raised & Decimal128Status.Underflow) != 0 || Decimal128Ordering.IsSubnormal(result))
        {
            // Stepping into or through the subnormal range is reported; a step that lands
            // on an ordinary value says nothing, however much rounding it took to get there.
            status |= raised & (Decimal128Status.Underflow | Decimal128Status.Inexact | Decimal128Status.Subnormal
                | Decimal128Status.Rounded | Decimal128Status.Clamped);
        }

        return result;
    }

    /// <summary>
    /// The next value from the first operand in the direction of the second. Unlike
    /// next-plus and next-minus this one reports a subnormal result.
    /// </summary>
    public static Decimal128Integer NextToward(Decimal128Integer value, Decimal128Integer target,
        ref Decimal128Status status)
    {
        if (Decimal128Encoding.IsNaN(value) || Decimal128Encoding.IsNaN(target))
        {
            return Decimal128Arithmetic.PropagateNaN(value, target, ref status);
        }

        var comparison = Decimal128Ordering.CompareValues(value, target);
        if (comparison == 0)
        {
            // Already there. The result keeps the first operand's digits and takes the
            // second's sign, which is the only thing left to move.
            var magnitude = Decimal128Ordering.Magnitude(Decimal128Encoding.Canonical(value));
            return new Decimal128Integer(magnitude.High | (target.High & Decimal128Encoding.SignMask), magnitude.Low);
        }

        return Next(value, comparison < 0, false, ref status);
    }

    /// <summary>
    /// Rounds to a given number of fractional digits, for the .NET Round overloads. Asking
    /// for more digits than the value has is not an error, it just leaves it be: padding it
    /// out would change the quantum, which is what quantize is for.
    /// </summary>
    public static Decimal128Integer Round(Decimal128Integer value, int digits, Decimal128Rounding rounding)
    {
        if (Decimal128Encoding.IsSpecial(value))
        {
            return value;
        }

        Decimal128Encoding.Unpack(value, out var exponent);
        if (exponent >= -digits)
        {
            return value;
        }

        var status = Decimal128Status.None;
        var result = Rescale(value, -digits, rounding, ref status);
        return (status & Decimal128Status.InvalidOperation) != 0 ? value : result;
    }
}
