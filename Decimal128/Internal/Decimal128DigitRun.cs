// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace Decimals.Internal;

/// <summary>
/// Collects the digits of a number's text into two words as they are read. The first 19
/// digits go into one word and the next 19 into the other. Any further digits are only
/// counted.
/// </summary>
/// <remarks>
/// <para>
/// A run is read in two passes. The first pass finds the end of the run: four characters
/// at a time while four remain and all are digits, then one at a time. The second pass
/// converts the digits: four at a time from a word of characters, then the last few one at
/// a time. Every branch depends only on the count. An earlier reader decided, for each
/// group of four, whether they were digits, whether they fit in the current word, and which
/// word that was. It mispredicted at each of those branches.
/// </para>
/// <para>
/// The decimal point does not end the run. The parser calls <see cref="Read"/> again after
/// the point, and the number of digits read after the point adjusts the exponent.
/// </para>
/// </remarks>
internal struct Decimal128DigitRun
{
    /// <summary>The number of digits one word can hold without overflow.</summary>
    public const int WordDigits = Decimal128Tables.MaxPower;

    /// <summary>The number of digits two words hold.</summary>
    public const int Capacity = 2 * WordDigits;

    /// <summary>The first 19 digits.</summary>
    public ulong High;

    /// <summary>The next 19 digits.</summary>
    public ulong Low;

    /// <summary>The number of digits collected into <see cref="Low"/>.</summary>
    public int LowDigits;

    /// <summary>The number of digits read, including those not collected.</summary>
    public int Digits;

    /// <summary>
    /// Collects a run of digits starting at <paramref name="index"/>. Returns the index of
    /// the first character that is not a digit.
    /// </summary>
    /// <param name="start">The first character of the text.</param>
    /// <param name="index">The index where the run starts.</param>
    /// <param name="length">The length of the text.</param>
    /// <returns>The index of the first character after the run, or <paramref name="length"/> if the run reaches the end.</returns>
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
    /// The collected digits as one value. It holds all the digits only if no more than
    /// <see cref="Capacity"/> were read.
    /// </summary>
    /// <returns>The collected digits as a two-word integer.</returns>
    public readonly Decimal128Integer ToCoefficient()
    {
        if (Digits <= WordDigits)
        {
            return Decimal128Integer.FromUInt64(High);
        }

        return Decimal128Integer.FromUInt64(High).MultiplyBy(Decimal128Tables.PowerOfTen(LowDigits)) + Low;
    }

    /// <summary>
    /// The value of <paramref name="count"/> digit characters, at most 19. Digits are
    /// converted four at a time from a word of characters, then the last one to three one
    /// at a time. The branches depend only on the count. A jump table on the count was
    /// tried, and it mispredicted more than these branches.
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
    /// Whether four characters are all digits. The characters are read as one word of four
    /// 16-bit lanes, and '0' is subtracted from each lane, which leaves each digit's value.
    /// A lane that was not a digit, or that took a borrow from a lower lane that was not a
    /// digit, has a bit set above its low 4 bits, either directly or after 6 is added.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool AreDigits(ref char start, int index)
    {
        var lanes = Lanes(ref start, index);
        return ((lanes | (lanes + 0x0006000600060006UL)) & 0xFFF0FFF0FFF0FFF0UL) == 0;
    }

    /// <summary>
    /// The value of four digit characters. Each even lane becomes 10 times itself plus the
    /// next lane. The two results are then combined as hundreds and units.
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
