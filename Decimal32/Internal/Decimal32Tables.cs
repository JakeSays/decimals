// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Decimals.Internal;

/// <summary>
/// Powers of ten, the reciprocals that stand in for dividing by them, and the digit count.
/// These sit under every operation, so each is a handful of instructions and no table is
/// read with a bounds check.
/// </summary>
/// <remarks>
/// <para>
/// Every table here is a span over the assembly's data section: nothing is allocated and no
/// class constructor runs before a lookup.
/// </para>
/// <para>
/// Division by a power of ten is a multiply and two shifts. 10^p is 2^p times 5^p, and
/// <c>floor(floor(n / a) / b)</c> is <c>floor(n / ab)</c>, so the division becomes a shift
/// right by p followed by a division by 5^p; the shift takes p bits off the numerator while
/// taking 2.32p bits off the divisor, which is what leaves room for a 64-bit multiplier.
/// For that division take <c>M = ceil(2^s / d)</c> with <c>s = 64 + floor(log2 d)</c> and
/// <c>e = M*d - 2^s</c>. The quotient <c>n*M / 2^s</c> is exact for every numerator up to
/// <c>n_max</c> when <c>n_max * e &lt; 2^s</c>, and with the pre-shift that holds for the
/// whole 64-bit range at every power: e is below d, which is below 2^(s-63), and the
/// shifted numerator is below 2^(64-p). So there is no correction step. The constants were
/// found by a generator and are re-derived by <c>Decimal32TableTests</c>.
/// </para>
/// <para>
/// The tables reach to 10^19 although the format's coefficient stops at seven digits,
/// because the intermediates do not: a product has fourteen and an aligned operand as
/// many as a word holds.
/// </para>
/// </remarks>
internal static class Decimal32Tables
{
    /// <summary>Largest power of ten that fits a <see cref="ulong"/>.</summary>
    public const int MaxPower = 19;

    private static ReadOnlySpan<ulong> Powers =>
    [
        1,
        10,
        100,
        1000,
        10000,
        100000,
        1000000,
        10000000,
        100000000,
        1000000000,
        10000000000,
        100000000000,
        1000000000000,
        10000000000000,
        100000000000000,
        1000000000000000,
        10000000000000000,
        100000000000000000,
        1000000000000000000,
        10000000000000000000
    ];

    /// <summary>
    /// <c>ceil(2^(64 + shift) / 5^p)</c> for each power. Power zero divides by one, which the
    /// callers never ask for.
    /// </summary>
    private static ReadOnlySpan<ulong> Multipliers =>
    [
        0,
        0xCCCCCCCCCCCCCCCD,
        0xA3D70A3D70A3D70B,
        0x83126E978D4FDF3C,
        0xD1B71758E219652C,
        0xA7C5AC471B478424,
        0x8637BD05AF6C69B6,
        0xD6BF94D5E57A42BD,
        0xABCC77118461CEFD,
        0x89705F4136B4A598,
        0xDBE6FECEBDEDD5BF,
        0xAFEBFF0BCB24AAFF,
        0x8CBCCC096F5088CC,
        0xE12E13424BB40E14,
        0xB424DC35095CD810,
        0x901D7CF73AB0ACDA,
        0xE69594BEC44DE15C,
        0xB877AA3236A4B44A,
        0x9392EE8E921D5D08,
        0xEC1E4A7DB69561A6
    ];

    /// <summary><c>floor(log2 5^p)</c>: what the high half of the product is shifted by.</summary>
    private static ReadOnlySpan<byte> Shifts =>
    [
        0, 2, 4, 6, 9, 11, 13, 16, 18, 20, 23, 25, 27, 30, 32, 34, 37, 39, 41, 44
    ];

    /// <summary>Ten to <paramref name="power"/>, which must be at most <see cref="MaxPower"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong PowerOfTen(int power)
    {
        return Unsafe.Add(ref MemoryMarshal.GetReference(Powers), power);
    }

    /// <summary>The multiplier for a power, so a test can re-derive it.</summary>
    public static ulong Multiplier(int power)
    {
        return Multipliers[power];
    }

    /// <summary>The post-shift for a power, so a test can re-derive it.</summary>
    public static int Shift(int power)
    {
        return Shifts[power];
    }

    /// <summary>Half of ten to <paramref name="power"/>, the point a discarded part is judged against.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong HalfPowerOfTen(int power)
    {
        return PowerOfTen(power) >> 1;
    }

    /// <summary>
    /// Divides by ten to <paramref name="power"/>, which must be between 1 and
    /// <see cref="MaxPower"/>, handing back the remainder as well.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong DivRemPowerOfTen(ulong value, int power, out ulong remainder)
    {
        var shifted = value >> power;
        var multiplier = Unsafe.Add(ref MemoryMarshal.GetReference(Multipliers), power);
        var shift = Unsafe.Add(ref MemoryMarshal.GetReference(Shifts), power);
        var quotient = Math.BigMul(shifted, multiplier, out _) >> shift;
        remainder = value - (quotient * PowerOfTen(power));
        return quotient;
    }

    /// <summary>
    /// How many digits a value is written with. Zero counts as one.
    /// </summary>
    /// <remarks>
    /// The bit length gives the count to within one, since a bit is worth log10(2) of a
    /// digit, and one comparison against a power of ten settles it. Or-ing in a one is what
    /// makes zero read as one digit without a branch of its own.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CountDigits(ulong value)
    {
        var nonZero = value | 1;
        var bitLength = 64 - BitOperations.LeadingZeroCount(nonZero);
        var digits = ((bitLength * 1233) >> 12) + 1;

        // The estimate is one too many for values below the power it names, which is a
        // coin flip on mixed values and so is settled by a compare rather than a branch.
        return digits - Unsafe.BitCast<bool, byte>(nonZero < PowerOfTen(digits - 1));
    }
}
