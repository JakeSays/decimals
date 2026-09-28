// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Decimals.Internal;

/// <summary>
/// The shape of a finite value's text in the specification's scientific or engineering
/// notation: how many digit characters the coefficient takes, where the point falls among
/// them, and what exponent follows. Planning the shape first is what lets the text be
/// written into its destination in one pass.
/// </summary>
/// <remarks>
/// <para>
/// Every form the two notations produce is one run of digit characters with at most one
/// point somewhere inside it, then an optional exponent. Leading zeros -- the <c>0.000005</c>
/// case -- are the run written wider than the coefficient needs, which the digit writer
/// does for free, and the point is put in by writing the run one place to the right and
/// sliding the digits ahead of the point back over it: one character for the exponential
/// forms, and never a division to split the coefficient.
/// </para>
/// <para>
/// A run with no point is one whose point falls after its last digit. Spelling it that way
/// rather than as a zero lets the writers treat every run alike.
/// </para>
/// </remarks>
internal readonly struct Decimal128TextLayout
{
    /// <summary>
    /// Characters that hold any finite value's text: a sign, a run of at most forty
    /// digits, a point, and an exponent of at most four digits with its sign and letter.
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

    /// <summary>The value the digit run is written from, leading zeros supplied by the width.</summary>
    public Decimal128Integer Coefficient { get; }

    /// <summary>Digit characters in the run.</summary>
    public int Digits { get; }

    /// <summary>Digits ahead of the point: all of them when there is no point.</summary>
    public int Point { get; }

    /// <summary>The exponent written after the digits, or zero when none is.</summary>
    public int Exponent { get; }

    /// <summary>
    /// Which notation is used is decided by the value, not by the caller: plain notation
    /// when the exponent is at most zero and the adjusted exponent is at least -6,
    /// exponential otherwise.
    /// </summary>
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
            // Plain notation puts the point after the digits the exponent leaves ahead of
            // it, which for an exponent of zero is after all of them.
            if (adjusted >= 0)
            {
                return new Decimal128TextLayout(coefficient, digits, adjusted + 1, 0);
            }

            // A zero, the point, and the coefficient behind as many zeros as the adjusted
            // exponent asks for: all one run, written wide enough to start with the zero.
            return new Decimal128TextLayout(coefficient, 1 - exponent, 1, 0);
        }

        // Scientific notation puts the point after the first digit, which for a single
        // digit is after the last.
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
            // A zero has no digits to move left, so the exponent goes up to the next
            // multiple of three instead and the gap is filled after the point: 0E+1 is
            // written 0.00E+3.
            if (offset == 0)
            {
                return new Decimal128TextLayout(Decimal128Integer.Zero, 1, 1, adjusted);
            }

            return new Decimal128TextLayout(Decimal128Integer.Zero, 4 - offset, 1, adjusted + (3 - offset));
        }

        // Otherwise digits move left across the point until the exponent is a multiple of
        // three, leaving one, two, or three of them ahead of it.
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
    /// Characters the exponent's digits take, or none when engineering notation has brought
    /// it to zero: 10E+1 is written 100, with no exponent part at all.
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

    /// <summary>One for a positive value, zero for zero. Never a negative one.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int IsPositive(int value)
    {
        return (int)((uint)-value >> 31);
    }

    /// <summary>
    /// The characters the text takes under given sign and separator widths.
    /// </summary>
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
    /// Writes the text with the specification's own symbols, which are the invariant
    /// culture's too, and touches nothing past it. The destination must hold
    /// <see cref="Length"/> characters for widths of one.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Write(Span<char> destination, bool negative)
    {
        ref var start = ref MemoryMarshal.GetReference(destination);

        // The sign goes down whatever the sign is; a positive value's digits then land on
        // top of it, which is cheaper than deciding.
        start = '-';
        var index = (int)Unsafe.BitCast<bool, byte>(negative);

        // The exponent goes down before the run: its writer may put a zero on the run's
        // last place, which the run then covers.
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
    /// Writes the text with a culture's signs and separator. The destination must hold
    /// <see cref="Length"/> characters for their widths.
    /// </summary>
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
            // A wider separator: the digits ahead of the point are written on their own,
            // which for once needs the coefficient split.
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
    /// The digit run with its point. The run is written one place to the right of where it
    /// starts and the digits ahead of the point slide back over that place, which leaves
    /// the point's own character free.
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
