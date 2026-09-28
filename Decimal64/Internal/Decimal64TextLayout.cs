// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;


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
/// <para>
/// Four fields, so the JIT keeps the whole layout in registers rather than in memory.
/// </para>
/// </remarks>
internal readonly struct Decimal64TextLayout
{
    /// <summary>
    /// Characters a destination needs for <see cref="WriteWide"/>: the longest text is
    /// twenty-four, and the writer reaches a few characters past the text it leaves.
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

    /// <summary>The value the digit run is written from, leading zeros supplied by the width.</summary>
    public ulong Coefficient { get; }

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
    /// <remarks>
    /// Two branches on the value's shape. Laying out both notations and choosing one with
    /// masks was tried: it took more instructions than the branches cost, since a
    /// predictor sees through most of what mixed values throw at these.
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
            // Plain notation puts the point after the digits the exponent leaves ahead of
            // it, which for an exponent of zero is after all of them.
            if (adjusted >= 0)
            {
                return new Decimal64TextLayout(coefficient, digits, adjusted + 1, 0);
            }

            // A zero, the point, and the coefficient behind as many zeros as the adjusted
            // exponent asks for: all one run, written wide enough to start with the zero.
            return new Decimal64TextLayout(coefficient, 1 - exponent, 1, 0);
        }

        // Scientific notation puts the point after the first digit, which for a single
        // digit is after the last.
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
            // A zero has no digits to move left, so the exponent goes up to the next
            // multiple of three instead and the gap is filled after the point: 0E+1 is
            // written 0.00E+3.
            if (offset == 0)
            {
                return new Decimal64TextLayout(0, 1, 1, adjusted);
            }

            return new Decimal64TextLayout(0, 4 - offset, 1, adjusted + (3 - offset));
        }

        // Otherwise digits move left across the point until the exponent is a multiple of
        // three, leaving one, two, or three of them ahead of it.
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
    /// Characters the exponent's digits take, or none when engineering notation has brought
    /// it to zero: 10E+1 is written 100, with no exponent part at all. Arithmetic rather
    /// than branches, since the width varies from value to value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ExponentWidth(int exponent)
    {
        var sign = exponent >> 31;
        var magnitude = (exponent ^ sign) - sign;
        var width = 1 + (magnitude >= 10 ? 1 : 0) + (magnitude >= 100 ? 1 : 0);
        return width & -IsPositive(magnitude);
    }

    /// <summary>One for a positive value, zero for zero. Never a negative one.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int IsPositive(int value)
    {
        return (int)((uint)-value >> 31);
    }

    /// <summary>
    /// The characters the text takes under given sign and separator widths. Arithmetic
    /// throughout: the sign, the point, and the exponent all vary from value to value,
    /// and a branch on any of them mispredicts.
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

        index = WriteRun(ref start, index);

        if (Exponent != 0)
        {
            index += Decimal64Digits.WriteExponent(Exponent, ref Unsafe.Add(ref start, index));
        }

        return index;
    }

    /// <summary>
    /// Writes the same text as <see cref="Write(Span{char}, bool)"/> into a destination of
    /// at least <see cref="WideLength"/> characters, without a branch on the value's shape,
    /// and returns the text's length. Characters past the text are left holding whatever
    /// the writing put there.
    /// </summary>
    /// <remarks>
    /// The run goes down one place to the right of where it starts, whether or not it has
    /// a point. Sixteen characters then move back one place in two halves of eight: the
    /// first from the run's start, the second ending at the point when more than eight
    /// digits are ahead of it and doubling the first otherwise. That carries the digits
    /// ahead of the point, and for a run with no point all of it, sixteen being the most
    /// digits such a run has. Eight characters from the point on are read before the
    /// move and put back after it, which undoes what the move did past the point. The
    /// point and the exponent are written whether the text has them or not: the point
    /// lands on the character after a run without one, which the exponent or the text's
    /// end then covers.
    /// </remarks>
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
    /// The digit run with its point. The run is written one place to the right of where it
    /// starts and the digits ahead of the point slide back over that place, which leaves
    /// the point's own character free.
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
