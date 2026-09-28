// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// The unit-array primitives the engine is built from, following decNumber's
/// <c>decUnitAddSub</c>, <c>decShiftToMost</c>, and <c>decShiftToLeast</c>.
/// </summary>
/// <remarks>
/// A unit holds nine decimal digits, so the two things decimal arithmetic does constantly
/// are cheap: scaling by a power of ten nine at a time is moving units, and scaling by less
/// is one multiply within a unit. There is no binary coefficient anywhere, and nothing here
/// is wider than 64 bits.
/// </remarks>
internal static unsafe class Decimal128WideUnits
{
    /// <summary>The largest value a unit holds.</summary>
    private const long UnitMax = Decimal128WideNumber.UnitBase - 1;

    /// <summary>Powers of ten inside one unit, for the sub-unit part of a shift.</summary>
    private static ReadOnlySpan<uint> UnitPowers =>
    [
        1, 10, 100, 1000, 10000, 100000, 1000000, 10000000, 100000000, 1000000000
    ];

    /// <summary>
    /// Adds <paramref name="b"/>, multiplied by <paramref name="multiplier"/> and shifted
    /// up by <paramref name="shift"/> units, to <paramref name="a"/>, writing the result at
    /// <paramref name="c"/>.
    /// </summary>
    /// <remarks>
    /// This is decNumber's <c>decUnitAddSub</c>, and it does both addition and subtraction:
    /// a multiplier of -1 subtracts. The carry is signed and runs wider than a unit, so one
    /// loop covers both directions and a borrow out of the top is complemented at the end.
    /// </remarks>
    /// <returns>
    /// Units written, negated when the result came out negative and was complemented.
    /// </returns>
    public static int AddSub(uint* a, int aLength, uint* b, int bLength, int shift, uint* c,
        int multiplier)
    {
        // The two starts are not the same buffer: one bounds reads from A, the other
        // measures how much of C has been written.
        var aStart = a;
        var start = c;
        var maximum = c + aLength;
        var minimum = c + bLength + shift;

        if (shift != 0)
        {
            // The low units of A sit below where B begins, so they carry straight across.
            if (a == c && shift <= aLength)
            {
                c += shift;
                a += shift;
            }
            else
            {
                for (; c < start + shift; a++, c++)
                {
                    *c = a < aStart + aLength ? *a : 0;
                }
            }
        }

        if (minimum > maximum)
        {
            var held = minimum;
            minimum = maximum;
            maximum = held;
        }

        var carry = 0L;

        // Two loops: the first where both contribute, the second where only one is left.
        // The carry handling is the same in each.
        for (; c < minimum; c++)
        {
            carry += *a;
            a++;
            carry += (long)(*b) * multiplier;
            b++;
            carry = PlaceUnit(carry, c);
        }

        for (; c < maximum; c++)
        {
            if (a < aStart + aLength)
            {
                carry += *a;
                a++;
            }
            else
            {
                carry += (long)(*b) * multiplier;
                b++;
            }

            carry = PlaceUnit(carry, c);
        }

        if (carry == 0)
        {
            return (int)(c - start);
        }

        if (carry > 0)
        {
            *c = (uint)carry;
            c++;
            return (int)(c - start);
        }

        // A borrow out of the top: the result is negative, and the units written so far are
        // its complement rather than its magnitude.
        var add = 1L;
        for (c = start; c < maximum; c++)
        {
            add = UnitMax + add - *c;
            if (add <= UnitMax)
            {
                *c = (uint)add;
                add = 0;
            }
            else
            {
                *c = 0;
                add = 1;
            }
        }

        if (add - carry - 1 != 0)
        {
            *c = (uint)(add - carry - 1);
            c++;
        }

        return (int)(start - c);
    }

    /// <summary>
    /// Splits a signed running total into the unit it writes and the carry it leaves. The
    /// remainder operator is undefined for a negative left operand, so the negative case is
    /// lifted into range first.
    /// </summary>
    private static long PlaceUnit(long carry, uint* destination)
    {
        if ((ulong)carry <= UnitMax)
        {
            *destination = (uint)carry;
            return 0;
        }

        if ((ulong)carry < (ulong)(UnitMax + 1) * 2)
        {
            // A carry of exactly one, which is what most additions leave.
            *destination = (uint)(carry - (UnitMax + 1));
            return 1;
        }

        if (carry >= 0)
        {
            *destination = (uint)(carry % (UnitMax + 1));
            return carry / (UnitMax + 1);
        }

        carry += (UnitMax + 1) * (UnitMax + 1);
        *destination = (uint)(carry % (UnitMax + 1));
        return (carry / (UnitMax + 1)) - (UnitMax + 1);
    }

    /// <summary>
    /// Scales a coefficient up by <paramref name="places"/> decimal digits in place, which
    /// is decNumber's <c>decShiftToMost</c>. Whole units move; what is left over is one
    /// multiply and a carry.
    /// </summary>
    /// <returns>Units in use afterwards.</returns>
    public static int ShiftUp(uint* units, int length, int places)
    {
        if (places == 0)
        {
            return length;
        }

        var unitShift = places / Decimal128WideNumber.DigitsPerUnit;
        var digitShift = places % Decimal128WideNumber.DigitsPerUnit;

        if (digitShift != 0)
        {
            // Every unit takes the low digits of the one below it.
            var multiplier = UnitPowers[digitShift];
            var divisor = UnitPowers[Decimal128WideNumber.DigitsPerUnit - digitShift];
            var carry = 0u;

            for (var index = 0; index < length; index++)
            {
                var value = units[index];
                units[index] = ((value % divisor) * multiplier) + carry;
                carry = value / divisor;
            }

            if (carry != 0)
            {
                units[length] = carry;
                length++;
            }
        }

        if (unitShift != 0)
        {
            for (var index = length - 1; index >= 0; index--)
            {
                units[index + unitShift] = units[index];
            }

            for (var index = 0; index < unitShift; index++)
            {
                units[index] = 0;
            }

            length += unitShift;
        }

        while (length > 1 && units[length - 1] == 0)
        {
            length--;
        }

        return length;
    }

    /// <summary>
    /// Scales a coefficient down by <paramref name="places"/> decimal digits in place,
    /// discarding what falls off. decNumber's <c>decShiftToLeast</c>.
    /// </summary>
    /// <returns>Units in use afterwards.</returns>
    public static int ShiftDown(uint* units, int length, int places)
    {
        if (places <= 0)
        {
            return length;
        }

        var unitShift = places / Decimal128WideNumber.DigitsPerUnit;
        var digitShift = places % Decimal128WideNumber.DigitsPerUnit;

        if (unitShift >= length)
        {
            *units = 0;
            return 1;
        }

        if (unitShift > 0)
        {
            for (var index = 0; index + unitShift < length; index++)
            {
                units[index] = units[index + unitShift];
            }

            length -= unitShift;
        }

        if (digitShift != 0)
        {
            // Each unit keeps its high digits and takes the low digits of the one above.
            var divisor = UnitPowers[digitShift];
            var multiplier = UnitPowers[Decimal128WideNumber.DigitsPerUnit - digitShift];
            var carry = 0u;

            for (var index = length - 1; index >= 0; index--)
            {
                var value = units[index];
                units[index] = (value / divisor) + (carry * multiplier);
                carry = value % divisor;
            }
        }

        while (length > 1 && units[length - 1] == 0)
        {
            length--;
        }

        return length;
    }

    /// <summary>
    /// The decimal digit at a position counted from the least significant end.
    /// </summary>
    public static uint DigitAt(uint* units, int length, int position)
    {
        var unit = position / Decimal128WideNumber.DigitsPerUnit;
        if (unit >= length)
        {
            return 0;
        }

        return (units[unit] / UnitPowers[position % Decimal128WideNumber.DigitsPerUnit]) % 10;
    }

    /// <summary>
    /// Whether any digit below <paramref name="position"/> is non-zero, which is what makes
    /// a shortening inexact beyond its guard digit.
    /// </summary>
    public static bool AnyBelow(uint* units, int length, int position)
    {
        if (position <= 0)
        {
            return false;
        }

        var unit = position / Decimal128WideNumber.DigitsPerUnit;
        var within = position % Decimal128WideNumber.DigitsPerUnit;

        for (var index = 0; index < unit && index < length; index++)
        {
            if (units[index] != 0)
            {
                return true;
            }
        }

        if (within == 0 || unit >= length)
        {
            return false;
        }

        return units[unit] % UnitPowers[within] != 0;
    }

    /// <summary>
    /// Doubles a coefficient in place, which is how a remainder is placed against a divisor
    /// to see which side of half it falls on.
    /// </summary>
    /// <returns>Units in use afterwards.</returns>
    public static int Double(uint* units, int length)
    {
        var carry = 0u;

        for (var index = 0; index < length; index++)
        {
            var value = ((ulong)units[index] * 2) + carry;
            units[index] = (uint)(value % Decimal128WideNumber.UnitBase);
            carry = (uint)(value / Decimal128WideNumber.UnitBase);
        }

        if (carry == 0)
        {
            return length;
        }

        units[length] = carry;
        return length + 1;
    }

    /// <summary>Halves a coefficient in place, discarding any odd remainder.</summary>
    /// <returns>Units in use afterwards.</returns>
    public static int Halve(uint* units, int length)
    {
        var carry = 0u;

        for (var index = length - 1; index >= 0; index--)
        {
            var value = ((ulong)carry * Decimal128WideNumber.UnitBase) + units[index];
            units[index] = (uint)(value / 2);
            carry = (uint)(value % 2);
        }

        while (length > 1 && units[length - 1] == 0)
        {
            length--;
        }

        return length;
    }

    /// <summary>
    /// Multiplies a coefficient by a value that fits one unit, which is how a power of two
    /// or five is built up without ever forming a wide intermediate.
    /// </summary>
    /// <returns>Units in use afterwards.</returns>
    public static int MultiplyBySmall(uint* units, int length, uint multiplier)
    {
        var carry = 0UL;

        for (var index = 0; index < length; index++)
        {
            var value = ((ulong)units[index] * multiplier) + carry;
            units[index] = (uint)(value % Decimal128WideNumber.UnitBase);
            carry = value / Decimal128WideNumber.UnitBase;
        }

        while (carry != 0)
        {
            units[length] = (uint)(carry % Decimal128WideNumber.UnitBase);
            carry /= Decimal128WideNumber.UnitBase;
            length++;
        }

        return length;
    }

    /// <summary>Adds one to a coefficient.</summary>
    /// <returns>Units in use afterwards, which grows when the carry runs off the top.</returns>
    public static int Increment(uint* units, int length)
    {
        for (var index = 0; index < length; index++)
        {
            units[index]++;
            if (units[index] < Decimal128WideNumber.UnitBase)
            {
                return length;
            }

            units[index] = 0;
        }

        units[length] = 1;
        return length + 1;
    }

    /// <summary>
    /// Whether the coefficient is a run of <paramref name="digits"/> nines, which is the
    /// case where rounding up lengthens it and the exponent has to move instead.
    /// </summary>
    public static bool IsAllNines(uint* units, int length, int digits)
    {
        var full = digits / Decimal128WideNumber.DigitsPerUnit;
        var remainder = digits % Decimal128WideNumber.DigitsPerUnit;

        if (length != (remainder == 0 ? full : full + 1))
        {
            return false;
        }

        for (var index = 0; index < full; index++)
        {
            if (units[index] != Decimal128WideNumber.UnitBase - 1)
            {
                return false;
            }
        }

        return remainder == 0 || units[full] == UnitPowers[remainder] - 1;
    }

    /// <summary>
    /// Whether the coefficient is exactly ten to the <paramref name="digits"/> less one
    /// power -- a one followed by zeros -- which is the mirror case, where rounding down
    /// shortens it.
    /// </summary>
    public static bool IsPowerOfTen(uint* units, int length, int digits)
    {
        var position = digits - 1;
        return DigitAt(units, length, position) == 1 && !AnyBelow(units, length, position);
    }

    /// <summary>Sets the coefficient to ten to the given power.</summary>
    /// <returns>Units in use.</returns>
    public static int SetPowerOfTen(uint* units, int power)
    {
        var length = Decimal128WideNumber.UnitsFor(power + 1);
        for (var index = 0; index < length; index++)
        {
            units[index] = 0;
        }

        units[power / Decimal128WideNumber.DigitsPerUnit] = UnitPowers[power % Decimal128WideNumber.DigitsPerUnit];
        return length;
    }

    /// <summary>Sets the coefficient to a run of <paramref name="digits"/> nines.</summary>
    /// <returns>Units in use.</returns>
    public static int SetNines(uint* units, int digits)
    {
        var full = digits / Decimal128WideNumber.DigitsPerUnit;
        var remainder = digits % Decimal128WideNumber.DigitsPerUnit;

        for (var index = 0; index < full; index++)
        {
            units[index] = Decimal128WideNumber.UnitBase - 1;
        }

        if (remainder == 0)
        {
            return full == 0 ? 1 : full;
        }

        units[full] = UnitPowers[remainder] - 1;
        return full + 1;
    }

    /// <summary>
    /// Compares two coefficients as magnitudes, ignoring exponents.
    /// </summary>
    public static int Compare(uint* a, int aLength, uint* b, int bLength)
    {
        while (aLength > 1 && a[aLength - 1] == 0)
        {
            aLength--;
        }

        while (bLength > 1 && b[bLength - 1] == 0)
        {
            bLength--;
        }

        if (aLength != bLength)
        {
            return aLength < bLength ? -1 : 1;
        }

        for (var index = aLength - 1; index >= 0; index--)
        {
            if (a[index] != b[index])
            {
                return a[index] < b[index] ? -1 : 1;
            }
        }

        return 0;
    }
}
