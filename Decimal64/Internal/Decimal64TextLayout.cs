// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

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
/// <para>
/// The struct has four fields, so the JIT keeps the whole layout in registers instead of
/// memory.
/// </para>
/// </remarks>
internal readonly struct Decimal64TextLayout
{
    /// <summary>
    /// The number of characters a destination needs for <see cref="WriteWide"/>. The
    /// longest text is 24 characters, and the writer writes a few characters past the end
    /// of the text.
    /// </summary>
    public const int WideLength = 32;

    private const int PlainNotationFloor = -6;

    private Decimal64TextLayout(ulong coefficient, int digits, int point, int exponent)
    {
        Coefficient = coefficient;
        Digits = digits;
        Point = point;
        Exponent = exponent;
    }

    /// <summary>The value the digit run is written from. The run's width supplies any leading zeros.</summary>
    public ulong Coefficient { get; }

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
    /// <remarks>
    /// This uses two branches on the value's shape. Planning both notations and choosing
    /// one with masks was tried. It took more instructions than the branches cost, because
    /// the branch predictor handles most mixes of values well.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal64TextLayout Plan(ulong coefficient, int exponent, bool engineering)
    {
        if (engineering)
        {
            return PlanEngineering(coefficient, exponent);
        }

        var digits = Decimal64Tables.CountDigits(coefficient);
        var adjusted = exponent + digits - 1;

        if (exponent <= 0 && adjusted >= PlainNotationFloor)
        {
            // Plain notation puts the point after the digits that the exponent leaves before
            // it. For an exponent of zero, that is after all the digits.
            if (adjusted >= 0)
            {
                return new Decimal64TextLayout(coefficient, digits, adjusted + 1, 0);
            }

            // A zero, the point, and the coefficient after as many zeros as the adjusted
            // exponent requires. This is one run, written wide enough to start with the zero.
            return new Decimal64TextLayout(coefficient, 1 - exponent, 1, 0);
        }

        // Scientific notation puts the point after the first digit. For a single digit,
        // that is after the last digit, so there is no point.
        return new Decimal64TextLayout(coefficient, digits, 1, adjusted);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Decimal64TextLayout PlanEngineering(ulong coefficient, int exponent)
    {
        var digits = Decimal64Tables.CountDigits(coefficient);
        var adjusted = exponent + digits - 1;

        if (exponent <= 0 && adjusted >= PlainNotationFloor)
        {
            return adjusted < 0
                ? new Decimal64TextLayout(coefficient, 1 - exponent, 1, 0)
                : new Decimal64TextLayout(coefficient, digits, adjusted + 1, 0);
        }

        var offset = adjusted % 3;
        if (offset < 0)
        {
            offset += 3;
        }

        if (coefficient == 0)
        {
            // A zero has no digits to move left, so the exponent is raised to the next
            // multiple of three instead, and the gap is filled with zeros after the point:
            // 0E+1 is written 0.00E+3.
            if (offset == 0)
            {
                return new Decimal64TextLayout(0, 1, 1, adjusted);
            }

            return new Decimal64TextLayout(0, 4 - offset, 1, adjusted + (3 - offset));
        }

        // Otherwise, digits move left across the point until the exponent is a multiple of
        // three, which leaves one, two, or three digits before the point.
        var integerLength = offset + 1;
        adjusted -= offset;

        if (digits <= integerLength)
        {
            var padded = coefficient * Decimal64Tables.PowerOfTen(integerLength - digits);
            return new Decimal64TextLayout(padded, integerLength, integerLength, adjusted);
        }

        return new Decimal64TextLayout(coefficient, digits, integerLength, adjusted);
    }

    /// <summary>
    /// The number of exponent digits, or zero when engineering notation has reduced the
    /// exponent to zero: 10E+1 is written 100, with no exponent. This uses arithmetic
    /// instead of branches, because the width varies from value to value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ExponentWidth(int exponent)
    {
        var sign = exponent >> 31;
        var magnitude = (exponent ^ sign) - sign;
        var width = 1 + (magnitude >= 10 ? 1 : 0) + (magnitude >= 100 ? 1 : 0);
        return width & -IsPositive(magnitude);
    }

    /// <summary>1 for a positive value and 0 for zero. The value must not be negative.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int IsPositive(int value)
    {
        return (int)((uint)-value >> 31);
    }

    /// <summary>
    /// The length of the text for the given sign and separator lengths. It uses only
    /// arithmetic: the sign, the point, and the exponent vary from value to value, and a
    /// branch on any of them would mispredict.
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

        index = WriteRun(ref start, index);

        if (Exponent != 0)
        {
            index += Decimal64Digits.WriteExponent(Exponent, ref Unsafe.Add(ref start, index));
        }

        return index;
    }

    /// <summary>
    /// Writes the same text as <see cref="Write(Span{char}, bool)"/> to a destination of at
    /// least <see cref="WideLength"/> characters, without branching on the value's shape.
    /// Returns the length of the text. Characters after the text are left with whatever the
    /// writing put there.
    /// </summary>
    /// <remarks>
    /// The run is written one place to the right of its start, whether or not it has a
    /// point. Then 16 characters move back one place, in two halves of eight. The first
    /// half starts at the run's start. The second half ends at the point when more than
    /// eight digits are before it, and otherwise repeats the first half. This moves the
    /// digits before the point, or the whole run when it has no point, because such a run
    /// has at most 16 digits. Eight characters starting at the point are read before the
    /// move and stored again after it, which undoes the move's effect past the point. The
    /// point and the exponent are always written. For a run without a point, the point
    /// lands on the character after the run. The exponent overwrites it, or it falls past
    /// the end of the text.
    /// </remarks>
    /// <param name="start">The first character of a destination of at least <see cref="WideLength"/> characters.</param>
    /// <param name="negative">Whether the value is negative.</param>
    /// <returns>The length of the text.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int WriteWide(ref char start, bool negative)
    {
        start = '-';
        var index = (int)Unsafe.BitCast<bool, byte>(negative);
        ref var run = ref Unsafe.Add(ref start, index);

        Decimal64Digits.Write(Coefficient, Digits, ref Unsafe.Add(ref run, 1));

        ref var lanes = ref Unsafe.As<char, ushort>(ref run);
        var past = Point - 8;
        var second = past & ~(past >> 31);
        var keep = Vector128.LoadUnsafe(ref lanes, (nuint)Point);
        var first = Vector128.LoadUnsafe(ref lanes, 1);
        var rest = Vector128.LoadUnsafe(ref lanes, (nuint)(second + 1));
        first.StoreUnsafe(ref lanes, 0);
        rest.StoreUnsafe(ref lanes, (nuint)second);
        keep.StoreUnsafe(ref lanes, (nuint)Point);
        Unsafe.Add(ref run, Point) = '.';

        var end = index + Digits + IsPositive(Digits - Point);
        return end + Decimal64Digits.WriteExponent(Exponent, ref Unsafe.Add(ref start, end));
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
            var head = Decimal64Tables.DivRemPowerOfTen(Coefficient, Digits - Point, out var tail);
            Decimal64Digits.Write(head, Point, ref destination[index]);
            index += Point;
            Put(destination, ref index, separator);
            Decimal64Digits.Write(tail, Digits - Point, ref destination[index]);
            index += Digits - Point;
        }

        var exponentWidth = ExponentWidth(Exponent);
        if (exponentWidth > 0)
        {
            destination[index++] = 'E';
            Put(destination, ref index, Exponent < 0 ? numberFormat.NegativeSign : numberFormat.PositiveSign);
            Decimal64Digits.Write((ulong)Math.Abs(Exponent), exponentWidth, ref destination[index]);
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
        Decimal64Digits.Write(Coefficient, Digits, ref Unsafe.Add(ref start, index + hasPoint));

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
