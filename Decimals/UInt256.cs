// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Decimals;

/// <summary>
/// A 256-bit unsigned integer, holding what Decimal128 arithmetic needs and nothing more.
/// Multiplying two 34-digit coefficients gives 68 digits, and dividing needs the dividend
/// scaled up by 10^34 first, so both outrun <see cref="UInt128"/>.
/// </summary>
/// <remarks>
/// This deliberately does not implement <see cref="IBinaryInteger{TSelf}"/>. That contract
/// is far wider than the need, and every member of it would be dead weight to test.
/// </remarks>
internal readonly struct UInt256 : IEquatable<UInt256>, IComparable<UInt256>
{
    public static UInt256 Zero => default;

    public static UInt256 One { get; } = new(0, 0, 0, 1);

    private readonly ulong _limb3;
    private readonly ulong _limb2;
    private readonly ulong _limb1;
    private readonly ulong _limb0;

    /// <summary>
    /// Builds a value from its four 64-bit limbs, most significant first.
    /// </summary>
    public UInt256(ulong limb3, ulong limb2, ulong limb1, ulong limb0)
    {
        _limb3 = limb3;
        _limb2 = limb2;
        _limb1 = limb1;
        _limb0 = limb0;
    }

    public UInt256(UInt128 value)
    {
        _limb3 = 0;
        _limb2 = 0;
        _limb1 = (ulong)(value >> 64);
        _limb0 = (ulong)value;
    }

    public UInt256(ulong value)
    {
        _limb3 = 0;
        _limb2 = 0;
        _limb1 = 0;
        _limb0 = value;
    }

    /// <summary>Bits 63 through 0.</summary>
    public ulong Limb0 => _limb0;

    /// <summary>Bits 127 through 64.</summary>
    public ulong Limb1 => _limb1;

    /// <summary>Bits 191 through 128.</summary>
    public ulong Limb2 => _limb2;

    /// <summary>Bits 255 through 192.</summary>
    public ulong Limb3 => _limb3;

    public bool IsZero => (_limb0 | _limb1 | _limb2 | _limb3) == 0;

    /// <summary>
    /// True when the value fits a <see cref="UInt128"/>.
    /// </summary>
    public bool FitsUInt128 => (_limb2 | _limb3) == 0;

    /// <summary>
    /// The low 128 bits. Callers that care about the rest test <see cref="FitsUInt128"/>.
    /// </summary>
    public UInt128 ToUInt128()
    {
        return new UInt128(_limb1, _limb0);
    }

    /// <summary>
    /// The full 256-bit product of two 128-bit values.
    /// </summary>
    public static UInt256 Multiply(UInt128 left, UInt128 right)
    {
        var leftLow = (ulong)left;
        var leftHigh = (ulong)(left >> 64);
        var rightLow = (ulong)right;
        var rightHigh = (ulong)(right >> 64);

        var lowLow = Math.BigMul(leftLow, rightLow, out var limb0);
        var lowHigh = Math.BigMul(leftLow, rightHigh, out var lowHighLow);
        var highLow = Math.BigMul(leftHigh, rightLow, out var highLowLow);
        var highHigh = Math.BigMul(leftHigh, rightHigh, out var highHighLow);

        // Three 64-bit terms land on limb1 and three more on limb2, so each column can
        // carry two bits out rather than one. Accumulating a column at a time in 128 bits
        // keeps that honest.
        var middle = (UInt128)lowLow + lowHighLow + highLowLow;
        var upper = (UInt128)highHighLow + lowHigh + highLow + (ulong)(middle >> 64);

        return new UInt256(highHigh + (ulong)(upper >> 64), (ulong)upper, (ulong)middle, limb0);
    }

    public static UInt256 operator +(UInt256 left, UInt256 right)
    {
        var carry = (ulong)0;
        var limb0 = AddWithCarry(left._limb0, right._limb0, ref carry);
        var limb1 = AddWithCarry(left._limb1, right._limb1, ref carry);
        var limb2 = AddWithCarry(left._limb2, right._limb2, ref carry);
        var limb3 = left._limb3 + right._limb3 + carry;
        return new UInt256(limb3, limb2, limb1, limb0);
    }

    public static UInt256 operator -(UInt256 left, UInt256 right)
    {
        var borrow = (ulong)0;
        var limb0 = SubtractWithBorrow(left._limb0, right._limb0, ref borrow);
        var limb1 = SubtractWithBorrow(left._limb1, right._limb1, ref borrow);
        var limb2 = SubtractWithBorrow(left._limb2, right._limb2, ref borrow);
        var limb3 = left._limb3 - right._limb3 - borrow;
        return new UInt256(limb3, limb2, limb1, limb0);
    }

    public static UInt256 operator <<(UInt256 value, int shift)
    {
        if (shift == 0)
        {
            return value;
        }

        Span<ulong> limbs = [value._limb0, value._limb1, value._limb2, value._limb3];
        Span<ulong> shifted = [0, 0, 0, 0];

        var limbShift = shift >> 6;
        var bitShift = shift & 63;
        for (var index = 3; index >= 0; index--)
        {
            var source = index - limbShift;
            if (source < 0)
            {
                continue;
            }

            var part = limbs[source] << bitShift;
            if (bitShift != 0 && source > 0)
            {
                part |= limbs[source - 1] >> (64 - bitShift);
            }

            shifted[index] = part;
        }

        return new UInt256(shifted[3], shifted[2], shifted[1], shifted[0]);
    }

    public static UInt256 operator >>(UInt256 value, int shift)
    {
        if (shift == 0)
        {
            return value;
        }

        Span<ulong> limbs = [value._limb0, value._limb1, value._limb2, value._limb3];
        Span<ulong> shifted = [0, 0, 0, 0];

        var limbShift = shift >> 6;
        var bitShift = shift & 63;
        for (var index = 0; index < 4; index++)
        {
            var source = index + limbShift;
            if (source > 3)
            {
                continue;
            }

            var part = limbs[source] >> bitShift;
            if (bitShift != 0 && source < 3)
            {
                part |= limbs[source + 1] << (64 - bitShift);
            }

            shifted[index] = part;
        }

        return new UInt256(shifted[3], shifted[2], shifted[1], shifted[0]);
    }

    /// <summary>
    /// Divides by a 64-bit divisor, handing back the quotient and the remainder.
    /// </summary>
    public static UInt256 DivRem(UInt256 value, ulong divisor, out ulong remainder)
    {
        // Most values reaching here are coefficients, which fit the narrower type: one
        // division rather than four, and none of them on limbs that are zero.
        if (value.FitsUInt128)
        {
            var narrow = value.ToUInt128();
            var narrowQuotient = narrow / divisor;
            remainder = (ulong)(narrow - (narrowQuotient * divisor));
            return new UInt256(narrowQuotient);
        }

        // Long division, most significant limb first, carrying the running remainder into
        // the next. Written out rather than looped over a span: the limbs are fields, and
        // indexing a span for them costs a bounds check apiece.
        var running = (UInt128)value._limb3;
        var limb3 = (ulong)(running / divisor);
        running %= divisor;

        running = (running << 64) | value._limb2;
        var limb2 = (ulong)(running / divisor);
        running %= divisor;

        running = (running << 64) | value._limb1;
        var limb1 = (ulong)(running / divisor);
        running %= divisor;

        running = (running << 64) | value._limb0;
        var limb0 = (ulong)(running / divisor);
        running %= divisor;

        remainder = (ulong)running;
        return new UInt256(limb3, limb2, limb1, limb0);
    }

    /// <summary>
    /// Divides by a 128-bit divisor. A divisor that fits 64 bits -- which is every
    /// Decimal32 and Decimal64 coefficient -- takes the limb-at-a-time path; wider ones
    /// fall back to bit-at-a-time long division, which only Decimal128 reaches.
    /// </summary>
    public static UInt256 DivRem(UInt256 value, UInt128 divisor, out UInt128 remainder)
    {
        if (divisor <= ulong.MaxValue)
        {
            var narrow = DivRem(value, (ulong)divisor, out var narrowRemainder);
            remainder = narrowRemainder;
            return narrow;
        }

        // A dividend inside 128 bits divides there in one step, instead of the two hundred
        // and fifty-six shift-and-subtract iterations below.
        if (value.FitsUInt128)
        {
            var narrow = value.ToUInt128();
            var narrowQuotient = narrow / divisor;
            remainder = narrow - (narrowQuotient * divisor);
            return new UInt256(narrowQuotient);
        }

        var quotient = Zero;
        var running = UInt128.Zero;
        for (var bit = 255; bit >= 0; bit--)
        {
            // The shift can carry out of 128 bits; when it does the true remainder is
            // above the divisor whatever the comparison says, so the subtraction is owed.
            var carried = (running >> 127) != UInt128.Zero;
            running = (running << 1) | (GetBit(value, bit) ? UInt128.One : UInt128.Zero);

            if (carried || running >= divisor)
            {
                running -= divisor;
                quotient = SetBit(quotient, bit);
            }
        }

        remainder = running;
        return quotient;
    }

    /// <summary>
    /// The integer square root: the largest value whose square does not exceed this one.
    /// Newton's method, which halves the error each step and so settles in a handful of
    /// iterations from a bit-length estimate.
    /// </summary>
    public static UInt128 Sqrt(UInt256 value)
    {
        if (value.IsZero)
        {
            return UInt128.Zero;
        }

        var bits = BitLength(value);
        if (bits <= 1)
        {
            return UInt128.One;
        }

        // 2^ceil(bits/2) is at or above the root, which is where Newton's method has to
        // start for it to descend rather than oscillate.
        var estimate = (bits + 1) / 2 >= 128
            ? UInt128.MaxValue
            : UInt128.One << ((bits + 1) / 2);

        while (true)
        {
            var quotient = DivRem(value, estimate, out _).ToUInt128();

            // The mean of two values that can each fill 128 bits, without overflowing.
            var next = (quotient >> 1) + (estimate >> 1) + (quotient & estimate & UInt128.One);
            if (next >= estimate)
            {
                return estimate;
            }

            estimate = next;
        }
    }

    public static int BitLength(UInt256 value)
    {
        if (value._limb3 != 0)
        {
            return 256 - System.Numerics.BitOperations.LeadingZeroCount(value._limb3);
        }

        if (value._limb2 != 0)
        {
            return 192 - System.Numerics.BitOperations.LeadingZeroCount(value._limb2);
        }

        if (value._limb1 != 0)
        {
            return 128 - System.Numerics.BitOperations.LeadingZeroCount(value._limb1);
        }

        return value._limb0 == 0 ? 0 : 64 - System.Numerics.BitOperations.LeadingZeroCount(value._limb0);
    }

    public static bool GetBit(UInt256 value, int bit)
    {
        var limb = bit switch
        {
            < 64 => value._limb0,
            < 128 => value._limb1,
            < 192 => value._limb2,
            _ => value._limb3
        };

        return ((limb >> (bit & 63)) & 1) != 0;
    }

    public static UInt256 SetBit(UInt256 value, int bit)
    {
        var mask = 1UL << (bit & 63);
        return bit switch
        {
            < 64 => new UInt256(value._limb3, value._limb2, value._limb1, value._limb0 | mask),
            < 128 => new UInt256(value._limb3, value._limb2, value._limb1 | mask, value._limb0),
            < 192 => new UInt256(value._limb3, value._limb2 | mask, value._limb1, value._limb0),
            _ => new UInt256(value._limb3 | mask, value._limb2, value._limb1, value._limb0)
        };
    }

    /// <summary>
    /// Divides by 10^<paramref name="power"/>, reporting through
    /// <paramref name="hasRemainder"/> whether anything was discarded. Rounding needs to
    /// know only that, plus the digit the caller peels off next, so the exact remainder is
    /// not worth carrying across the chunked division.
    /// </summary>
    public static UInt256 DivideByPowerOfTen(UInt256 value, int power, out bool hasRemainder)
    {
        // A value inside 128 bits is divided there, in one step. The loop below costs a
        // 128-by-64 division per limb per chunk of nineteen digits, and every coefficient
        // reaching this before an operation has widened it fits the narrower type.
        if (value.FitsUInt128 && power <= PowersOfTen.MaxUInt128Power)
        {
            var narrow = value.ToUInt128();
            var divisor = PowersOfTen.UInt128(power);
            var narrowQuotient = narrow / divisor;
            hasRemainder = narrow != narrowQuotient * divisor;
            return new UInt256(narrowQuotient);
        }

        hasRemainder = false;
        var quotient = value;

        while (power > 0)
        {
            var chunk = Math.Min(power, PowersOfTen.MaxUInt64Power);
            quotient = DivRem(quotient, PowersOfTen.UInt64(chunk), out var remainder);
            hasRemainder |= remainder != 0;
            power -= chunk;
        }

        return quotient;
    }

    /// <summary>
    /// Multiplies by 10^<paramref name="power"/>. The caller is responsible for knowing the
    /// product fits; decimal arithmetic always does, because it scales to a known width.
    /// </summary>
    public static UInt256 MultiplyByPowerOfTen(UInt256 value, int power)
    {
        var result = value;
        while (power > 0)
        {
            var chunk = Math.Min(power, PowersOfTen.MaxUInt64Power);
            result = MultiplyByUInt64(result, PowersOfTen.UInt64(chunk));
            power -= chunk;
        }

        return result;
    }

    public static UInt256 MultiplyByUInt64(UInt256 value, ulong multiplier)
    {
        var carry = (ulong)0;
        var limb0 = MultiplyAccumulate(value._limb0, multiplier, ref carry);
        var limb1 = MultiplyAccumulate(value._limb1, multiplier, ref carry);
        var limb2 = MultiplyAccumulate(value._limb2, multiplier, ref carry);
        var limb3 = MultiplyAccumulate(value._limb3, multiplier, ref carry);
        return new UInt256(limb3, limb2, limb1, limb0);
    }

    /// <summary>
    /// How many decimal digits the value is written with; zero counts as one digit.
    /// </summary>
    /// <summary>
    /// How many digits the value is written with. Zero counts as one.
    /// </summary>
    /// <remarks>
    /// Counting by repeated division -- which is what this did first -- costs a 128-bit
    /// division per digit, and addition asks for the count of both its operands before it
    /// can line them up. Every coefficient of every format fits 128 bits, so that case is
    /// handed straight to the narrower routine; wider values are intermediates, and they
    /// take the same bit-length estimate against a table of powers.
    /// </remarks>
    public int CountDigits()
    {
        if (FitsUInt128)
        {
            return DecimalRounder.CountDigits(ToUInt128());
        }

        var digits = (int)((BitLength(this) * 30103L) / 100000) + 1;
        if (digits > 1 && CompareTo(PowersOfTen.UInt256(digits - 1)) < 0)
        {
            digits--;
        }

        return digits;
    }

    public int CompareTo(UInt256 other)
    {
        if (_limb3 != other._limb3)
        {
            return _limb3 < other._limb3 ? -1 : 1;
        }

        if (_limb2 != other._limb2)
        {
            return _limb2 < other._limb2 ? -1 : 1;
        }

        if (_limb1 != other._limb1)
        {
            return _limb1 < other._limb1 ? -1 : 1;
        }

        if (_limb0 != other._limb0)
        {
            return _limb0 < other._limb0 ? -1 : 1;
        }

        return 0;
    }

    public bool Equals(UInt256 other)
    {
        return _limb0 == other._limb0
            && _limb1 == other._limb1
            && _limb2 == other._limb2
            && _limb3 == other._limb3;
    }

    public override bool Equals(object? other)
    {
        return other is UInt256 value && Equals(value);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_limb0, _limb1, _limb2, _limb3);
    }

    public static bool operator ==(UInt256 left, UInt256 right) => left.Equals(right);

    public static bool operator !=(UInt256 left, UInt256 right) => !left.Equals(right);

    public static bool operator <(UInt256 left, UInt256 right) => left.CompareTo(right) < 0;

    public static bool operator >(UInt256 left, UInt256 right) => left.CompareTo(right) > 0;

    public static bool operator <=(UInt256 left, UInt256 right) => left.CompareTo(right) <= 0;

    public static bool operator >=(UInt256 left, UInt256 right) => left.CompareTo(right) >= 0;

    public override string ToString()
    {
        if (IsZero)
        {
            return "0";
        }

        var text = string.Empty;
        var remaining = this;
        while (!remaining.IsZero)
        {
            remaining = DivRem(remaining, PowersOfTen.UInt64(PowersOfTen.MaxUInt64Power), out var chunk);
            text = remaining.IsZero
                ? chunk.ToString() + text
                : chunk.ToString("D" + PowersOfTen.MaxUInt64Power) + text;
        }

        return text;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong AddWithCarry(ulong left, ulong right, ref ulong carry)
    {
        var sum = left + right;
        var overflowed = sum < left ? 1UL : 0UL;
        var total = sum + carry;
        overflowed |= total < sum ? 1UL : 0UL;
        carry = overflowed;
        return total;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong SubtractWithBorrow(ulong left, ulong right, ref ulong borrow)
    {
        var difference = left - right;
        var borrowed = difference > left ? 1UL : 0UL;
        var total = difference - borrow;
        borrowed |= total > difference ? 1UL : 0UL;
        borrow = borrowed;
        return total;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong MultiplyAccumulate(ulong value, ulong multiplier, ref ulong carry)
    {
        var high = Math.BigMul(value, multiplier, out var low);
        var sum = low + carry;
        if (sum < low)
        {
            high++;
        }

        carry = high;
        return sum;
    }
}
