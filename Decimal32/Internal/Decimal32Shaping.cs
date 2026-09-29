// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// Operations on a value's digits and exponent: rescaling to an exponent, rounding to an
/// integer, removing trailing zeros, stepping to the next value, and reading or shifting
/// the exponent.
/// </summary>
internal static class Decimal32Shaping
{
    /// <summary>
    /// Rescales the value to the pattern's exponent. If the result needs more digits than
    /// the format holds, the operation is invalid instead of rounding, because the value
    /// cannot be represented at that exponent.
    /// </summary>
    /// <param name="value">The encoded value to rescale.</param>
    /// <param name="pattern">The encoded value whose exponent the result takes.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded value at the pattern's exponent, or a quiet NaN if it does not fit.</returns>
    public static uint Quantize(uint value, uint pattern, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsSpecial(value) || Decimal32Encoding.IsSpecial(pattern))
        {
            if (Decimal32Encoding.IsNaN(value) || Decimal32Encoding.IsNaN(pattern))
            {
                return Decimal32Arithmetic.PropagateNaN(value, pattern, ref status);
            }

            // Two infinities give an infinity. An infinity with a finite value is invalid.
            if (Decimal32Encoding.IsInfinity(value) && Decimal32Encoding.IsInfinity(pattern))
            {
                return Decimal32Encoding.Infinity(Decimal32Encoding.IsNegative(value));
            }

            return Decimal32Arithmetic.Invalid(ref status);
        }

        Decimal32Encoding.Unpack(pattern, out var exponent);
        return Rescale(value, exponent, rounding, ref status);
    }

    /// <summary>Rescales a finite value to the given exponent.</summary>
    /// <param name="value">The encoded value to rescale.</param>
    /// <param name="exponent">The exponent the result takes.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded value at <paramref name="exponent"/>, or a quiet NaN if the exponent is out of range or the value does not fit.</returns>
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

        // Quantize never signals underflow. Losing digits to reach the requested exponent
        // is the intended result, not a result that became too small.
        status &= ~Decimal32Status.Underflow;
        return result;
    }

    /// <summary>
    /// Rounds to an integer. <paramref name="exact"/> chooses between the specification's
    /// two operations: the exact one reports that digits were lost, and the other never
    /// reports rounding.
    /// </summary>
    /// <param name="value">The encoded value to round.</param>
    /// <param name="exact">Whether to report Rounded and Inexact when digits are removed.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded integer value.</returns>
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
            // The value is already an integer, with a non-negative exponent.
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
    /// Removes trailing zeros from the coefficient and raises the exponent to match, which
    /// gives the shortest coefficient with the same value. Stops at
    /// <paramref name="exponentLimit"/>, so an integer keeps the zeros that are part of its
    /// magnitude.
    /// </summary>
    /// <param name="coefficient">The coefficient. Receives the coefficient without its trailing zeros.</param>
    /// <param name="exponent">The coefficient's exponent. Receives the raised exponent.</param>
    /// <param name="exponentLimit">The highest exponent the result can take.</param>
    public static void StripTrailingZeros(ref ulong coefficient, ref int exponent, int exponentLimit)
    {
        if (coefficient == 0)
        {
            return;
        }

        // Remove zeros four at a time, then two, and one. A run of zeros takes a few
        // divisions by constants instead of one division per zero.
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
    /// Removes trailing zeros, which gives the shortest coefficient with the same value.
    /// </summary>
    /// <param name="value">The encoded value to reduce.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded value with no trailing zeros in its coefficient.</returns>
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
            // Every zero reduces to the same zero, whatever its exponent, and keeps its sign.
            // Applying the context first would make a negative zero positive, as plus does.
            return Decimal32Encoding.Zero(Decimal32Encoding.IsNegative(value), 0);
        }

        // Apply the context first, so a subnormal operand raises its conditions before its
        // zeros are removed.
        var applied = Decimal32Arithmetic.AddToZero(value, false, rounding, ref status);
        ulong coefficient = Decimal32Encoding.Unpack(applied, out var exponent);

        StripTrailingZeros(ref coefficient, ref exponent, Decimal32Encoding.MaxQuantumExponent);
        return Decimal32Encoding.Pack(Decimal32Encoding.IsNegative(applied), exponent, (uint)coefficient);
    }

    /// <summary>
    /// Like <see cref="Reduce"/>, but stops at exponent zero, so an integer keeps the zeros
    /// that are part of its magnitude.
    /// </summary>
    /// <param name="value">The encoded value to trim.</param>
    /// <returns>The encoded value with no trailing zeros after the decimal point.</returns>
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
    /// The adjusted exponent as a decimal value. Zero has no leading digit, so it returns
    /// negative infinity and signals division by zero, as a logarithm of zero would.
    /// </summary>
    /// <param name="value">The encoded operand.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded adjusted exponent.</returns>
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
    /// The adjusted exponent as an <see cref="int"/>, as .NET's ILogB returns it. The
    /// results for special inputs follow .NET, not the specification: zero gives
    /// <see cref="int.MinValue"/>, and a NaN or an infinity gives <see cref="int.MaxValue"/>.
    /// The specification's logb gives -Infinity, a NaN, and +Infinity for these.
    /// </summary>
    /// <param name="value">The encoded operand.</param>
    /// <returns>The adjusted exponent, or one of the special results above.</returns>
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
    /// The largest shift scaleb accepts. It is enough to move any value from one end of the
    /// format's range to the other.
    /// </summary>
    private const int ScaleLimit = 2 * (Decimal32Encoding.MaxExponent + Decimal32Encoding.Precision);

    /// <summary>Multiplies by a power of ten given as a second operand.</summary>
    /// <param name="value">The encoded value to scale.</param>
    /// <param name="scale">The encoded power of ten. It must be an integer with exponent zero.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded result, rounded to the format, or a quiet NaN if the scale is invalid.</returns>
    public static uint ScaleB(uint value, uint scale, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsNaN(value) || Decimal32Encoding.IsNaN(scale))
        {
            return Decimal32Arithmetic.PropagateNaN(value, scale, ref status);
        }

        // The shift must be an integer within the limit. Anything else is invalid, not
        // clamped.
        if (ReadInteger(scale, ScaleLimit) is not { } shift)
        {
            return Decimal32Arithmetic.Invalid(ref status);
        }

        return ScaleB(value, shift, rounding, ref status);
    }

    /// <summary>
    /// Multiplies by 10 to the power <paramref name="shift"/>. A shift beyond the limit is
    /// an invalid operation, as it is when given as an operand, not an overflow.
    /// </summary>
    /// <param name="value">The encoded value to scale.</param>
    /// <param name="shift">The power of ten.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded result, rounded to the format, or a quiet NaN if the shift is beyond the limit.</returns>
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
    /// Reads an operand as an integer, for the operations that take a shift count instead
    /// of a general value. The operand must be finite, have exponent zero, and be within the
    /// limit.
    /// </summary>
    /// <param name="value">The encoded operand.</param>
    /// <param name="limit">The largest magnitude accepted.</param>
    /// <returns>The integer, or null if the operand is not an integer within the limit.</returns>
    public static int? ReadInteger(uint value, int limit)
    {
        if (Decimal32Encoding.IsSpecial(value))
        {
            return null;
        }

        var coefficient = Decimal32Encoding.Unpack(value, out var exponent);
        if (exponent != 0 || coefficient > (uint)limit)
        {
            return null;
        }

        var magnitude = (int)coefficient;
        return Decimal32Encoding.IsNegative(value)
            ? -magnitude
            : magnitude;
    }

    /// <summary>
    /// Moves the coefficient's digits within the format's full width. A rotate moves digits
    /// that leave one end back in at the other end. A shift drops them and brings in zeros.
    /// </summary>
    /// <param name="value">The encoded value whose digits move.</param>
    /// <param name="places">The encoded number of positions. A positive count moves digits toward the most significant end.</param>
    /// <param name="rotate">Whether digits that leave one end come back in at the other, instead of being dropped.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded result, or a quiet NaN if the count is not an integer within the precision.</returns>
    public static uint RotateOrShift(uint value, uint places, bool rotate, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsNaN(value) || Decimal32Encoding.IsNaN(places))
        {
            return Decimal32Arithmetic.PropagateNaN(value, places, ref status);
        }

        // The count must be an integer no larger than the precision in either direction.
        // Anything else is invalid, not clamped.
        if (ReadInteger(places, Decimal32Encoding.Precision) is not { } count)
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

        // A positive count moves digits toward the most significant end. Digits that leave
        // the top of the 7-digit field come back in at the bottom for a rotate, and are
        // lost for a shift. A negative count moves digits the other way.
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

    /// <summary>The next representable value above or below.</summary>
    /// <param name="value">The encoded value to step from.</param>
    /// <param name="toward">True to step toward positive infinity, false to step toward negative infinity.</param>
    /// <param name="quiet">Whether to report only InvalidOperation, as next-plus and next-minus do. Otherwise overflow and a subnormal result are also reported, as next-toward does.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded adjacent value.</returns>
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
                // Stepping inward from an infinity gives the largest finite value.
                return Decimal32Encoding.Pack(Decimal32Encoding.IsNegative(value), Decimal32Encoding.MaxQuantumExponent,
                    Decimal32Encoding.MaxCoefficient);
            }

            // Stepping outward from an infinity returns the same infinity.
            return Decimal32Encoding.Infinity(Decimal32Encoding.IsNegative(value));
        }

        // Add a value smaller than the smallest subnormal. The rounding direction moves the
        // result to the next value, and the added amount itself never appears in the result.
        var coefficient = Decimal32Encoding.Unpack(value, out var exponent);
        var rounding = toward ? Decimal32Rounding.Ceiling : Decimal32Rounding.Floor;
        var raised = Decimal32Status.None;

        var result = Decimal32Arithmetic.AddFinite(Decimal32Encoding.IsNegative(value), coefficient, exponent,
            !toward, 1, Decimal32Encoding.MinQuantumExponent - 1, rounding, ref raised);

        // next-plus and next-minus report no conditions except invalid operation.
        // next-toward is an arithmetic operation and reports underflow like one.
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
            // A step into or within the subnormal range is reported. A step that lands on a
            // normal value reports nothing, even though it was rounded.
            status |= raised & (Decimal32Status.Underflow | Decimal32Status.Inexact | Decimal32Status.Subnormal
                | Decimal32Status.Rounded | Decimal32Status.Clamped);
        }

        return result;
    }

    /// <summary>
    /// The next value from the first operand toward the second. Unlike next-plus and
    /// next-minus, it reports a subnormal result.
    /// </summary>
    /// <param name="value">The encoded value to step from.</param>
    /// <param name="target">The encoded value to step toward.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded next value, or the first operand with the second operand's sign when they are equal.</returns>
    public static uint NextToward(uint value, uint target, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsNaN(value) || Decimal32Encoding.IsNaN(target))
        {
            return Decimal32Arithmetic.PropagateNaN(value, target, ref status);
        }

        var comparison = Decimal32Ordering.CompareValues(value, target);
        if (comparison == 0)
        {
            // The operands are equal. The result keeps the first operand's digits and takes
            // the second operand's sign.
            var magnitude = Decimal32Encoding.Canonical(value) & ~Decimal32Encoding.SignMask;
            return magnitude | (target & Decimal32Encoding.SignMask);
        }

        return Next(value, comparison < 0, false, ref status);
    }

    /// <summary>
    /// Rounds to the given number of fractional digits, for the .NET Round overloads. If
    /// the value already has that many fractional digits or fewer, it is returned
    /// unchanged. Adding zeros would change the quantum, which is what quantize is for.
    /// </summary>
    /// <param name="value">The encoded value to round.</param>
    /// <param name="digits">The number of fractional digits to keep.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <returns>The encoded rounded value, or <paramref name="value"/> unchanged if it needs no rounding or the rounded value does not fit.</returns>
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
