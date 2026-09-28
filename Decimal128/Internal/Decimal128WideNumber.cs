// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// An arbitrary-precision decimal as the elementary functions work on it: the coefficient
/// in base-billion units, least significant first, beside a sign and an exponent. This is
/// decNumber's <c>decNumber</c>.
/// </summary>
/// <remarks>
/// <para>
/// decNumber holds its coefficient in units of <c>DECDPUN</c> digits rather than as a
/// binary integer, because every operation on it is decimal: rounding to a digit count,
/// scaling by a power of ten, counting digits. Nine digits is the widest unit a 32-bit word
/// holds, and a product of two of them fits 64 bits, which is what makes the multiply and
/// divide loops work.
/// </para>
/// <para>
/// The units live in a buffer the caller owns. <see cref="Digits"/> is carried rather than
/// derived, the way decNumber carries it, because almost every operation needs it and
/// recomputing costs a scan.
/// </para>
/// </remarks>
internal unsafe struct Decimal128WideNumber
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

    public Decimal128Kind Kind;

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

    public readonly bool IsFinite => Kind == Decimal128Kind.Finite;

    public readonly bool IsNaN => Kind is Decimal128Kind.QuietNaN or Decimal128Kind.SignalingNaN;

    public readonly bool IsInfinity => Kind == Decimal128Kind.Infinity;

    /// <summary>Where the value's leading digit sits.</summary>
    public readonly int AdjustedExponent => Exponent + Digits - 1;

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
    /// Reads a format-width value into units. The coefficient is taken nine digits at a
    /// time from the low end, which is where a unit boundary falls whatever its length.
    /// </summary>
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
    /// Back to the encoding. The caller is responsible for having finalized the value into
    /// the format first, since only then is the coefficient known to fit.
    /// </summary>
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

    /// <summary>A small signed integer, which is what the series constants arrive as.</summary>
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
            return !Decimal128WideUnits.AnyBelow(Lsu, Units, -Exponent);
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
            return Decimal128WideUnits.DigitAt(Lsu, Units, -Exponent) % 2 != 0;
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
            integral = (integral * 10) + Decimal128WideUnits.DigitAt(Lsu, Units, position);
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

        Digits = (top * DigitsPerUnit) + Decimal128Tables.CountDigits(leading);
    }

    /// <summary>Makes this a zero, keeping the buffer it points at.</summary>
    public void SetZero()
    {
        *Lsu = 0;
        Units = 1;
        Digits = 1;
        Kind = Decimal128Kind.Finite;
    }

    /// <summary>Copies another value's coefficient and shape into this one's buffer.</summary>
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
    /// Reads the coefficient as two machine words, for a value the format holds. Only
    /// meaningful when the value is short enough to fit them.
    /// </summary>
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
    /// Reads the coefficient as one machine word, for the small values the series work
    /// with. Only meaningful when the value is short enough to fit one.
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
