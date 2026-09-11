// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// The operations that move a value's digits without changing what it is worth, on the
/// digit form. This is <see cref="DecimalShaping"/>'s work.
/// </summary>
/// <remarks>
/// These are what the representation is for. Rescaling to a given exponent is appending
/// zeros or dropping digits; stripping trailing zeros is walking the last digit back;
/// reading the adjusted exponent is arithmetic on the digit count. A binary coefficient
/// pays a multiply or a divide for every one of them.
/// </remarks>
internal static unsafe class BcdShaping
{
    /// <summary>
    /// Rescales to a given exponent, which is what quantize is underneath. Growing the
    /// coefficient past the format's width makes the operation invalid rather than
    /// rounded: the value cannot be said at that exponent.
    /// </summary>
    public static bool TryRescale<TFormat>(ref BcdNumber value, int exponent,
        DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (exponent > TFormat.MaxQuantumExponent || exponent < TFormat.MinQuantumExponent)
        {
            return false;
        }

        var shift = value.Exponent - exponent;

        if (shift > 0 && !value.IsZero)
        {
            if (value.DigitCount + shift > TFormat.Precision)
            {
                return false;
            }

            value.AppendZeros(shift);
        }
        else if (shift < 0)
        {
            var wasZero = value.IsZero;
            BcdRounder.Round(ref value, -shift, rounding, out var inexact);

            if (value.DigitCount > TFormat.Precision)
            {
                return false;
            }

            if (!wasZero)
            {
                status |= DecimalStatus.Rounded;
            }

            if (inexact)
            {
                status |= DecimalStatus.Inexact;
            }
        }

        value.Exponent = exponent;
        BcdFinalizer.Finalize<TFormat>(ref value, rounding, ref status);

        // Quantize is defined never to signal underflow: losing precision to reach the
        // requested exponent is the operation working, not a result vanishing.
        status &= ~DecimalStatus.Underflow;
        return true;
    }

    /// <summary>
    /// Walks the last digit back over trailing zeros, raising the exponent to match, which
    /// leaves the shortest coefficient of the same value. Stops at
    /// <paramref name="exponentLimit"/> so that an integer keeps the zeros that are part of
    /// its magnitude.
    /// </summary>
    public static void StripZeros(ref BcdNumber value, int exponentLimit)
    {
        while (value.Exponent < exponentLimit && value.Lsd > value.Msd && *value.Lsd == 0)
        {
            value.Lsd--;
            value.Exponent++;
        }
    }

    /// <summary>
    /// Rounds to an integer. <paramref name="exact"/> chooses between the two operations
    /// the specification offers: the exact one reports that digits were lost, the other is
    /// defined never to say it rounded.
    /// </summary>
    public static void ToIntegral<TFormat>(ref BcdNumber value, bool exact,
        DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (value.Exponent >= 0)
        {
            // Already an integer, and one whose exponent says so.
            return;
        }

        var before = status;
        var wasZero = value.IsZero;

        BcdRounder.Round(ref value, -value.Exponent, rounding, out var inexact);
        value.Exponent = 0;
        BcdFinalizer.Finalize<TFormat>(ref value, rounding, ref status);

        if (exact && !wasZero)
        {
            status |= DecimalStatus.Rounded;
            if (inexact)
            {
                status |= DecimalStatus.Inexact;
            }
        }
        else
        {
            status = before;
        }
    }

    /// <summary>
    /// The adjusted exponent as an integer value. A zero has no leading digit to point at,
    /// so it reports negative infinity and signals division by zero, the way the logarithm
    /// it stands in for would.
    /// </summary>
    public static UInt128 LogB<TFormat>(BcdNumber value, byte* buffer, DecimalRounding rounding,
        ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (value.Kind == DecimalKind.Infinity)
        {
            *buffer = 0;
            return BcdCodec.Encode<TFormat>(new BcdNumber(DecimalKind.Infinity, false, 0,
                buffer, buffer));
        }

        if (value.IsZero)
        {
            status |= DecimalStatus.DivisionByZero;
            *buffer = 0;
            return BcdCodec.Encode<TFormat>(new BcdNumber(DecimalKind.Infinity, true, 0,
                buffer, buffer));
        }

        var adjusted = value.AdjustedExponent;
        var magnitude = Math.Abs(adjusted);

        // The adjusted exponent of any format fits four digits, so laying it out is a short
        // walk rather than anything needing the general path.
        var digits = buffer + 1;
        var count = 0;
        do
        {
            digits[count] = (byte)(magnitude % 10);
            magnitude /= 10;
            count++;
        }
        while (magnitude != 0);

        for (var index = 0; index < count / 2; index++)
        {
            (digits[index], digits[count - index - 1]) = (digits[count - index - 1], digits[index]);
        }

        var result = new BcdNumber(DecimalKind.Finite, adjusted < 0, 0, digits, digits + count - 1);
        BcdFinalizer.Finalize<TFormat>(ref result, rounding, ref status);
        return BcdCodec.Encode<TFormat>(result);
    }

    /// <summary>
    /// Moves the coefficient's digits within the format's full width. Rotating carries
    /// digits round the ends; shifting drops them and brings zeros in.
    /// </summary>
    public static void RotateOrShift<TFormat>(ref BcdNumber value, int places, bool rotate,
        byte* scratch)
        where TFormat : IDecimalFormat
    {
        // The digits are laid out right-aligned across the format's full width, because
        // that is the field these operations are defined on.
        var width = TFormat.Precision;
        for (var index = 0; index < width; index++)
        {
            scratch[index] = 0;
        }

        var offset = width - value.DigitCount;
        var source = value.Msd;
        for (var index = offset; index < width; index++, source++)
        {
            scratch[index] = *source;
        }

        var destination = value.Msd;
        for (var index = 0; index < width; index++)
        {
            // A positive count moves digits toward the leading end, so the digit landing at
            // a position comes from further down the field.
            var from = index + places;
            if (rotate)
            {
                // Carrying round the ends, so a digit leaving one side arrives at the other.
                from = ((from % width) + width) % width;
                destination[index] = scratch[from];
            }
            else
            {
                destination[index] = from >= 0 && from < width ? scratch[from] : (byte)0;
            }
        }

        value.Lsd = value.Msd + width - 1;
        value.Trim();
    }
}
