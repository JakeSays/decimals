// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// A decimal value as arithmetic works on it: the coefficient as one decimal digit per
/// byte, most significant first, beside a sign and a quantum exponent. This is decNumber's
/// <c>bcdnum</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Msd"/> addresses the leading digit and <see cref="Lsd"/> the last one, both
/// inclusive, so the digit count is their difference plus one. decNumber carries the same
/// pair, and every operation that shortens or extends a coefficient does it by moving one
/// of them rather than by moving digits.
/// </para>
/// <para>
/// The digits live in a buffer the caller stack-allocates and outlive nothing: this struct
/// borrows them for the length of an operation. Nothing here may be stored past the frame
/// that supplied the buffer.
/// </para>
/// <para>
/// A coefficient in this form is not an integer, and no operation on it forms one. That is
/// the whole point of the representation -- scaling by a power of ten moves a pointer where
/// a binary coefficient would divide.
/// </para>
/// </remarks>
internal unsafe struct BcdNumber
{
    /// <summary>The leading digit.</summary>
    public byte* Msd;

    /// <summary>The last digit, inclusive.</summary>
    public byte* Lsd;

    public DecimalKind Kind;

    public bool IsNegative;

    /// <summary>
    /// The unbiased quantum exponent. Meaningless unless <see cref="Kind"/> is
    /// <see cref="DecimalKind.Finite"/>.
    /// </summary>
    public int Exponent;

    public BcdNumber(DecimalKind kind, bool isNegative, int exponent, byte* msd, byte* lsd)
    {
        Kind = kind;
        IsNegative = isNegative;
        Exponent = exponent;
        Msd = msd;
        Lsd = lsd;
    }

    public readonly int DigitCount => (int)(Lsd - Msd) + 1;

    public readonly bool IsFinite => Kind == DecimalKind.Finite;

    public readonly bool IsNaN => Kind is DecimalKind.QuietNaN or DecimalKind.SignalingNaN;

    /// <summary>
    /// Where the value's leading digit sits, which is what the exponent range is checked
    /// against. Meaningless for a zero, which has no leading digit.
    /// </summary>
    public readonly int AdjustedExponent => Exponent + DigitCount - 1;

    /// <summary>
    /// Whether this is a finite zero. An infinity and a NaN carry a placeholder digit
    /// rather than a coefficient, so asking about their digits alone would call them zero
    /// and they are not: the kind has to be part of the question.
    /// </summary>
    public readonly bool IsZero
    {
        get
        {
            if (Kind != DecimalKind.Finite)
            {
                return false;
            }

            for (var digit = Msd; digit <= Lsd; digit++)
            {
                if (*digit != 0)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Drops leading zeros by moving <see cref="Msd"/> forward, which is what every
    /// operation hands back. A coefficient of nothing but zeros keeps one digit.
    /// </summary>
    public void Trim()
    {
        while (Msd < Lsd && *Msd == 0)
        {
            Msd++;
        }
    }

    /// <summary>
    /// Drops the last <paramref name="count"/> digits and raises the exponent to match,
    /// which is division by a power of ten. The digits are not read or moved: only
    /// <see cref="Lsd"/> is.
    /// </summary>
    public void DropDigits(int count)
    {
        Lsd -= count;
        Exponent += count;
    }

    /// <summary>
    /// Appends <paramref name="count"/> zeros and lowers the exponent to match, which is
    /// multiplication by a power of ten. The buffer has to have room past
    /// <see cref="Lsd"/> for them.
    /// </summary>
    public void AppendZeros(int count)
    {
        var last = Lsd + count;
        for (var digit = Lsd + 1; digit <= last; digit++)
        {
            *digit = 0;
        }

        Lsd = last;
        Exponent -= count;
    }

    /// <summary>
    /// Adds one to the last digit, carrying toward the leading one. A carry out of the
    /// leading digit takes the position before <see cref="Msd"/>, so the buffer has to have
    /// one digit of headroom there -- 999 becomes 1000, one digit longer.
    /// </summary>
    public void Increment()
    {
        for (var digit = Lsd; digit >= Msd; digit--)
        {
            if (*digit != 9)
            {
                (*digit)++;
                return;
            }

            *digit = 0;
        }

        Msd--;
        *Msd = 1;
    }

    /// <summary>
    /// Whether any of the last <paramref name="count"/> digits is non-zero, which is what
    /// makes a shortening inexact.
    /// </summary>
    public readonly bool AnyNonZeroInLast(int count)
    {
        for (var digit = Lsd - count + 1; digit <= Lsd; digit++)
        {
            if (*digit != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Makes this a zero of the given exponent, keeping the sign.</summary>
    public void SetZero(int exponent)
    {
        *Msd = 0;
        Lsd = Msd;
        Exponent = exponent;
    }

    /// <summary>Makes this a run of <paramref name="count"/> nines, the largest coefficient
    /// a format holds.</summary>
    public void SetNines(int count, int exponent)
    {
        var last = Msd + count - 1;
        for (var digit = Msd; digit <= last; digit++)
        {
            *digit = 9;
        }

        Lsd = last;
        Exponent = exponent;
    }

    /// <summary>
    /// Compares two coefficients as unsigned magnitudes, ignoring both exponents. Callers
    /// that need the values themselves compared have to align them first.
    /// </summary>
    public static int CompareDigits(byte* leftMsd, byte* leftLsd, byte* rightMsd, byte* rightLsd)
    {
        while (leftMsd < leftLsd && *leftMsd == 0)
        {
            leftMsd++;
        }

        while (rightMsd < rightLsd && *rightMsd == 0)
        {
            rightMsd++;
        }

        var leftLength = leftLsd - leftMsd;
        var rightLength = rightLsd - rightMsd;
        if (leftLength != rightLength)
        {
            return leftLength < rightLength ? -1 : 1;
        }

        for (; leftMsd <= leftLsd; leftMsd++, rightMsd++)
        {
            if (*leftMsd != *rightMsd)
            {
                return *leftMsd < *rightMsd ? -1 : 1;
            }
        }

        return 0;
    }
}
