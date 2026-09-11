// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// Multiplies two coefficients, following decNumber's <c>decFiniteMultiply</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the one operation the digit form is wrong for. Multiplying digit by digit is
/// quadratic in the digit count, and nine digits fit a machine word with room for a
/// product, so the coefficients are packed into base-billion limbs first and the result is
/// laid back out as digits at the end.
/// </para>
/// <para>
/// The partial products accumulate lazily, which is what makes it worth doing. Each column
/// of the accumulator holds the sum of the products landing on it without being resolved
/// back to base-billion in between: a product of two limbs is under 10^18, and no format
/// has enough limbs for a column to sum past what 64 bits hold, so the carries are settled
/// once at the end instead of on every addition.
/// </para>
/// <para>
/// decNumber resolves those carries with Clark and Cowlishaw's quotient estimation, because
/// a 64-bit divide was slow on the 32-bit machines it targeted. Dividing by a constant is
/// a multiply and a shift in the code the JIT emits, which is what that estimation was
/// hand-rolling, so this divides.
/// </para>
/// </remarks>
internal static unsafe class BcdMultiplier
{
    /// <summary>The base the multiplication works in.</summary>
    private const uint Base = 1000000000;

    /// <summary>Digits in one base-billion limb.</summary>
    public const int DigitsPerLimb = 9;

    /// <summary>
    /// Limbs a coefficient of this format occupies. decNumber's <c>DECPMAX9</c>.
    /// </summary>
    public static int LimbCount<TFormat>()
        where TFormat : IDecimalFormat
    {
        return (TFormat.Precision + DigitsPerLimb - 1) / DigitsPerLimb;
    }

    /// <summary>Columns in the accumulator, which is twice the operand length.</summary>
    public static int AccumulatorLength<TFormat>()
        where TFormat : IDecimalFormat
    {
        return LimbCount<TFormat>() * 2;
    }

    /// <summary>
    /// Digits the product can occupy, plus a digit of headroom before the leading one for a
    /// rounding carry in the finalizer.
    /// </summary>
    public static int ProductBufferLength<TFormat>()
        where TFormat : IDecimalFormat
    {
        return (AccumulatorLength<TFormat>() * DigitsPerLimb) + 2;
    }

    /// <summary>
    /// Multiplies two finite values into <paramref name="buffer"/>. The product is exact:
    /// nothing is rounded here, which is what lets a fused multiply-add take the result
    /// straight into an addition.
    /// </summary>
    public static BcdNumber Multiply<TFormat>(BcdNumber left, BcdNumber right, byte* buffer,
        uint* leftLimbs, uint* rightLimbs, ulong* accumulator)
        where TFormat : IDecimalFormat
    {
        var limbs = LimbCount<TFormat>();
        var columns = limbs * 2;

        Pack(left, leftLimbs, limbs);
        Pack(right, rightLimbs, limbs);

        for (var column = 0; column < columns; column++)
        {
            accumulator[column] = 0;
        }

        for (var rightIndex = 0; rightIndex < limbs; rightIndex++)
        {
            var multiplier = rightLimbs[rightIndex];
            if (multiplier == 0)
            {
                // The whole row would add nothing.
                continue;
            }

            for (var leftIndex = 0; leftIndex < limbs; leftIndex++)
            {
                accumulator[rightIndex + leftIndex] += (ulong)multiplier * leftLimbs[leftIndex];
            }
        }

        // The columns hold unresolved sums; carrying settles them into base-billion. The
        // top column cannot carry out, because the product of two coefficients of this
        // width does not reach the accumulator's own width.
        var carry = 0UL;
        for (var column = 0; column < columns; column++)
        {
            var total = accumulator[column] + carry;
            carry = total / Base;
            accumulator[column] = total % Base;
        }

        // A sign is the two signs differing, and the exponents simply add.
        var result = new BcdNumber(DecimalKind.Finite, left.IsNegative != right.IsNegative,
            left.Exponent + right.Exponent, buffer + 1, buffer + (columns * DigitsPerLimb));

        Unpack(accumulator, columns, result.Msd);
        result.Trim();
        return result;
    }

    /// <summary>
    /// Reads a coefficient into base-billion limbs, least significant limb first. The
    /// digits are taken nine at a time from the low end, which is where a limb boundary
    /// falls regardless of how long the coefficient is.
    /// </summary>
    private static void Pack(BcdNumber value, uint* limbs, int limbCount)
    {
        var digit = value.Lsd;
        var remaining = value.DigitCount;

        for (var index = 0; index < limbCount; index++)
        {
            var limb = 0u;
            var scale = 1u;
            for (var position = 0; position < DigitsPerLimb && remaining > 0; position++)
            {
                limb += *digit * scale;
                scale *= 10;
                digit--;
                remaining--;
            }

            limbs[index] = limb;
        }
    }

    /// <summary>
    /// Lays the accumulator out as digits, most significant limb first, nine digits each.
    /// Leading zeros are written and the caller trims them.
    /// </summary>
    private static void Unpack(ulong* accumulator, int columns, byte* digits)
    {
        var destination = digits;
        for (var column = columns - 1; column >= 0; column--)
        {
            var limb = (uint)accumulator[column];
            Dpd.WriteTriple(limb / 1000000, destination);
            Dpd.WriteTriple((limb / 1000) % 1000, destination + 3);
            Dpd.WriteTriple(limb % 1000, destination + 6);
            destination += DigitsPerLimb;
        }
    }
}
