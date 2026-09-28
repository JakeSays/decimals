// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// Addition and comparison on the engine's unit arrays: decNumber's <c>decAddOp</c> and
/// <c>decCompareOp</c>.
/// </summary>
/// <remarks>
/// Alignment is bounded rather than literal. Padding one operand out to meet an exponent
/// hundreds of decades away would need a coefficient hundreds of digits wide, and every one
/// of those digits below the last one kept can only tip the rounding -- so a lower operand
/// that far down becomes a residue instead, which is what decNumber does.
/// </remarks>
internal static unsafe class Decimal128WideArithmetic
{
    /// <summary>
    /// Adds two values, optionally negating the second, and settles the sum into the
    /// context. <paramref name="work"/> holds the aligned larger operand and must have room
    /// for the context's digits plus the operands' own.
    /// </summary>
    public static void Add(ref Decimal128WideNumber result, Decimal128WideNumber left, Decimal128WideNumber right,
        bool negateRight, Decimal128WideContext context, ref Decimal128Status status, uint* work)
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
                // Infinities of opposite sign have no sum.
                status |= Decimal128Status.InvalidOperation;
                result.SetZero();
                result.Kind = Decimal128Kind.QuietNaN;
                result.IsNegative = false;
                return;
            }

            result.SetZero();
            result.Kind = Decimal128Kind.Infinity;
            result.IsNegative = left.IsInfinity ? left.IsNegative : rightNegative;
            return;
        }

        if (left.IsZero || right.IsZero)
        {
            AddZero(ref result, left, right, rightNegative, differingSigns, context, ref status);
            return;
        }

        // Line the operands up on the lower of the two exponents.
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

        // When the lower operand falls entirely past the digit after the last one kept, it
        // cannot reach the result except to tip the rounding. Say so with a residue rather
        // than materializing an alignment hundreds of digits wide.
        if (padding > 0 && upper.Digits + padding > lower.Digits + context.Digits + 1)
        {
            var farResidue = differingSigns ? -1 : 1;
            var shift = context.Digits - upper.Digits;

            result.CopyFrom(upper);
            result.IsNegative = upperNegative;
            Decimal128WideRounding.SetCoefficient(ref result, context.Digits, ref farResidue, ref status);

            if (shift > 0)
            {
                result.Units = Decimal128WideUnits.ShiftUp(result.Lsu, result.Units, shift);
                result.Exponent -= shift;
                result.CountDigits();
            }

            Decimal128WideRounding.Finalize(ref result, farResidue, context, ref status);
            return;
        }

        // The larger operand scaled down to the smaller's exponent, in the work buffer so
        // the caller's operands are left alone.
        for (var index = 0; index < upper.Units; index++)
        {
            work[index] = upper.Lsu[index];
        }

        var upperUnits = Decimal128WideUnits.ShiftUp(work, upper.Units, padding);

        // One multiplier settles both the addition and the subtraction: the signs decide
        // which, and a borrow out of the top says the result came out the other way round.
        var multiplier = differingSigns ? -1 : 1;
        var written = Decimal128WideUnits.AddSub(work, upperUnits, lower.Lsu, lower.Units, 0,
            result.Lsu, multiplier);

        result.Units = Math.Abs(written);
        result.Exponent = lower.Exponent;
        result.Kind = Decimal128Kind.Finite;
        result.CountDigits();

        // The sign follows whichever operand won; a borrow means the lower one did.
        result.IsNegative = written < 0 ? lowerNegative : upperNegative;

        if (result.IsZero)
        {
            // A sum that cancels exactly is positive in every rounding mode but one.
            result.IsNegative = differingSigns && context.Rounding == Decimal128Rounding.Floor;
        }

        var residue = 0;
        Decimal128WideRounding.SetCoefficient(ref result, context.Digits, ref residue, ref status);
        Decimal128WideRounding.Finalize(ref result, residue, context, ref status);
    }

    /// <summary>
    /// The result is the other operand, except that a zero still contributes its exponent
    /// when that is the lower of the two.
    /// </summary>
    private static void AddZero(ref Decimal128WideNumber result, Decimal128WideNumber left, Decimal128WideNumber right,
        bool rightNegative, bool differingSigns, Decimal128WideContext context, ref Decimal128Status status)
    {
        var zeroExponent = left.IsZero ? left.Exponent : right.Exponent;
        var other = left.IsZero ? right : left;
        var negative = left.IsZero ? rightNegative : left.IsNegative;

        result.CopyFrom(other);
        result.IsNegative = negative;

        var residue = 0;
        Decimal128WideRounding.SetCoefficient(ref result, context.Digits, ref residue, ref status);

        if (result.IsZero)
        {
            if (zeroExponent < result.Exponent)
            {
                result.Exponent = zeroExponent;
            }

            if (differingSigns)
            {
                result.IsNegative = context.Rounding == Decimal128Rounding.Floor;
            }
        }
        else
        {
            var pad = result.Exponent - zeroExponent;
            if (pad > 0)
            {
                if (result.Digits + pad > context.Digits)
                {
                    // Only so many zeros fit; the value is unchanged, but digits went.
                    pad = context.Digits - result.Digits;
                    status |= Decimal128Status.Rounded;
                }

                result.Units = Decimal128WideUnits.ShiftUp(result.Lsu, result.Units, pad);
                result.Exponent -= pad;
                result.CountDigits();
            }
        }

        Decimal128WideRounding.Finalize(ref result, residue, context, ref status);
    }

    /// <summary>
    /// Compares two values numerically, giving -1, 0, or 1.
    /// <paramref name="ignoreSigns"/> compares magnitudes instead.
    /// </summary>
    public static int Compare(Decimal128WideNumber left, Decimal128WideNumber right, bool ignoreSigns)
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
    /// Orders two values by magnitude. Where the leading digits sit decides it unless they
    /// sit in the same place, and then the coefficients do once lined up.
    /// </summary>
    private static int CompareMagnitudes(Decimal128WideNumber left, Decimal128WideNumber right)
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

        // The same magnitude range, so the digits settle it. They are walked from each
        // value's own leading digit rather than by position, since two coefficients of the
        // same value need not be the same length -- 123 and 1230 line up at the front, not
        // at the back -- and whichever runs out first is padded with zeros.
        var leftPosition = left.Digits - 1;
        var rightPosition = right.Digits - 1;

        while (leftPosition >= 0 || rightPosition >= 0)
        {
            var leftDigit = leftPosition >= 0
                ? Decimal128WideUnits.DigitAt(left.Lsu, left.Units, leftPosition)
                : 0;

            var rightDigit = rightPosition >= 0
                ? Decimal128WideUnits.DigitAt(right.Lsu, right.Units, rightPosition)
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
