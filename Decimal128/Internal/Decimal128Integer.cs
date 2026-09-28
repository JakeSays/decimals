// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace Decimals.Internal;

/// <summary>
/// An unsigned integer in two machine words: the bits of a <see cref="Decimal128"/>, a
/// coefficient, or an intermediate of up to thirty-eight digits on its way to becoming
/// one.
/// </summary>
/// <remarks>
/// <para>
/// This is what the arithmetic computes on instead of a 128-bit integer type: two words
/// the JIT keeps in two registers, with only the operations the arithmetic needs -- add,
/// subtract, compare, and multiply by a word or by another two words when the product is
/// known to fit. Division by a power of ten is in <see cref="Decimal128Tables"/>, since
/// it is a table lookup and two reciprocal steps rather than a division.
/// </para>
/// <para>
/// Addition and subtraction wrap, as the words they are built from do. Every caller keeps
/// its operands inside thirty-eight digits, where a sum of two of them still fits, and
/// subtracts only the smaller from the larger.
/// </para>
/// <para>
/// The comparisons are written without short-circuit operators so that they compile to
/// flag-setting instructions rather than branches: which way a coefficient compare goes
/// is decided by the data, and a branch on it mispredicts as often as not. The
/// conversions to and from double avoid the unsigned conversions for the same reason:
/// each of those is a branch on the top bit of a word, which a random word makes a coin
/// flip.
/// </para>
/// <para>
/// Every member is marked to inline. Each is a few instructions, and left to the JIT's
/// judgment they were emitted as calls once the operation around them had used up its
/// inlining budget, which turned a two-register add into a call with four moves.
/// </para>
/// </remarks>
internal readonly struct Decimal128Integer : IEquatable<Decimal128Integer>
{
    /// <summary>Two to the sixty-fourth, which is what the high word counts in.</summary>
    private const double WordScale = 18446744073709551616.0;

    /// <summary>Two to the thirty-second, which is what the top half of a word counts in.</summary>
    private const double HalfWordScale = 4294967296.0;

    public readonly ulong High;

    public readonly ulong Low;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Decimal128Integer(ulong high, ulong low)
    {
        High = high;
        Low = low;
    }

    public static Decimal128Integer Zero
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => default;
    }

    public static Decimal128Integer One
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(0, 1);
    }

    public bool IsZero
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (High | Low) == 0;
    }

    /// <summary>Whether the value fits one word, in which case <see cref="Low"/> is the whole of it.</summary>
    public bool IsWord
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => High == 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Integer FromUInt64(ulong value)
    {
        return new Decimal128Integer(0, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Integer operator +(Decimal128Integer left, Decimal128Integer right)
    {
        var low = left.Low + right.Low;
        var carry = low < left.Low ? 1UL : 0UL;
        return new Decimal128Integer(left.High + right.High + carry, low);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Integer operator +(Decimal128Integer left, ulong right)
    {
        var low = left.Low + right;
        var carry = low < right ? 1UL : 0UL;
        return new Decimal128Integer(left.High + carry, low);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Integer operator -(Decimal128Integer left, Decimal128Integer right)
    {
        var borrow = left.Low < right.Low ? 1UL : 0UL;
        return new Decimal128Integer(left.High - right.High - borrow, left.Low - right.Low);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Integer operator -(Decimal128Integer left, ulong right)
    {
        var borrow = left.Low < right ? 1UL : 0UL;
        return new Decimal128Integer(left.High - borrow, left.Low - right);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Decimal128Integer left, Decimal128Integer right)
    {
        return ((left.High ^ right.High) | (left.Low ^ right.Low)) == 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Decimal128Integer left, Decimal128Integer right)
    {
        return ((left.High ^ right.High) | (left.Low ^ right.Low)) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator <(Decimal128Integer left, Decimal128Integer right)
    {
        return (left.High < right.High) | ((left.High == right.High) & (left.Low < right.Low));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator >(Decimal128Integer left, Decimal128Integer right)
    {
        return right < left;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator <=(Decimal128Integer left, Decimal128Integer right)
    {
        return !(right < left);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator >=(Decimal128Integer left, Decimal128Integer right)
    {
        return !(left < right);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int CompareTo(Decimal128Integer other)
    {
        return Unsafe.BitCast<bool, byte>(other < this) - Unsafe.BitCast<bool, byte>(this < other);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Decimal128Integer other)
    {
        return this == other;
    }

    public override bool Equals(object? obj)
    {
        return obj is Decimal128Integer other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(High, Low);
    }

    /// <summary>The low 128 bits of the product with a word, which the caller knows is all of it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Decimal128Integer MultiplyBy(ulong multiplier)
    {
        var high = Math.BigMul(Low, multiplier, out var low);
        return new Decimal128Integer(high + (High * multiplier), low);
    }

    /// <summary>The low 128 bits of the product of two values, which the caller knows is all of it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Decimal128Integer Multiply(Decimal128Integer multiplier)
    {
        var high = Math.BigMul(Low, multiplier.Low, out var low);
        return new Decimal128Integer(high + (Low * multiplier.High) + (High * multiplier.Low), low);
    }

    /// <summary>Half the value, which for an even one is exact.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Decimal128Integer Halved()
    {
        return new Decimal128Integer(High >> 1, (Low >> 1) | (High << 63));
    }

    /// <summary>
    /// The last decimal digit. A word is six more than a multiple of ten, so the high word
    /// contributes six times its own last digit.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint LastDigit()
    {
        return (uint)(((Low % 10) + ((High % 10) * 6)) % 10);
    }

    /// <summary>
    /// A word as a double, to the nearest fifty-three bits, converted as two signed halves
    /// so that no conversion looks at a top bit.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double WordToDouble(ulong word)
    {
        return ((double)(long)(word >> 32) * HalfWordScale) + (double)(long)(uint)word;
    }

    /// <summary>The nearest double, which carries the top fifty-three bits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ToDouble()
    {
        return (WordToDouble(High) * WordScale) + WordToDouble(Low);
    }

    /// <summary>
    /// A double's integer part as two words. The value must be non-negative and below
    /// 2^128. Each split is exact: the upper part takes the bits above a boundary, and
    /// what is left is the bits below it, which a double holds whole. The low word is
    /// split once more so that its conversions back are of values below 2^63; the high
    /// word's conversion is the unsigned one, whose branch is predicted where this is
    /// used, since the roots and corrections there stay far below 2^63.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Integer FromDouble(double value)
    {
        var high = Math.Floor(value * (1.0 / WordScale));
        var low = value - (high * WordScale);
        var lowTop = Math.Floor(low * (1.0 / HalfWordScale));
        var lowBottom = low - (lowTop * HalfWordScale);
        var lowWord = ((ulong)(long)lowTop << 32) | (ulong)(long)lowBottom;
        return new Decimal128Integer((ulong)high, lowWord);
    }
}
