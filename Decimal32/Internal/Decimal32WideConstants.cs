// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// Constants the elementary functions store instead of computing, and a test for whether a
/// value is a power of two.
/// </summary>
internal static unsafe class Decimal32WideConstants
{
    /// <summary>
    /// ln(10) to 40 digits, as base-billion units, least significant first. Log10 divides by
    /// this on every call, and decNumber stores it for the same reason.
    /// </summary>
    private static ReadOnlySpan<uint> NaturalLogOfTenUnits =>
    [
        364207601, 991454684, 45684017, 585092994, 2302
    ];

    /// <summary>ln(2) to 40 digits, stored for the same reason.</summary>
    private static ReadOnlySpan<uint> NaturalLogOfTwoUnits =>
    [
        765680755, 321214581, 453094172, 471805599, 6931
    ];

    /// <summary>Loads ln(10) into a buffer the caller owns.</summary>
    /// <param name="lsu">The buffer for the coefficient's units. It must hold at least five units.</param>
    /// <returns>ln(10) to 40 digits, with its coefficient in <paramref name="lsu"/>.</returns>
    public static Decimal32WideNumber NaturalLogOfTen(uint* lsu)
    {
        return Load(NaturalLogOfTenUnits, -39, lsu);
    }

    /// <summary>Loads ln(2) into a buffer the caller owns.</summary>
    /// <param name="lsu">The buffer for the coefficient's units. It must hold at least five units.</param>
    /// <returns>ln(2) to 40 digits, with its coefficient in <paramref name="lsu"/>.</returns>
    public static Decimal32WideNumber NaturalLogOfTwo(uint* lsu)
    {
        return Load(NaturalLogOfTwoUnits, -40, lsu);
    }

    private static Decimal32WideNumber Load(ReadOnlySpan<uint> units, int exponent, uint* lsu)
    {
        var number = new Decimal32WideNumber(lsu);
        for (var index = 0; index < units.Length; index++)
        {
            lsu[index] = units[index];
        }

        number.Units = units.Length;
        number.Exponent = exponent;
        number.CountDigits();
        return number;
    }

    /// <summary>
    /// Whether the value is an exact power of two, and if so, which power.
    /// </summary>
    /// <remarks>
    /// A decimal value is a coefficient times a power of ten, and ten is two times five. A
    /// positive exponent leaves a factor of five that no power of two has, so only an
    /// exponent of zero or less can qualify. The coefficient must contain exactly as many
    /// factors of five as the exponent's magnitude, and the rest must be a power of two.
    /// </remarks>
    /// <param name="value">The value to test.</param>
    /// <param name="work">A buffer at least as long as the value's coefficient. Its contents are overwritten.</param>
    /// <returns>The power of two, or null if the value is not a positive power of two.</returns>
    public static int? PowerOfTwoExponent(Decimal32WideNumber value, uint* work)
    {
        if (!value.IsFinite || value.IsNegative || value.IsZero || value.Exponent > 0)
        {
            return null;
        }

        var length = value.Units;
        for (var index = 0; index < length; index++)
        {
            work[index] = value.Lsu[index];
        }

        // Divide out one factor of five for each digit below the decimal point. A remainder
        // at any step means the value has a factor of five, which no power of two has.
        var fives = -value.Exponent;
        for (var index = 0; index < fives; index++)
        {
            if (DivideBySmall(work, ref length, 5) != 0)
            {
                return null;
            }
        }

        // The rest must be a power of two. The number of halvings, minus the factors of two
        // in the denominator, is the exponent.
        var twos = 0;
        while (true)
        {
            if (length == 1 && work[0] == 1)
            {
                return twos - fives;
            }

            if (DivideBySmall(work, ref length, 2) != 0)
            {
                return null;
            }

            twos++;
        }
    }

    /// <summary>
    /// Divides a unit array by a small divisor in place, and returns the remainder.
    /// </summary>
    private static uint DivideBySmall(uint* units, ref int length, uint divisor)
    {
        var carry = 0UL;

        for (var index = length - 1; index >= 0; index--)
        {
            var value = (carry * Decimal32WideNumber.UnitBase) + units[index];
            units[index] = (uint)(value / divisor);
            carry = value % divisor;
        }

        while (length > 1 && units[length - 1] == 0)
        {
            length--;
        }

        return (uint)carry;
    }
}
