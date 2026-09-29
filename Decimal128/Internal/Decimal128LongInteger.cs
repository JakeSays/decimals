// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace Decimals.Internal;

/// <summary>
/// An unsigned integer in four 64-bit words, for intermediate values too large for two:
/// the exact product of two coefficients (up to 68 digits), a dividend scaled by a power
/// of ten, and a square root's radicand scaled to 70 digits.
/// </summary>
/// <remarks>
/// Nothing here rounds. A value is built, compared, added, and subtracted exactly. Then
/// <see cref="Decimal128Tables"/> or <see cref="Decimal128Divider"/> reduces it to two
/// words by dividing one word at a time. Every member is marked for aggressive inlining,
/// and the comparisons have no branches, for the reasons given in
/// <see cref="Decimal128Integer"/>.
/// </remarks>
internal readonly struct Decimal128LongInteger : IEquatable<Decimal128LongInteger>
{
    /// <summary>2^64, the weight of each word relative to the word below it.</summary>
    private const double WordScale = 18446744073709551616.0;

    /// <summary>The highest 64 bits.</summary>
    public readonly ulong Word3;

    /// <summary>The second-highest 64 bits.</summary>
    public readonly ulong Word2;

    /// <summary>The second-lowest 64 bits.</summary>
    public readonly ulong Word1;

    /// <summary>The lowest 64 bits.</summary>
    public readonly ulong Word0;

    /// <summary>Creates a value from its four words, highest first.</summary>
    /// <param name="word3">The highest 64 bits.</param>
    /// <param name="word2">The second-highest 64 bits.</param>
    /// <param name="word1">The second-lowest 64 bits.</param>
    /// <param name="word0">The lowest 64 bits.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Decimal128LongInteger(ulong word3, ulong word2, ulong word1, ulong word0)
    {
        Word3 = word3;
        Word2 = word2;
        Word1 = word1;
        Word0 = word0;
    }

    /// <summary>Widens a two-word value to four words.</summary>
    /// <param name="value">The two-word value.</param>
    /// <returns>The value, with zero upper words.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128LongInteger FromInteger(Decimal128Integer value)
    {
        return new Decimal128LongInteger(0, 0, value.High, value.Low);
    }

    /// <summary>Whether the value is 0.</summary>
    public bool IsZero
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Word3 | Word2 | Word1 | Word0) == 0;
    }

    /// <summary>Whether the value fits in two words. If it does, <see cref="ToInteger"/> returns all of it.</summary>
    public bool IsInteger
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Word3 | Word2) == 0;
    }

    /// <summary>The low two words. They hold the whole value only if <see cref="IsInteger"/> is true.</summary>
    /// <returns>The low 128 bits.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Decimal128Integer ToInteger()
    {
        return new Decimal128Integer(Word1, Word0);
    }

    /// <summary>The exact product of two two-word values.</summary>
    /// <param name="left">The first factor.</param>
    /// <param name="right">The second factor.</param>
    /// <returns>The product, in four words.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128LongInteger Multiply(Decimal128Integer left, Decimal128Integer right)
    {
        var high00 = Math.BigMul(left.Low, right.Low, out var low00);
        var high01 = Math.BigMul(left.Low, right.High, out var low01);
        var high10 = Math.BigMul(left.High, right.Low, out var low10);
        var high11 = Math.BigMul(left.High, right.High, out var low11);

        var word1 = high00 + low01;
        var carry = word1 < high00 ? 1UL : 0UL;
        word1 += low10;
        carry += word1 < low10 ? 1UL : 0UL;

        var word2 = high01 + high10;
        var carry2 = word2 < high01 ? 1UL : 0UL;
        word2 += low11;
        carry2 += word2 < low11 ? 1UL : 0UL;
        word2 += carry;
        carry2 += word2 < carry ? 1UL : 0UL;

        return new Decimal128LongInteger(high11 + carry2, word2, word1, low00);
    }

    /// <summary>The exact square of a two-word value. It uses three products and doubles the cross product.</summary>
    /// <param name="value">The value to square.</param>
    /// <returns>The square, in four words.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128LongInteger Square(Decimal128Integer value)
    {
        var high00 = Math.BigMul(value.Low, value.Low, out var low00);
        var high01 = Math.BigMul(value.Low, value.High, out var low01);
        var high11 = Math.BigMul(value.High, value.High, out var low11);

        var crossTop = high01 >> 63;
        var crossHigh = (high01 << 1) | (low01 >> 63);
        var crossLow = low01 << 1;

        var word1 = high00 + crossLow;
        var carry1 = word1 < high00 ? 1UL : 0UL;

        var word2 = low11 + crossHigh;
        var carry2 = word2 < low11 ? 1UL : 0UL;
        word2 += carry1;
        carry2 += word2 < carry1 ? 1UL : 0UL;

        return new Decimal128LongInteger(high11 + crossTop + carry2, word2, word1, low00);
    }

    /// <summary>
    /// The low four words of the product of a two-word value and a four-word value. The
    /// caller must know the product fits in four words. Partial products that lie entirely
    /// above the fourth word are skipped, and the two that end in the fourth word are
    /// computed only up to it.
    /// </summary>
    /// <param name="left">The two-word factor.</param>
    /// <param name="right">The four-word factor.</param>
    /// <returns>The product, in four words.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128LongInteger Multiply(Decimal128Integer left, Decimal128LongInteger right)
    {
        var high00 = Math.BigMul(left.Low, right.Word0, out var word0);
        var high01 = Math.BigMul(left.Low, right.Word1, out var low01);
        var high02 = Math.BigMul(left.Low, right.Word2, out var low02);
        var low03 = left.Low * right.Word3;
        var high10 = Math.BigMul(left.High, right.Word0, out var low10);
        var high11 = Math.BigMul(left.High, right.Word1, out var low11);
        var low12 = left.High * right.Word2;

        var word1 = high00 + low01;
        var carry1 = word1 < high00 ? 1UL : 0UL;
        word1 += low10;
        carry1 += word1 < low10 ? 1UL : 0UL;

        var word2 = high01 + low02;
        var carry2 = word2 < high01 ? 1UL : 0UL;
        word2 += high10;
        carry2 += word2 < high10 ? 1UL : 0UL;
        word2 += low11;
        carry2 += word2 < low11 ? 1UL : 0UL;
        word2 += carry1;
        carry2 += word2 < carry1 ? 1UL : 0UL;

        var word3 = high02 + low03 + high11 + low12 + carry2;
        return new Decimal128LongInteger(word3, word2, word1, word0);
    }

    /// <summary>The product with a word. The caller must know the product fits in four words.</summary>
    /// <param name="multiplier">The word to multiply by.</param>
    /// <returns>The product.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Decimal128LongInteger MultiplyBy(ulong multiplier)
    {
        var carry0 = Math.BigMul(Word0, multiplier, out var word0);
        var carry1 = Math.BigMul(Word1, multiplier, out var product1);
        var word1 = product1 + carry0;
        carry1 += word1 < carry0 ? 1UL : 0UL;

        var carry2 = Math.BigMul(Word2, multiplier, out var product2);
        var word2 = product2 + carry1;
        carry2 += word2 < carry1 ? 1UL : 0UL;

        var word3 = (Word3 * multiplier) + carry2;
        return new Decimal128LongInteger(word3, word2, word1, word0);
    }

    /// <summary>The sum of two values. It wraps if the sum does not fit in four words.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>The low 256 bits of the sum.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128LongInteger operator +(Decimal128LongInteger left, Decimal128LongInteger right)
    {
        var word0 = left.Word0 + right.Word0;
        var carry = word0 < left.Word0 ? 1UL : 0UL;

        var word1 = left.Word1 + right.Word1;
        var carry1 = word1 < left.Word1 ? 1UL : 0UL;
        word1 += carry;
        carry1 += word1 < carry ? 1UL : 0UL;

        var word2 = left.Word2 + right.Word2;
        var carry2 = word2 < left.Word2 ? 1UL : 0UL;
        word2 += carry1;
        carry2 += word2 < carry1 ? 1UL : 0UL;

        return new Decimal128LongInteger(left.Word3 + right.Word3 + carry2, word2, word1, word0);
    }

    /// <summary>The difference. It wraps if the right operand is larger.</summary>
    /// <param name="left">The value to subtract from.</param>
    /// <param name="right">The value to subtract.</param>
    /// <returns>The difference, modulo 2^256.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128LongInteger operator -(Decimal128LongInteger left, Decimal128LongInteger right)
    {
        var word0 = left.Word0 - right.Word0;
        var borrow = left.Word0 < right.Word0 ? 1UL : 0UL;

        var word1 = left.Word1 - right.Word1;
        var borrow1 = left.Word1 < right.Word1 ? 1UL : 0UL;
        borrow1 += word1 < borrow ? 1UL : 0UL;
        word1 -= borrow;

        var word2 = left.Word2 - right.Word2;
        var borrow2 = left.Word2 < right.Word2 ? 1UL : 0UL;
        borrow2 += word2 < borrow1 ? 1UL : 0UL;
        word2 -= borrow1;

        return new Decimal128LongInteger(left.Word3 - right.Word3 - borrow2, word2, word1, word0);
    }

    /// <summary>Whether two values are equal.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>True if all four words are equal.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Decimal128LongInteger left, Decimal128LongInteger right)
    {
        return ((left.Word3 ^ right.Word3) | (left.Word2 ^ right.Word2) | (left.Word1 ^ right.Word1)
            | (left.Word0 ^ right.Word0)) == 0;
    }

    /// <summary>Whether two values differ.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>True if any word differs.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Decimal128LongInteger left, Decimal128LongInteger right)
    {
        return !(left == right);
    }

    /// <summary>Whether one value is below another. It compares from the bottom word up, without branches.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>True if <paramref name="left"/> is less than <paramref name="right"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator <(Decimal128LongInteger left, Decimal128LongInteger right)
    {
        var less0 = left.Word0 < right.Word0;
        var less1 = (left.Word1 < right.Word1) | ((left.Word1 == right.Word1) & less0);
        var less2 = (left.Word2 < right.Word2) | ((left.Word2 == right.Word2) & less1);
        return (left.Word3 < right.Word3) | ((left.Word3 == right.Word3) & less2);
    }

    /// <summary>Whether one value is above another.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>True if <paramref name="left"/> is greater than <paramref name="right"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator >(Decimal128LongInteger left, Decimal128LongInteger right)
    {
        return right < left;
    }

    /// <summary>Whether one value is at most another.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>True if <paramref name="left"/> is less than or equal to <paramref name="right"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator <=(Decimal128LongInteger left, Decimal128LongInteger right)
    {
        return !(right < left);
    }

    /// <summary>Whether one value is at least another.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>True if <paramref name="left"/> is greater than or equal to <paramref name="right"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator >=(Decimal128LongInteger left, Decimal128LongInteger right)
    {
        return !(left < right);
    }

    /// <summary>Compares this value with another, without branches.</summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns>-1, 0, or 1 as this value is less than, equal to, or greater than <paramref name="other"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int CompareTo(Decimal128LongInteger other)
    {
        return Unsafe.BitCast<bool, byte>(other < this) - Unsafe.BitCast<bool, byte>(this < other);
    }

    /// <summary>Whether this value equals another.</summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns>True if all four words are equal.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Decimal128LongInteger other)
    {
        return this == other;
    }

    /// <summary>Whether this value equals an object.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>True if <paramref name="obj"/> is a <see cref="Decimal128LongInteger"/> with the same words.</returns>
    public override bool Equals(object? obj)
    {
        return obj is Decimal128LongInteger other && Equals(other);
    }

    /// <summary>A hash code built from all four words.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(Word3, Word2, Word1, Word0);
    }

    /// <summary>The value converted to a double, which keeps the top 53 bits.</summary>
    /// <returns>The value as a double.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ToDouble()
    {
        var top = (Decimal128Integer.WordToDouble(Word3) * WordScale) + Decimal128Integer.WordToDouble(Word2);
        var bottom = (Decimal128Integer.WordToDouble(Word1) * WordScale) + Decimal128Integer.WordToDouble(Word0);
        return (top * WordScale * WordScale) + bottom;
    }
}
