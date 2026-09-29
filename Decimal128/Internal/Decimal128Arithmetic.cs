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
/// Every path here works on a coefficient in two 64-bit words and an exponent in an int.
/// Two intermediate values do not fit in two words: an operand whose exponent is far above
/// the other's, and the product of two 34-digit coefficients. Each is reduced to what fits
/// alongside the other operand, plus a <see cref="Decimal128Residue"/>, before the
/// addition, and the finalizer then rounds once.
/// </para>
/// <para>
/// Alignment scales the operand with the higher exponent. Scaling it down to the lower
/// exponent is exact while the result fits in the precision, or, for a difference, which
/// can cancel, while it fits in two words. Beyond that, it is scaled to exactly 34 digits,
/// and the lower operand is folded to that exponent instead, with its discarded digits
/// becoming the residue. The sum then fits in the format, with at most a carry to drop.
/// Only the lower operand is ever folded. For a difference it is folded by at least five
/// digits, so at most one leading digit can cancel, and folding one digit less corrects
/// that.
/// </para>
/// </remarks>
internal static class Decimal128Arithmetic
{
    private const int WideDigits = Decimal128Tables.MaxWidePower;

    /// <summary>The number of quotient digits a division computes: the precision plus one for rounding.</summary>
    private const int QuotientDigits = Decimal128Encoding.Precision + 1;

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
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Decimal128Integer PropagateNaN(Decimal128Integer left, Decimal128Integer right,
        ref Decimal128Status status)
    {
        if (Decimal128Encoding.IsSignalingNaN(left))
        {
            status |= Decimal128Status.InvalidOperation;
            return Decimal128Encoding.Quiet(left);
        }

        if (Decimal128Encoding.IsSignalingNaN(right))
        {
            status |= Decimal128Status.InvalidOperation;
            return Decimal128Encoding.Quiet(right);
        }

        return Decimal128Encoding.Quiet(Decimal128Encoding.IsNaN(left) ? left : right);
    }

    /// <summary>The NaN result of an operation with one operand, which is a NaN.</summary>
    /// <param name="value">The encoded NaN.</param>
    /// <param name="status">Receives InvalidOperation if the value is a signaling NaN.</param>
    /// <returns>The NaN, made quiet.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Decimal128Integer PropagateNaN(Decimal128Integer value, ref Decimal128Status status)
    {
        if (Decimal128Encoding.IsSignalingNaN(value))
        {
            status |= Decimal128Status.InvalidOperation;
        }

        return Decimal128Encoding.Quiet(value);
    }

    /// <summary>The result of an invalid operation.</summary>
    /// <param name="status">Receives InvalidOperation.</param>
    /// <returns>The default quiet NaN.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Decimal128Integer Invalid(ref Decimal128Status status)
    {
        status |= Decimal128Status.InvalidOperation;
        return Decimal128Encoding.QuietNaN();
    }

    // Addition and subtraction. These methods are not inlined into the public type's
    // wrappers, so each one starts its own inlining budget and the two-word operators
    // inside it are inlined. The rare paths for special values are separate methods for the
    // same reason.

    /// <summary>Adds two values.</summary>
    /// <param name="left">The encoded left operand.</param>
    /// <param name="right">The encoded right operand.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded sum, rounded to the format.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Decimal128Integer Add(Decimal128Integer left, Decimal128Integer right, Decimal128Rounding rounding,
        ref Decimal128Status status)
    {
        if (Decimal128Encoding.IsSpecial(left) || Decimal128Encoding.IsSpecial(right))
        {
            if (Decimal128Encoding.IsNaN(left) || Decimal128Encoding.IsNaN(right))
            {
                return PropagateNaN(left, right, ref status);
            }

            return AddInfinity(left, right, ref status);
        }

        var leftCoefficient = Decimal128Encoding.Unpack(left, out var leftExponent);
        var rightCoefficient = Decimal128Encoding.Unpack(right, out var rightExponent);

        return AddFinite(Decimal128Encoding.IsNegative(left), leftCoefficient, leftExponent,
            Decimal128Encoding.IsNegative(right), rightCoefficient, rightExponent, rounding, ref status);
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
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Decimal128Integer Subtract(Decimal128Integer left, Decimal128Integer right,
        Decimal128Rounding rounding, ref Decimal128Status status)
    {
        if (Decimal128Encoding.IsSpecial(left) || Decimal128Encoding.IsSpecial(right))
        {
            if (Decimal128Encoding.IsNaN(left) || Decimal128Encoding.IsNaN(right))
            {
                return PropagateNaN(left, right, ref status);
            }

            var flipped = new Decimal128Integer(right.High ^ Decimal128Encoding.SignMask, right.Low);
            return AddInfinity(left, flipped, ref status);
        }

        var leftCoefficient = Decimal128Encoding.Unpack(left, out var leftExponent);
        var rightCoefficient = Decimal128Encoding.Unpack(right, out var rightExponent);

        return AddFinite(Decimal128Encoding.IsNegative(left), leftCoefficient, leftExponent,
            !Decimal128Encoding.IsNegative(right), rightCoefficient, rightExponent, rounding, ref status);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Decimal128Integer AddInfinity(Decimal128Integer left, Decimal128Integer right,
        ref Decimal128Status status)
    {
        if (Decimal128Encoding.IsInfinity(left))
        {
            if (Decimal128Encoding.IsInfinity(right)
                && Decimal128Encoding.IsNegative(left) != Decimal128Encoding.IsNegative(right))
            {
                // The sum of infinities with opposite signs is invalid.
                return Invalid(ref status);
            }

            return Decimal128Encoding.Infinity(Decimal128Encoding.IsNegative(left));
        }

        return Decimal128Encoding.Infinity(Decimal128Encoding.IsNegative(right));
    }

    /// <summary>
    /// Adds two finite values, each given as a sign, a coefficient of at most 34 digits,
    /// and an exponent.
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
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Decimal128Integer AddFinite(bool leftNegative, Decimal128Integer leftCoefficient, int leftExponent,
        bool rightNegative, Decimal128Integer rightCoefficient, int rightExponent, Decimal128Rounding rounding,
        ref Decimal128Status status)
    {
        if (leftCoefficient.IsZero || rightCoefficient.IsZero)
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
                return Decimal128Finalizer.Finalize(leftNegative, leftCoefficient + rightCoefficient, leftExponent,
                    Decimal128Residue.Exact, rounding, ref status);
            }

            return SubtractAligned(leftNegative, leftCoefficient, rightNegative, rightCoefficient,
                leftExponent, rounding, ref status);
        }

        // Compute how many digits the left operand has when scaled down to the right
        // operand's exponent. Within the precision, the sum is exact, with at most a carry to
        // drop. A difference is exact while it fits in two words. It can cancel any number
        // of digits, so it is computed in full.
        var leftDigits = Decimal128Tables.CountDigits(leftCoefficient);
        var reach = leftDigits + distance;
        var sameSign = leftNegative == rightNegative;

        if (reach <= Decimal128Encoding.Precision || (!sameSign && reach <= WideDigits))
        {
            var scaled = Decimal128Tables.Scale(leftCoefficient, distance);
            if (sameSign)
            {
                return Decimal128Finalizer.Finalize(leftNegative, scaled + rightCoefficient, rightExponent,
                    Decimal128Residue.Exact, rounding, ref status);
            }

            return SubtractAligned(leftNegative, scaled, rightNegative, rightCoefficient, rightExponent,
                rounding, ref status);
        }

        // The left operand is scaled to exactly the precision, and the right operand is
        // folded to that exponent, with its discarded digits becoming the residue. The sum
        // then has 34 or 35 digits, and the finalizer drops the carry digit and uses the
        // residue below it.
        var widen = Decimal128Encoding.Precision - leftDigits;
        var wide = Decimal128Tables.Scale(leftCoefficient, widen);
        var exponent = leftExponent - widen;
        var drop = distance - widen;

        Decimal128Integer folded;
        Decimal128Residue residue;
        if (drop > Decimal128Encoding.Precision)
        {
            folded = Decimal128Integer.Zero;
            residue = Decimal128Residue.BelowHalf;
        }
        else
        {
            folded = Decimal128Rounder.DropDigits(rightCoefficient, drop, Decimal128Residue.Exact, out residue);
        }

        // Digits of the right operand were discarded. The specification counts that as
        // rounding, even if they are zeros.
        status |= Decimal128Status.Rounded;

        if (sameSign)
        {
            return Decimal128Finalizer.Finalize(leftNegative, wide + folded, exponent, residue, rounding, ref status);
        }

        // A difference only reaches this point when it does not fit in two words, so the
        // fold is at least five digits, and the folded value has at most 29 digits. The
        // difference keeps at least 33 digits, and 34 unless the left operand is a 1
        // followed by zeros. Subtracting an inexact operand subtracts one more unit, and the
        // residue is flipped to describe what remains of that unit.
        var difference = wide - folded;
        if (residue != Decimal128Residue.Exact)
        {
            difference -= 1;
            residue = Decimal128Rounder.Flip(residue);
        }

        if (difference >= Decimal128Tables.WidePowerOfTen(Decimal128Encoding.Precision - 1))
        {
            return Decimal128Finalizer.Finalize(leftNegative, difference, exponent, residue, rounding, ref status);
        }

        return SubtractCancelled(leftNegative, wide, exponent, rightCoefficient, drop, rounding, ref status);
    }

    /// <summary>
    /// Recomputes a difference that cancelled its leading digit, one digit lower. The left
    /// operand, already at the precision, is scaled by one more digit, and the right operand
    /// is folded by one digit less. For the cancellation to happen, the left operand was
    /// within 10^29 of 10^33, so after scaling it is below 10^34 + 10^30. The right operand,
    /// folded by at least four digits, is below 10^30. So the difference has 34 digits.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Decimal128Integer SubtractCancelled(bool negative, Decimal128Integer wide, int exponent,
        Decimal128Integer rightCoefficient, int drop, Decimal128Rounding rounding, ref Decimal128Status status)
    {
        var wider = wide.MultiplyBy(10);
        exponent--;
        drop--;

        Decimal128Integer folded;
        Decimal128Residue residue;
        if (drop > Decimal128Encoding.Precision)
        {
            folded = Decimal128Integer.Zero;
            residue = Decimal128Residue.BelowHalf;
        }
        else
        {
            folded = Decimal128Rounder.DropDigits(rightCoefficient, drop, Decimal128Residue.Exact, out residue);
        }

        var difference = wider - folded;
        if (residue != Decimal128Residue.Exact)
        {
            difference -= 1;
            residue = Decimal128Rounder.Flip(residue);
        }

        return Decimal128Finalizer.Finalize(negative, difference, exponent, residue, rounding, ref status);
    }

    /// <summary>
    /// The difference of two coefficients with the same exponent and opposite signs. The
    /// result takes the sign of the larger coefficient.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Decimal128Integer SubtractAligned(bool leftNegative, Decimal128Integer left, bool rightNegative,
        Decimal128Integer right, int exponent, Decimal128Rounding rounding, ref Decimal128Status status)
    {
        if (left == right)
        {
            // Opposite signs that cancel exactly give positive zero, except when rounding
            // toward negative infinity.
            return Decimal128Finalizer.Zero(rounding == Decimal128Rounding.Floor, exponent, ref status);
        }

        if (left > right)
        {
            return Decimal128Finalizer.Finalize(leftNegative, left - right, exponent, Decimal128Residue.Exact,
                rounding, ref status);
        }

        return Decimal128Finalizer.Finalize(rightNegative, right - left, exponent, Decimal128Residue.Exact,
            rounding, ref status);
    }

    /// <summary>
    /// Adds when at least one operand is zero. A zero operand contributes only its exponent,
    /// and only when that exponent is the lower one. The other operand is then padded with
    /// zeros toward it, as far as the precision allows.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Decimal128Integer AddWithZero(bool leftNegative, Decimal128Integer leftCoefficient, int leftExponent,
        bool rightNegative, Decimal128Integer rightCoefficient, int rightExponent, Decimal128Rounding rounding,
        ref Decimal128Status status)
    {
        if (leftCoefficient.IsZero && rightCoefficient.IsZero)
        {
            // Two zeros give the lower exponent. The result keeps their sign if they agree.
            // Otherwise the rounding mode chooses the sign.
            var negative = leftNegative == rightNegative
                ? leftNegative
                : rounding == Decimal128Rounding.Floor;

            return Decimal128Finalizer.Zero(negative, Math.Min(leftExponent, rightExponent), ref status);
        }

        bool otherNegative;
        Decimal128Integer coefficient;
        int exponent;
        int zeroExponent;
        if (leftCoefficient.IsZero)
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
            var digits = Decimal128Tables.CountDigits(coefficient);
            if (digits + pad > Decimal128Encoding.Precision)
            {
                // Only this many zeros fit. The value is unchanged, but the exponent could not
                // reach the zero's exponent, which counts as rounding.
                pad = Decimal128Encoding.Precision - digits;
                status |= Decimal128Status.Rounded;
            }

            coefficient = Decimal128Tables.Scale(coefficient, pad);
            exponent -= pad;
        }

        return Decimal128Finalizer.Finalize(otherNegative, coefficient, exponent, Decimal128Residue.Exact, rounding,
            ref status);
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
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Decimal128Integer AddToZero(Decimal128Integer value, bool negate, Decimal128Rounding rounding,
        ref Decimal128Status status)
    {
        if (Decimal128Encoding.IsSpecial(value))
        {
            if (Decimal128Encoding.IsNaN(value))
            {
                return PropagateNaN(value, ref status);
            }

            return Decimal128Encoding.Infinity(Decimal128Encoding.IsNegative(value) != negate);
        }

        var coefficient = Decimal128Encoding.Unpack(value, out var exponent);
        var negative = Decimal128Encoding.IsNegative(value) != negate;

        return AddFinite(false, Decimal128Integer.Zero, exponent, negative, coefficient, exponent, rounding, ref status);
    }

    // Multiplication.

    /// <summary>Multiplies two values.</summary>
    /// <param name="left">The encoded left operand.</param>
    /// <param name="right">The encoded right operand.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded product, rounded to the format.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Decimal128Integer Multiply(Decimal128Integer left, Decimal128Integer right,
        Decimal128Rounding rounding, ref Decimal128Status status)
    {
        if (Decimal128Encoding.IsSpecial(left) || Decimal128Encoding.IsSpecial(right))
        {
            if (Decimal128Encoding.IsNaN(left) || Decimal128Encoding.IsNaN(right))
            {
                return PropagateNaN(left, right, ref status);
            }

            return MultiplyInfinity(left, right, ref status);
        }

        var leftCoefficient = Decimal128Encoding.Unpack(left, out var leftExponent);
        var rightCoefficient = Decimal128Encoding.Unpack(right, out var rightExponent);
        var negative = Decimal128Encoding.IsNegative(left) != Decimal128Encoding.IsNegative(right);
        var exponent = leftExponent + rightExponent;

        if ((leftCoefficient.High | rightCoefficient.High) == 0)
        {
            // Both coefficients fit in one word, so the product fits in two words and is exact.
            var high = Math.BigMul(leftCoefficient.Low, rightCoefficient.Low, out var low);
            return Decimal128Finalizer.Finalize(negative, new Decimal128Integer(high, low), exponent,
                Decimal128Residue.Exact, rounding, ref status);
        }

        var product = Decimal128LongInteger.Multiply(leftCoefficient, rightCoefficient);
        return Decimal128Product.Reduce(negative, product, exponent, Decimal128Residue.Exact, rounding, ref status);
    }

    /// <summary>Multiplies when at least one operand is infinite and neither is a NaN.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Decimal128Integer MultiplyInfinity(Decimal128Integer left, Decimal128Integer right,
        ref Decimal128Status status)
    {
        var leftInfinite = Decimal128Encoding.IsInfinity(left);
        var rightInfinite = Decimal128Encoding.IsInfinity(right);

        if ((leftInfinite && Decimal128Encoding.IsZero(right)) || (rightInfinite && Decimal128Encoding.IsZero(left)))
        {
            // An infinity times zero is invalid.
            return Invalid(ref status);
        }

        return Decimal128Encoding.Infinity(Decimal128Encoding.IsNegative(left) != Decimal128Encoding.IsNegative(right));
    }

    // Fused multiply-add.

    /// <summary>Multiplies two values and adds a third, with a single rounding.</summary>
    /// <param name="left">The encoded first multiplicand.</param>
    /// <param name="right">The encoded second multiplicand.</param>
    /// <param name="addend">The encoded value added to the product.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded value of <c>left * right + addend</c>, rounded once to the format.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Decimal128Integer FusedMultiplyAdd(Decimal128Integer left, Decimal128Integer right,
        Decimal128Integer addend, Decimal128Rounding rounding, ref Decimal128Status status)
    {
        if (Decimal128Encoding.IsSpecial(left) || Decimal128Encoding.IsSpecial(right)
            || Decimal128Encoding.IsSpecial(addend))
        {
            return FusedMultiplyAddSpecial(left, right, addend, ref status);
        }

        var leftCoefficient = Decimal128Encoding.Unpack(left, out var leftExponent);
        var rightCoefficient = Decimal128Encoding.Unpack(right, out var rightExponent);
        var addendCoefficient = Decimal128Encoding.Unpack(addend, out var addendExponent);
        var productNegative = Decimal128Encoding.IsNegative(left) != Decimal128Encoding.IsNegative(right);
        var productExponent = leftExponent + rightExponent;
        var addendNegative = Decimal128Encoding.IsNegative(addend);

        if ((leftCoefficient.High | rightCoefficient.High) == 0)
        {
            var high = Math.BigMul(leftCoefficient.Low, rightCoefficient.Low, out var low);
            var product = new Decimal128Integer(high, low);
            if (product <= Decimal128Encoding.MaxCoefficient)
            {
                return AddFinite(productNegative, product, productExponent, addendNegative,
                    addendCoefficient, addendExponent, rounding, ref status);
            }

            return Decimal128Product.FusedAdd(productNegative, Decimal128LongInteger.FromInteger(product),
                productExponent, addendNegative, addendCoefficient, addendExponent, rounding, ref status);
        }

        var wide = Decimal128LongInteger.Multiply(leftCoefficient, rightCoefficient);
        if (wide.IsInteger && wide.ToInteger() <= Decimal128Encoding.MaxCoefficient)
        {
            // A coefficient wider than one word does not mean the product exceeds the
            // format. A product that fits in the format uses the ordinary addition, which
            // pads it to the precision when the addend is below it.
            return AddFinite(productNegative, wide.ToInteger(), productExponent, addendNegative,
                addendCoefficient, addendExponent, rounding, ref status);
        }

        return Decimal128Product.FusedAdd(productNegative, wide, productExponent, addendNegative, addendCoefficient,
            addendExponent, rounding, ref status);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Decimal128Integer FusedMultiplyAddSpecial(Decimal128Integer left, Decimal128Integer right,
        Decimal128Integer addend, ref Decimal128Status status)
    {
        if (Decimal128Encoding.IsSignalingNaN(left))
        {
            status |= Decimal128Status.InvalidOperation;
            return Decimal128Encoding.Quiet(left);
        }

        if (Decimal128Encoding.IsSignalingNaN(right))
        {
            status |= Decimal128Status.InvalidOperation;
            return Decimal128Encoding.Quiet(right);
        }

        if (Decimal128Encoding.IsSignalingNaN(addend))
        {
            status |= Decimal128Status.InvalidOperation;
            return Decimal128Encoding.Quiet(addend);
        }

        // An invalid multiplication is reported even when the addend is a quiet NaN, so the
        // product is checked before the addend's NaN.
        if (!Decimal128Encoding.IsNaN(left) && !Decimal128Encoding.IsNaN(right)
            && (Decimal128Encoding.IsInfinity(left) || Decimal128Encoding.IsInfinity(right)))
        {
            var product = MultiplyInfinity(left, right, ref status);
            if (Decimal128Encoding.IsNaN(addend))
            {
                return Decimal128Encoding.Quiet(addend);
            }

            if (Decimal128Encoding.IsInfinity(product))
            {
                // An infinite product is still added to the addend. If the addend is an
                // infinity of the opposite sign, the addition is invalid.
                return AddInfinity(product, addend, ref status);
            }

            return product;
        }

        if (Decimal128Encoding.IsNaN(left))
        {
            return Decimal128Encoding.Quiet(left);
        }

        if (Decimal128Encoding.IsNaN(right))
        {
            return Decimal128Encoding.Quiet(right);
        }

        if (Decimal128Encoding.IsNaN(addend))
        {
            return Decimal128Encoding.Quiet(addend);
        }

        // The multiplicands are finite, so the addend is the infinity.
        return Decimal128Encoding.Infinity(Decimal128Encoding.IsNegative(addend));
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
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Decimal128Integer Divide(Decimal128Integer left, Decimal128Integer right,
        Decimal128Rounding rounding, ref Decimal128Status status)
    {
        if (Decimal128Encoding.IsSpecial(left) || Decimal128Encoding.IsSpecial(right))
        {
            return DivideSpecial(left, right, Decimal128DivisionKind.Divide, ref status);
        }

        var leftCoefficient = Decimal128Encoding.Unpack(left, out var leftExponent);
        var rightCoefficient = Decimal128Encoding.Unpack(right, out var rightExponent);
        var negative = Decimal128Encoding.IsNegative(left) != Decimal128Encoding.IsNegative(right);

        if (rightCoefficient.IsZero)
        {
            if (leftCoefficient.IsZero)
            {
                status |= Decimal128Status.DivisionUndefined;
                return Decimal128Encoding.QuietNaN();
            }

            status |= Decimal128Status.DivisionByZero;
            return Decimal128Encoding.Infinity(negative);
        }

        var idealExponent = leftExponent - rightExponent;
        if (leftCoefficient.IsZero)
        {
            return Decimal128Finalizer.Zero(negative, idealExponent, ref status);
        }

        return DivideFinite(negative, leftCoefficient, rightCoefficient, idealExponent, rounding, ref status);
    }

    /// <summary>
    /// Computes the quotient to 34 or 35 digits, and uses the remainder to set the residue.
    /// The dividend is scaled by 10 to the power
    /// <c>34 + divisor digits - dividend digits</c>, which puts the quotient between 10^33
    /// and 10^35. With 34 digits, the remainder alone decides the rounding. With 35 digits,
    /// the finalizer drops the extra digit and uses the remainder's residue below it.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Decimal128Integer DivideFinite(bool negative, Decimal128Integer dividend, Decimal128Integer divisor,
        int idealExponent, Decimal128Rounding rounding, ref Decimal128Status status)
    {
        var dividendDigits = Decimal128Tables.CountDigits(dividend);
        var divisorDigits = Decimal128Tables.CountDigits(divisor);
        var scale = Decimal128Encoding.Precision + divisorDigits - dividendDigits;

        var quotient = Decimal128Divider.Divide(dividend, scale, divisor, out var remainder);
        var exponent = idealExponent - scale;

        if (remainder.IsZero)
        {
            // The quotient is exact, so the specification requires the exponent closest to
            // the ideal. Remove the trailing zeros the scaling added, and no more.
            Decimal128Shaping.StripTrailingZeros(ref quotient, ref exponent, idealExponent);
            return Decimal128Finalizer.Finalize(negative, quotient, exponent, Decimal128Residue.Exact, rounding,
                ref status);
        }

        // Comparing twice the remainder with the divisor shows whether the remainder is
        // below, at, or above half. This is the same classification as comparing a
        // discarded part with its half.
        var residue = Decimal128Rounder.Of(remainder + remainder, divisor);
        return Decimal128Finalizer.Finalize(negative, quotient, exponent, residue, rounding, ref status);
    }

    /// <summary>The integer part of the quotient, with exponent zero.</summary>
    /// <param name="left">The encoded dividend.</param>
    /// <param name="right">The encoded divisor.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded integer quotient, or a quiet NaN if it does not fit in the precision.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Decimal128Integer DivideInteger(Decimal128Integer left, Decimal128Integer right,
        Decimal128Rounding rounding, ref Decimal128Status status)
    {
        if (Decimal128Encoding.IsSpecial(left) || Decimal128Encoding.IsSpecial(right))
        {
            return DivideSpecial(left, right, Decimal128DivisionKind.DivideInteger, ref status);
        }

        var leftCoefficient = Decimal128Encoding.Unpack(left, out var leftExponent);
        var rightCoefficient = Decimal128Encoding.Unpack(right, out var rightExponent);
        var negative = Decimal128Encoding.IsNegative(left) != Decimal128Encoding.IsNegative(right);

        if (rightCoefficient.IsZero)
        {
            if (leftCoefficient.IsZero)
            {
                status |= Decimal128Status.DivisionUndefined;
                return Decimal128Encoding.QuietNaN();
            }

            status |= Decimal128Status.DivisionByZero;
            return Decimal128Encoding.Infinity(negative);
        }

        if (leftCoefficient.IsZero)
        {
            return Decimal128Encoding.Zero(negative, 0);
        }

        if (!IntegerDivide(leftCoefficient, leftExponent, rightCoefficient, rightExponent,
            out var quotient, out _, out _, out _))
        {
            status |= Decimal128Status.DivisionImpossible;
            return Decimal128Encoding.QuietNaN();
        }

        return Decimal128Finalizer.Finalize(negative, quotient, 0, Decimal128Residue.Exact, rounding, ref status);
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
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Decimal128Integer Remainder(Decimal128Integer left, Decimal128Integer right, bool near,
        Decimal128Rounding rounding, ref Decimal128Status status)
    {
        var kind = near ? Decimal128DivisionKind.RemainderNear : Decimal128DivisionKind.Remainder;

        if (Decimal128Encoding.IsSpecial(left) || Decimal128Encoding.IsSpecial(right))
        {
            return DivideSpecial(left, right, kind, ref status);
        }

        var leftCoefficient = Decimal128Encoding.Unpack(left, out var leftExponent);
        var rightCoefficient = Decimal128Encoding.Unpack(right, out var rightExponent);
        var negative = Decimal128Encoding.IsNegative(left);

        if (rightCoefficient.IsZero)
        {
            if (leftCoefficient.IsZero)
            {
                status |= Decimal128Status.DivisionUndefined;
                return Decimal128Encoding.QuietNaN();
            }

            return Invalid(ref status);
        }

        if (leftCoefficient.IsZero)
        {
            // A zero dividend is its own remainder. It keeps its sign and takes the lower of
            // the two exponents.
            return Decimal128Finalizer.Zero(negative, Math.Min(leftExponent, rightExponent), ref status);
        }

        if (!IntegerDivide(leftCoefficient, leftExponent, rightCoefficient, rightExponent,
            out var quotient, out var remainder, out var remainderExponent, out var scaledDivisor))
        {
            status |= Decimal128Status.DivisionImpossible;
            return Decimal128Encoding.QuietNaN();
        }

        if (near && !scaledDivisor.IsZero)
        {
            // The nearest integer quotient can be one higher. Then the remainder is measured
            // from that quotient and changes sign. A tie goes to the even quotient. The
            // remainder is compared with (divisor - remainder) instead of being doubled,
            // because a divisor scaled to 38 digits leaves no room to double.
            var other = scaledDivisor - remainder;
            if (remainder > other || (remainder == other && (quotient.Low & 1) != 0))
            {
                if (quotient == Decimal128Encoding.MaxCoefficient)
                {
                    status |= Decimal128Status.DivisionImpossible;
                    return Decimal128Encoding.QuietNaN();
                }

                remainder = scaledDivisor - remainder;
                negative = !negative;
            }
        }

        if (remainder.IsZero)
        {
            // A zero remainder takes the dividend's sign.
            return Decimal128Finalizer.Zero(Decimal128Encoding.IsNegative(left), remainderExponent, ref status);
        }

        return Decimal128Finalizer.Finalize(negative, remainder, remainderExponent, Decimal128Residue.Exact, rounding,
            ref status);
    }

    /// <summary>
    /// The exact integer quotient and remainder of two finite non-zero values. The quotient
    /// must fit in the precision. If it does not, the method returns false and the division
    /// is impossible. The remainder is returned at the lower of the two exponents.
    /// <paramref name="scaledDivisor"/> is the divisor scaled to that exponent when it fits
    /// in two words. Otherwise it is zero, and the remainder is the whole dividend, which
    /// is below half the divisor.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IntegerDivide(Decimal128Integer dividend, int dividendExponent, Decimal128Integer divisor,
        int divisorExponent, out Decimal128Integer quotient, out Decimal128Integer remainder,
        out int remainderExponent, out Decimal128Integer scaledDivisor)
    {
        var dividendDigits = Decimal128Tables.CountDigits(dividend);
        var divisorDigits = Decimal128Tables.CountDigits(divisor);

        if (dividendExponent >= divisorExponent)
        {
            var scale = dividendExponent - divisorExponent;
            remainderExponent = divisorExponent;
            scaledDivisor = divisor;

            // The dividend scaled to the divisor's exponent has this many digits. If it has
            // 35 or more digits than the divisor, the quotient cannot fit.
            var wideDigits = dividendDigits + scale;
            if (wideDigits - divisorDigits >= QuotientDigits)
            {
                quotient = Decimal128Integer.Zero;
                remainder = Decimal128Integer.Zero;
                return false;
            }

            quotient = Decimal128Divider.Divide(dividend, scale, divisor, out remainder);
            return quotient <= Decimal128Encoding.MaxCoefficient;
        }

        var lift = divisorExponent - dividendExponent;
        remainderExponent = dividendExponent;

        if (divisorDigits + lift > WideDigits)
        {
            // The divisor scaled to the dividend's exponent does not fit in two words, so it
            // is larger than the dividend, and the quotient is zero.
            quotient = Decimal128Integer.Zero;
            remainder = dividend;
            scaledDivisor = Decimal128Integer.Zero;
            return true;
        }

        scaledDivisor = Decimal128Tables.Scale(divisor, lift);
        if (dividend < scaledDivisor)
        {
            quotient = Decimal128Integer.Zero;
            remainder = dividend;
            return true;
        }

        quotient = Decimal128Divider.Divide(dividend, 0, scaledDivisor, out remainder);
        return true;
    }

    /// <summary>
    /// Handles infinities and NaNs for the four division operations.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Decimal128Integer DivideSpecial(Decimal128Integer left, Decimal128Integer right,
        Decimal128DivisionKind kind, ref Decimal128Status status)
    {
        if (Decimal128Encoding.IsNaN(left) || Decimal128Encoding.IsNaN(right))
        {
            return PropagateNaN(left, right, ref status);
        }

        var isRemainder = kind is Decimal128DivisionKind.Remainder or Decimal128DivisionKind.RemainderNear;
        var negative = Decimal128Encoding.IsNegative(left) != Decimal128Encoding.IsNegative(right);

        if (Decimal128Encoding.IsInfinity(left))
        {
            if (Decimal128Encoding.IsInfinity(right) || isRemainder)
            {
                // An infinity divided by an infinity is invalid, and so is the remainder of
                // an infinity.
                return Invalid(ref status);
            }

            // An infinity divided by a finite value is infinite, even when dividing by zero.
            return Decimal128Encoding.Infinity(negative);
        }

        // The divisor is the infinity.
        if (isRemainder)
        {
            // The quotient is zero, so the remainder is the whole dividend.
            return Decimal128Encoding.Canonical(left);
        }

        if (kind == Decimal128DivisionKind.DivideInteger)
        {
            // The integer quotient is zero. An integer result has exponent zero and is not
            // clamped.
            return Decimal128Encoding.Zero(negative, 0);
        }

        // A finite value divided by an infinity is a zero with an unbounded negative
        // exponent. It is clamped to the smallest exponent the format holds.
        status |= Decimal128Status.Clamped;
        return Decimal128Encoding.Zero(negative, Decimal128Encoding.MinQuantumExponent);
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
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compare(Decimal128Integer left, Decimal128Integer right, bool signaling,
        ref Decimal128Status status, out Decimal128Integer nan)
    {
        nan = Decimal128Integer.Zero;

        if (Decimal128Encoding.IsSpecial(left) || Decimal128Encoding.IsSpecial(right))
        {
            if (Decimal128Encoding.IsNaN(left) || Decimal128Encoding.IsNaN(right))
            {
                // The quiet comparison reports only a signaling NaN. The signaling comparison
                // reports every NaN.
                if (signaling)
                {
                    status |= Decimal128Status.InvalidOperation;
                }

                nan = PropagateNaN(left, right, ref status);
                return int.MinValue;
            }

            return CompareInfinity(left, right);
        }

        var leftCoefficient = Decimal128Encoding.Unpack(left, out var leftExponent);
        var rightCoefficient = Decimal128Encoding.Unpack(right, out var rightExponent);

        return CompareFinite(Decimal128Encoding.IsNegative(left), leftCoefficient, leftExponent,
            Decimal128Encoding.IsNegative(right), rightCoefficient, rightExponent);
    }

    /// <summary>
    /// Compares two values when at least one is infinite and neither is a NaN. An infinity
    /// is beyond every finite value of its sign, and two infinities of the same sign are
    /// equal.
    /// </summary>
    /// <param name="left">The encoded left operand.</param>
    /// <param name="right">The encoded right operand.</param>
    /// <returns>-1, 0, or 1 as the left value is less than, equal to, or greater than the right.</returns>
    public static int CompareInfinity(Decimal128Integer left, Decimal128Integer right)
    {
        if (Decimal128Encoding.IsInfinity(left) && Decimal128Encoding.IsInfinity(right))
        {
            if (Decimal128Encoding.IsNegative(left) == Decimal128Encoding.IsNegative(right))
            {
                return 0;
            }

            return Decimal128Encoding.IsNegative(left) ? -1 : 1;
        }

        if (Decimal128Encoding.IsInfinity(left))
        {
            return Decimal128Encoding.IsNegative(left) ? -1 : 1;
        }

        return Decimal128Encoding.IsNegative(right) ? 1 : -1;
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
    public static int CompareFinite(bool leftNegative, Decimal128Integer leftCoefficient, int leftExponent,
        bool rightNegative, Decimal128Integer rightCoefficient, int rightExponent)
    {
        if (leftCoefficient.IsZero)
        {
            if (rightCoefficient.IsZero)
            {
                return 0;
            }

            return rightNegative ? 1 : -1;
        }

        if (rightCoefficient.IsZero)
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
    /// stays within 34 digits because their leading digits are in the same position.
    /// </summary>
    /// <param name="leftCoefficient">The left operand's coefficient.</param>
    /// <param name="leftExponent">The left operand's exponent.</param>
    /// <param name="rightCoefficient">The right operand's coefficient.</param>
    /// <param name="rightExponent">The right operand's exponent.</param>
    /// <returns>-1, 0, or 1 as the left magnitude is less than, equal to, or greater than the right.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CompareMagnitude(Decimal128Integer leftCoefficient, int leftExponent,
        Decimal128Integer rightCoefficient, int rightExponent)
    {
        var leftTop = leftExponent + Decimal128Tables.CountDigits(leftCoefficient);
        var rightTop = rightExponent + Decimal128Tables.CountDigits(rightCoefficient);

        if (leftTop != rightTop)
        {
            return leftTop < rightTop ? -1 : 1;
        }

        if (leftExponent > rightExponent)
        {
            leftCoefficient = Decimal128Tables.Scale(leftCoefficient, leftExponent - rightExponent);
        }
        else if (rightExponent > leftExponent)
        {
            rightCoefficient = Decimal128Tables.Scale(rightCoefficient, rightExponent - leftExponent);
        }

        return leftCoefficient.CompareTo(rightCoefficient);
    }
}
