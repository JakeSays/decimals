// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// Operations on a value's digits and exponent: rescaling to an exponent, rounding to an
/// integer, removing trailing zeros, stepping to the next value, and reading or shifting
/// the exponent.
/// </summary>
internal static class Decimal64Shaping
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
    public static ulong Quantize(ulong value, ulong pattern, Decimal64Rounding rounding, ref Decimal64Status status)
    {
        if (Decimal64Encoding.IsSpecial(value) || Decimal64Encoding.IsSpecial(pattern))
        {
            if (Decimal64Encoding.IsNaN(value) || Decimal64Encoding.IsNaN(pattern))
            {
                return Decimal64Arithmetic.PropagateNaN(value, pattern, ref status);
            }

            // Two infinities give an infinity. An infinity with a finite value is invalid.
            if (Decimal64Encoding.IsInfinity(value) && Decimal64Encoding.IsInfinity(pattern))
            {
                return Decimal64Encoding.Infinity(Decimal64Encoding.IsNegative(value));
            }

            return Decimal64Arithmetic.Invalid(ref status);
        }

        Decimal64Encoding.Unpack(pattern, out var exponent);
        return Rescale(value, exponent, rounding, ref status);
    }

    /// <summary>Rescales a finite value to the given exponent.</summary>
    /// <param name="value">The encoded value to rescale.</param>
    /// <param name="exponent">The exponent the result takes.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded value at <paramref name="exponent"/>, or a quiet NaN if the exponent is out of range or the value does not fit.</returns>
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

        // Quantize never signals underflow. Losing digits to reach the requested exponent
        // is the intended result, not a result that became too small.
        status &= ~Decimal64Status.Underflow;
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
            // The value is already an integer, with a non-negative exponent.
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

        // Remove zeros eight at a time, then four, two, and one. A run of zeros takes a few
        // divisions by constants instead of one division per zero.
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
    /// Removes trailing zeros, which gives the shortest coefficient with the same value.
    /// </summary>
    /// <param name="value">The encoded value to reduce.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded value with no trailing zeros in its coefficient.</returns>
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
            // Every zero reduces to the same zero, whatever its exponent, and keeps its sign.
            // Applying the context first would make a negative zero positive, as plus does.
            return Decimal64Encoding.Zero(Decimal64Encoding.IsNegative(value), 0);
        }

        // Apply the context first, so a subnormal operand raises its conditions before its
        // zeros are removed.
        var applied = Decimal64Arithmetic.AddToZero(value, false, rounding, ref status);
        var coefficient = Decimal64Encoding.Unpack(applied, out var exponent);

        StripTrailingZeros(ref coefficient, ref exponent, Decimal64Encoding.MaxQuantumExponent);
        return Decimal64Encoding.Pack(Decimal64Encoding.IsNegative(applied), exponent, coefficient);
    }

    /// <summary>
    /// Like <see cref="Reduce"/>, but stops at exponent zero, so an integer keeps the zeros
    /// that are part of its magnitude.
    /// </summary>
    /// <param name="value">The encoded value to trim.</param>
    /// <returns>The encoded value with no trailing zeros after the decimal point.</returns>
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
    /// The adjusted exponent as a decimal value. Zero has no leading digit, so it returns
    /// negative infinity and signals division by zero, as a logarithm of zero would.
    /// </summary>
    /// <param name="value">The encoded operand.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded adjusted exponent.</returns>
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
    /// The adjusted exponent as an <see cref="int"/>, as .NET's ILogB returns it. The
    /// results for special inputs follow .NET, not the specification: zero gives
    /// <see cref="int.MinValue"/>, and a NaN or an infinity gives <see cref="int.MaxValue"/>.
    /// The specification's logb gives -Infinity, a NaN, and +Infinity for these.
    /// </summary>
    /// <param name="value">The encoded operand.</param>
    /// <returns>The adjusted exponent, or one of the special results above.</returns>
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
    /// The largest shift scaleb accepts. It is enough to move any value from one end of the
    /// format's range to the other.
    /// </summary>
    private const int ScaleLimit = 2 * (Decimal64Encoding.MaxExponent + Decimal64Encoding.Precision);

    /// <summary>Multiplies by a power of ten given as a second operand.</summary>
    /// <param name="value">The encoded value to scale.</param>
    /// <param name="scale">The encoded power of ten. It must be an integer with exponent zero.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded result, rounded to the format, or a quiet NaN if the scale is invalid.</returns>
    public static ulong ScaleB(ulong value, ulong scale, Decimal64Rounding rounding, ref Decimal64Status status)
    {
        if (Decimal64Encoding.IsNaN(value) || Decimal64Encoding.IsNaN(scale))
        {
            return Decimal64Arithmetic.PropagateNaN(value, scale, ref status);
        }

        // The shift must be an integer within the limit. Anything else is invalid, not
        // clamped.
        if (ReadInteger(scale, ScaleLimit) is not { } shift)
        {
            return Decimal64Arithmetic.Invalid(ref status);
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
    /// Reads an operand as an integer, for the operations that take a shift count instead
    /// of a general value. The operand must be finite, have exponent zero, and be within the
    /// limit.
    /// </summary>
    /// <param name="value">The encoded operand.</param>
    /// <param name="limit">The largest magnitude accepted.</param>
    /// <returns>The integer, or null if the operand is not an integer within the limit.</returns>
    public static int? ReadInteger(ulong value, int limit)
    {
        if (Decimal64Encoding.IsSpecial(value))
        {
            return null;
        }

        var coefficient = Decimal64Encoding.Unpack(value, out var exponent);
        if (exponent != 0 || coefficient > (ulong)limit)
        {
            return null;
        }

        var magnitude = (int)coefficient;
        return Decimal64Encoding.IsNegative(value)
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
    public static ulong RotateOrShift(ulong value, ulong places, bool rotate, ref Decimal64Status status)
    {
        if (Decimal64Encoding.IsNaN(value) || Decimal64Encoding.IsNaN(places))
        {
            return Decimal64Arithmetic.PropagateNaN(value, places, ref status);
        }

        // The count must be an integer no larger than the precision in either direction.
        // Anything else is invalid, not clamped.
        if (ReadInteger(places, Decimal64Encoding.Precision) is not { } count)
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

        // A positive count moves digits toward the most significant end. Digits that leave
        // the top of the 16-digit field come back in at the bottom for a rotate, and are
        // lost for a shift. A negative count moves digits the other way.
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

    /// <summary>The next representable value above or below.</summary>
    /// <param name="value">The encoded value to step from.</param>
    /// <param name="toward">True to step toward positive infinity, false to step toward negative infinity.</param>
    /// <param name="quiet">Whether to report only InvalidOperation, as next-plus and next-minus do. Otherwise overflow and a subnormal result are also reported, as next-toward does.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded adjacent value.</returns>
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
                // Stepping inward from an infinity gives the largest finite value.
                return Decimal64Encoding.Pack(Decimal64Encoding.IsNegative(value), Decimal64Encoding.MaxQuantumExponent,
                    Decimal64Encoding.MaxCoefficient);
            }

            // Stepping outward from an infinity returns the same infinity.
            return Decimal64Encoding.Infinity(Decimal64Encoding.IsNegative(value));
        }

        // Add a value smaller than the smallest subnormal. The rounding direction moves the
        // result to the next value, and the added amount itself never appears in the result.
        var coefficient = Decimal64Encoding.Unpack(value, out var exponent);
        var rounding = toward ? Decimal64Rounding.Ceiling : Decimal64Rounding.Floor;
        var raised = Decimal64Status.None;

        var result = Decimal64Arithmetic.AddFinite(Decimal64Encoding.IsNegative(value), coefficient, exponent,
            !toward, 1, Decimal64Encoding.MinQuantumExponent - 1, rounding, ref raised);

        // next-plus and next-minus report no conditions except invalid operation.
        // next-toward is an arithmetic operation and reports underflow like one.
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
            // A step into or within the subnormal range is reported. A step that lands on a
            // normal value reports nothing, even though it was rounded.
            status |= raised & (Decimal64Status.Underflow | Decimal64Status.Inexact | Decimal64Status.Subnormal
                | Decimal64Status.Rounded | Decimal64Status.Clamped);
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
    public static ulong NextToward(ulong value, ulong target, ref Decimal64Status status)
    {
        if (Decimal64Encoding.IsNaN(value) || Decimal64Encoding.IsNaN(target))
        {
            return Decimal64Arithmetic.PropagateNaN(value, target, ref status);
        }

        var comparison = Decimal64Ordering.CompareValues(value, target);
        if (comparison == 0)
        {
            // The operands are equal. The result keeps the first operand's digits and takes
            // the second operand's sign.
            var magnitude = Decimal64Encoding.Canonical(value) & ~Decimal64Encoding.SignMask;
            return magnitude | (target & Decimal64Encoding.SignMask);
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
