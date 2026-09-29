// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// The unit-array operations that wide numbers are built on, following decNumber's
/// <c>decUnitAddSub</c>, <c>decShiftToMost</c>, and <c>decShiftToLeast</c>.
/// </summary>
/// <remarks>
/// A unit holds nine decimal digits, so the two most common decimal operations are cheap.
/// Scaling by a multiple of nine digits moves whole units, and scaling by fewer digits is
/// one multiply per unit. No coefficient is ever converted to binary, and nothing here is
/// wider than 64 bits.
/// </remarks>
internal static unsafe class Decimal128WideUnits
{
    /// <summary>The largest value a unit holds.</summary>
    private const long UnitMax = Decimal128WideNumber.UnitBase - 1;

    /// <summary>The powers of ten that fit in one unit, for the part of a shift smaller than a unit.</summary>
    private static ReadOnlySpan<uint> UnitPowers =>
    [
        1, 10, 100, 1000, 10000, 100000, 1000000, 10000000, 100000000, 1000000000
    ];

    /// <summary>
    /// Adds <paramref name="b"/>, multiplied by <paramref name="multiplier"/> and shifted
    /// up by <paramref name="shift"/> units, to <paramref name="a"/>, and writes the result
    /// to <paramref name="c"/>.
    /// </summary>
    /// <remarks>
    /// This is decNumber's <c>decUnitAddSub</c>. It does both addition and subtraction: a
    /// multiplier of -1 subtracts. The carry is signed and can exceed one unit, so one loop
    /// handles both directions. If a borrow comes out of the top, the result is complemented
    /// at the end.
    /// </remarks>
    /// <param name="a">The first operand's units, least significant first.</param>
    /// <param name="aLength">The number of units in <paramref name="a"/>.</param>
    /// <param name="b">The second operand's units, least significant first.</param>
    /// <param name="bLength">The number of units in <paramref name="b"/>.</param>
    /// <param name="shift">The number of units to shift <paramref name="b"/> up before adding.</param>
    /// <param name="c">Receives the result's magnitude. It can be the same buffer as <paramref name="a"/>.</param>
    /// <param name="multiplier">The multiplier for <paramref name="b"/>: 1 adds, -1 subtracts.</param>
    /// <returns>
    /// The number of units written. It is negated when the result was negative and
    /// <paramref name="c"/> holds its magnitude.
    /// </returns>
    public static int AddSub(uint* a, int aLength, uint* b, int bLength, int shift, uint* c,
        int multiplier)
    {
        // These two starting pointers are different: one limits reads from a, and the other
        // measures how much of c has been written.
        var aStart = a;
        var start = c;
        var maximum = c + aLength;
        var minimum = c + bLength + shift;

        if (shift != 0)
        {
            // The low units of a are below the start of b, so they are copied unchanged.
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

        // Two loops: the first covers units where both operands contribute, and the second
        // covers units where only one does. The carry handling is the same in each.
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

        // A borrow came out of the top. The result is negative, and the units written so far
        // are its complement, not its magnitude.
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
    /// Splits a signed running total into the unit to write and the carry to keep. The
    /// remainder of a negative number has the wrong sign for this, so a negative total is
    /// first raised into the non-negative range.
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
            // A carry of exactly one, which is the most common case in addition.
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
    /// Multiplies a coefficient by 10 to the power <paramref name="places"/>, in place:
    /// decNumber's <c>decShiftToMost</c>. Whole units are moved, and the remaining digits
    /// take one multiply and a carry per unit.
    /// </summary>
    /// <param name="units">The coefficient's units. The buffer must have room for the longer result.</param>
    /// <param name="length">The number of units in use.</param>
    /// <param name="places">The number of decimal digits to shift by.</param>
    /// <returns>The number of units in use afterward.</returns>
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
            // Each unit takes the high digits of the unit below it as its low digits.
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
    /// Divides a coefficient by 10 to the power <paramref name="places"/>, in place, and
    /// discards the remainder: decNumber's <c>decShiftToLeast</c>.
    /// </summary>
    /// <param name="units">The coefficient's units.</param>
    /// <param name="length">The number of units in use.</param>
    /// <param name="places">The number of decimal digits to shift by.</param>
    /// <returns>The number of units in use afterward.</returns>
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
            // Each unit keeps its high digits and takes the low digits of the unit above it.
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
    /// <param name="units">The coefficient's units.</param>
    /// <param name="length">The number of units in use.</param>
    /// <param name="position">The digit position. 0 is the least significant digit.</param>
    /// <returns>The digit, or 0 if the position is beyond the coefficient.</returns>
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
    /// Whether any digit below <paramref name="position"/> is non-zero. When shortening, this
    /// shows whether anything non-zero is below the guard digit.
    /// </summary>
    /// <param name="units">The coefficient's units.</param>
    /// <param name="length">The number of units in use.</param>
    /// <param name="position">The digit position. Digits 0 through <paramref name="position"/> - 1 are checked.</param>
    /// <returns>True if any of those digits is non-zero.</returns>
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
    /// Doubles a coefficient in place. Division uses this to compare a remainder with half
    /// the divisor.
    /// </summary>
    /// <param name="units">The coefficient's units. The buffer must have room for one more unit.</param>
    /// <param name="length">The number of units in use.</param>
    /// <returns>The number of units in use afterward.</returns>
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

    /// <summary>Halves a coefficient in place and discards the remainder.</summary>
    /// <param name="units">The coefficient's units.</param>
    /// <param name="length">The number of units in use.</param>
    /// <returns>The number of units in use afterward.</returns>
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
    /// Multiplies a coefficient in place by a value that fits in one unit. Powers of two and
    /// five are applied this way, without computing a wide intermediate value.
    /// </summary>
    /// <param name="units">The coefficient's units. The buffer must have room for the longer result.</param>
    /// <param name="length">The number of units in use.</param>
    /// <param name="multiplier">The multiplier, below one billion.</param>
    /// <returns>The number of units in use afterward.</returns>
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

    /// <summary>Adds one to a coefficient in place.</summary>
    /// <param name="units">The coefficient's units. The buffer must have room for one more unit.</param>
    /// <param name="length">The number of units in use.</param>
    /// <returns>The number of units in use afterward. It grows by one when the carry passes the top unit.</returns>
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
    /// Whether the coefficient is exactly <paramref name="digits"/> nines. Rounding such a
    /// coefficient up would add a digit, so the exponent changes instead.
    /// </summary>
    /// <param name="units">The coefficient's units.</param>
    /// <param name="length">The number of units in use.</param>
    /// <param name="digits">The number of digits in the coefficient.</param>
    /// <returns>True if every digit is 9.</returns>
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
    /// Whether the coefficient is exactly 10 to the power (<paramref name="digits"/> - 1),
    /// that is, 1 followed by zeros. Rounding such a coefficient down would remove a digit,
    /// so the exponent changes instead.
    /// </summary>
    /// <param name="units">The coefficient's units.</param>
    /// <param name="length">The number of units in use.</param>
    /// <param name="digits">The number of digits in the coefficient.</param>
    /// <returns>True if the coefficient is 1 followed by zeros.</returns>
    public static bool IsPowerOfTen(uint* units, int length, int digits)
    {
        var position = digits - 1;
        return DigitAt(units, length, position) == 1 && !AnyBelow(units, length, position);
    }

    /// <summary>Sets the coefficient to 10 to the given power.</summary>
    /// <param name="units">Receives the coefficient's units.</param>
    /// <param name="power">The power of ten.</param>
    /// <returns>The number of units in use.</returns>
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

    /// <summary>Sets the coefficient to <paramref name="digits"/> nines.</summary>
    /// <param name="units">Receives the coefficient's units.</param>
    /// <param name="digits">The number of nines.</param>
    /// <returns>The number of units in use.</returns>
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
    /// Compares two coefficients as integers, ignoring exponents.
    /// </summary>
    /// <param name="a">The first coefficient's units.</param>
    /// <param name="aLength">The number of units in <paramref name="a"/>.</param>
    /// <param name="b">The second coefficient's units.</param>
    /// <param name="bLength">The number of units in <paramref name="b"/>.</param>
    /// <returns>-1 if <paramref name="a"/> is smaller, 0 if they are equal, or 1 if it is larger.</returns>
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
