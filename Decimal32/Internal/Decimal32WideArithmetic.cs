// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// Addition and comparison on wide numbers: decNumber's <c>decAddOp</c> and
/// <c>decCompareOp</c>.
/// </summary>
/// <remarks>
/// Alignment is limited. Padding one operand to meet an exponent hundreds of digits away
/// would need a coefficient hundreds of digits wide, and every digit below the last one
/// kept can only affect the rounding. So an operand that far below becomes a residue
/// instead, as in decNumber.
/// </remarks>
internal static unsafe class Decimal32WideArithmetic
{
    /// <summary>
    /// Adds two values, optionally negating the second, and rounds the sum to the context.
    /// </summary>
    /// <param name="result">Receives the sum.</param>
    /// <param name="left">The first operand.</param>
    /// <param name="right">The second operand.</param>
    /// <param name="negateRight">True to subtract <paramref name="right"/> instead of adding it.</param>
    /// <param name="context">The precision, rounding, and exponent limits to apply.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <param name="work">
    /// A buffer for the aligned larger operand. It must have room for the context's digits
    /// plus the operands' digits.
    /// </param>
    public static void Add(ref Decimal32WideNumber result, Decimal32WideNumber left, Decimal32WideNumber right,
        bool negateRight, Decimal32WideContext context, ref Decimal32Status status, uint* work)
    {
        var rightNegative = right.IsNegative ^ negateRight;
        var differingSigns = left.IsNegative != rightNegative;

        if (left.IsNaN)
        {
            result.CopyFrom(left);
            return;
        }

        if (right.IsNaN)
        {
            result.CopyFrom(right);
            result.IsNegative = right.IsNegative;
            return;
        }

        if (left.IsInfinity || right.IsInfinity)
        {
            if (left.IsInfinity && right.IsInfinity && differingSigns)
            {
                // The sum of infinities with opposite signs is invalid.
                status |= Decimal32Status.InvalidOperation;
                result.SetZero();
                result.Kind = Decimal32Kind.QuietNaN;
                result.IsNegative = false;
                return;
            }

            result.SetZero();
            result.Kind = Decimal32Kind.Infinity;
            result.IsNegative = left.IsInfinity ? left.IsNegative : rightNegative;
            return;
        }

        if (left.IsZero || right.IsZero)
        {
            AddZero(ref result, left, right, rightNegative, differingSigns, context, ref status);
            return;
        }

        // Align the operands at the lower of the two exponents.
        var lower = left;
        var lowerNegative = left.IsNegative;
        var upper = right;
        var upperNegative = rightNegative;

        if (right.Exponent < left.Exponent)
        {
            lower = right;
            lowerNegative = rightNegative;
            upper = left;
            upperNegative = left.IsNegative;
        }

        var padding = upper.Exponent - lower.Exponent;

        // When the lower operand is entirely below the digit after the last one kept, it can
        // only affect the rounding. Represent it with a residue instead of building an
        // alignment hundreds of digits wide.
        if (padding > 0 && upper.Digits + padding > lower.Digits + context.Digits + 1)
        {
            var farResidue = differingSigns ? -1 : 1;
            var shift = context.Digits - upper.Digits;

            result.CopyFrom(upper);
            result.IsNegative = upperNegative;
            Decimal32WideRounding.SetCoefficient(ref result, context.Digits, ref farResidue, ref status);

            if (shift > 0)
            {
                result.Units = Decimal32WideUnits.ShiftUp(result.Lsu, result.Units, shift);
                result.Exponent -= shift;
                result.CountDigits();
            }

            Decimal32WideRounding.Finalize(ref result, farResidue, context, ref status);
            return;
        }

        // Scale the upper operand down to the lower operand's exponent, in the work buffer
        // so the caller's operands are not changed.
        for (var index = 0; index < upper.Units; index++)
        {
            work[index] = upper.Lsu[index];
        }

        var upperUnits = Decimal32WideUnits.ShiftUp(work, upper.Units, padding);

        // One multiplier handles both addition and subtraction, and the signs choose which.
        // A borrow out of the top means the result has the opposite sign.
        var multiplier = differingSigns ? -1 : 1;
        var written = Decimal32WideUnits.AddSub(work, upperUnits, lower.Lsu, lower.Units, 0,
            result.Lsu, multiplier);

        result.Units = Math.Abs(written);
        result.Exponent = lower.Exponent;
        result.Kind = Decimal32Kind.Finite;
        result.CountDigits();

        // The result takes the sign of the larger magnitude. A borrow means the lower operand
        // was larger.
        result.IsNegative = written < 0 ? lowerNegative : upperNegative;

        if (result.IsZero)
        {
            // A sum that cancels exactly is positive in every rounding mode except Floor.
            result.IsNegative = differingSigns && context.Rounding == Decimal32Rounding.Floor;
        }

        var residue = 0;
        Decimal32WideRounding.SetCoefficient(ref result, context.Digits, ref residue, ref status);
        Decimal32WideRounding.Finalize(ref result, residue, context, ref status);
    }

    /// <summary>
    /// Adds when at least one operand is zero. The result is the other operand, except that
    /// a zero still contributes its exponent when that exponent is the lower one.
    /// </summary>
    private static void AddZero(ref Decimal32WideNumber result, Decimal32WideNumber left, Decimal32WideNumber right,
        bool rightNegative, bool differingSigns, Decimal32WideContext context, ref Decimal32Status status)
    {
        var zeroExponent = left.IsZero ? left.Exponent : right.Exponent;
        var other = left.IsZero ? right : left;
        var negative = left.IsZero ? rightNegative : left.IsNegative;

        result.CopyFrom(other);
        result.IsNegative = negative;

        var residue = 0;
        Decimal32WideRounding.SetCoefficient(ref result, context.Digits, ref residue, ref status);

        if (result.IsZero)
        {
            if (zeroExponent < result.Exponent)
            {
                result.Exponent = zeroExponent;
            }

            if (differingSigns)
            {
                result.IsNegative = context.Rounding == Decimal32Rounding.Floor;
            }
        }
        else
        {
            var pad = result.Exponent - zeroExponent;
            if (pad > 0)
            {
                if (result.Digits + pad > context.Digits)
                {
                    // Only this many zeros fit. The value is unchanged, but the exponent could
                    // not reach the zero's exponent, which counts as rounding.
                    pad = context.Digits - result.Digits;
                    status |= Decimal32Status.Rounded;
                }

                result.Units = Decimal32WideUnits.ShiftUp(result.Lsu, result.Units, pad);
                result.Exponent -= pad;
                result.CountDigits();
            }
        }

        Decimal32WideRounding.Finalize(ref result, residue, context, ref status);
    }

    /// <summary>
    /// Compares two values numerically. Neither may be a NaN.
    /// </summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <param name="ignoreSigns">True to compare magnitudes instead of signed values.</param>
    /// <returns>-1 if <paramref name="left"/> is smaller, 0 if they are equal, or 1 if it is larger.</returns>
    public static int Compare(Decimal32WideNumber left, Decimal32WideNumber right, bool ignoreSigns)
    {
        var leftNegative = !ignoreSigns && left.IsNegative;
        var rightNegative = !ignoreSigns && right.IsNegative;

        if (left.IsZero && right.IsZero)
        {
            return 0;
        }

        if (leftNegative != rightNegative)
        {
            return leftNegative ? -1 : 1;
        }

        var magnitude = CompareMagnitudes(left, right);
        return leftNegative ? -magnitude : magnitude;
    }

    /// <summary>
    /// Compares the magnitudes of two values. The positions of the leading digits decide
    /// the order unless they are equal. Then the digits are compared from the leading digit
    /// down.
    /// </summary>
    private static int CompareMagnitudes(Decimal32WideNumber left, Decimal32WideNumber right)
    {
        if (left.IsZero)
        {
            return right.IsZero ? 0 : -1;
        }

        if (right.IsZero)
        {
            return 1;
        }

        if (left.Kind != right.Kind)
        {
            // An infinity is beyond every finite value.
            return left.IsInfinity ? 1 : -1;
        }

        if (left.IsInfinity)
        {
            return 0;
        }

        if (left.AdjustedExponent != right.AdjustedExponent)
        {
            return left.AdjustedExponent < right.AdjustedExponent ? -1 : 1;
        }

        // The leading digits are in the same position, so the digits decide. They are read
        // from each value's leading digit, not by position, because two coefficients of the
        // same value can have different lengths: 123 and 1230 align at the front, not at the
        // back. The shorter one is padded with zeros.
        var leftPosition = left.Digits - 1;
        var rightPosition = right.Digits - 1;

        while (leftPosition >= 0 || rightPosition >= 0)
        {
            var leftDigit = leftPosition >= 0
                ? Decimal32WideUnits.DigitAt(left.Lsu, left.Units, leftPosition)
                : 0;

            var rightDigit = rightPosition >= 0
                ? Decimal32WideUnits.DigitAt(right.Lsu, right.Units, rightPosition)
                : 0;

            if (leftDigit != rightDigit)
            {
                return leftDigit < rightDigit ? -1 : 1;
            }

            leftPosition--;
            rightPosition--;
        }

        return 0;
    }
}
