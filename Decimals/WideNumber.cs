// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// An arbitrary-precision decimal as the elementary functions work on it: the coefficient
/// in base-billion units, least significant first, beside a sign and an exponent. This is
/// decNumber's <c>decNumber</c>.
/// </summary>
/// <remarks>
/// <para>
/// decNumber holds its coefficient in units of <c>DECDPUN</c> digits -- three by default,
/// nine at its widest setting -- rather than as a binary integer, because every operation
/// on it is decimal: rounding to a digit count, scaling by a power of ten, counting digits.
/// Nine digits is the widest unit a 32-bit word holds, and a product of two of them fits 64
/// bits, which is what makes the multiply and divide loops work.
/// </para>
/// <para>
/// The units live in a buffer the caller owns, as everywhere else here. <see cref="Digits"/>
/// is carried rather than derived, the way decNumber carries it, because almost every
/// operation needs it and recomputing costs a scan.
/// </para>
/// </remarks>
internal unsafe struct WideNumber
{
    /// <summary>Digits in one unit.</summary>
    public const int DigitsPerUnit = 9;

    /// <summary>The base each unit counts in.</summary>
    public const uint UnitBase = 1000000000;

    /// <summary>The least significant unit.</summary>
    public uint* Lsu;

    /// <summary>Units in use, which is always enough to hold <see cref="Digits"/>.</summary>
    public int Units;

    /// <summary>Digits in the coefficient. A zero has one.</summary>
    public int Digits;

    public int Exponent;

    public bool IsNegative;

    public DecimalKind Kind;

    public WideNumber(uint* lsu)
    {
        Lsu = lsu;
        Units = 1;
        Digits = 1;
        Exponent = 0;
        IsNegative = false;
        Kind = DecimalKind.Finite;
        *lsu = 0;
    }

    public readonly bool IsFinite => Kind == DecimalKind.Finite;

    public readonly bool IsNaN => Kind is DecimalKind.QuietNaN or DecimalKind.SignalingNaN;

    public readonly bool IsInfinity => Kind == DecimalKind.Infinity;

    /// <summary>Where the value's leading digit sits.</summary>
    public readonly int AdjustedExponent => Exponent + Digits - 1;

    public readonly bool IsZero
    {
        get
        {
            if (Kind != DecimalKind.Finite)
            {
                return false;
            }

            for (var index = 0; index < Units; index++)
            {
                if (Lsu[index] != 0)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Reads a format-width value into units. The coefficient is taken nine digits at a
    /// time from the low end, which is where a unit boundary falls whatever its length.
    /// </summary>
    public static WideNumber FromUnpacked(UnpackedDecimal<UInt128> value, uint* lsu)
    {
        var number = new WideNumber(lsu);
        number.Kind = value.Kind;
        number.IsNegative = value.IsNegative;
        number.Exponent = value.Exponent;

        var remaining = value.Coefficient;
        var length = 0;

        do
        {
            lsu[length] = (uint)(remaining % UnitBase);
            remaining /= UnitBase;
            length++;
        }
        while (remaining != UInt128.Zero);

        number.Units = length;
        number.CountDigits();
        return number;
    }

    /// <summary>
    /// Back to the width-bounded form. The caller is responsible for having finalized the
    /// value into a format first, since only then is the coefficient known to fit.
    /// </summary>
    public readonly UnpackedDecimal<UInt128> ToUnpacked()
    {
        var coefficient = UInt128.Zero;
        for (var index = Units - 1; index >= 0; index--)
        {
            coefficient = (coefficient * UnitBase) + Lsu[index];
        }

        return new UnpackedDecimal<UInt128>(Kind, IsNegative, Exponent, coefficient);
    }

    /// <summary>A small signed integer, which is what the series constants arrive as.</summary>
    public static WideNumber FromInt32(int value, uint* lsu)
    {
        var number = new WideNumber(lsu);
        number.IsNegative = value < 0;

        var magnitude = (ulong)Math.Abs((long)value);
        var length = 0;

        do
        {
            lsu[length] = (uint)(magnitude % UnitBase);
            magnitude /= UnitBase;
            length++;
        }
        while (magnitude != 0);

        number.Units = length;
        number.CountDigits();
        return number;
    }

    /// <summary>Ten to a given power, which the series use to place a value's magnitude.</summary>
    public static WideNumber PowerOfTen(int power, uint* lsu)
    {
        var number = new WideNumber(lsu);
        number.Units = WideUnits.SetPowerOfTen(lsu, 0);
        number.Exponent = power;
        number.Digits = 1;
        return number;
    }

    /// <summary>Whether the value is a whole number.</summary>
    public readonly bool IsIntegerValued
    {
        get
        {
            if (!IsFinite)
            {
                return false;
            }

            if (IsZero || Exponent >= 0)
            {
                return true;
            }

            // Whole exactly when the digits below the point are all zeros.
            return !WideUnits.AnyBelow(Lsu, Units, -Exponent);
        }
    }

    /// <summary>Whether the value is a whole number and that number is odd.</summary>
    public readonly bool IsOddIntegerValued
    {
        get
        {
            if (!IsIntegerValued || IsZero)
            {
                return false;
            }

            // A positive exponent means trailing zeros, so the integer ends in one.
            if (Exponent > 0)
            {
                return false;
            }

            // The last integral digit is the one just above the point.
            return WideUnits.DigitAt(Lsu, Units, -Exponent) % 2 != 0;
        }
    }

    /// <summary>
    /// The value as a machine integer, when it is a whole number small enough to be one.
    /// </summary>
    public readonly bool TryGetInt32(out int value)
    {
        value = 0;

        if (!IsIntegerValued)
        {
            return false;
        }

        if (IsZero)
        {
            return true;
        }

        // Beyond eleven integral digits the magnitude is past the window regardless.
        if (Digits + Exponent > 11)
        {
            return false;
        }

        // The integral part, read a digit at a time from the point upward. A positive
        // exponent puts the point below the coefficient entirely, so the walk stops at its
        // last digit and the zeros are appended after. Eleven digits fit a machine word
        // many times over, so nothing here can overflow.
        var lowest = Exponent < 0 ? -Exponent : 0;
        var integral = 0L;

        for (var position = Digits - 1; position >= lowest; position--)
        {
            integral = (integral * 10) + WideUnits.DigitAt(Lsu, Units, position);
        }

        for (var index = 0; index < Exponent; index++)
        {
            integral *= 10;
        }

        var limit = IsNegative ? 1999999997L : 999999999L;
        if (integral > limit)
        {
            return false;
        }

        value = (int)(IsNegative ? -integral : integral);
        return true;
    }

    /// <summary>Flips the sign.</summary>
    public void Negate()
    {
        IsNegative = !IsNegative;
    }

    /// <summary>Units needed to hold a given number of digits.</summary>
    public static int UnitsFor(int digits)
    {
        return ((digits + DigitsPerUnit) - 1) / DigitsPerUnit;
    }

    /// <summary>
    /// Recomputes the digit count from the units, which is what every operation that
    /// changes the coefficient has to leave correct. decNumber's <c>decGetDigits</c>.
    /// </summary>
    public void CountDigits()
    {
        var top = Units - 1;
        while (top > 0 && Lsu[top] == 0)
        {
            top--;
        }

        Units = top + 1;

        var leading = Lsu[top];
        if (leading == 0)
        {
            // Nothing but zeros, which is a zero and counts as one digit.
            Digits = 1;
            return;
        }

        Digits = (top * DigitsPerUnit) + DigitsInUnit(leading);
    }

    /// <summary>Digits in a single unit's value, which is one through nine.</summary>
    public static int DigitsInUnit(uint unit)
    {
        if (unit >= 100000000)
        {
            return 9;
        }

        if (unit >= 10000000)
        {
            return 8;
        }

        if (unit >= 1000000)
        {
            return 7;
        }

        if (unit >= 100000)
        {
            return 6;
        }

        if (unit >= 10000)
        {
            return 5;
        }

        if (unit >= 1000)
        {
            return 4;
        }

        if (unit >= 100)
        {
            return 3;
        }

        return unit >= 10 ? 2 : 1;
    }

    /// <summary>Makes this a zero, keeping the buffer it points at.</summary>
    public void SetZero()
    {
        *Lsu = 0;
        Units = 1;
        Digits = 1;
        Kind = DecimalKind.Finite;
    }

    /// <summary>Copies another value's coefficient and shape into this one's buffer.</summary>
    public void CopyFrom(WideNumber source)
    {
        for (var index = 0; index < source.Units; index++)
        {
            Lsu[index] = source.Lsu[index];
        }

        Units = source.Units;
        Digits = source.Digits;
        Exponent = source.Exponent;
        IsNegative = source.IsNegative;
        Kind = source.Kind;
    }

    /// <summary>
    /// Reads the coefficient as a machine word, for the small values the series work with.
    /// Only meaningful when the value is short enough to fit one.
    /// </summary>
    public readonly ulong ToUInt64()
    {
        var value = 0UL;
        for (var index = Units - 1; index >= 0; index--)
        {
            value = (value * UnitBase) + Lsu[index];
        }

        return value;
    }
}
