// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace Decimals.Internal;

/// <summary>
/// An unsigned integer in four machine words, for the intermediates that outgrow two:
/// the exact product of two coefficients, which runs to sixty-eight digits, a dividend
/// scaled by a power of ten, and the radicand of a square root, scaled to seventy.
/// </summary>
/// <remarks>
/// Nothing here rounds. A value is built exactly, compared, added to or subtracted from
/// another exactly, and then cut down to two words by <see cref="Decimal128Tables"/> or
/// <see cref="Decimal128Divider"/>, which divide it a word at a time. Every member is
/// marked to inline, and the comparisons are written without branches, for the reasons
/// <see cref="Decimal128Integer"/> gives.
/// </remarks>
internal readonly struct Decimal128LongInteger : IEquatable<Decimal128LongInteger>
{
    /// <summary>Two to the sixty-fourth, which is what each word counts in against the one below.</summary>
    private const double WordScale = 18446744073709551616.0;

    public readonly ulong Word3;

    public readonly ulong Word2;

    public readonly ulong Word1;

    public readonly ulong Word0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Decimal128LongInteger(ulong word3, ulong word2, ulong word1, ulong word0)
    {
        Word3 = word3;
        Word2 = word2;
        Word1 = word1;
        Word0 = word0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128LongInteger FromInteger(Decimal128Integer value)
    {
        return new Decimal128LongInteger(0, 0, value.High, value.Low);
    }

    public bool IsZero
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Word3 | Word2 | Word1 | Word0) == 0;
    }

    /// <summary>Whether the value fits two words, in which case <see cref="ToInteger"/> is the whole of it.</summary>
    public bool IsInteger
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Word3 | Word2) == 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Decimal128Integer ToInteger()
    {
        return new Decimal128Integer(Word1, Word0);
    }

    /// <summary>The exact product of two two-word values.</summary>
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

    /// <summary>The exact square of a two-word value: three products, the cross one doubled.</summary>
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
    /// The low four words of the product of a two-word value and a four-word one, which
    /// the caller knows is all of it. The partial products that reach past the fourth
    /// word are not formed, and the two that end in it are only formed as far as it.
    /// </summary>
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

    /// <summary>The product with a word, which the caller knows still fits four words.</summary>
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

    /// <summary>The difference, which wraps when the right operand is the larger.</summary>
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Decimal128LongInteger left, Decimal128LongInteger right)
    {
        return ((left.Word3 ^ right.Word3) | (left.Word2 ^ right.Word2) | (left.Word1 ^ right.Word1)
            | (left.Word0 ^ right.Word0)) == 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Decimal128LongInteger left, Decimal128LongInteger right)
    {
        return !(left == right);
    }

    /// <summary>Whether one value is below another, decided from the bottom word up without a branch.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator <(Decimal128LongInteger left, Decimal128LongInteger right)
    {
        var less0 = left.Word0 < right.Word0;
        var less1 = (left.Word1 < right.Word1) | ((left.Word1 == right.Word1) & less0);
        var less2 = (left.Word2 < right.Word2) | ((left.Word2 == right.Word2) & less1);
        return (left.Word3 < right.Word3) | ((left.Word3 == right.Word3) & less2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator >(Decimal128LongInteger left, Decimal128LongInteger right)
    {
        return right < left;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator <=(Decimal128LongInteger left, Decimal128LongInteger right)
    {
        return !(right < left);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator >=(Decimal128LongInteger left, Decimal128LongInteger right)
    {
        return !(left < right);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int CompareTo(Decimal128LongInteger other)
    {
        return Unsafe.BitCast<bool, byte>(other < this) - Unsafe.BitCast<bool, byte>(this < other);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Decimal128LongInteger other)
    {
        return this == other;
    }

    public override bool Equals(object? obj)
    {
        return obj is Decimal128LongInteger other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Word3, Word2, Word1, Word0);
    }

    /// <summary>The nearest double, which carries the top fifty-three bits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ToDouble()
    {
        var top = (Decimal128Integer.WordToDouble(Word3) * WordScale) + Decimal128Integer.WordToDouble(Word2);
        var bottom = (Decimal128Integer.WordToDouble(Word1) * WordScale) + Decimal128Integer.WordToDouble(Word0);
        return (top * WordScale * WordScale) + bottom;
    }
}
