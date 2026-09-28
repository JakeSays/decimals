// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace Decimals.Internal;

/// <summary>
/// The arithmetic operations: encoding in, encoding out, with the special values settled
/// before the coefficients are looked at.
/// </summary>
/// <remarks>
/// <para>
/// Every path here works on a coefficient in two machine words and an exponent in an
/// integer. The two intermediates that do not fit two words -- an operand whose exponent
/// lies far above the other's, and a product of two thirty-four digit coefficients -- are
/// cut down to what fits beside the other operand and a <see cref="Decimal128Residue"/>
/// before they are added, which the finalizer then rounds once.
/// </para>
/// <para>
/// Alignment works on the value with the higher exponent. Scaling it down to meet the
/// lower one is exact while the result stays within the precision, or within two words
/// for a difference, which can cancel; past that it is scaled to exactly thirty-four
/// digits and the lower operand is folded up to that exponent instead, its discarded
/// digits becoming the residue, so that the sum lands in the format with at most a carry
/// to drop. Only the lower operand is ever folded, and for a difference it is folded at
/// least five digits down, so at most one leading digit can cancel, which one more digit
/// of the fold puts right.
/// </para>
/// </remarks>
internal static class Decimal128Arithmetic
{
    private const int WideDigits = Decimal128Tables.MaxWidePower;

    /// <summary>Digits of quotient a division computes: the precision and one to round on.</summary>
    private const int QuotientDigits = Decimal128Encoding.Precision + 1;

    // The special values, settled the same way in every operation: a signaling NaN is
    // invalid and comes out quiet, a quiet NaN passes through, left before right.

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

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Decimal128Integer PropagateNaN(Decimal128Integer value, ref Decimal128Status status)
    {
        if (Decimal128Encoding.IsSignalingNaN(value))
        {
            status |= Decimal128Status.InvalidOperation;
        }

        return Decimal128Encoding.Quiet(value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Decimal128Integer Invalid(ref Decimal128Status status)
    {
        status |= Decimal128Status.InvalidOperation;
        return Decimal128Encoding.QuietNaN();
    }

    // Addition and subtraction. The operations are called rather than inlined into the
    // public type's wrappers, so that each is the root of its own inlining budget and the
    // two-word operators inside it are the ones that get inlined; the rare paths for the
    // specials are called out of them for the same reason.

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
    /// Subtraction is addition with the right operand's sign flipped -- after the NaNs are
    /// settled, since a NaN keeps the sign it arrived with.
    /// </summary>
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
                // Infinities of opposite sign have no sum.
                return Invalid(ref status);
            }

            return Decimal128Encoding.Infinity(Decimal128Encoding.IsNegative(left));
        }

        return Decimal128Encoding.Infinity(Decimal128Encoding.IsNegative(right));
    }

    /// <summary>
    /// Adds two finite values given as sign, coefficient of at most thirty-four digits, and
    /// exponent.
    /// </summary>
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

        // The operand with the higher exponent is the one that gets scaled.
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

        // Where the left operand's last digit lands when it is scaled all the way down to
        // the right one. Within the precision the sum is exact and has at most a carry to
        // drop; a difference is exact while it fits two words, and can cancel any number of
        // digits, so it is formed whole.
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

        // The left operand goes to exactly the precision and the right one is folded up to
        // that exponent, its discarded digits becoming the residue. The sum then has
        // thirty-four digits or one more, and the finalizer drops the carry with the
        // residue under it.
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

        // Digits of the right operand were discarded, zeros or not, and the specification
        // counts that as rounding.
        status |= Decimal128Status.Rounded;

        if (sameSign)
        {
            return Decimal128Finalizer.Finalize(leftNegative, wide + folded, exponent, residue, rounding, ref status);
        }

        // A difference only comes here past two words, so the fold is at least five digits
        // and the folded value has at most twenty-nine: the difference keeps thirty-three
        // digits at the least, and thirty-four unless the left operand is a one followed
        // by zeros. Subtracting an inexact operand takes one more unit off, and the
        // fraction of that unit left behind is the flipped residue.
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
    /// A difference that cancelled its leading digit is formed again one digit lower: the
    /// left operand, already at the precision, is scaled by one more, and the right one is
    /// folded one digit less. The left operand was within 10^29 of 10^33 for the
    /// cancellation to happen, so scaled it lies below 10^34 + 10^30, and the right one,
    /// folded at least four digits, is below 10^30: the difference has thirty-four digits.
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
    /// The difference of two coefficients at the same exponent and opposite signs, which
    /// takes the sign of the larger.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Decimal128Integer SubtractAligned(bool leftNegative, Decimal128Integer left, bool rightNegative,
        Decimal128Integer right, int exponent, Decimal128Rounding rounding, ref Decimal128Status status)
    {
        if (left == right)
        {
            // Opposite signs canceling exactly gives a positive zero, except when the
            // rounding runs toward negative infinity.
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
    /// A zero operand contributes only its exponent, and only when that is the lower of the
    /// two: the other operand is padded down to meet it as far as the precision allows.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Decimal128Integer AddWithZero(bool leftNegative, Decimal128Integer leftCoefficient, int leftExponent,
        bool rightNegative, Decimal128Integer rightCoefficient, int rightExponent, Decimal128Rounding rounding,
        ref Decimal128Status status)
    {
        if (leftCoefficient.IsZero && rightCoefficient.IsZero)
        {
            // Two zeros keep the lower exponent, and the sign is theirs only when they
            // agree; otherwise it follows the rounding.
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
                // Only so many zeros fit; the value is unchanged, but digits went.
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
    /// Applies the context to a value without otherwise changing it, which is addition to a
    /// zero of the value's own exponent. That zero is what makes <c>plus -0</c> a positive
    /// zero under every rounding but one. <paramref name="negate"/> makes it a subtraction.
    /// </summary>
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
            // Both fit a word, so the product fits two and is exact.
            var high = Math.BigMul(leftCoefficient.Low, rightCoefficient.Low, out var low);
            return Decimal128Finalizer.Finalize(negative, new Decimal128Integer(high, low), exponent,
                Decimal128Residue.Exact, rounding, ref status);
        }

        var product = Decimal128LongInteger.Multiply(leftCoefficient, rightCoefficient);
        return Decimal128Product.Reduce(negative, product, exponent, Decimal128Residue.Exact, rounding, ref status);
    }

    /// <summary>At least one operand is infinite and neither is a NaN.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Decimal128Integer MultiplyInfinity(Decimal128Integer left, Decimal128Integer right,
        ref Decimal128Status status)
    {
        var leftInfinite = Decimal128Encoding.IsInfinity(left);
        var rightInfinite = Decimal128Encoding.IsInfinity(right);

        if ((leftInfinite && Decimal128Encoding.IsZero(right)) || (rightInfinite && Decimal128Encoding.IsZero(left)))
        {
            // An infinity times a zero has no product.
            return Invalid(ref status);
        }

        return Decimal128Encoding.Infinity(Decimal128Encoding.IsNegative(left) != Decimal128Encoding.IsNegative(right));
    }

    // Fused multiply-add.

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
            // A factor past a word does not make the product past the format: a short
            // one is an ordinary addition, which pads it out to the precision when the
            // addend lies below it.
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
        // product is settled before the addend's NaN is looked at.
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
                // An infinite product still has to meet the addend, which can be the other
                // infinity and make the addition invalid in its own right.
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

        // Everything else is finite, so the addend is the infinity.
        return Decimal128Encoding.Infinity(Decimal128Encoding.IsNegative(addend));
    }

    // Division and its relatives.

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
    /// The quotient to thirty-four or thirty-five digits, with the remainder deciding the
    /// residue. The dividend is scaled by ten to <c>34 + divisor digits - dividend digits</c>,
    /// which puts the quotient between 10^33 and 10^35: at the precision, when the
    /// remainder alone decides the rounding, or one digit over, which the finalizer drops
    /// with the remainder's residue under it.
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
            // Exact, so the specification wants the exponent nearest the ideal: give back
            // the trailing zeros the scaling introduced, and no more.
            Decimal128Shaping.StripTrailingZeros(ref quotient, ref exponent, idealExponent);
            return Decimal128Finalizer.Finalize(negative, quotient, exponent, Decimal128Residue.Exact, rounding,
                ref status);
        }

        // Twice the remainder against the divisor says which side of half it falls on,
        // which is the residue of a part measured against its half.
        var residue = Decimal128Rounder.Of(remainder + remainder, divisor);
        return Decimal128Finalizer.Finalize(negative, quotient, exponent, residue, rounding, ref status);
    }

    /// <summary>The integer part of the quotient, with a zero exponent.</summary>
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

        if (!TryIntegerDivide(leftCoefficient, leftExponent, rightCoefficient, rightExponent,
            out var quotient, out _, out _, out _))
        {
            status |= Decimal128Status.DivisionImpossible;
            return Decimal128Encoding.QuietNaN();
        }

        return Decimal128Finalizer.Finalize(negative, quotient, 0, Decimal128Residue.Exact, rounding, ref status);
    }

    /// <summary>
    /// What is left after taking out the integer quotient, which the division leaves behind
    /// exactly. The near form takes the quotient to the nearest integer instead, so the
    /// remainder can come out the other side of zero.
    /// </summary>
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
            // Nothing is taken out of a zero, so the dividend is what is left: its own
            // sign, at the lower of the two exponents.
            return Decimal128Finalizer.Zero(negative, Math.Min(leftExponent, rightExponent), ref status);
        }

        if (!TryIntegerDivide(leftCoefficient, leftExponent, rightCoefficient, rightExponent,
            out var quotient, out var remainder, out var remainderExponent, out var scaledDivisor))
        {
            status |= Decimal128Status.DivisionImpossible;
            return Decimal128Encoding.QuietNaN();
        }

        if (near && !scaledDivisor.IsZero)
        {
            // The nearest integer quotient may be one higher, in which case the remainder is
            // measured from that one and changes sign. A tie goes to the even quotient. The
            // remainder is set against what is left of the divisor rather than doubled,
            // since a divisor lifted to thirty-eight digits leaves no room to double.
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
    /// The integer quotient of two finite non-zero values and what it leaves, exactly. The
    /// quotient has to fit the precision; one that would not makes the division impossible.
    /// The remainder comes back at the lower of the two exponents, with the divisor scaled
    /// to that exponent beside it when it fits two words, and zero when it does not -- in
    /// which case the remainder is the whole dividend and is below half of it.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool TryIntegerDivide(Decimal128Integer dividend, int dividendExponent, Decimal128Integer divisor,
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

            // The dividend scaled to the divisor's exponent has this many digits; past
            // thirty-four more than the divisor the quotient cannot fit.
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
            // The divisor lifted to the dividend's exponent is past two words, so it is
            // past the dividend too: nothing divides out.
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
    /// The infinities and NaNs, for all four operations built on division.
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
                // One infinity over another has no quotient, and there is nothing left over
                // from an infinity.
                return Invalid(ref status);
            }

            // An infinity over anything finite is infinite, even over a zero.
            return Decimal128Encoding.Infinity(negative);
        }

        // The divisor is the infinity.
        if (isRemainder)
        {
            // Nothing has been taken out, so the whole dividend is left over.
            return Decimal128Encoding.Canonical(left);
        }

        if (kind == Decimal128DivisionKind.DivideInteger)
        {
            // No whole copies of an infinity come out, and an integer result sits at an
            // exponent of zero rather than being clamped down.
            return Decimal128Encoding.Zero(negative, 0);
        }

        // A finite over an infinity is a zero whose exponent wants to be unboundedly small;
        // it comes to rest at the smallest the format holds, which is a clamp.
        status |= Decimal128Status.Clamped;
        return Decimal128Encoding.Zero(negative, Decimal128Encoding.MinQuantumExponent);
    }

    // Comparison.

    /// <summary>
    /// Compares two values numerically, giving -1, 0, 1, or <see cref="int.MinValue"/> with
    /// a NaN in <paramref name="nan"/> when the two are unordered. The two zeros are equal
    /// here, unlike under the total order.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compare(Decimal128Integer left, Decimal128Integer right, bool signaling,
        ref Decimal128Status status, out Decimal128Integer nan)
    {
        nan = Decimal128Integer.Zero;

        if (Decimal128Encoding.IsSpecial(left) || Decimal128Encoding.IsSpecial(right))
        {
            if (Decimal128Encoding.IsNaN(left) || Decimal128Encoding.IsNaN(right))
            {
                // The quiet comparison lets a quiet NaN through without a condition; the
                // signaling one reports every NaN.
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
    /// Orders values where at least one is infinite and neither is a NaN. An infinity is
    /// beyond every finite value on its own side, and two of the same sign are equal.
    /// </summary>
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
    /// Orders two non-zero coefficients by value, ignoring both signs. Where the leading
    /// digits sit decides it unless they sit in the same place; then the coefficients are
    /// lined up, which stays inside thirty-four digits because their leading digits agree.
    /// </summary>
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
