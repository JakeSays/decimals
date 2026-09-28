// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// The constants the elementary functions carry rather than iterate for, and the one
/// question about a value that is decimal rather than arithmetic.
/// </summary>
internal static unsafe class Decimal32WideConstants
{
    /// <summary>
    /// ln(10) to forty digits, as base-billion units least significant first. Log10 divides
    /// by this every time it runs, and decNumber carries it for the same reason.
    /// </summary>
    private static ReadOnlySpan<uint> NaturalLogOfTenUnits =>
    [
        364207601, 991454684, 45684017, 585092994, 2302
    ];

    /// <summary>ln(2) to forty digits, carried for the same reason.</summary>
    private static ReadOnlySpan<uint> NaturalLogOfTwoUnits =>
    [
        765680755, 321214581, 453094172, 471805599, 6931
    ];

    /// <summary>ln(10), laid into a slot the caller owns.</summary>
    public static Decimal32WideNumber NaturalLogOfTen(uint* lsu)
    {
        return Load(NaturalLogOfTenUnits, -39, lsu);
    }

    /// <summary>ln(2), laid into a slot the caller owns.</summary>
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
    /// Whether the value is a power of two, and which.
    /// </summary>
    /// <remarks>
    /// A decimal value is a coefficient times a power of ten, and ten is two times five. A
    /// positive exponent therefore leaves a factor of five that no power of two has, so
    /// only a zero or negative exponent can qualify: the coefficient has to carry exactly
    /// as many fives as the exponent has, and what remains has to be a power of two.
    /// </remarks>
    public static bool TryPowerOfTwoExponent(Decimal32WideNumber value, uint* work, out int exponent)
    {
        exponent = 0;

        if (!value.IsFinite || value.IsNegative || value.IsZero || value.Exponent > 0)
        {
            return false;
        }

        var length = value.Units;
        for (var index = 0; index < length; index++)
        {
            work[index] = value.Lsu[index];
        }

        // Take out one factor of five for each decade below the point; a remainder at any
        // step means the value keeps a five, which no power of two does.
        var fives = -value.Exponent;
        for (var index = 0; index < fives; index++)
        {
            if (DivideBySmall(work, ref length, 5) != 0)
            {
                return false;
            }
        }

        // What is left has to be a power of two, and how many times it halves is the
        // exponent, less the twos the denominator held.
        var twos = 0;
        while (true)
        {
            if (length == 1 && work[0] == 1)
            {
                exponent = twos - fives;
                return true;
            }

            if (DivideBySmall(work, ref length, 2) != 0)
            {
                return false;
            }

            twos++;
        }
    }

    /// <summary>
    /// Divides a unit array by a small divisor in place, handing back the remainder.
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
