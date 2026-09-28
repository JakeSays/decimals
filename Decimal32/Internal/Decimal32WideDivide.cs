// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// Division on the engine's unit arrays: decNumber's <c>decDivideOp</c>.
/// </summary>
/// <remarks>
/// <para>
/// The operands are scaled first so that the quotient lands on the requested digit count,
/// which turns a decimal division into one integer division of unit arrays. What the
/// division leaves over decides the residue, and an exact result gives back the trailing
/// zeros the scaling introduced so the exponent comes out where the specification wants it.
/// </para>
/// <para>
/// The integer division is schoolbook in base-billion and the remainder is kept. The
/// multiplier estimate divides by the leading divisor unit plus one, which can never run
/// high, so each subtraction is always safe and the inner loop corrects what little the
/// estimate leaves.
/// </para>
/// </remarks>
internal static unsafe class Decimal32WideDivide
{
    /// <summary>
    /// Divides two values and settles the quotient into the context.
    /// </summary>
    /// <remarks>
    /// The three work buffers hold the scaled numerator, the scaled denominator, and the
    /// running remainder; each needs room for the operands' digits plus the context's.
    /// </remarks>
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
            // The ideal exponent runs off to negative infinity, so the zero settles at the
            // smallest exponent the context allows and says it was clamped.
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

        // Scale so the quotient lands on the requested number of digits, give or take one.
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
            // The specification wants the exponent nearest dividend minus divisor: give
            // back the trailing zeros the scaling introduced, and no more.
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
            // Twice the remainder against the divisor says which side of half it falls on.
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
    /// <returns>Units in the quotient.</returns>
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

        // One above the numerator, for the window to reach into.
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

        // The estimate divides by one more than the leading divisor unit, which can never
        // run high; what it leaves low the loop takes off in another pass or two.
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
    /// Whether the window, which is one unit longer than the divisor, is at least as large
    /// as it.
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
    /// Subtracts the divisor times <paramref name="multiplier"/> out of the window.
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
