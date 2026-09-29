// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Decimals.Internal;

/// <summary>
/// Powers of ten, the reciprocals used to divide by them, and the digit count. Every
/// operation uses these, so each takes only a few instructions and no lookup has a bounds
/// check.
/// </summary>
/// <remarks>
/// <para>
/// Every table here is a span over the assembly's data section. Nothing is allocated, and
/// no static constructor runs before a lookup.
/// </para>
/// <para>
/// Division by a power of ten is a multiply and two shifts. 10^p is 2^p times 5^p, and
/// <c>floor(floor(n / a) / b)</c> equals <c>floor(n / ab)</c>. So the division is a right
/// shift by p followed by a division by 5^p. The shift removes p bits from the numerator
/// and about 2.32p bits from the divisor, which leaves room for a 64-bit multiplier. For
/// the division by d = 5^p, let <c>s = 64 + floor(log2 d)</c>, <c>M = ceil(2^s / d)</c>,
/// and <c>e = M*d - 2^s</c>. The quotient <c>n*M / 2^s</c> is exact for every numerator up
/// to <c>n_max</c> if <c>n_max * e &lt; 2^s</c>. With the pre-shift, this holds for the
/// whole 64-bit range at every power: e is below d, d is below 2^(s-63), and the shifted
/// numerator is below 2^(64-p). So no correction step is needed. The constants were
/// generated, and <c>Decimal32TableTests</c> recomputes them.
/// </para>
/// <para>
/// The tables go up to 10^19 even though the coefficient has only 7 digits, because
/// intermediate results are wider: a product has up to 14 digits, and an aligned operand
/// can fill a 64-bit word.
/// </para>
/// </remarks>
internal static class Decimal32Tables
{
    /// <summary>The largest power of ten that fits in a <see cref="ulong"/>.</summary>
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
    /// <c>ceil(2^(64 + shift) / 5^p)</c> for each power. Power zero would divide by one,
    /// which no caller does.
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

    /// <summary><c>floor(log2 5^p)</c>: the right shift applied to the high half of the product.</summary>
    private static ReadOnlySpan<byte> Shifts =>
    [
        0, 2, 4, 6, 9, 11, 13, 16, 18, 20, 23, 25, 27, 30, 32, 34, 37, 39, 41, 44
    ];

    /// <summary>10 to the power <paramref name="power"/>, which must be at most <see cref="MaxPower"/>.</summary>
    /// <param name="power">The power of ten, from 0 to <see cref="MaxPower"/>. It is not range-checked.</param>
    /// <returns>10^<paramref name="power"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong PowerOfTen(int power)
    {
        return Unsafe.Add(ref MemoryMarshal.GetReference(Powers), power);
    }

    /// <summary>The multiplier for a power. Tests use it to check the table.</summary>
    /// <param name="power">The power of ten, from 0 to <see cref="MaxPower"/>.</param>
    /// <returns><c>ceil(2^(64 + shift) / 5^power)</c>, or 0 for power zero.</returns>
    public static ulong Multiplier(int power)
    {
        return Multipliers[power];
    }

    /// <summary>The post-shift for a power. Tests use it to check the table.</summary>
    /// <param name="power">The power of ten, from 0 to <see cref="MaxPower"/>.</param>
    /// <returns><c>floor(log2 5^power)</c>.</returns>
    public static int Shift(int power)
    {
        return Shifts[power];
    }

    /// <summary>Half of 10 to the power <paramref name="power"/>, used to classify a discarded part.</summary>
    /// <param name="power">The power of ten, from 0 to <see cref="MaxPower"/>. It is not range-checked.</param>
    /// <returns>10^<paramref name="power"/> / 2, which is 0 for power zero.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong HalfPowerOfTen(int power)
    {
        return PowerOfTen(power) >> 1;
    }

    /// <summary>
    /// Divides by 10 to the power <paramref name="power"/> and returns the remainder too.
    /// The power must be between 1 and <see cref="MaxPower"/>.
    /// </summary>
    /// <param name="value">The dividend.</param>
    /// <param name="power">The power of ten to divide by, from 1 to <see cref="MaxPower"/>. It is not range-checked.</param>
    /// <param name="remainder">Receives the remainder.</param>
    /// <returns>The quotient.</returns>
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
    /// The number of decimal digits in a value. Zero has one digit.
    /// </summary>
    /// <remarks>
    /// The bit length gives the digit count to within one, because each bit is worth
    /// log10(2) of a digit. One comparison with a power of ten gives the exact count. OR-ing
    /// in a 1 makes zero count as one digit without a separate branch.
    /// </remarks>
    /// <param name="value">The value to measure.</param>
    /// <returns>The number of decimal digits, from 1 to 20.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CountDigits(ulong value)
    {
        var nonZero = value | 1;
        var bitLength = 64 - BitOperations.LeadingZeroCount(nonZero);
        var digits = ((bitLength * 1233) >> 12) + 1;

        // The estimate is one too high for values below the power it names. That is random
        // for mixed values, so it is corrected with a comparison instead of a branch.
        return digits - Unsafe.BitCast<bool, byte>(nonZero < PowerOfTen(digits - 1));
    }
}
