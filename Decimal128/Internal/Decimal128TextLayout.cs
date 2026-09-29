// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Decimals.Internal;

/// <summary>
/// The shape of a finite value's text in the specification's scientific or engineering
/// notation: the number of digit characters, the position of the decimal point among them,
/// and the exponent that follows. Planning the shape first allows the text to be written
/// to its destination in one pass.
/// </summary>
/// <remarks>
/// <para>
/// Every form the two notations produce is one run of digit characters, with at most one
/// decimal point inside it, followed by an optional exponent. Leading zeros, as in
/// <c>0.000005</c>, come from writing the run wider than the coefficient needs, which the
/// digit writer does at no extra cost. The point is inserted by writing the run one place
/// to the right and then moving the digits before the point back one place. For the
/// exponential forms that is one character, and the coefficient is never split with a
/// division.
/// </para>
/// <para>
/// A run without a point is represented as one whose point falls after its last digit.
/// This is used instead of zero so that the writers handle every run the same way.
/// </para>
/// </remarks>
internal readonly struct Decimal128TextLayout
{
    /// <summary>
    /// The length that holds any finite value's text: a sign, a run of at most 40 digits,
    /// a point, and an exponent of at most 4 digits with its letter and sign.
    /// </summary>
    public const int MaxLength = 48;

    private const int PlainNotationFloor = -6;

    private Decimal128TextLayout(Decimal128Integer coefficient, int digits, int point, int exponent)
    {
        Coefficient = coefficient;
        Digits = digits;
        Point = point;
        Exponent = exponent;
    }

    /// <summary>The value the digit run is written from. The run's width supplies any leading zeros.</summary>
    public Decimal128Integer Coefficient { get; }

    /// <summary>The number of digit characters in the run.</summary>
    public int Digits { get; }

    /// <summary>The number of digits before the decimal point. It equals <see cref="Digits"/> when there is no point.</summary>
    public int Point { get; }

    /// <summary>The exponent written after the digits, or zero when there is no exponent.</summary>
    public int Exponent { get; }

    /// <summary>
    /// Plans the text. The value chooses the notation, not the caller: plain notation when
    /// the exponent is at most zero and the adjusted exponent is at least -6, exponential
    /// notation otherwise.
    /// </summary>
    /// <param name="coefficient">The value's coefficient.</param>
    /// <param name="exponent">The value's quantum exponent.</param>
    /// <param name="engineering">True for engineering notation, false for scientific notation.</param>
    /// <returns>The layout of the value's text.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128TextLayout Plan(Decimal128Integer coefficient, int exponent, bool engineering)
    {
        if (engineering)
        {
            return PlanEngineering(coefficient, exponent);
        }

        var digits = Decimal128Tables.CountDigits(coefficient);
        var adjusted = exponent + digits - 1;

        if (exponent <= 0 && adjusted >= PlainNotationFloor)
        {
            // Plain notation puts the point after the digits that the exponent leaves before
            // it. For an exponent of zero, that is after all the digits.
            if (adjusted >= 0)
            {
                return new Decimal128TextLayout(coefficient, digits, adjusted + 1, 0);
            }

            // A zero, the point, and the coefficient after as many zeros as the adjusted
            // exponent requires. This is one run, written wide enough to start with the zero.
            return new Decimal128TextLayout(coefficient, 1 - exponent, 1, 0);
        }

        // Scientific notation puts the point after the first digit. For a single digit,
        // that is after the last digit, so there is no point.
        return new Decimal128TextLayout(coefficient, digits, 1, adjusted);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Decimal128TextLayout PlanEngineering(Decimal128Integer coefficient, int exponent)
    {
        var digits = Decimal128Tables.CountDigits(coefficient);
        var adjusted = exponent + digits - 1;

        if (exponent <= 0 && adjusted >= PlainNotationFloor)
        {
            return adjusted < 0
                ? new Decimal128TextLayout(coefficient, 1 - exponent, 1, 0)
                : new Decimal128TextLayout(coefficient, digits, adjusted + 1, 0);
        }

        var offset = adjusted % 3;
        if (offset < 0)
        {
            offset += 3;
        }

        if (coefficient.IsZero)
        {
            // A zero has no digits to move left, so the exponent is raised to the next
            // multiple of three instead, and the gap is filled with zeros after the point:
            // 0E+1 is written 0.00E+3.
            if (offset == 0)
            {
                return new Decimal128TextLayout(Decimal128Integer.Zero, 1, 1, adjusted);
            }

            return new Decimal128TextLayout(Decimal128Integer.Zero, 4 - offset, 1, adjusted + (3 - offset));
        }

        // Otherwise, digits move left across the point until the exponent is a multiple of
        // three, which leaves one, two, or three digits before the point.
        var integerLength = offset + 1;
        adjusted -= offset;

        if (digits <= integerLength)
        {
            var padded = Decimal128Tables.Scale(coefficient, integerLength - digits);
            return new Decimal128TextLayout(padded, integerLength, integerLength, adjusted);
        }

        return new Decimal128TextLayout(coefficient, digits, integerLength, adjusted);
    }

    /// <summary>
    /// The number of exponent digits, or zero when engineering notation has reduced the
    /// exponent to zero: 10E+1 is written 100, with no exponent.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ExponentWidth(int exponent)
    {
        if (exponent == 0)
        {
            return 0;
        }

        return Decimal128Digits.ExponentWidth((uint)Math.Abs(exponent));
    }

    /// <summary>1 for a positive value and 0 for zero. The value must not be negative.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int IsPositive(int value)
    {
        return (int)((uint)-value >> 31);
    }

    /// <summary>
    /// The length of the text for the given sign and separator lengths.
    /// </summary>
    /// <param name="negative">Whether the value is negative.</param>
    /// <param name="negativeSignLength">The length of the negative sign, used for the value and the exponent.</param>
    /// <param name="positiveSignLength">The length of the positive sign, used for the exponent.</param>
    /// <param name="separatorLength">The length of the decimal separator.</param>
    /// <returns>The number of characters in the text.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Length(bool negative, int negativeSignLength, int positiveSignLength, int separatorLength)
    {
        var signLength = Unsafe.BitCast<bool, byte>(negative) * negativeSignLength;
        var pointLength = IsPositive(Digits - Point) * separatorLength;

        var exponentWidth = ExponentWidth(Exponent);
        var exponentSign = positiveSignLength + ((negativeSignLength - positiveSignLength) & (Exponent >> 31));
        var exponentLength = IsPositive(exponentWidth) * (1 + exponentSign + exponentWidth);

        return Digits + signLength + pointLength + exponentLength;
    }

    /// <summary>
    /// Writes the text with the specification's symbols, which are also the invariant
    /// culture's symbols. Nothing past the text is written. The destination must hold
    /// <see cref="Length"/> characters for symbol lengths of one.
    /// </summary>
    /// <param name="destination">Receives the text.</param>
    /// <param name="negative">Whether the value is negative.</param>
    /// <returns>The number of characters written.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Write(Span<char> destination, bool negative)
    {
        ref var start = ref MemoryMarshal.GetReference(destination);

        // The minus sign is always written. For a positive value, the digits then
        // overwrite it, which is cheaper than a branch.
        start = '-';
        var index = (int)Unsafe.BitCast<bool, byte>(negative);

        // The exponent is written before the run, because the exponent writer can store a
        // zero on the run's last character. Writing the run afterward overwrites it.
        var runEnd = index + Digits + IsPositive(Digits - Point);
        var length = runEnd;
        if (Exponent != 0)
        {
            length += Decimal128Digits.WriteExponent(Exponent, ref Unsafe.Add(ref start, runEnd));
        }

        WriteRun(ref start, index);
        return length;
    }

    /// <summary>
    /// Writes the text with a culture's signs and decimal separator. The destination must
    /// hold <see cref="Length"/> characters for those symbol lengths.
    /// </summary>
    /// <param name="destination">Receives the text.</param>
    /// <param name="negative">Whether the value is negative.</param>
    /// <param name="numberFormat">The culture's signs and decimal separator.</param>
    /// <returns>The number of characters written.</returns>
    public int Write(Span<char> destination, bool negative, NumberFormatInfo numberFormat)
    {
        var index = 0;
        if (negative)
        {
            Put(destination, ref index, numberFormat.NegativeSign);
        }

        var separator = numberFormat.NumberDecimalSeparator;
        if (Point == Digits || (separator.Length == 1 && separator[0] == '.'))
        {
            index = WriteRun(ref MemoryMarshal.GetReference(destination), index);
        }
        else
        {
            // The separator is not a single period, so the digits before the point are
            // written separately. This is the only place the coefficient is split.
            var head = Decimal128Tables.DivRemWidePowerOfTen(Coefficient, Digits - Point, out var tail);
            Decimal128Digits.Write(head, Point, ref destination[index]);
            index += Point;
            Put(destination, ref index, separator);
            Decimal128Digits.Write(tail, Digits - Point, ref destination[index]);
            index += Digits - Point;
        }

        var exponentWidth = ExponentWidth(Exponent);
        if (exponentWidth > 0)
        {
            destination[index++] = 'E';
            Put(destination, ref index, Exponent < 0 ? numberFormat.NegativeSign : numberFormat.PositiveSign);
            Decimal128Digits.Write((ulong)Math.Abs(Exponent), exponentWidth, ref destination[index]);
            index += exponentWidth;
        }

        return index;
    }

    /// <summary>
    /// Writes the digit run with its point. The run is written one place to the right of
    /// its start, and the digits before the point move back one place, which frees the
    /// character for the point.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int WriteRun(ref char start, int index)
    {
        var hasPoint = IsPositive(Digits - Point);
        Decimal128Digits.Write(Coefficient, Digits, ref Unsafe.Add(ref start, index + hasPoint));

        if (hasPoint != 0)
        {
            for (var slide = 0; slide < Point; slide++)
            {
                Unsafe.Add(ref start, index + slide) = Unsafe.Add(ref start, index + slide + 1);
            }

            Unsafe.Add(ref start, index + Point) = '.';
        }

        return index + Digits + hasPoint;
    }

    private static void Put(Span<char> destination, ref int index, ReadOnlySpan<char> text)
    {
        text.CopyTo(destination[index..]);
        index += text.Length;
    }
}
