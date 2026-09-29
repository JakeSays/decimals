// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace Decimals.Internal;

/// <summary>
/// The arithmetic operations. Each takes encoded operands and returns an encoded result,
/// and handles the special values before it reads the coefficients.
/// </summary>
/// <remarks>
/// <para>
/// Every path here works on a coefficient in a 64-bit word and an exponent in an int. The
/// product of two 7-digit coefficients has at most 14 digits and fits in a word. So the
/// only intermediate value that does not fit is an operand aligned far above the other.
/// It is reduced to 19 digits and a <see cref="Decimal32Residue"/> before the addition, and
/// the finalizer then rounds once.
/// </para>
/// <para>
/// Alignment scales the operand with the higher exponent. Scaling it down to the lower
/// exponent is exact while the result fits in 19 digits. Beyond that, it is scaled to
/// exactly 19 digits, and the lower operand is folded to that exponent instead, with its
/// discarded digits becoming the residue. Only the lower operand is ever folded. When it
/// is, the higher operand has at least 18 digits above the fold, so no subtraction can
/// cancel into it.
/// </para>
/// </remarks>
internal static class Decimal32Arithmetic
{
    private const int WideDigits = Decimal32Tables.MaxPower;

    /// <summary>The number of quotient digits a division computes: the precision plus one for rounding.</summary>
    private const int QuotientDigits = Decimal32Encoding.Precision + 1;

    // Every operation handles the special values the same way: a signaling NaN is invalid
    // and becomes quiet, and a quiet NaN passes through. The left operand is checked first.

    /// <summary>
    /// The NaN result of an operation with two operands, at least one of which is a NaN. A
    /// signaling NaN is chosen over a quiet one, and the left operand over the right.
    /// </summary>
    /// <param name="left">The encoded left operand.</param>
    /// <param name="right">The encoded right operand.</param>
    /// <param name="status">Receives InvalidOperation if either operand is a signaling NaN.</param>
    /// <returns>The chosen NaN, made quiet.</returns>
    public static uint PropagateNaN(uint left, uint right, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsSignalingNaN(left))
        {
            status |= Decimal32Status.InvalidOperation;
            return Decimal32Encoding.Quiet(left);
        }

        if (Decimal32Encoding.IsSignalingNaN(right))
        {
            status |= Decimal32Status.InvalidOperation;
            return Decimal32Encoding.Quiet(right);
        }

        return Decimal32Encoding.Quiet(Decimal32Encoding.IsNaN(left) ? left : right);
    }

    /// <summary>The NaN result of an operation with one operand, which is a NaN.</summary>
    /// <param name="value">The encoded NaN.</param>
    /// <param name="status">Receives InvalidOperation if the value is a signaling NaN.</param>
    /// <returns>The NaN, made quiet.</returns>
    public static uint PropagateNaN(uint value, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsSignalingNaN(value))
        {
            status |= Decimal32Status.InvalidOperation;
        }

        return Decimal32Encoding.Quiet(value);
    }

    /// <summary>The result of an invalid operation.</summary>
    /// <param name="status">Receives InvalidOperation.</param>
    /// <returns>The default quiet NaN.</returns>
    public static uint Invalid(ref Decimal32Status status)
    {
        status |= Decimal32Status.InvalidOperation;
        return Decimal32Encoding.QuietNaN();
    }

    // Addition and subtraction.

    /// <summary>Adds two values.</summary>
    /// <param name="left">The encoded left operand.</param>
    /// <param name="right">The encoded right operand.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded sum, rounded to the format.</returns>
    public static uint Add(uint left, uint right, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsSpecial(left) || Decimal32Encoding.IsSpecial(right))
        {
            if (Decimal32Encoding.IsNaN(left) || Decimal32Encoding.IsNaN(right))
            {
                return PropagateNaN(left, right, ref status);
            }

            return AddInfinity(left, right, ref status);
        }

        var leftCoefficient = Decimal32Encoding.Unpack(left, out var leftExponent);
        var rightCoefficient = Decimal32Encoding.Unpack(right, out var rightExponent);

        return AddFinite(Decimal32Encoding.IsNegative(left), leftCoefficient, leftExponent,
            Decimal32Encoding.IsNegative(right), rightCoefficient, rightExponent, rounding, ref status);
    }

    /// <summary>
    /// Subtraction is addition with the right operand's sign flipped. The sign is flipped
    /// after the NaNs are handled, because a NaN keeps its original sign.
    /// </summary>
    /// <param name="left">The encoded value to subtract from.</param>
    /// <param name="right">The encoded value to subtract.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded difference, rounded to the format.</returns>
    public static uint Subtract(uint left, uint right, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsSpecial(left) || Decimal32Encoding.IsSpecial(right))
        {
            if (Decimal32Encoding.IsNaN(left) || Decimal32Encoding.IsNaN(right))
            {
                return PropagateNaN(left, right, ref status);
            }

            return AddInfinity(left, right ^ Decimal32Encoding.SignMask, ref status);
        }

        var leftCoefficient = Decimal32Encoding.Unpack(left, out var leftExponent);
        var rightCoefficient = Decimal32Encoding.Unpack(right, out var rightExponent);

        return AddFinite(Decimal32Encoding.IsNegative(left), leftCoefficient, leftExponent,
            !Decimal32Encoding.IsNegative(right), rightCoefficient, rightExponent, rounding, ref status);
    }

    private static uint AddInfinity(uint left, uint right, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsInfinity(left))
        {
            if (Decimal32Encoding.IsInfinity(right)
                && Decimal32Encoding.IsNegative(left) != Decimal32Encoding.IsNegative(right))
            {
                // The sum of infinities with opposite signs is invalid.
                return Invalid(ref status);
            }

            return Decimal32Encoding.Infinity(Decimal32Encoding.IsNegative(left));
        }

        return Decimal32Encoding.Infinity(Decimal32Encoding.IsNegative(right));
    }

    /// <summary>
    /// Adds two finite values, each given as a sign, a coefficient of at most 7 digits, and
    /// an exponent.
    /// </summary>
    /// <param name="leftNegative">Whether the left operand is negative.</param>
    /// <param name="leftCoefficient">The left operand's coefficient.</param>
    /// <param name="leftExponent">The left operand's exponent.</param>
    /// <param name="rightNegative">Whether the right operand is negative.</param>
    /// <param name="rightCoefficient">The right operand's coefficient.</param>
    /// <param name="rightExponent">The right operand's exponent.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded sum, rounded to the format.</returns>
    public static uint AddFinite(bool leftNegative, ulong leftCoefficient, int leftExponent,
        bool rightNegative, ulong rightCoefficient, int rightExponent, Decimal32Rounding rounding,
        ref Decimal32Status status)
    {
        if (leftCoefficient == 0 || rightCoefficient == 0)
        {
            return AddWithZero(leftNegative, leftCoefficient, leftExponent, rightNegative,
                rightCoefficient, rightExponent, rounding, ref status);
        }

        // The operand with the higher exponent is scaled. Put it on the left.
        if (leftExponent < rightExponent)
        {
            var heldNegative = leftNegative;
            var heldCoefficient = leftCoefficient;
            var heldExponent = leftExponent;
            leftNegative = rightNegative;
            leftCoefficient = rightCoefficient;
            leftExponent = rightExponent;
            rightNegative = heldNegative;
            rightCoefficient = heldCoefficient;
            rightExponent = heldExponent;
        }

        var distance = leftExponent - rightExponent;
        if (distance == 0)
        {
            if (leftNegative == rightNegative)
            {
                return Decimal32Finalizer.Finalize(leftNegative, leftCoefficient + rightCoefficient, leftExponent,
                    Decimal32Residue.Exact, rounding, ref status);
            }

            return SubtractAligned(leftNegative, leftCoefficient, rightNegative, rightCoefficient,
                leftExponent, rounding, ref status);
        }

        var leftDigits = Decimal32Tables.CountDigits(leftCoefficient);
        if (leftDigits + distance <= WideDigits)
        {
            var scaled = leftCoefficient * Decimal32Tables.PowerOfTen(distance);
            if (leftNegative == rightNegative)
            {
                return Decimal32Finalizer.Finalize(leftNegative, scaled + rightCoefficient, rightExponent,
                    Decimal32Residue.Exact, rounding, ref status);
            }

            return SubtractAligned(leftNegative, scaled, rightNegative, rightCoefficient, rightExponent,
                rounding, ref status);
        }

        // The exponents are too far apart to align exactly. The higher operand is scaled to
        // 19 digits, and the lower operand is folded to that exponent. The fold keeps
        // everything the rounding needs.
        var widen = WideDigits - leftDigits;
        var wide = leftCoefficient * Decimal32Tables.PowerOfTen(widen);
        var exponent = leftExponent - widen;
        var drop = distance - widen;

        ulong folded;
        Decimal32Residue residue;
        if (drop > Decimal32Encoding.Precision)
        {
            folded = 0;
            residue = Decimal32Residue.BelowHalf;
        }
        else
        {
            folded = Decimal32Tables.DivRemPowerOfTen(rightCoefficient, drop, out var discarded);
            residue = Decimal32Rounder.Of(discarded, Decimal32Tables.HalfPowerOfTen(drop));
        }

        if (leftNegative == rightNegative)
        {
            return Decimal32Finalizer.Finalize(leftNegative, wide + folded, exponent, residue, rounding, ref status);
        }

        if (residue == Decimal32Residue.Exact)
        {
            return Decimal32Finalizer.Finalize(leftNegative, wide - folded, exponent, Decimal32Residue.Exact,
                rounding, ref status);
        }

        // Subtracting an inexact operand subtracts one more unit. The residue is flipped to
        // describe what remains of that unit.
        return Decimal32Finalizer.Finalize(leftNegative, wide - folded - 1, exponent, Decimal32Rounder.Flip(residue),
            rounding, ref status);
    }

    /// <summary>
    /// The difference of two coefficients with the same exponent and opposite signs. The
    /// result takes the sign of the larger coefficient.
    /// </summary>
    private static uint SubtractAligned(bool leftNegative, ulong left, bool rightNegative, ulong right,
        int exponent, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        if (left == right)
        {
            // Opposite signs that cancel exactly give positive zero, except when rounding
            // toward negative infinity.
            return Decimal32Finalizer.Zero(rounding == Decimal32Rounding.Floor, exponent, ref status);
        }

        if (left > right)
        {
            return Decimal32Finalizer.Finalize(leftNegative, left - right, exponent, Decimal32Residue.Exact,
                rounding, ref status);
        }

        return Decimal32Finalizer.Finalize(rightNegative, right - left, exponent, Decimal32Residue.Exact,
            rounding, ref status);
    }

    /// <summary>
    /// Adds when at least one operand is zero. A zero operand contributes only its exponent,
    /// and only when that exponent is the lower one. The other operand is then padded with
    /// zeros toward it, as far as the precision allows.
    /// </summary>
    private static uint AddWithZero(bool leftNegative, ulong leftCoefficient, int leftExponent,
        bool rightNegative, ulong rightCoefficient, int rightExponent, Decimal32Rounding rounding,
        ref Decimal32Status status)
    {
        if (leftCoefficient == 0 && rightCoefficient == 0)
        {
            // Two zeros give the lower exponent. The result keeps their sign if they agree.
            // Otherwise the rounding mode chooses the sign.
            var negative = leftNegative == rightNegative
                ? leftNegative
                : rounding == Decimal32Rounding.Floor;

            return Decimal32Finalizer.Zero(negative, Math.Min(leftExponent, rightExponent), ref status);
        }

        bool otherNegative;
        ulong coefficient;
        int exponent;
        int zeroExponent;
        if (leftCoefficient == 0)
        {
            otherNegative = rightNegative;
            coefficient = rightCoefficient;
            exponent = rightExponent;
            zeroExponent = leftExponent;
        }
        else
        {
            otherNegative = leftNegative;
            coefficient = leftCoefficient;
            exponent = leftExponent;
            zeroExponent = rightExponent;
        }

        if (zeroExponent < exponent)
        {
            var pad = exponent - zeroExponent;
            var digits = Decimal32Tables.CountDigits(coefficient);
            if (digits + pad > Decimal32Encoding.Precision)
            {
                // Only this many zeros fit. The value is unchanged, but the exponent could not
                // reach the zero's exponent, which counts as rounding.
                pad = Decimal32Encoding.Precision - digits;
                status |= Decimal32Status.Rounded;
            }

            coefficient *= Decimal32Tables.PowerOfTen(pad);
            exponent -= pad;
        }

        return Decimal32Finalizer.Finalize(otherNegative, coefficient, exponent, Decimal32Residue.Exact, rounding, ref status);
    }

    /// <summary>
    /// Applies the context to a value without otherwise changing it, by adding it to a zero
    /// with the same exponent. That zero makes <c>plus -0</c> a positive zero under every
    /// rounding mode except Floor. <paramref name="negate"/> makes it a subtraction.
    /// </summary>
    /// <param name="value">The encoded operand.</param>
    /// <param name="negate">Whether the value is subtracted from the zero instead of added to it.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded result, rounded to the format.</returns>
    public static uint AddToZero(uint value, bool negate, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsSpecial(value))
        {
            if (Decimal32Encoding.IsNaN(value))
            {
                return PropagateNaN(value, ref status);
            }

            return Decimal32Encoding.Infinity(Decimal32Encoding.IsNegative(value) != negate);
        }

        var coefficient = Decimal32Encoding.Unpack(value, out var exponent);
        var negative = Decimal32Encoding.IsNegative(value) != negate;

        return AddFinite(false, 0, exponent, negative, coefficient, exponent, rounding, ref status);
    }

    // Multiplication.

    /// <summary>
    /// Multiplies two values. The product of two coefficients of at most 7 digits has at
    /// most 14 digits, so it is exact in a word, and the finalizer rounds it.
    /// </summary>
    /// <param name="left">The encoded left operand.</param>
    /// <param name="right">The encoded right operand.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded product, rounded to the format.</returns>
    public static uint Multiply(uint left, uint right, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsSpecial(left) || Decimal32Encoding.IsSpecial(right))
        {
            if (Decimal32Encoding.IsNaN(left) || Decimal32Encoding.IsNaN(right))
            {
                return PropagateNaN(left, right, ref status);
            }

            return MultiplyInfinity(left, right, ref status);
        }

        var leftCoefficient = Decimal32Encoding.Unpack(left, out var leftExponent);
        var rightCoefficient = Decimal32Encoding.Unpack(right, out var rightExponent);
        var negative = Decimal32Encoding.IsNegative(left) != Decimal32Encoding.IsNegative(right);

        return Decimal32Finalizer.Finalize(negative, (ulong)leftCoefficient * rightCoefficient,
            leftExponent + rightExponent, Decimal32Residue.Exact, rounding, ref status);
    }

    /// <summary>Multiplies when at least one operand is infinite and neither is a NaN.</summary>
    private static uint MultiplyInfinity(uint left, uint right, ref Decimal32Status status)
    {
        var leftInfinite = Decimal32Encoding.IsInfinity(left);
        var rightInfinite = Decimal32Encoding.IsInfinity(right);

        if ((leftInfinite && Decimal32Encoding.IsZero(right)) || (rightInfinite && Decimal32Encoding.IsZero(left)))
        {
            // An infinity times zero is invalid.
            return Invalid(ref status);
        }

        return Decimal32Encoding.Infinity(Decimal32Encoding.IsNegative(left) != Decimal32Encoding.IsNegative(right));
    }

    // Fused multiply-add.

    /// <summary>Multiplies two values and adds a third, with a single rounding.</summary>
    /// <param name="left">The encoded first multiplicand.</param>
    /// <param name="right">The encoded second multiplicand.</param>
    /// <param name="addend">The encoded value added to the product.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded value of <c>left * right + addend</c>, rounded once to the format.</returns>
    public static uint FusedMultiplyAdd(uint left, uint right, uint addend, Decimal32Rounding rounding,
        ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsSpecial(left) || Decimal32Encoding.IsSpecial(right) || Decimal32Encoding.IsSpecial(addend))
        {
            return FusedMultiplyAddSpecial(left, right, addend, ref status);
        }

        var leftCoefficient = Decimal32Encoding.Unpack(left, out var leftExponent);
        var rightCoefficient = Decimal32Encoding.Unpack(right, out var rightExponent);
        var addendCoefficient = Decimal32Encoding.Unpack(addend, out var addendExponent);
        var productNegative = Decimal32Encoding.IsNegative(left) != Decimal32Encoding.IsNegative(right);
        var productExponent = leftExponent + rightExponent;
        var addendNegative = Decimal32Encoding.IsNegative(addend);

        var product = (ulong)leftCoefficient * rightCoefficient;
        if (product <= Decimal32Encoding.MaxCoefficient)
        {
            // A product that fits in the format uses the ordinary addition, which pads it to
            // the precision when the addend is below it.
            return AddFinite(productNegative, product, productExponent, addendNegative,
                addendCoefficient, addendExponent, rounding, ref status);
        }

        return Decimal32Product.FusedAdd(productNegative, product, productExponent, addendNegative,
            addendCoefficient, addendExponent, rounding, ref status);
    }

    private static uint FusedMultiplyAddSpecial(uint left, uint right, uint addend, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsSignalingNaN(left))
        {
            status |= Decimal32Status.InvalidOperation;
            return Decimal32Encoding.Quiet(left);
        }

        if (Decimal32Encoding.IsSignalingNaN(right))
        {
            status |= Decimal32Status.InvalidOperation;
            return Decimal32Encoding.Quiet(right);
        }

        if (Decimal32Encoding.IsSignalingNaN(addend))
        {
            status |= Decimal32Status.InvalidOperation;
            return Decimal32Encoding.Quiet(addend);
        }

        // An invalid multiplication is reported even when the addend is a quiet NaN, so the
        // product is checked before the addend's NaN.
        if (!Decimal32Encoding.IsNaN(left) && !Decimal32Encoding.IsNaN(right)
            && (Decimal32Encoding.IsInfinity(left) || Decimal32Encoding.IsInfinity(right)))
        {
            var product = MultiplyInfinity(left, right, ref status);
            if (Decimal32Encoding.IsNaN(addend))
            {
                return Decimal32Encoding.Quiet(addend);
            }

            if (Decimal32Encoding.IsInfinity(product))
            {
                // An infinite product is still added to the addend. If the addend is an
                // infinity of the opposite sign, the addition is invalid.
                return AddInfinity(product, addend, ref status);
            }

            return product;
        }

        if (Decimal32Encoding.IsNaN(left))
        {
            return Decimal32Encoding.Quiet(left);
        }

        if (Decimal32Encoding.IsNaN(right))
        {
            return Decimal32Encoding.Quiet(right);
        }

        if (Decimal32Encoding.IsNaN(addend))
        {
            return Decimal32Encoding.Quiet(addend);
        }

        // The multiplicands are finite, so the addend is the infinity.
        return Decimal32Encoding.Infinity(Decimal32Encoding.IsNegative(addend));
    }

    // Division and its relatives.

    /// <summary>
    /// Divides two values. An exact quotient takes the exponent closest to the difference of
    /// the operands' exponents.
    /// </summary>
    /// <param name="left">The encoded dividend.</param>
    /// <param name="right">The encoded divisor.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded quotient, rounded to the format.</returns>
    public static uint Divide(uint left, uint right, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsSpecial(left) || Decimal32Encoding.IsSpecial(right))
        {
            return DivideSpecial(left, right, Decimal32DivisionKind.Divide, ref status);
        }

        var leftCoefficient = Decimal32Encoding.Unpack(left, out var leftExponent);
        var rightCoefficient = Decimal32Encoding.Unpack(right, out var rightExponent);
        var negative = Decimal32Encoding.IsNegative(left) != Decimal32Encoding.IsNegative(right);

        if (rightCoefficient == 0)
        {
            if (leftCoefficient == 0)
            {
                status |= Decimal32Status.DivisionUndefined;
                return Decimal32Encoding.QuietNaN();
            }

            status |= Decimal32Status.DivisionByZero;
            return Decimal32Encoding.Infinity(negative);
        }

        var idealExponent = leftExponent - rightExponent;
        if (leftCoefficient == 0)
        {
            return Decimal32Finalizer.Zero(negative, idealExponent, ref status);
        }

        return DivideFinite(negative, leftCoefficient, rightCoefficient, idealExponent, rounding, ref status);
    }

    /// <summary>
    /// Computes the quotient to 7 or 8 digits, and uses the remainder to set the residue.
    /// The dividend is scaled by 10 to the power <c>7 + divisor digits - dividend digits</c>,
    /// which puts the quotient between 10^6 and 10^8. With 7 digits, the remainder alone
    /// decides the rounding. With 8 digits, the finalizer drops the extra digit and uses the
    /// remainder's residue below it. The scaled dividend has at most 14 digits.
    /// </summary>
    private static uint DivideFinite(bool negative, ulong dividend, ulong divisor, int idealExponent,
        Decimal32Rounding rounding, ref Decimal32Status status)
    {
        var dividendDigits = Decimal32Tables.CountDigits(dividend);
        var divisorDigits = Decimal32Tables.CountDigits(divisor);
        var scale = Decimal32Encoding.Precision + divisorDigits - dividendDigits;

        var scaled = dividend * Decimal32Tables.PowerOfTen(scale);
        var quotient = Decimal32Divider.Divide(scaled, divisor, out var remainder);
        var exponent = idealExponent - scale;

        if (remainder == 0)
        {
            // The quotient is exact, so the specification requires the exponent closest to
            // the ideal. Remove the trailing zeros the scaling added, and no more.
            Decimal32Shaping.StripTrailingZeros(ref quotient, ref exponent, idealExponent);
            return Decimal32Finalizer.Finalize(negative, quotient, exponent, Decimal32Residue.Exact, rounding, ref status);
        }

        // Comparing twice the remainder with the divisor shows whether the remainder is
        // below, at, or above half.
        var doubled = remainder * 2;
        var residue = doubled < divisor
            ? Decimal32Residue.BelowHalf
            : doubled == divisor ? Decimal32Residue.Half : Decimal32Residue.AboveHalf;

        return Decimal32Finalizer.Finalize(negative, quotient, exponent, residue, rounding, ref status);
    }

    /// <summary>The integer part of the quotient, with exponent zero.</summary>
    /// <param name="left">The encoded dividend.</param>
    /// <param name="right">The encoded divisor.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded integer quotient, or a quiet NaN if it does not fit in the precision.</returns>
    public static uint DivideInteger(uint left, uint right, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsSpecial(left) || Decimal32Encoding.IsSpecial(right))
        {
            return DivideSpecial(left, right, Decimal32DivisionKind.DivideInteger, ref status);
        }

        var leftCoefficient = Decimal32Encoding.Unpack(left, out var leftExponent);
        var rightCoefficient = Decimal32Encoding.Unpack(right, out var rightExponent);
        var negative = Decimal32Encoding.IsNegative(left) != Decimal32Encoding.IsNegative(right);

        if (rightCoefficient == 0)
        {
            if (leftCoefficient == 0)
            {
                status |= Decimal32Status.DivisionUndefined;
                return Decimal32Encoding.QuietNaN();
            }

            status |= Decimal32Status.DivisionByZero;
            return Decimal32Encoding.Infinity(negative);
        }

        if (leftCoefficient == 0)
        {
            return Decimal32Encoding.Zero(negative, 0);
        }

        if (!IntegerDivide(leftCoefficient, leftExponent, rightCoefficient, rightExponent,
            out var quotient, out _, out _, out _))
        {
            status |= Decimal32Status.DivisionImpossible;
            return Decimal32Encoding.QuietNaN();
        }

        return Decimal32Finalizer.Finalize(negative, quotient, 0, Decimal32Residue.Exact, rounding, ref status);
    }

    /// <summary>
    /// The exact remainder after dividing by the integer quotient. The near form rounds the
    /// quotient to the nearest integer instead of truncating it, so the remainder can have
    /// the opposite sign.
    /// </summary>
    /// <param name="left">The encoded dividend.</param>
    /// <param name="right">The encoded divisor.</param>
    /// <param name="near">Whether the quotient is rounded to the nearest integer instead of truncated.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded remainder, or a quiet NaN if the integer quotient does not fit in the precision.</returns>
    public static uint Remainder(uint left, uint right, bool near, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        var kind = near ? Decimal32DivisionKind.RemainderNear : Decimal32DivisionKind.Remainder;

        if (Decimal32Encoding.IsSpecial(left) || Decimal32Encoding.IsSpecial(right))
        {
            return DivideSpecial(left, right, kind, ref status);
        }

        var leftCoefficient = Decimal32Encoding.Unpack(left, out var leftExponent);
        var rightCoefficient = Decimal32Encoding.Unpack(right, out var rightExponent);
        var negative = Decimal32Encoding.IsNegative(left);

        if (rightCoefficient == 0)
        {
            if (leftCoefficient == 0)
            {
                status |= Decimal32Status.DivisionUndefined;
                return Decimal32Encoding.QuietNaN();
            }

            return Invalid(ref status);
        }

        if (leftCoefficient == 0)
        {
            // A zero dividend is its own remainder. It keeps its sign and takes the lower of
            // the two exponents.
            return Decimal32Finalizer.Zero(negative, Math.Min(leftExponent, rightExponent), ref status);
        }

        if (!IntegerDivide(leftCoefficient, leftExponent, rightCoefficient, rightExponent,
            out var quotient, out var remainder, out var remainderExponent, out var scaledDivisor))
        {
            status |= Decimal32Status.DivisionImpossible;
            return Decimal32Encoding.QuietNaN();
        }

        if (near && scaledDivisor != 0)
        {
            // The nearest integer quotient can be one higher. Then the remainder is measured
            // from that quotient and changes sign. A tie goes to the even quotient. The
            // remainder is compared with (divisor - remainder) instead of being doubled,
            // because a divisor scaled to 19 digits leaves no room to double.
            var other = scaledDivisor - remainder;
            if (remainder > other || (remainder == other && (quotient & 1) != 0))
            {
                if (quotient == Decimal32Encoding.MaxCoefficient)
                {
                    status |= Decimal32Status.DivisionImpossible;
                    return Decimal32Encoding.QuietNaN();
                }

                remainder = scaledDivisor - remainder;
                negative = !negative;
            }
        }

        if (remainder == 0)
        {
            // A zero remainder takes the dividend's sign.
            return Decimal32Finalizer.Zero(Decimal32Encoding.IsNegative(left), remainderExponent, ref status);
        }

        return Decimal32Finalizer.Finalize(negative, remainder, remainderExponent, Decimal32Residue.Exact, rounding, ref status);
    }

    /// <summary>
    /// The exact integer quotient and remainder of two finite non-zero values. The quotient
    /// must fit in the precision. If it does not, the method returns false and the division
    /// is impossible. The remainder is returned at the lower of the two exponents.
    /// <paramref name="scaledDivisor"/> is the divisor scaled to that exponent when it fits
    /// in a word. Otherwise it is zero, and the remainder is the whole dividend, which is
    /// below half the divisor.
    /// </summary>
    private static bool IntegerDivide(ulong dividend, int dividendExponent, ulong divisor, int divisorExponent,
        out ulong quotient, out ulong remainder, out int remainderExponent, out ulong scaledDivisor)
    {
        var dividendDigits = Decimal32Tables.CountDigits(dividend);
        var divisorDigits = Decimal32Tables.CountDigits(divisor);

        if (dividendExponent >= divisorExponent)
        {
            var scale = dividendExponent - divisorExponent;
            remainderExponent = divisorExponent;
            scaledDivisor = divisor;

            // The dividend scaled to the divisor's exponent has this many digits. If it has
            // 8 or more digits than the divisor, the quotient cannot fit. Otherwise it has at
            // most 14 digits, which the divider handles in one step.
            var wideDigits = dividendDigits + scale;
            if (wideDigits - divisorDigits >= QuotientDigits)
            {
                quotient = 0;
                remainder = 0;
                return false;
            }

            var wide = dividend * Decimal32Tables.PowerOfTen(scale);
            quotient = Decimal32Divider.Divide(wide, divisor, out remainder);
            return quotient <= Decimal32Encoding.MaxCoefficient;
        }

        var lift = divisorExponent - dividendExponent;
        remainderExponent = dividendExponent;

        if (divisorDigits + lift > WideDigits)
        {
            // The divisor scaled to the dividend's exponent does not fit in a word, so it is
            // larger than the dividend, and the quotient is zero.
            quotient = 0;
            remainder = dividend;
            scaledDivisor = 0;
            return true;
        }

        scaledDivisor = divisor * Decimal32Tables.PowerOfTen(lift);
        if (dividend < scaledDivisor)
        {
            quotient = 0;
            remainder = dividend;
            return true;
        }

        // Both values have at most 7 digits here, so the divider handles them.
        quotient = Decimal32Divider.Divide(dividend, scaledDivisor, out remainder);
        return true;
    }

    /// <summary>
    /// Handles infinities and NaNs for the four division operations.
    /// </summary>
    private static uint DivideSpecial(uint left, uint right, Decimal32DivisionKind kind, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsNaN(left) || Decimal32Encoding.IsNaN(right))
        {
            return PropagateNaN(left, right, ref status);
        }

        var isRemainder = kind is Decimal32DivisionKind.Remainder or Decimal32DivisionKind.RemainderNear;
        var negative = Decimal32Encoding.IsNegative(left) != Decimal32Encoding.IsNegative(right);

        if (Decimal32Encoding.IsInfinity(left))
        {
            if (Decimal32Encoding.IsInfinity(right) || isRemainder)
            {
                // An infinity divided by an infinity is invalid, and so is the remainder of
                // an infinity.
                return Invalid(ref status);
            }

            // An infinity divided by a finite value is infinite, even when dividing by zero.
            return Decimal32Encoding.Infinity(negative);
        }

        // The divisor is the infinity.
        if (isRemainder)
        {
            // The quotient is zero, so the remainder is the whole dividend.
            return Decimal32Encoding.Canonical(left);
        }

        if (kind == Decimal32DivisionKind.DivideInteger)
        {
            // The integer quotient is zero. An integer result has exponent zero and is not
            // clamped.
            return Decimal32Encoding.Zero(negative, 0);
        }

        // A finite value divided by an infinity is a zero with an unbounded negative
        // exponent. It is clamped to the smallest exponent the format holds.
        status |= Decimal32Status.Clamped;
        return Decimal32Encoding.Zero(negative, Decimal32Encoding.MinQuantumExponent);
    }

    // Comparison.

    /// <summary>
    /// Compares two values numerically. Returns -1, 0, or 1, or <see cref="int.MinValue"/>
    /// when the values are unordered, with the resulting NaN in <paramref name="nan"/>.
    /// Positive and negative zero are equal here, unlike in the total order.
    /// </summary>
    /// <param name="left">The encoded left operand.</param>
    /// <param name="right">The encoded right operand.</param>
    /// <param name="signaling">Whether a quiet NaN operand also raises InvalidOperation, not only a signaling one.</param>
    /// <param name="status">Receives the conditions the comparison raises.</param>
    /// <param name="nan">Receives the quiet NaN result when the values are unordered, and zero otherwise.</param>
    /// <returns>-1, 0, or 1 as the left value is less than, equal to, or greater than the right, or <see cref="int.MinValue"/> when they are unordered.</returns>
    public static int Compare(uint left, uint right, bool signaling, ref Decimal32Status status, out uint nan)
    {
        nan = 0;

        if (Decimal32Encoding.IsSpecial(left) || Decimal32Encoding.IsSpecial(right))
        {
            if (Decimal32Encoding.IsNaN(left) || Decimal32Encoding.IsNaN(right))
            {
                // The quiet comparison reports only a signaling NaN. The signaling comparison
                // reports every NaN.
                if (signaling)
                {
                    status |= Decimal32Status.InvalidOperation;
                }

                nan = PropagateNaN(left, right, ref status);
                return int.MinValue;
            }

            return CompareInfinity(left, right);
        }

        var leftCoefficient = Decimal32Encoding.Unpack(left, out var leftExponent);
        var rightCoefficient = Decimal32Encoding.Unpack(right, out var rightExponent);

        return CompareFinite(Decimal32Encoding.IsNegative(left), leftCoefficient, leftExponent,
            Decimal32Encoding.IsNegative(right), rightCoefficient, rightExponent);
    }

    /// <summary>
    /// Compares two values when at least one is infinite and neither is a NaN. An infinity
    /// is beyond every finite value of its sign, and two infinities of the same sign are
    /// equal.
    /// </summary>
    /// <param name="left">The encoded left operand.</param>
    /// <param name="right">The encoded right operand.</param>
    /// <returns>-1, 0, or 1 as the left value is less than, equal to, or greater than the right.</returns>
    public static int CompareInfinity(uint left, uint right)
    {
        if (Decimal32Encoding.IsInfinity(left) && Decimal32Encoding.IsInfinity(right))
        {
            if (Decimal32Encoding.IsNegative(left) == Decimal32Encoding.IsNegative(right))
            {
                return 0;
            }

            return Decimal32Encoding.IsNegative(left) ? -1 : 1;
        }

        if (Decimal32Encoding.IsInfinity(left))
        {
            return Decimal32Encoding.IsNegative(left) ? -1 : 1;
        }

        return Decimal32Encoding.IsNegative(right) ? 1 : -1;
    }

    /// <summary>
    /// Compares two finite values numerically. Every zero is equal to every other zero,
    /// whatever its sign or exponent.
    /// </summary>
    /// <param name="leftNegative">Whether the left operand is negative.</param>
    /// <param name="leftCoefficient">The left operand's coefficient.</param>
    /// <param name="leftExponent">The left operand's exponent.</param>
    /// <param name="rightNegative">Whether the right operand is negative.</param>
    /// <param name="rightCoefficient">The right operand's coefficient.</param>
    /// <param name="rightExponent">The right operand's exponent.</param>
    /// <returns>-1, 0, or 1 as the left value is less than, equal to, or greater than the right.</returns>
    public static int CompareFinite(bool leftNegative, ulong leftCoefficient, int leftExponent,
        bool rightNegative, ulong rightCoefficient, int rightExponent)
    {
        if (leftCoefficient == 0)
        {
            if (rightCoefficient == 0)
            {
                return 0;
            }

            return rightNegative ? 1 : -1;
        }

        if (rightCoefficient == 0)
        {
            return leftNegative ? -1 : 1;
        }

        if (leftNegative != rightNegative)
        {
            return leftNegative ? -1 : 1;
        }

        var magnitude = CompareMagnitude(leftCoefficient, leftExponent, rightCoefficient, rightExponent);
        return leftNegative ? -magnitude : magnitude;
    }

    /// <summary>
    /// Compares the magnitudes of two non-zero values. The positions of the leading digits
    /// decide the order unless they are equal. Then the coefficients are aligned, which
    /// stays within 7 digits because their leading digits are in the same position.
    /// </summary>
    /// <param name="leftCoefficient">The left operand's coefficient.</param>
    /// <param name="leftExponent">The left operand's exponent.</param>
    /// <param name="rightCoefficient">The right operand's coefficient.</param>
    /// <param name="rightExponent">The right operand's exponent.</param>
    /// <returns>-1, 0, or 1 as the left magnitude is less than, equal to, or greater than the right.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CompareMagnitude(ulong leftCoefficient, int leftExponent, ulong rightCoefficient,
        int rightExponent)
    {
        var leftTop = leftExponent + Decimal32Tables.CountDigits(leftCoefficient);
        var rightTop = rightExponent + Decimal32Tables.CountDigits(rightCoefficient);

        if (leftTop != rightTop)
        {
            return leftTop < rightTop ? -1 : 1;
        }

        if (leftExponent > rightExponent)
        {
            leftCoefficient *= Decimal32Tables.PowerOfTen(leftExponent - rightExponent);
        }
        else if (rightExponent > leftExponent)
        {
            rightCoefficient *= Decimal32Tables.PowerOfTen(rightExponent - leftExponent);
        }

        if (leftCoefficient == rightCoefficient)
        {
            return 0;
        }

        return leftCoefficient < rightCoefficient ? -1 : 1;
    }
}
