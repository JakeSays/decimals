// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;


namespace Decimals.Internal;

/// <summary>
/// Writes a value's decimal digits directly into a caller's characters. It writes a fixed
/// number of digits, including leading zeros.
/// </summary>
/// <remarks>
/// <para>
/// Digits are written two at a time from a 100-entry table. Each entry holds both
/// characters of a pair, so one 32-bit store writes them. A value is first split into
/// 8-digit pieces with a 64-bit division by a constant. Each piece is split into 4-digit
/// and then 2-digit parts with 32-bit divisions by constants. The JIT turns all of these
/// divisions into multiplies.
/// </para>
/// <para>
/// The number of digits varies from call to call. A branch on it mispredicts when the
/// values are mixed, at about 15 cycles each time. So a run of up to 8 digits is written
/// without branching on its length. All four pairs are computed, and each is stored at its
/// position counted back from the end of the run. A pair that would start before the run
/// is stored at the start instead, and a later store overwrites it with the right
/// characters. The exponent is written the same way.
/// </para>
/// <para>
/// A run longer than 8 digits does branch on how many 8-digit pieces it has. Writing every
/// possible piece and discarding the extra ones was tried, and it cost more than the
/// mispredictions it avoided.
/// </para>
/// <para>
/// Nothing here checks bounds. The caller sized the destination from the layout it is
/// writing, and each writer takes a reference to the first character of its field.
/// </para>
/// </remarks>
internal static class Decimal64Digits
{
    private const uint EightDigits = 100000000;

    /// <summary>
    /// The two characters of each pair as one little-endian 32-bit word. Entry
    /// 10 * tens + units holds the tens digit's character in the low half and the units
    /// digit's character in the high half.
    /// </summary>
    private static ReadOnlySpan<uint> Pairs =>
    [
        0x00300030, 0x00310030, 0x00320030, 0x00330030, 0x00340030, 0x00350030, 0x00360030, 0x00370030, 0x00380030, 0x00390030,
        0x00300031, 0x00310031, 0x00320031, 0x00330031, 0x00340031, 0x00350031, 0x00360031, 0x00370031, 0x00380031, 0x00390031,
        0x00300032, 0x00310032, 0x00320032, 0x00330032, 0x00340032, 0x00350032, 0x00360032, 0x00370032, 0x00380032, 0x00390032,
        0x00300033, 0x00310033, 0x00320033, 0x00330033, 0x00340033, 0x00350033, 0x00360033, 0x00370033, 0x00380033, 0x00390033,
        0x00300034, 0x00310034, 0x00320034, 0x00330034, 0x00340034, 0x00350034, 0x00360034, 0x00370034, 0x00380034, 0x00390034,
        0x00300035, 0x00310035, 0x00320035, 0x00330035, 0x00340035, 0x00350035, 0x00360035, 0x00370035, 0x00380035, 0x00390035,
        0x00300036, 0x00310036, 0x00320036, 0x00330036, 0x00340036, 0x00350036, 0x00360036, 0x00370036, 0x00380036, 0x00390036,
        0x00300037, 0x00310037, 0x00320037, 0x00330037, 0x00340037, 0x00350037, 0x00360037, 0x00370037, 0x00380037, 0x00390037,
        0x00300038, 0x00310038, 0x00320038, 0x00330038, 0x00340038, 0x00350038, 0x00360038, 0x00370038, 0x00380038, 0x00390038,
        0x00300039, 0x00310039, 0x00320039, 0x00330039, 0x00340039, 0x00350039, 0x00360039, 0x00370039, 0x00380039, 0x00390039
    ];

    /// <summary>
    /// Writes exactly <paramref name="count"/> digits of <paramref name="value"/>, starting
    /// at <paramref name="destination"/>. The value must be below 10 to the power of count.
    /// </summary>
    /// <remarks>
    /// This method is not inlined. Its helpers are inlined into it, and its callers stay
    /// small enough to be inlined into their own callers. That keeps the text layout in
    /// registers on the way here.
    /// </remarks>
    /// <param name="value">The value to write.</param>
    /// <param name="count">The number of digits to write, including leading zeros. It must be at most 24.</param>
    /// <param name="destination">The first character to write. At least <paramref name="count"/> characters must follow it.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Write(ulong value, int count, ref char destination)
    {
        if (count > 8)
        {
            var high = value / EightDigits;
            var low = (uint)(value - (high * EightDigits));
            WriteEight(low, ref Unsafe.Add(ref destination, count - 8));
            count -= 8;
            value = high;

            if (count > 8)
            {
                high = value / EightDigits;
                low = (uint)(value - (high * EightDigits));
                WriteEight(low, ref Unsafe.Add(ref destination, count - 8));
                count -= 8;
                value = high;
            }
        }

        if (count == 1)
        {
            // A pair store would write past a single character, so write it directly.
            destination = (char)('0' + (uint)value);
            return;
        }

        WriteUpToEight((uint)value, count, ref destination);
    }

    /// <summary>
    /// Writes all of a value's digits so that they end at the end of
    /// <paramref name="destination"/>. Returns the number of digits written.
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <param name="destination">The span whose end receives the digits. It must hold at least as many characters as the value has digits.</param>
    /// <returns>The number of digits written. Zero writes one digit.</returns>
    public static int WriteTrailing(ulong value, Span<char> destination)
    {
        var count = Decimal64Tables.CountDigits(value);
        Write(value, count, ref destination[destination.Length - count]);
        return count;
    }

    /// <summary>
    /// Writes an exponent as <c>E</c>, a sign, and one to three digits, starting at
    /// <paramref name="destination"/>. Returns the number of characters written, which is
    /// zero for an exponent of zero even though characters are still stored. The magnitude
    /// must be below 1000.
    /// </summary>
    /// <remarks>
    /// The three-digit image is always computed. When there is only one digit, the pair
    /// store lands one place early, on the sign's position. The sign is written last so it
    /// overwrites that character.
    /// </remarks>
    /// <param name="exponent">The exponent. Its magnitude must be below 1000.</param>
    /// <param name="destination">The first character to write. At least five characters must follow it.</param>
    /// <returns>The number of characters in the exponent text, or zero if the exponent is zero.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WriteExponent(int exponent, ref char destination)
    {
        var sign = exponent >> 31;
        var magnitude = (uint)((exponent ^ sign) - sign);

        var hundreds = magnitude / 100;
        var rest = magnitude - (hundreds * 100);
        var pair = Unsafe.Add(ref MemoryMarshal.GetReference(Pairs), (int)rest);

        // The image: hundreds in the low character, then the tens and units pair.
        var image = ('0' + hundreds) | ((ulong)pair << 16);
        var width = 1 + (magnitude >= 10 ? 1 : 0) + (magnitude >= 100 ? 1 : 0);

        // The digits start at offset 2. The pair belongs at their end, at offset
        // 2 + width - 2, which is the width. For a single digit that is one character early,
        // on the sign's position, and the sign store then overwrites it.
        destination = 'E';
        StorePair(pair, ref destination, width);
        Unsafe.Add(ref destination, 2) = (char)(image >> ((3 - width) * 16));
        Unsafe.Add(ref destination, 1) = (char)('+' + (sign & 2));

        var some = (int)((0u - magnitude) >> 31);
        return (2 + width) & -some;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteEight(uint value, ref char destination)
    {
        var upper = value / 10000;
        var lower = value - (upper * 10000);
        WriteFour(upper, ref destination);
        WriteFour(lower, ref Unsafe.Add(ref destination, 4));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteFour(uint value, ref char destination)
    {
        var high = value / 100;
        var low = value - (high * 100);
        ref var pairs = ref MemoryMarshal.GetReference(Pairs);
        StorePair(Unsafe.Add(ref pairs, (int)high), ref destination, 0);
        StorePair(Unsafe.Add(ref pairs, (int)low), ref destination, 2);
    }

    /// <summary>
    /// Writes 2 to 8 digits, right-aligned in <paramref name="count"/> characters, without
    /// branching on the count. Pairs are stored from the most significant down, each at its
    /// position counted back from the end of the run. A pair that would start before the run
    /// is stored at the start instead, and later stores overwrite it. The first character is
    /// stored last, because for an odd count it is the second half of a pair.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteUpToEight(uint value, int count, ref char destination)
    {
        var upper = value / 10000;
        var lower = value - (upper * 10000);
        var first = upper / 100;
        var second = upper - (first * 100);
        var third = lower / 100;
        var fourth = lower - (third * 100);

        ref var pairs = ref MemoryMarshal.GetReference(Pairs);
        var pair0 = Unsafe.Add(ref pairs, (int)first);
        var pair1 = Unsafe.Add(ref pairs, (int)second);
        var pair2 = Unsafe.Add(ref pairs, (int)third);
        var pair3 = Unsafe.Add(ref pairs, (int)fourth);

        StorePair(pair0, ref destination, Math.Max(count - 8, 0));
        StorePair(pair1, ref destination, Math.Max(count - 6, 0));
        StorePair(pair2, ref destination, Math.Max(count - 4, 0));
        StorePair(pair3, ref destination, Math.Max(count - 2, 0));

        var lowImage = pair0 | ((ulong)pair1 << 32);
        var highImage = pair2 | ((ulong)pair3 << 32);
        var top = 8 - count;
        var word = top < 4 ? lowImage : highImage;
        destination = (char)(word >> ((top & 3) * 16));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StorePair(uint pair, ref char destination, int offset)
    {
        ref var target = ref Unsafe.Add(ref destination, offset);
        if (BitConverter.IsLittleEndian)
        {
            Unsafe.WriteUnaligned(ref Unsafe.As<char, byte>(ref target), pair);
            return;
        }

        target = (char)(pair & 0xFFFF);
        Unsafe.Add(ref target, 1) = (char)(pair >> 16);
    }
}
