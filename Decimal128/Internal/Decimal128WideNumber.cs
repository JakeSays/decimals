// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// An arbitrary-precision decimal number for the elementary functions: a coefficient in
/// base-billion units, least significant first, with a sign and an exponent. This is
/// decNumber's <c>decNumber</c>.
/// </summary>
/// <remarks>
/// <para>
/// decNumber stores its coefficient in units of <c>DECDPUN</c> digits instead of as a
/// binary integer, because every operation on it is decimal: rounding to a number of
/// digits, scaling by a power of ten, and counting digits. Nine digits is the widest unit
/// a 32-bit word holds, and the product of two units fits in 64 bits, which the multiply
/// and divide loops rely on.
/// </para>
/// <para>
/// The units are in a buffer the caller owns. <see cref="Digits"/> is stored instead of
/// computed, as in decNumber, because almost every operation needs it and computing it
/// requires a scan.
/// </para>
/// </remarks>
internal unsafe struct Decimal128WideNumber
{
    /// <summary>The number of decimal digits in one unit.</summary>
    public const int DigitsPerUnit = 9;

    /// <summary>The base of each unit: 10^9.</summary>
    public const uint UnitBase = 1000000000;

    /// <summary>A pointer to the least significant unit of the coefficient.</summary>
    public uint* Lsu;

    /// <summary>The number of units in use. It is always enough to hold <see cref="Digits"/>.</summary>
    public int Units;

    /// <summary>The number of digits in the coefficient. Zero has one digit.</summary>
    public int Digits;

    /// <summary>The exponent: the value is the coefficient times 10 to this power.</summary>
    public int Exponent;

    /// <summary>Whether the sign is negative.</summary>
    public bool IsNegative;

    /// <summary>Whether the value is finite, infinite, or a NaN.</summary>
    public Decimal128Kind Kind;

    /// <summary>Creates a positive zero with exponent zero over a buffer the caller owns.</summary>
    /// <param name="lsu">The buffer for the coefficient's units. Its first unit is set to zero.</param>
    public Decimal128WideNumber(uint* lsu)
    {
        Lsu = lsu;
        Units = 1;
        Digits = 1;
        Exponent = 0;
        IsNegative = false;
        Kind = Decimal128Kind.Finite;
        *lsu = 0;
    }

    /// <summary>Whether the value is finite.</summary>
    public readonly bool IsFinite => Kind == Decimal128Kind.Finite;

    /// <summary>Whether the value is a quiet or signaling NaN.</summary>
    public readonly bool IsNaN => Kind is Decimal128Kind.QuietNaN or Decimal128Kind.SignalingNaN;

    /// <summary>Whether the value is an infinity.</summary>
    public readonly bool IsInfinity => Kind == Decimal128Kind.Infinity;

    /// <summary>The exponent of the value's leading digit.</summary>
    public readonly int AdjustedExponent => Exponent + Digits - 1;

    /// <summary>Whether the value is a finite zero.</summary>
    public readonly bool IsZero
    {
        get
        {
            if (Kind != Decimal128Kind.Finite)
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
    /// Converts a value of the format to units. The coefficient is split into nine-digit
    /// units from the low end, where unit boundaries fall regardless of its length.
    /// </summary>
    /// <param name="bits">The encoded value.</param>
    /// <param name="lsu">The buffer for the coefficient's units. It must hold at least four units.</param>
    /// <returns>The value as a wide number, with its coefficient in <paramref name="lsu"/>.</returns>
    public static Decimal128WideNumber FromBits(Decimal128Integer bits, uint* lsu)
    {
        if (Decimal128Encoding.IsSpecial(bits))
        {
            var kind = Decimal128Kind.Infinity;
            if (Decimal128Encoding.IsNaN(bits))
            {
                kind = Decimal128Encoding.IsSignalingNaN(bits) ? Decimal128Kind.SignalingNaN : Decimal128Kind.QuietNaN;
            }

            return FromParts(kind, Decimal128Encoding.IsNegative(bits), 0, Decimal128Encoding.Payload(bits), lsu);
        }

        var coefficient = Decimal128Encoding.Unpack(bits, out var exponent);
        return FromParts(Decimal128Kind.Finite, Decimal128Encoding.IsNegative(bits), exponent, coefficient, lsu);
    }

    /// <summary>Creates a value from its parts.</summary>
    /// <param name="kind">Whether the value is finite, infinite, or a NaN.</param>
    /// <param name="negative">Whether the sign is negative.</param>
    /// <param name="exponent">The exponent.</param>
    /// <param name="coefficient">The coefficient, or a NaN's payload.</param>
    /// <param name="lsu">The buffer for the coefficient's units. It must hold at least five units.</param>
    /// <returns>The value, with its coefficient in <paramref name="lsu"/>.</returns>
    public static Decimal128WideNumber FromParts(Decimal128Kind kind, bool negative, int exponent,
        Decimal128Integer coefficient, uint* lsu)
    {
        var number = new Decimal128WideNumber(lsu);
        number.Kind = kind;
        number.IsNegative = negative;
        number.Exponent = exponent;

        var remaining = coefficient;
        var length = 0;

        do
        {
            remaining = Decimal128Tables.DivRemPowerOfTen(remaining, DigitsPerUnit, out var unit);
            lsu[length] = (uint)unit;
            length++;
        }
        while (!remaining.IsZero);

        number.Units = length;
        number.CountDigits();
        return number;
    }

    /// <summary>
    /// Converts the value back to the format's encoding. The caller must first fit the value
    /// to the format, because only then does the coefficient fit.
    /// </summary>
    /// <returns>The encoded value.</returns>
    public readonly Decimal128Integer ToBits()
    {
        if (Kind == Decimal128Kind.Infinity)
        {
            return Decimal128Encoding.Infinity(IsNegative);
        }

        if (Kind != Decimal128Kind.Finite)
        {
            return Decimal128Encoding.NaN(IsNegative, Kind == Decimal128Kind.SignalingNaN, ToInteger());
        }

        return Decimal128Encoding.Pack(IsNegative, Exponent, ToInteger());
    }

    /// <summary>Creates a value from a small integer. The series constants are given this way.</summary>
    /// <param name="value">The integer.</param>
    /// <param name="lsu">The buffer for the coefficient's units. It must hold at least two units.</param>
    /// <returns>The integer with exponent zero, with its coefficient in <paramref name="lsu"/>.</returns>
    public static Decimal128WideNumber FromInt32(int value, uint* lsu)
    {
        var number = new Decimal128WideNumber(lsu);
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

    /// <summary>Whether the value is a finite integer.</summary>
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

            // The value is an integer if and only if all digits below the decimal point are
            // zeros.
            return !Decimal128WideUnits.AnyBelow(Lsu, Units, -Exponent);
        }
    }

    /// <summary>Whether the value is an odd integer.</summary>
    public readonly bool IsOddIntegerValued
    {
        get
        {
            if (!IsIntegerValued || IsZero)
            {
                return false;
            }

            // A positive exponent means the integer ends in zeros, so it is even.
            if (Exponent > 0)
            {
                return false;
            }

            // The integer's last digit is the one just above the decimal point.
            return Decimal128WideUnits.DigitAt(Lsu, Units, -Exponent) % 2 != 0;
        }
    }

    /// <summary>
    /// Converts the value to an <see cref="int"/>, if it is an integer small enough.
    /// </summary>
    /// <returns>The integer, or null if the value is not an integer from -1999999997 to 999999999.</returns>
    public readonly int? ToInt32()
    {
        if (!IsIntegerValued)
        {
            return null;
        }

        if (IsZero)
        {
            return 0;
        }

        // With more than 11 integer digits, the magnitude is out of range.
        if (Digits + Exponent > 11)
        {
            return null;
        }

        // Read the integer part one digit at a time, from the top down to the decimal
        // point. A positive exponent puts the decimal point below the coefficient, so the
        // loop stops at the last digit and the zeros are appended afterward. 11 digits fit
        // easily in a 64-bit word, so nothing here can overflow.
        var lowest = Exponent < 0 ? -Exponent : 0;
        var integral = 0L;

        for (var position = Digits - 1; position >= lowest; position--)
        {
            integral = (integral * 10) + Decimal128WideUnits.DigitAt(Lsu, Units, position);
        }

        for (var index = 0; index < Exponent; index++)
        {
            integral *= 10;
        }

        var limit = IsNegative ? 1999999997L : 999999999L;
        if (integral > limit)
        {
            return null;
        }

        var signed = IsNegative
            ? -integral
            : integral;

        return (int)signed;
    }

    /// <summary>Flips the sign.</summary>
    public void Negate()
    {
        IsNegative = !IsNegative;
    }

    /// <summary>The number of units needed to hold a number of digits.</summary>
    /// <param name="digits">The number of digits.</param>
    /// <returns>The number of units, rounded up.</returns>
    public static int UnitsFor(int digits)
    {
        return ((digits + DigitsPerUnit) - 1) / DigitsPerUnit;
    }

    /// <summary>
    /// Recomputes <see cref="Digits"/> from the units and removes leading zero units. Every
    /// operation that changes the coefficient must leave these correct. decNumber's
    /// <c>decGetDigits</c>.
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
            // All units are zero, so the value is zero, which has one digit.
            Digits = 1;
            return;
        }

        Digits = (top * DigitsPerUnit) + Decimal128Tables.CountDigits(leading);
    }

    /// <summary>Sets the value to a finite zero, keeping its buffer, sign, and exponent.</summary>
    public void SetZero()
    {
        *Lsu = 0;
        Units = 1;
        Digits = 1;
        Kind = Decimal128Kind.Finite;
    }

    /// <summary>Copies another value's coefficient, sign, exponent, and kind into this value's buffer.</summary>
    /// <param name="source">The value to copy.</param>
    public void CopyFrom(Decimal128WideNumber source)
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
    /// Reads the coefficient as a two-word integer, for a value the format can hold. The
    /// result is only correct when the coefficient fits in two words.
    /// </summary>
    /// <returns>The coefficient.</returns>
    public readonly Decimal128Integer ToInteger()
    {
        var value = Decimal128Integer.Zero;
        for (var index = Units - 1; index >= 0; index--)
        {
            value = value.MultiplyBy(UnitBase) + Lsu[index];
        }

        return value;
    }

    /// <summary>
    /// Reads the coefficient as a 64-bit integer, for the small values the series use. The
    /// result is only correct when the coefficient fits in 64 bits.
    /// </summary>
    /// <returns>The coefficient.</returns>
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
