// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// Division on wide numbers: decNumber's <c>decDivideOp</c>.
/// </summary>
/// <remarks>
/// <para>
/// The operands are scaled first so the quotient has the requested number of digits. This
/// turns a decimal division into one integer division of unit arrays. The remainder sets
/// the residue. For an exact result, the trailing zeros the scaling added are removed, so
/// the exponent is the one the specification requires.
/// </para>
/// <para>
/// The integer division is long division in base one billion, and it keeps the remainder.
/// The multiplier estimate divides by the leading divisor unit plus one, so it is never
/// too high. Each subtraction is therefore safe, and the inner loop corrects the small
/// amount the estimate is too low.
/// </para>
/// </remarks>
internal static unsafe class Decimal32WideDivide
{
    /// <summary>
    /// Divides two values and rounds the quotient to the context.
    /// </summary>
    /// <remarks>
    /// Each of the three work buffers needs room for the operands' digits plus the context's
    /// digits.
    /// </remarks>
    /// <param name="result">Receives the quotient.</param>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <param name="context">The precision, rounding, and exponent limits to apply.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <param name="numerator">A work buffer for the scaled dividend.</param>
    /// <param name="denominator">A work buffer for the scaled divisor.</param>
    /// <param name="accumulator">A work buffer for the running remainder.</param>
    public static void Divide(ref Decimal32WideNumber result, Decimal32WideNumber left, Decimal32WideNumber right,
        Decimal32WideContext context, ref Decimal32Status status, uint* numerator, uint* denominator,
        uint* accumulator)
    {
        if (left.IsNaN)
        {
            result.CopyFrom(left);
            return;
        }

        if (right.IsNaN)
        {
            result.CopyFrom(right);
            return;
        }

        var negative = left.IsNegative ^ right.IsNegative;

        if (left.IsInfinity)
        {
            result.SetZero();
            result.IsNegative = negative;

            if (right.IsInfinity)
            {
                status |= Decimal32Status.InvalidOperation;
                result.Kind = Decimal32Kind.QuietNaN;
                result.IsNegative = false;
                return;
            }

            result.Kind = Decimal32Kind.Infinity;
            return;
        }

        if (right.IsInfinity)
        {
            // The ideal exponent is unbounded below, so the zero gets the smallest exponent
            // the context allows, and the result is reported as clamped.
            status |= Decimal32Status.Clamped;
            result.SetZero();
            result.IsNegative = negative;
            result.Exponent = context.TinyExponent;
            return;
        }

        if (right.IsZero)
        {
            result.SetZero();
            result.IsNegative = negative;

            if (left.IsZero)
            {
                status |= Decimal32Status.DivisionUndefined;
                result.Kind = Decimal32Kind.QuietNaN;
                result.IsNegative = false;
                return;
            }

            status |= Decimal32Status.DivisionByZero;
            result.Kind = Decimal32Kind.Infinity;
            return;
        }

        var idealExponent = left.Exponent - right.Exponent;

        if (left.IsZero)
        {
            var zeroResidue = 0;
            result.SetZero();
            result.IsNegative = negative;
            result.Exponent = idealExponent;
            Decimal32WideRounding.SetCoefficient(ref result, context.Digits, ref zeroResidue, ref status);
            Decimal32WideRounding.Finalize(ref result, zeroResidue, context, ref status);
            return;
        }

        // Scale so the quotient has the requested number of digits, plus or minus one.
        var shift = context.Digits - left.Digits + right.Digits;

        var numeratorLength = Copy(left, numerator);
        var denominatorLength = Copy(right, denominator);

        if (shift >= 0)
        {
            numeratorLength = Decimal32WideUnits.ShiftUp(numerator, numeratorLength, shift);
        }
        else
        {
            denominatorLength = Decimal32WideUnits.ShiftUp(denominator, denominatorLength, -shift);
        }

        var quotientLength = DivRem(numerator, numeratorLength, denominator, denominatorLength,
            accumulator, result.Lsu, out var remainderLength);

        result.Units = quotientLength;
        result.Kind = Decimal32Kind.Finite;
        result.IsNegative = negative;
        result.Exponent = idealExponent - shift;
        result.CountDigits();

        var residue = 0;
        var exact = remainderLength == 1 && *accumulator == 0;

        if (exact)
        {
            // The specification requires the exponent closest to the dividend's exponent
            // minus the divisor's. Remove the trailing zeros the scaling added, and no more.
            while (result.Exponent < idealExponent
                && Decimal32WideUnits.DigitAt(result.Lsu, result.Units, 0) == 0
                && !result.IsZero)
            {
                result.Units = Decimal32WideUnits.ShiftDown(result.Lsu, result.Units, 1);
                result.Exponent++;
                result.CountDigits();
            }
        }
        else
        {
            // Comparing twice the remainder with the divisor shows whether the remainder is
            // below, at, or above half.
            var doubled = Decimal32WideUnits.Double(accumulator, remainderLength);
            residue = Decimal32WideUnits.Compare(accumulator, doubled, denominator, denominatorLength) switch
            {
                < 0 => 3,
                0 => 5,
                _ => 7
            };
        }

        Decimal32WideRounding.SetCoefficient(ref result, context.Digits, ref residue, ref status);
        Decimal32WideRounding.Finalize(ref result, residue, context, ref status);
    }

    /// <summary>
    /// Integer division of two unit arrays. The remainder is left in
    /// <paramref name="accumulator"/>.
    /// </summary>
    /// <param name="numerator">The dividend's units, least significant first.</param>
    /// <param name="numeratorLength">The number of units in the dividend.</param>
    /// <param name="divisor">The divisor's units, least significant first. The divisor must not be zero.</param>
    /// <param name="divisorLength">The number of units in the divisor.</param>
    /// <param name="accumulator">
    /// A work buffer of at least <paramref name="numeratorLength"/> + 1 units. It receives the
    /// remainder.
    /// </param>
    /// <param name="quotient">Receives the quotient's units.</param>
    /// <param name="remainderLength">Receives the number of units in the remainder.</param>
    /// <returns>The number of units in the quotient.</returns>
    public static int DivRem(uint* numerator, int numeratorLength, uint* divisor,
        int divisorLength, uint* accumulator, uint* quotient, out int remainderLength)
    {
        while (divisorLength > 1 && divisor[divisorLength - 1] == 0)
        {
            divisorLength--;
        }

        for (var index = 0; index < numeratorLength; index++)
        {
            accumulator[index] = numerator[index];
        }

        // One extra unit above the numerator, because the window reads one unit past the
        // divisor's length.
        accumulator[numeratorLength] = 0;

        var quotientLength = numeratorLength - divisorLength + 1;
        if (quotientLength < 1)
        {
            *quotient = 0;
            remainderLength = numeratorLength;
            return 1;
        }

        for (var index = 0; index < quotientLength; index++)
        {
            quotient[index] = 0;
        }

        // The estimate divides by one more than the leading divisor unit, so it is never
        // too high. When it is too low, the loop subtracts again, usually once or twice.
        var estimateDivisor = (ulong)divisor[divisorLength - 1] + 1;

        for (var position = quotientLength - 1; position >= 0; position--)
        {
            var window = accumulator + position;
            var digit = 0u;

            while (true)
            {
                if (CompareWindow(window, divisor, divisorLength) < 0)
                {
                    break;
                }

                var high = ((ulong)window[divisorLength] * Decimal32WideNumber.UnitBase)
                    + window[divisorLength - 1];

                var multiplier = high / estimateDivisor;
                if (multiplier == 0)
                {
                    multiplier = 1;
                }

                SubtractScaled(window, divisor, divisorLength, multiplier);
                digit += (uint)multiplier;
            }

            quotient[position] = digit;
        }

        while (quotientLength > 1 && quotient[quotientLength - 1] == 0)
        {
            quotientLength--;
        }

        remainderLength = divisorLength;
        while (remainderLength > 1 && accumulator[remainderLength - 1] == 0)
        {
            remainderLength--;
        }

        return quotientLength;
    }

    /// <summary>
    /// Compares the window, which is one unit longer than the divisor, with the divisor.
    /// Returns -1, 0, or 1.
    /// </summary>
    private static int CompareWindow(uint* window, uint* divisor, int divisorLength)
    {
        if (window[divisorLength] != 0)
        {
            return 1;
        }

        for (var index = divisorLength - 1; index >= 0; index--)
        {
            if (window[index] != divisor[index])
            {
                return window[index] < divisor[index] ? -1 : 1;
            }
        }

        return 0;
    }

    /// <summary>
    /// Subtracts the divisor times <paramref name="multiplier"/> from the window.
    /// </summary>
    private static void SubtractScaled(uint* window, uint* divisor, int divisorLength,
        ulong multiplier)
    {
        var carry = 0UL;

        for (var index = 0; index < divisorLength; index++)
        {
            var product = (multiplier * divisor[index]) + carry;
            carry = product / Decimal32WideNumber.UnitBase;
            var low = (uint)(product % Decimal32WideNumber.UnitBase);

            if (low > window[index])
            {
                window[index] += Decimal32WideNumber.UnitBase;
                carry++;
            }

            window[index] -= low;
        }

        if (carry != 0)
        {
            window[divisorLength] -= (uint)carry;
        }
    }

    private static int Copy(Decimal32WideNumber value, uint* destination)
    {
        for (var index = 0; index < value.Units; index++)
        {
            destination[index] = value.Lsu[index];
        }

        return value.Units;
    }
}
