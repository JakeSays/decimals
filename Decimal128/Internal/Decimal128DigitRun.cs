// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace Decimals.Internal;

/// <summary>
/// The digits of a number's text gathered into two words as they are read: the first
/// nineteen into one, the next nineteen into the other, and any beyond only counted.
/// </summary>
/// <remarks>
/// <para>
/// A run is read in two passes. The first finds where it ends, four characters at a time
/// while four are there and all are digits, then one at a time. The second converts it,
/// four digits at a time from a word of characters and then the last few one at a time,
/// with every branch following the count alone: a reader that decided for each four
/// whether they were digits, whether they fit the current word, and which word that was,
/// mispredicted at each of its turns.
/// </para>
/// <para>
/// The point in the text does not interrupt the run: the parser calls back in after it,
/// and the count of digits it read after the point is the exponent's correction.
/// </para>
/// </remarks>
internal struct Decimal128DigitRun
{
    /// <summary>The digits one word gathers without overflowing.</summary>
    public const int WordDigits = Decimal128Tables.MaxPower;

    /// <summary>The digits two words gather.</summary>
    public const int Capacity = 2 * WordDigits;

    /// <summary>The first nineteen digits.</summary>
    public ulong High;

    /// <summary>The nineteen after them.</summary>
    public ulong Low;

    /// <summary>Digits gathered into <see cref="Low"/>.</summary>
    public int LowDigits;

    /// <summary>Digits seen, whether or not they were gathered.</summary>
    public int Digits;

    /// <summary>
    /// Gathers a run of digits starting at <paramref name="index"/>, and returns the index
    /// of the first character that is not one.
    /// </summary>
    public int Read(ref char start, int index, int length)
    {
        var end = index;
        while (end + 4 <= length && AreDigits(ref start, end))
        {
            end += 4;
        }

        while (end < length && (uint)(Unsafe.Add(ref start, end) - '0') <= 9)
        {
            end++;
        }

        var count = end - index;

        if (Digits < WordDigits)
        {
            var take = Math.Min(count, WordDigits - Digits);
            High = (High * Decimal128Tables.PowerOfTen(take)) + Convert(ref start, index, take);
            index += take;
            count -= take;
            Digits += take;
        }

        if (count > 0 && Digits < Capacity)
        {
            var take = Math.Min(count, Capacity - Digits);
            Low = (Low * Decimal128Tables.PowerOfTen(take)) + Convert(ref start, index, take);
            LowDigits += take;
            count -= take;
            Digits += take;
        }

        Digits += count;
        return end;
    }

    /// <summary>
    /// The digits as one value, which is only the whole of them when no more than the
    /// capacity were seen.
    /// </summary>
    public readonly Decimal128Integer ToCoefficient()
    {
        if (Digits <= WordDigits)
        {
            return Decimal128Integer.FromUInt64(High);
        }

        return Decimal128Integer.FromUInt64(High).MultiplyBy(Decimal128Tables.PowerOfTen(LowDigits)) + Low;
    }

    /// <summary>
    /// The value of <paramref name="count"/> digit characters, at most nineteen of them:
    /// four at a time from a word of characters, then the last one to three one at a
    /// time. The tests here follow only the count; a jump table on it was tried and
    /// mispredicted more than they do.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Convert(ref char start, int index, int count)
    {
        var value = 0UL;
        var quads = count >> 2;

        if (quads > 0)
        {
            value = ReadFour(ref start, index);
            index += 4;
        }

        if (quads > 1)
        {
            value = (value * 10000) + ReadFour(ref start, index);
            index += 4;
        }

        if (quads > 2)
        {
            value = (value * 10000) + ReadFour(ref start, index);
            index += 4;
        }

        if (quads > 3)
        {
            value = (value * 10000) + ReadFour(ref start, index);
            index += 4;
        }

        var tail = count & 3;
        if (tail > 0)
        {
            value = (value * 10) + (uint)(Unsafe.Add(ref start, index) - '0');
            index++;
        }

        if (tail > 1)
        {
            value = (value * 10) + (uint)(Unsafe.Add(ref start, index) - '0');
            index++;
        }

        if (tail > 2)
        {
            value = (value * 10) + (uint)(Unsafe.Add(ref start, index) - '0');
        }

        return value;
    }

    /// <summary>
    /// Whether four characters are all digits, read as one word of four 16-bit lanes.
    /// Subtracting '0' from every lane leaves each digit's value, and a lane that was not a
    /// digit -- or that borrowed from a lower lane that was not -- shows a high nibble,
    /// either as it is or once six is added to it.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool AreDigits(ref char start, int index)
    {
        var lanes = Lanes(ref start, index);
        return ((lanes | (lanes + 0x0006000600060006UL)) & 0xFFF0FFF0FFF0FFF0UL) == 0;
    }

    /// <summary>
    /// Four digit characters as their value. The lanes fold: each even lane takes ten
    /// times itself plus the lane above it, and the two results combine as hundreds and
    /// units.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ReadFour(ref char start, int index)
    {
        var lanes = Lanes(ref start, index);
        var pairs = ((lanes * 10) + (lanes >> 16)) & 0x0000FFFF0000FFFFUL;
        return ((uint)pairs * 100) + (uint)(pairs >> 32);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Lanes(ref char start, int index)
    {
        var word = Unsafe.ReadUnaligned<ulong>(ref Unsafe.As<char, byte>(ref Unsafe.Add(ref start, index)));
        return word - 0x0030003000300030UL;
    }
}
