// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace Decimals.Internal;

/// <summary>
/// An unsigned integer in two 64-bit words. It holds the bits of a
/// <see cref="Decimal128"/>, a coefficient, or an intermediate value of up to 38 digits.
/// </summary>
/// <remarks>
/// <para>
/// The arithmetic uses this type instead of a 128-bit integer type. The JIT keeps the two
/// words in two registers. The type has only the operations the arithmetic needs: add,
/// subtract, compare, and multiply by a word or by another two-word value when the product
/// is known to fit. Division by a power of ten is in <see cref="Decimal128Tables"/>,
/// because it uses a table lookup and two reciprocal steps instead of a division.
/// </para>
/// <para>
/// Addition and subtraction wrap, like the words they are built from. Every caller keeps
/// its operands within 38 digits, where the sum of two still fits, and only subtracts the
/// smaller value from the larger.
/// </para>
/// <para>
/// The comparisons do not use short-circuit operators, so they compile to flag-setting
/// instructions instead of branches. The result of a coefficient comparison depends on the
/// data, and a branch on it mispredicts about half the time. For the same reason, the
/// conversions to and from double avoid the unsigned conversions. Each unsigned conversion
/// branches on the top bit of a word, which is random for random values.
/// </para>
/// <para>
/// Every member is marked for aggressive inlining. Each is only a few instructions. Without
/// the attribute, the JIT emitted them as calls once the surrounding operation had used up
/// its inlining budget, which turned a two-register add into a call with four moves.
/// </para>
/// </remarks>
internal readonly struct Decimal128Integer : IEquatable<Decimal128Integer>
{
    /// <summary>2^64, the weight of the high word.</summary>
    private const double WordScale = 18446744073709551616.0;

    /// <summary>2^32, the weight of the top half of a word.</summary>
    private const double HalfWordScale = 4294967296.0;

    /// <summary>The upper 64 bits.</summary>
    public readonly ulong High;

    /// <summary>The lower 64 bits.</summary>
    public readonly ulong Low;

    /// <summary>Creates a value from its two words.</summary>
    /// <param name="high">The upper 64 bits.</param>
    /// <param name="low">The lower 64 bits.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Decimal128Integer(ulong high, ulong low)
    {
        High = high;
        Low = low;
    }

    /// <summary>The value 0.</summary>
    public static Decimal128Integer Zero
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => default;
    }

    /// <summary>The value 1.</summary>
    public static Decimal128Integer One
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(0, 1);
    }

    /// <summary>Whether the value is 0.</summary>
    public bool IsZero
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (High | Low) == 0;
    }

    /// <summary>Whether the value fits in one word. If it does, <see cref="Low"/> holds all of it.</summary>
    public bool IsWord
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => High == 0;
    }

    /// <summary>Widens a word to two words.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The value, with a zero high word.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Integer FromUInt64(ulong value)
    {
        return new Decimal128Integer(0, value);
    }

    /// <summary>The sum of two values. It wraps if the sum does not fit in 128 bits.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>The low 128 bits of the sum.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Integer operator +(Decimal128Integer left, Decimal128Integer right)
    {
        var low = left.Low + right.Low;
        var carry = low < left.Low ? 1UL : 0UL;
        return new Decimal128Integer(left.High + right.High + carry, low);
    }

    /// <summary>The sum of a value and a word. It wraps if the sum does not fit in 128 bits.</summary>
    /// <param name="left">The two-word value.</param>
    /// <param name="right">The word to add.</param>
    /// <returns>The low 128 bits of the sum.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Integer operator +(Decimal128Integer left, ulong right)
    {
        var low = left.Low + right;
        var carry = low < right ? 1UL : 0UL;
        return new Decimal128Integer(left.High + carry, low);
    }

    /// <summary>The difference of two values. It wraps if the right operand is larger.</summary>
    /// <param name="left">The value to subtract from.</param>
    /// <param name="right">The value to subtract.</param>
    /// <returns>The difference, modulo 2^128.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Integer operator -(Decimal128Integer left, Decimal128Integer right)
    {
        var borrow = left.Low < right.Low ? 1UL : 0UL;
        return new Decimal128Integer(left.High - right.High - borrow, left.Low - right.Low);
    }

    /// <summary>The difference of a value and a word. It wraps if the word is larger.</summary>
    /// <param name="left">The value to subtract from.</param>
    /// <param name="right">The word to subtract.</param>
    /// <returns>The difference, modulo 2^128.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Integer operator -(Decimal128Integer left, ulong right)
    {
        var borrow = left.Low < right ? 1UL : 0UL;
        return new Decimal128Integer(left.High - borrow, left.Low - right);
    }

    /// <summary>Whether two values are equal.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>True if both words are equal.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Decimal128Integer left, Decimal128Integer right)
    {
        return ((left.High ^ right.High) | (left.Low ^ right.Low)) == 0;
    }

    /// <summary>Whether two values differ.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>True if either word differs.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Decimal128Integer left, Decimal128Integer right)
    {
        return ((left.High ^ right.High) | (left.Low ^ right.Low)) != 0;
    }

    /// <summary>Whether one value is below another.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>True if <paramref name="left"/> is less than <paramref name="right"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator <(Decimal128Integer left, Decimal128Integer right)
    {
        return (left.High < right.High) | ((left.High == right.High) & (left.Low < right.Low));
    }

    /// <summary>Whether one value is above another.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>True if <paramref name="left"/> is greater than <paramref name="right"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator >(Decimal128Integer left, Decimal128Integer right)
    {
        return right < left;
    }

    /// <summary>Whether one value is at most another.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>True if <paramref name="left"/> is less than or equal to <paramref name="right"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator <=(Decimal128Integer left, Decimal128Integer right)
    {
        return !(right < left);
    }

    /// <summary>Whether one value is at least another.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>True if <paramref name="left"/> is greater than or equal to <paramref name="right"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator >=(Decimal128Integer left, Decimal128Integer right)
    {
        return !(left < right);
    }

    /// <summary>Compares this value with another, without branches.</summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns>-1, 0, or 1 as this value is less than, equal to, or greater than <paramref name="other"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int CompareTo(Decimal128Integer other)
    {
        return Unsafe.BitCast<bool, byte>(other < this) - Unsafe.BitCast<bool, byte>(this < other);
    }

    /// <summary>Whether this value equals another.</summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns>True if both words are equal.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Decimal128Integer other)
    {
        return this == other;
    }

    /// <summary>Whether this value equals an object.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>True if <paramref name="obj"/> is a <see cref="Decimal128Integer"/> with the same words.</returns>
    public override bool Equals(object? obj)
    {
        return obj is Decimal128Integer other && Equals(other);
    }

    /// <summary>A hash code built from both words.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(High, Low);
    }

    /// <summary>The low 128 bits of the product with a word. The caller must know the product fits in 128 bits.</summary>
    /// <param name="multiplier">The word to multiply by.</param>
    /// <returns>The product.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Decimal128Integer MultiplyBy(ulong multiplier)
    {
        var high = Math.BigMul(Low, multiplier, out var low);
        return new Decimal128Integer(high + (High * multiplier), low);
    }

    /// <summary>The low 128 bits of the product of two values. The caller must know the product fits in 128 bits.</summary>
    /// <param name="multiplier">The value to multiply by.</param>
    /// <returns>The product.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Decimal128Integer Multiply(Decimal128Integer multiplier)
    {
        var high = Math.BigMul(Low, multiplier.Low, out var low);
        return new Decimal128Integer(high + (Low * multiplier.High) + (High * multiplier.Low), low);
    }

    /// <summary>The value shifted right by one bit. It is exact for an even value.</summary>
    /// <returns>Half the value, rounded down.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Decimal128Integer Halved()
    {
        return new Decimal128Integer(High >> 1, (Low >> 1) | (High << 63));
    }

    /// <summary>
    /// The last decimal digit. 2^64 is 6 more than a multiple of 10, so the high word adds
    /// 6 times its own last digit.
    /// </summary>
    /// <returns>The value modulo 10.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint LastDigit()
    {
        return (uint)(((Low % 10) + ((High % 10) * 6)) % 10);
    }

    /// <summary>
    /// A word converted to the nearest double. The word is converted as two signed 32-bit
    /// halves, so no conversion branches on the top bit.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double WordToDouble(ulong word)
    {
        return ((double)(long)(word >> 32) * HalfWordScale) + (double)(long)(uint)word;
    }

    /// <summary>The value converted to a double, which keeps the top 53 bits.</summary>
    /// <returns>The value as a double.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ToDouble()
    {
        return (WordToDouble(High) * WordScale) + WordToDouble(Low);
    }

    /// <summary>
    /// The integer part of a double, as two words. The value must be non-negative and below
    /// 2^128. Each split is exact: the upper part gets the bits above a boundary, and the
    /// remainder is the bits below it, which a double holds exactly. The low word is split
    /// again so that each part converts from a value below 2^63. The high word uses the
    /// unsigned conversion. Its branch predicts well here, because the roots and corrections
    /// that call this stay far below 2^63.
    /// </summary>
    /// <param name="value">The double to convert.</param>
    /// <returns>The integer part of <paramref name="value"/>.</returns>
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
