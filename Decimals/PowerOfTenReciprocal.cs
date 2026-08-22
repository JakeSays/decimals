// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// A division by a power of ten, turned into a multiply and two shifts.
/// </summary>
/// <remarks>
/// There is no hardware 128-bit divide, so <c>value / PowersOfTen.UInt128(power)</c> runs a
/// software routine -- and that division sits on the rounding path of every arithmetic
/// operation. This replaces it.
///
/// The starting point is that 10^p is 2^p times 5^p, and that <c>floor(floor(n / a) / b)</c>
/// equals <c>floor(n / ab)</c> for positive integers. So dividing by 10^p is a shift right by
/// p followed by a division by 5^p. The shift is what makes the rest work: it takes p bits
/// off the numerator while taking 2.32p bits off the divisor, which leaves room for a
/// multiplier that still fits 128 bits.
///
/// For the division that remains, take <c>M = ceil(2^s / d)</c> and let <c>e = M*d - 2^s</c>,
/// so <c>0 &lt;= e &lt; d</c>. Then <c>n*M / 2^s</c> is <c>n/d + n*e/(d * 2^s)</c>, and writing
/// n as <c>qd + r</c> the floor is q exactly when <c>r + n*e/2^s &lt; d</c>. The worst case is
/// <c>r = d-1</c>, which leaves the condition <c>n_max * e &lt; 2^s</c>. Every pair in the
/// table satisfies that for a numerator of the full 128 bits, so this is exact rather than
/// approximate -- it is the same quotient the division gives, for every input.
/// </remarks>
internal readonly struct PowerOfTenReciprocal
{
    /// <summary>Every shift in the table is at least this, so the high half is all that matters.</summary>
    private const int ProductHalf = 128;

    public PowerOfTenReciprocal(UInt128 multiplier, int shift, int preShift, UInt128 powerOfTen)
    {
        Multiplier = multiplier;
        Shift = shift;
        PreShift = preShift;
        PowerOfTen = powerOfTen;
    }

    public UInt128 Multiplier { get; }

    public int Shift { get; }

    /// <summary>The power itself: the 2^p half of 10^p, taken off before the multiply.</summary>
    public int PreShift { get; }

    /// <summary>
    /// 10^p, carried here rather than looked up separately. A caller dividing by it almost
    /// always wants the remainder too, and two table reads where one would do cost more than
    /// the sixteen bytes this adds -- it put a nanosecond and a half back on rounding.
    /// </summary>
    public UInt128 PowerOfTen { get; }

    public UInt128 Divide(UInt128 value)
    {
        // This is unconditional on purpose. Below 10^20 the divisor fits a machine word and
        // the framework's division is competitive, so branching to it looks like a free win
        // -- but the discarded-digit count varies from call to call, the branch mispredicts,
        // and it measured worse on both rounding benchmarks than doing the multiply every
        // time. One path, no branch.
        var shifted = value >> PreShift;
        var product = UInt256.Multiply(shifted, Multiplier);

        // The shift is past 128 for every power, so the low half cannot reach the result and
        // the high half needs only what is left over.
        var high = new UInt128(product.Limb3, product.Limb2);
        return high >> (Shift - ProductHalf);
    }
}
