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
internal unsafe struct Decimal64WideNumber
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

    public Decimal64Kind Kind;

    public Decimal64WideNumber(uint* lsu)
    {
        Lsu = lsu;
        Units = 1;
        Digits = 1;
        Exponent = 0;
        IsNegative = false;
        Kind = Decimal64Kind.Finite;
        *lsu = 0;
    }

    public readonly bool IsFinite => Kind == Decimal64Kind.Finite;

    public readonly bool IsNaN => Kind is Decimal64Kind.QuietNaN or Decimal64Kind.SignalingNaN;

    public readonly bool IsInfinity => Kind == Decimal64Kind.Infinity;

    /// <summary>Where the value's leading digit sits.</summary>
    public readonly int AdjustedExponent => Exponent + Digits - 1;

    public readonly bool IsZero
    {
        get
        {
            if (Kind != Decimal64Kind.Finite)
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
    public static Decimal64WideNumber FromBits(ulong bits, uint* lsu)
    {
        if (Decimal64Encoding.IsSpecial(bits))
        {
            var kind = Decimal64Kind.Infinity;
            if (Decimal64Encoding.IsNaN(bits))
            {
                kind = Decimal64Encoding.IsSignalingNaN(bits) ? Decimal64Kind.SignalingNaN : Decimal64Kind.QuietNaN;
            }

            return FromParts(kind, Decimal64Encoding.IsNegative(bits), 0, Decimal64Encoding.Payload(bits), lsu);
        }

        var coefficient = Decimal64Encoding.Unpack(bits, out var exponent);
        return FromParts(Decimal64Kind.Finite, Decimal64Encoding.IsNegative(bits), exponent, coefficient, lsu);
    }

    public static Decimal64WideNumber FromParts(Decimal64Kind kind, bool negative, int exponent, ulong coefficient, uint* lsu)
    {
        var number = new Decimal64WideNumber(lsu);
        number.Kind = kind;
        number.IsNegative = negative;
        number.Exponent = exponent;

        var remaining = coefficient;
        var length = 0;

        do
        {
            lsu[length] = (uint)(remaining % UnitBase);
            remaining /= UnitBase;
            length++;
        }
        while (remaining != 0);

        number.Units = length;
        number.CountDigits();
        return number;
    }

    /// <summary>
    /// Back to the encoding. The caller is responsible for having finalized the value into
    /// the format first, since only then is the coefficient known to fit.
    /// </summary>
    public readonly ulong ToBits()
    {
        if (Kind == Decimal64Kind.Infinity)
        {
            return Decimal64Encoding.Infinity(IsNegative);
        }

        if (Kind != Decimal64Kind.Finite)
        {
            return Decimal64Encoding.NaN(IsNegative, Kind == Decimal64Kind.SignalingNaN, ToUInt64());
        }

        return Decimal64Encoding.Pack(IsNegative, Exponent, ToUInt64());
    }

    /// <summary>A small signed integer, which is what the series constants arrive as.</summary>
    public static Decimal64WideNumber FromInt32(int value, uint* lsu)
    {
        var number = new Decimal64WideNumber(lsu);
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
            return !Decimal64WideUnits.AnyBelow(Lsu, Units, -Exponent);
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
            return Decimal64WideUnits.DigitAt(Lsu, Units, -Exponent) % 2 != 0;
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
            integral = (integral * 10) + Decimal64WideUnits.DigitAt(Lsu, Units, position);
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

        Digits = (top * DigitsPerUnit) + Decimal64Tables.CountDigits(leading);
    }

    /// <summary>Makes this a zero, keeping the buffer it points at.</summary>
    public void SetZero()
    {
        *Lsu = 0;
        Units = 1;
        Digits = 1;
        Kind = Decimal64Kind.Finite;
    }

    /// <summary>Copies another value's coefficient and shape into this one's buffer.</summary>
    public void CopyFrom(Decimal64WideNumber source)
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
