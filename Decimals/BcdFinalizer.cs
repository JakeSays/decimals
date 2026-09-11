// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// Fits an exact result into a format: rounds the coefficient to precision, then brings the
/// exponent into range, raising the conditions each step calls for. This is
/// <see cref="DecimalFinalizer"/>'s work on the digit form.
/// </summary>
/// <remarks>
/// <para>
/// Like the finalizer it replaces, this follows decNumber's <c>decFinish</c> and
/// <c>decSetSubnormal</c> rather than decFloat's <c>decFinalize</c>. The two agree on the
/// arithmetic, but the decFloat sources never raise <see cref="DecimalStatus.Rounded"/>,
/// <see cref="DecimalStatus.Clamped"/>, or <see cref="DecimalStatus.Subnormal"/> at all,
/// and the testcases expect all three.
/// </para>
/// <para>
/// Order matters. Precision rounding happens first, then the exponent range, because
/// <see cref="DecimalStatus.Subnormal"/> is decided on the value as it stands after
/// rounding to precision but before it is rescaled up to the smallest exponent.
/// </para>
/// <para>
/// The buffer behind the value has to have one digit of headroom before the leading digit,
/// for a carry out of a rounding, and room for <c>TFormat.Precision</c> digits from the
/// leading one, for a fold-down's trailing zeros.
/// </para>
/// </remarks>
internal static unsafe class BcdFinalizer
{
    public static void Finalize<TFormat>(ref BcdNumber value, DecimalRounding rounding,
        ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        // The digit count decides whether the value needs rounding, so leading zeros have
        // to be gone before it is asked for.
        value.Trim();

        if (value.DigitCount > TFormat.Precision)
        {
            var drop = value.DigitCount - TFormat.Precision;
            BcdRounder.Round(ref value, drop, rounding, out var inexact);
            status |= DecimalStatus.Rounded;
            if (inexact)
            {
                status |= DecimalStatus.Inexact;
            }

            // Rounding up can carry into a new digit -- 999 becomes 1000 -- which has to
            // come back out.
            if (value.DigitCount > TFormat.Precision)
            {
                value.DropDigits(1);
            }
        }

        if (value.IsZero)
        {
            Zero<TFormat>(ref value, ref status);
            return;
        }

        value.Trim();

        var adjusted = value.AdjustedExponent;

        if (adjusted > TFormat.MaxExponent)
        {
            Overflowed<TFormat>(ref value, rounding, ref status);
            return;
        }

        if (adjusted < TFormat.MinExponent)
        {
            Subnormal<TFormat>(ref value, rounding, ref status);
            return;
        }

        if (value.Exponent > TFormat.MaxQuantumExponent)
        {
            // Folded down: the value fits, but only if the coefficient carries the extra
            // magnitude as trailing zeros rather than the exponent.
            value.AppendZeros(value.Exponent - TFormat.MaxQuantumExponent);
            status |= DecimalStatus.Clamped;
        }
    }

    /// <summary>
    /// A zero has no digits to place, so only its exponent has to be brought into range.
    /// It is never subnormal, whatever its exponent.
    /// </summary>
    private static void Zero<TFormat>(ref BcdNumber value, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        var exponent = value.Exponent;
        if (exponent < TFormat.MinQuantumExponent)
        {
            exponent = TFormat.MinQuantumExponent;
            status |= DecimalStatus.Clamped;
        }
        else if (exponent > TFormat.MaxQuantumExponent)
        {
            exponent = TFormat.MaxQuantumExponent;
            status |= DecimalStatus.Clamped;
        }

        value.SetZero(exponent);
    }

    private static void Subnormal<TFormat>(ref BcdNumber value, DecimalRounding rounding,
        ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        status |= DecimalStatus.Subnormal;

        if (value.Exponent < TFormat.MinQuantumExponent)
        {
            var drop = TFormat.MinQuantumExponent - value.Exponent;
            BcdRounder.Round(ref value, drop, rounding, out var inexact);
            value.Exponent = TFormat.MinQuantumExponent;
            status |= DecimalStatus.Rounded;
            if (inexact)
            {
                status |= DecimalStatus.Inexact;
            }
        }

        // IEEE 754's default rule: a subnormal result underflows exactly when it is inexact.
        if ((status & DecimalStatus.Inexact) != 0)
        {
            status |= DecimalStatus.Underflow;
        }

        if (value.IsZero)
        {
            // Everything was rounded away. The exponent the value wanted was below the
            // smallest the format holds, so it has been clamped up to reach here.
            status |= DecimalStatus.Clamped;
            value.SetZero(TFormat.MinQuantumExponent);
            return;
        }

        value.Trim();
    }

    /// <summary>
    /// What overflow produces depends on the rounding mode: the modes that round away from
    /// the value give an infinity, the ones that round toward it give the largest finite.
    /// </summary>
    private static void Overflowed<TFormat>(ref BcdNumber value, DecimalRounding rounding,
        ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        status |= DecimalStatus.Overflow | DecimalStatus.Inexact | DecimalStatus.Rounded;

        var givesLargestFinite = rounding switch
        {
            DecimalRounding.Down => true,
            DecimalRounding.ZeroFiveUp => true,
            DecimalRounding.Ceiling => value.IsNegative,
            DecimalRounding.Floor => !value.IsNegative,
            _ => false
        };

        if (!givesLargestFinite)
        {
            value.Kind = DecimalKind.Infinity;
            value.SetZero(0);
            return;
        }

        value.SetNines(TFormat.Precision, TFormat.MaxQuantumExponent);
    }
}
