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
/// Every path here works on a coefficient in a machine word and an exponent in an integer.
/// The two intermediates that do not fit a word -- an operand aligned far above the other,
/// and a product of two sixteen-digit coefficients -- are cut down to nineteen digits and a
/// <see cref="Decimal64Residue"/> before they are added, which the finalizer then rounds once.
/// </para>
/// <para>
/// Alignment works on the value with the higher exponent. Scaling it up to meet the lower
/// one is exact while the result fits nineteen digits; past that it is scaled to exactly
/// nineteen digits and the lower operand is folded to that exponent instead, its discarded
/// digits becoming the residue. Only the lower operand is ever folded, and when it is, the
/// higher one has at least eighteen digits above the fold, so no subtraction can cancel
/// down into it.
/// </para>
/// </remarks>
internal static class Decimal64Arithmetic
{
    private const int WideDigits = Decimal64Tables.MaxPower;

    // The special values, settled the same way in every operation: a signaling NaN is
    // invalid and comes out quiet, a quiet NaN passes through, left before right.

    public static ulong PropagateNaN(ulong left, ulong right, ref Decimal64Status status)
    {
        if (Decimal64Encoding.IsSignalingNaN(left))
        {
            status |= Decimal64Status.InvalidOperation;
            return Decimal64Encoding.Quiet(left);
        }

        if (Decimal64Encoding.IsSignalingNaN(right))
        {
            status |= Decimal64Status.InvalidOperation;
            return Decimal64Encoding.Quiet(right);
        }

        return Decimal64Encoding.Quiet(Decimal64Encoding.IsNaN(left) ? left : right);
    }

    public static ulong PropagateNaN(ulong value, ref Decimal64Status status)
    {
        if (Decimal64Encoding.IsSignalingNaN(value))
        {
            status |= Decimal64Status.InvalidOperation;
        }

        return Decimal64Encoding.Quiet(value);
    }

    public static ulong Invalid(ref Decimal64Status status)
    {
        status |= Decimal64Status.InvalidOperation;
        return Decimal64Encoding.QuietNaN();
    }

    // Addition and subtraction.

    public static ulong Add(ulong left, ulong right, Decimal64Rounding rounding, ref Decimal64Status status)
    {
        if (Decimal64Encoding.IsSpecial(left) || Decimal64Encoding.IsSpecial(right))
        {
            if (Decimal64Encoding.IsNaN(left) || Decimal64Encoding.IsNaN(right))
            {
                return PropagateNaN(left, right, ref status);
            }

            return AddInfinity(left, right, ref status);
        }

        var leftCoefficient = Decimal64Encoding.Unpack(left, out var leftExponent);
        var rightCoefficient = Decimal64Encoding.Unpack(right, out var rightExponent);

        return AddFinite(Decimal64Encoding.IsNegative(left), leftCoefficient, leftExponent,
            Decimal64Encoding.IsNegative(right), rightCoefficient, rightExponent, rounding, ref status);
    }

    /// <summary>
    /// Subtraction is addition with the right operand's sign flipped -- after the NaNs are
    /// settled, since a NaN keeps the sign it arrived with.
    /// </summary>
    public static ulong Subtract(ulong left, ulong right, Decimal64Rounding rounding, ref Decimal64Status status)
    {
        if (Decimal64Encoding.IsSpecial(left) || Decimal64Encoding.IsSpecial(right))
        {
            if (Decimal64Encoding.IsNaN(left) || Decimal64Encoding.IsNaN(right))
            {
                return PropagateNaN(left, right, ref status);
            }

            return AddInfinity(left, right ^ Decimal64Encoding.SignMask, ref status);
        }

        var leftCoefficient = Decimal64Encoding.Unpack(left, out var leftExponent);
        var rightCoefficient = Decimal64Encoding.Unpack(right, out var rightExponent);

        return AddFinite(Decimal64Encoding.IsNegative(left), leftCoefficient, leftExponent,
            !Decimal64Encoding.IsNegative(right), rightCoefficient, rightExponent, rounding, ref status);
    }

    private static ulong AddInfinity(ulong left, ulong right, ref Decimal64Status status)
    {
        if (Decimal64Encoding.IsInfinity(left))
        {
            if (Decimal64Encoding.IsInfinity(right)
                && Decimal64Encoding.IsNegative(left) != Decimal64Encoding.IsNegative(right))
            {
                // Infinities of opposite sign have no sum.
                return Invalid(ref status);
            }

            return Decimal64Encoding.Infinity(Decimal64Encoding.IsNegative(left));
        }

        return Decimal64Encoding.Infinity(Decimal64Encoding.IsNegative(right));
    }

    /// <summary>
    /// Adds two finite values given as sign, coefficient of at most sixteen digits, and
    /// exponent.
    /// </summary>
    public static ulong AddFinite(bool leftNegative, ulong leftCoefficient, int leftExponent,
        bool rightNegative, ulong rightCoefficient, int rightExponent, Decimal64Rounding rounding,
        ref Decimal64Status status)
    {
        if (leftCoefficient == 0 || rightCoefficient == 0)
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
                return Decimal64Finalizer.Finalize(leftNegative, leftCoefficient + rightCoefficient, leftExponent,
                    Decimal64Residue.Exact, rounding, ref status);
            }

            return SubtractAligned(leftNegative, leftCoefficient, rightNegative, rightCoefficient,
                leftExponent, rounding, ref status);
        }

        var leftDigits = Decimal64Tables.CountDigits(leftCoefficient);
        if (leftDigits + distance <= WideDigits)
        {
            var scaled = leftCoefficient * Decimal64Tables.PowerOfTen(distance);
            if (leftNegative == rightNegative)
            {
                return Decimal64Finalizer.Finalize(leftNegative, scaled + rightCoefficient, rightExponent,
                    Decimal64Residue.Exact, rounding, ref status);
            }

            return SubtractAligned(leftNegative, scaled, rightNegative, rightCoefficient, rightExponent,
                rounding, ref status);
        }

        // Too far apart to align exactly: the higher operand goes to nineteen digits and the
        // lower one is folded up to that exponent, which loses nothing that rounding needs.
        var widen = WideDigits - leftDigits;
        var wide = leftCoefficient * Decimal64Tables.PowerOfTen(widen);
        var exponent = leftExponent - widen;
        var drop = distance - widen;

        ulong folded;
        Decimal64Residue residue;
        if (drop > Decimal64Encoding.Precision)
        {
            folded = 0;
            residue = Decimal64Residue.BelowHalf;
        }
        else
        {
            folded = Decimal64Tables.DivRemPowerOfTen(rightCoefficient, drop, out var discarded);
            residue = Decimal64Rounder.Of(discarded, Decimal64Tables.HalfPowerOfTen(drop));
        }

        if (leftNegative == rightNegative)
        {
            return Decimal64Finalizer.Finalize(leftNegative, wide + folded, exponent, residue, rounding, ref status);
        }

        if (residue == Decimal64Residue.Exact)
        {
            return Decimal64Finalizer.Finalize(leftNegative, wide - folded, exponent, Decimal64Residue.Exact,
                rounding, ref status);
        }

        // Subtracting an inexact operand takes one more unit off, and the fraction of that
        // unit left behind is the flipped residue.
        return Decimal64Finalizer.Finalize(leftNegative, wide - folded - 1, exponent, Decimal64Rounder.Flip(residue),
            rounding, ref status);
    }

    /// <summary>
    /// The difference of two coefficients at the same exponent and opposite signs, which
    /// takes the sign of the larger.
    /// </summary>
    private static ulong SubtractAligned(bool leftNegative, ulong left, bool rightNegative, ulong right,
        int exponent, Decimal64Rounding rounding, ref Decimal64Status status)
    {
        if (left == right)
        {
            // Opposite signs canceling exactly gives a positive zero, except when the
            // rounding runs toward negative infinity.
            return Decimal64Finalizer.Zero(rounding == Decimal64Rounding.Floor, exponent, ref status);
        }

        if (left > right)
        {
            return Decimal64Finalizer.Finalize(leftNegative, left - right, exponent, Decimal64Residue.Exact,
                rounding, ref status);
        }

        return Decimal64Finalizer.Finalize(rightNegative, right - left, exponent, Decimal64Residue.Exact,
            rounding, ref status);
    }

    /// <summary>
    /// A zero operand contributes only its exponent, and only when that is the lower of the
    /// two: the other operand is padded down to meet it as far as the precision allows.
    /// </summary>
    private static ulong AddWithZero(bool leftNegative, ulong leftCoefficient, int leftExponent,
        bool rightNegative, ulong rightCoefficient, int rightExponent, Decimal64Rounding rounding,
        ref Decimal64Status status)
    {
        if (leftCoefficient == 0 && rightCoefficient == 0)
        {
            // Two zeros keep the lower exponent, and the sign is theirs only when they
            // agree; otherwise it follows the rounding.
            var negative = leftNegative == rightNegative
                ? leftNegative
                : rounding == Decimal64Rounding.Floor;

            return Decimal64Finalizer.Zero(negative, Math.Min(leftExponent, rightExponent), ref status);
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
            var digits = Decimal64Tables.CountDigits(coefficient);
            if (digits + pad > Decimal64Encoding.Precision)
            {
                // Only so many zeros fit; the value is unchanged, but digits went.
                pad = Decimal64Encoding.Precision - digits;
                status |= Decimal64Status.Rounded;
            }

            coefficient *= Decimal64Tables.PowerOfTen(pad);
            exponent -= pad;
        }

        return Decimal64Finalizer.Finalize(otherNegative, coefficient, exponent, Decimal64Residue.Exact, rounding, ref status);
    }

    /// <summary>
    /// Applies the context to a value without otherwise changing it, which is addition to a
    /// zero of the value's own exponent. That zero is what makes <c>plus -0</c> a positive
    /// zero under every rounding but one. <paramref name="negate"/> makes it a subtraction.
    /// </summary>
    public static ulong AddToZero(ulong value, bool negate, Decimal64Rounding rounding, ref Decimal64Status status)
    {
        if (Decimal64Encoding.IsSpecial(value))
        {
            if (Decimal64Encoding.IsNaN(value))
            {
                return PropagateNaN(value, ref status);
            }

            return Decimal64Encoding.Infinity(Decimal64Encoding.IsNegative(value) != negate);
        }

        var coefficient = Decimal64Encoding.Unpack(value, out var exponent);
        var negative = Decimal64Encoding.IsNegative(value) != negate;

        return AddFinite(false, 0, exponent, negative, coefficient, exponent, rounding, ref status);
    }

    // Multiplication.

    public static ulong Multiply(ulong left, ulong right, Decimal64Rounding rounding, ref Decimal64Status status)
    {
        if (Decimal64Encoding.IsSpecial(left) || Decimal64Encoding.IsSpecial(right))
        {
            if (Decimal64Encoding.IsNaN(left) || Decimal64Encoding.IsNaN(right))
            {
                return PropagateNaN(left, right, ref status);
            }

            return MultiplyInfinity(left, right, ref status);
        }

        var leftCoefficient = Decimal64Encoding.Unpack(left, out var leftExponent);
        var rightCoefficient = Decimal64Encoding.Unpack(right, out var rightExponent);
        var negative = Decimal64Encoding.IsNegative(left) != Decimal64Encoding.IsNegative(right);
        var exponent = leftExponent + rightExponent;

        if (((leftCoefficient | rightCoefficient) >> 32) == 0)
        {
            // Both halves fit thirty-two bits, so the product fits a word and is exact.
            return Decimal64Finalizer.Finalize(negative, leftCoefficient * rightCoefficient, exponent,
                Decimal64Residue.Exact, rounding, ref status);
        }

        Decimal64Product.Multiply(leftCoefficient, rightCoefficient, out var high, out var low);
        return Decimal64Product.Reduce(negative, high, low, exponent, rounding, ref status);
    }

    /// <summary>At least one operand is infinite and neither is a NaN.</summary>
    private static ulong MultiplyInfinity(ulong left, ulong right, ref Decimal64Status status)
    {
        var leftInfinite = Decimal64Encoding.IsInfinity(left);
        var rightInfinite = Decimal64Encoding.IsInfinity(right);

        if ((leftInfinite && Decimal64Encoding.IsZero(right)) || (rightInfinite && Decimal64Encoding.IsZero(left)))
        {
            // An infinity times a zero has no product.
            return Invalid(ref status);
        }

        return Decimal64Encoding.Infinity(Decimal64Encoding.IsNegative(left) != Decimal64Encoding.IsNegative(right));
    }

    // Fused multiply-add.

    public static ulong FusedMultiplyAdd(ulong left, ulong right, ulong addend, Decimal64Rounding rounding,
        ref Decimal64Status status)
    {
        if (Decimal64Encoding.IsSpecial(left) || Decimal64Encoding.IsSpecial(right) || Decimal64Encoding.IsSpecial(addend))
        {
            return FusedMultiplyAddSpecial(left, right, addend, ref status);
        }

        var leftCoefficient = Decimal64Encoding.Unpack(left, out var leftExponent);
        var rightCoefficient = Decimal64Encoding.Unpack(right, out var rightExponent);
        var addendCoefficient = Decimal64Encoding.Unpack(addend, out var addendExponent);
        var productNegative = Decimal64Encoding.IsNegative(left) != Decimal64Encoding.IsNegative(right);
        var productExponent = leftExponent + rightExponent;
        var addendNegative = Decimal64Encoding.IsNegative(addend);

        if (((leftCoefficient | rightCoefficient) >> 32) == 0)
        {
            var product = leftCoefficient * rightCoefficient;
            if (product <= Decimal64Encoding.MaxCoefficient)
            {
                return AddFinite(productNegative, product, productExponent, addendNegative,
                    addendCoefficient, addendExponent, rounding, ref status);
            }

            var productHigh = product / Decimal64Encoding.CoefficientLimit;
            var productLow = product - (productHigh * Decimal64Encoding.CoefficientLimit);
            return Decimal64Product.FusedAdd(productNegative, productHigh, productLow, productExponent,
                addendNegative, addendCoefficient, addendExponent, rounding, ref status);
        }

        Decimal64Product.Multiply(leftCoefficient, rightCoefficient, out var high, out var low);
        if (high == 0)
        {
            return AddFinite(productNegative, low, productExponent, addendNegative, addendCoefficient,
                addendExponent, rounding, ref status);
        }

        return Decimal64Product.FusedAdd(productNegative, high, low, productExponent, addendNegative,
            addendCoefficient, addendExponent, rounding, ref status);
    }

    private static ulong FusedMultiplyAddSpecial(ulong left, ulong right, ulong addend, ref Decimal64Status status)
    {
        if (Decimal64Encoding.IsSignalingNaN(left))
        {
            status |= Decimal64Status.InvalidOperation;
            return Decimal64Encoding.Quiet(left);
        }

        if (Decimal64Encoding.IsSignalingNaN(right))
        {
            status |= Decimal64Status.InvalidOperation;
            return Decimal64Encoding.Quiet(right);
        }

        if (Decimal64Encoding.IsSignalingNaN(addend))
        {
            status |= Decimal64Status.InvalidOperation;
            return Decimal64Encoding.Quiet(addend);
        }

        // An invalid multiplication is reported even when the addend is a quiet NaN, so the
        // product is settled before the addend's NaN is looked at.
        if (!Decimal64Encoding.IsNaN(left) && !Decimal64Encoding.IsNaN(right)
            && (Decimal64Encoding.IsInfinity(left) || Decimal64Encoding.IsInfinity(right)))
        {
            var product = MultiplyInfinity(left, right, ref status);
            if (Decimal64Encoding.IsNaN(addend))
            {
                return Decimal64Encoding.Quiet(addend);
            }

            if (Decimal64Encoding.IsInfinity(product))
            {
                // An infinite product still has to meet the addend, which can be the other
                // infinity and make the addition invalid in its own right.
                return AddInfinity(product, addend, ref status);
            }

            return product;
        }

        if (Decimal64Encoding.IsNaN(left))
        {
            return Decimal64Encoding.Quiet(left);
        }

        if (Decimal64Encoding.IsNaN(right))
        {
            return Decimal64Encoding.Quiet(right);
        }

        if (Decimal64Encoding.IsNaN(addend))
        {
            return Decimal64Encoding.Quiet(addend);
        }

        // Everything else is finite, so the addend is the infinity.
        return Decimal64Encoding.Infinity(Decimal64Encoding.IsNegative(addend));
    }

    // Division and its relatives.

    public static ulong Divide(ulong left, ulong right, Decimal64Rounding rounding, ref Decimal64Status status)
    {
        if (Decimal64Encoding.IsSpecial(left) || Decimal64Encoding.IsSpecial(right))
        {
            return DivideSpecial(left, right, Decimal64DivisionKind.Divide, ref status);
        }

        var leftCoefficient = Decimal64Encoding.Unpack(left, out var leftExponent);
        var rightCoefficient = Decimal64Encoding.Unpack(right, out var rightExponent);
        var negative = Decimal64Encoding.IsNegative(left) != Decimal64Encoding.IsNegative(right);

        if (rightCoefficient == 0)
        {
            if (leftCoefficient == 0)
            {
                status |= Decimal64Status.DivisionUndefined;
                return Decimal64Encoding.QuietNaN();
            }

            status |= Decimal64Status.DivisionByZero;
            return Decimal64Encoding.Infinity(negative);
        }

        var idealExponent = leftExponent - rightExponent;
        if (leftCoefficient == 0)
        {
            return Decimal64Finalizer.Zero(negative, idealExponent, ref status);
        }

        return DivideFinite(negative, leftCoefficient, rightCoefficient, idealExponent, rounding, ref status);
    }

    /// <summary>
    /// The quotient to seventeen or eighteen digits, with the remainder deciding the
    /// residue. The dividend is scaled by ten to <c>17 + divisor digits - dividend digits</c>,
    /// which puts the quotient between 10^16 and 10^18.
    /// </summary>
    private static ulong DivideFinite(bool negative, ulong dividend, ulong divisor, int idealExponent,
        Decimal64Rounding rounding, ref Decimal64Status status)
    {
        var dividendDigits = Decimal64Tables.CountDigits(dividend);
        var divisorDigits = Decimal64Tables.CountDigits(divisor);
        var scale = 17 + divisorDigits - dividendDigits;

        // Eighteen digits of dividend, then as many zeros as the divisor has digits less
        // one: the top divisor-less-one digits of that seed the remainder, and the rest is
        // exactly eighteen digits of chunks.
        var wide = dividend * Decimal64Tables.PowerOfTen(18 - dividendDigits);
        var leading = Decimal64Tables.DivRemPowerOfTen(wide, WideDigits - divisorDigits, out var rest);
        var chunks = rest * Decimal64Tables.PowerOfTen(divisorDigits - 1);

        var quotient = Decimal64Divider.Divide(leading, chunks, 18, divisor, out var remainder);
        var exponent = idealExponent - scale;

        if (remainder == 0)
        {
            // Exact, so the specification wants the exponent nearest the ideal: give back
            // the trailing zeros the scaling introduced, and no more.
            while (exponent < idealExponent)
            {
                var shorter = quotient / 10;
                if ((shorter * 10) != quotient)
                {
                    break;
                }

                quotient = shorter;
                exponent++;
            }

            return Decimal64Finalizer.Finalize(negative, quotient, exponent, Decimal64Residue.Exact, rounding, ref status);
        }

        // Twice the remainder against the divisor says which side of half it falls on.
        var doubled = remainder * 2;
        var residue = doubled < divisor
            ? Decimal64Residue.BelowHalf
            : doubled == divisor ? Decimal64Residue.Half : Decimal64Residue.AboveHalf;

        return Decimal64Finalizer.Finalize(negative, quotient, exponent, residue, rounding, ref status);
    }

    /// <summary>The integer part of the quotient, with a zero exponent.</summary>
    public static ulong DivideInteger(ulong left, ulong right, Decimal64Rounding rounding, ref Decimal64Status status)
    {
        if (Decimal64Encoding.IsSpecial(left) || Decimal64Encoding.IsSpecial(right))
        {
            return DivideSpecial(left, right, Decimal64DivisionKind.DivideInteger, ref status);
        }

        var leftCoefficient = Decimal64Encoding.Unpack(left, out var leftExponent);
        var rightCoefficient = Decimal64Encoding.Unpack(right, out var rightExponent);
        var negative = Decimal64Encoding.IsNegative(left) != Decimal64Encoding.IsNegative(right);

        if (rightCoefficient == 0)
        {
            if (leftCoefficient == 0)
            {
                status |= Decimal64Status.DivisionUndefined;
                return Decimal64Encoding.QuietNaN();
            }

            status |= Decimal64Status.DivisionByZero;
            return Decimal64Encoding.Infinity(negative);
        }

        if (leftCoefficient == 0)
        {
            return Decimal64Encoding.Zero(negative, 0);
        }

        if (!TryIntegerDivide(leftCoefficient, leftExponent, rightCoefficient, rightExponent,
            out var quotient, out _, out _, out _))
        {
            status |= Decimal64Status.DivisionImpossible;
            return Decimal64Encoding.QuietNaN();
        }

        return Decimal64Finalizer.Finalize(negative, quotient, 0, Decimal64Residue.Exact, rounding, ref status);
    }

    /// <summary>
    /// What is left after taking out the integer quotient, which the division leaves behind
    /// exactly. The near form takes the quotient to the nearest integer instead, so the
    /// remainder can come out the other side of zero.
    /// </summary>
    public static ulong Remainder(ulong left, ulong right, bool near, Decimal64Rounding rounding, ref Decimal64Status status)
    {
        var kind = near ? Decimal64DivisionKind.RemainderNear : Decimal64DivisionKind.Remainder;

        if (Decimal64Encoding.IsSpecial(left) || Decimal64Encoding.IsSpecial(right))
        {
            return DivideSpecial(left, right, kind, ref status);
        }

        var leftCoefficient = Decimal64Encoding.Unpack(left, out var leftExponent);
        var rightCoefficient = Decimal64Encoding.Unpack(right, out var rightExponent);
        var negative = Decimal64Encoding.IsNegative(left);

        if (rightCoefficient == 0)
        {
            if (leftCoefficient == 0)
            {
                status |= Decimal64Status.DivisionUndefined;
                return Decimal64Encoding.QuietNaN();
            }

            return Invalid(ref status);
        }

        if (leftCoefficient == 0)
        {
            // Nothing is taken out of a zero, so the dividend is what is left: its own
            // sign, at the lower of the two exponents.
            return Decimal64Finalizer.Zero(negative, Math.Min(leftExponent, rightExponent), ref status);
        }

        if (!TryIntegerDivide(leftCoefficient, leftExponent, rightCoefficient, rightExponent,
            out var quotient, out var remainder, out var remainderExponent, out var scaledDivisor))
        {
            status |= Decimal64Status.DivisionImpossible;
            return Decimal64Encoding.QuietNaN();
        }

        if (near && scaledDivisor != 0)
        {
            // The nearest integer quotient may be one higher, in which case the remainder is
            // measured from that one and changes sign. A tie goes to the even quotient. The
            // remainder is set against what is left of the divisor rather than doubled,
            // since a divisor lifted to nineteen digits leaves no room to double.
            var other = scaledDivisor - remainder;
            if (remainder > other || (remainder == other && (quotient & 1) != 0))
            {
                if (quotient == Decimal64Encoding.MaxCoefficient)
                {
                    status |= Decimal64Status.DivisionImpossible;
                    return Decimal64Encoding.QuietNaN();
                }

                remainder = scaledDivisor - remainder;
                negative = !negative;
            }
        }

        if (remainder == 0)
        {
            // A zero remainder takes the dividend's sign.
            return Decimal64Finalizer.Zero(Decimal64Encoding.IsNegative(left), remainderExponent, ref status);
        }

        return Decimal64Finalizer.Finalize(negative, remainder, remainderExponent, Decimal64Residue.Exact, rounding, ref status);
    }

    /// <summary>
    /// The integer quotient of two finite non-zero values and what it leaves, exactly. The
    /// quotient has to fit the precision; one that would not makes the division impossible.
    /// The remainder comes back at the lower of the two exponents, with the divisor scaled
    /// to that exponent beside it when it fits a word, and zero when it does not -- in which
    /// case the remainder is the whole dividend and is below half of it.
    /// </summary>
    private static bool TryIntegerDivide(ulong dividend, int dividendExponent, ulong divisor, int divisorExponent,
        out ulong quotient, out ulong remainder, out int remainderExponent, out ulong scaledDivisor)
    {
        var dividendDigits = Decimal64Tables.CountDigits(dividend);
        var divisorDigits = Decimal64Tables.CountDigits(divisor);

        if (dividendExponent >= divisorExponent)
        {
            var scale = dividendExponent - divisorExponent;
            remainderExponent = divisorExponent;
            scaledDivisor = divisor;

            // The dividend scaled to the divisor's exponent has this many digits; past
            // sixteen more than the divisor the quotient cannot fit.
            var wideDigits = dividendDigits + scale;
            if (wideDigits - divisorDigits >= 17)
            {
                quotient = 0;
                remainder = 0;
                return false;
            }

            // The digits below the divisor's top ones are what the chunked division eats.
            var chunkDigits = wideDigits - divisorDigits + 1;

            if (wideDigits <= WideDigits)
            {
                var wide = dividend * Decimal64Tables.PowerOfTen(scale);
                if (chunkDigits <= 0)
                {
                    quotient = 0;
                    remainder = wide;
                    return true;
                }

                var leading = Decimal64Tables.DivRemPowerOfTen(wide, chunkDigits, out var chunks);
                quotient = Decimal64Divider.Divide(leading, chunks, chunkDigits, divisor, out remainder);
            }
            else
            {
                // Twenty to thirty-two digits: the dividend is written as eighteen digits
                // and a count of zeros, and the top divisor-less-one digits come off the
                // eighteen before the zeros are put back on what remains.
                var seed = dividend * Decimal64Tables.PowerOfTen(18 - dividendDigits);
                var zeros = wideDigits - 18;
                var leading = Decimal64Tables.DivRemPowerOfTen(seed, WideDigits - divisorDigits, out var rest);
                var chunks = rest * Decimal64Tables.PowerOfTen(zeros);
                quotient = Decimal64Divider.Divide(leading, chunks, chunkDigits, divisor, out remainder);
            }

            return quotient <= Decimal64Encoding.MaxCoefficient;
        }

        var lift = divisorExponent - dividendExponent;
        remainderExponent = dividendExponent;

        if (divisorDigits + lift > WideDigits)
        {
            // The divisor lifted to the dividend's exponent is past any word, so it is past
            // the dividend too: nothing divides out.
            quotient = 0;
            remainder = dividend;
            scaledDivisor = 0;
            return true;
        }

        scaledDivisor = divisor * Decimal64Tables.PowerOfTen(lift);
        if (dividend < scaledDivisor)
        {
            quotient = 0;
            remainder = dividend;
            return true;
        }

        // The quotient is under sixteen digits here and this path is rare, so the hardware
        // division is fine.
        quotient = dividend / scaledDivisor;
        remainder = dividend - (quotient * scaledDivisor);
        return true;
    }

    /// <summary>
    /// The infinities and NaNs, for all four operations built on division.
    /// </summary>
    private static ulong DivideSpecial(ulong left, ulong right, Decimal64DivisionKind kind, ref Decimal64Status status)
    {
        if (Decimal64Encoding.IsNaN(left) || Decimal64Encoding.IsNaN(right))
        {
            return PropagateNaN(left, right, ref status);
        }

        var isRemainder = kind is Decimal64DivisionKind.Remainder or Decimal64DivisionKind.RemainderNear;
        var negative = Decimal64Encoding.IsNegative(left) != Decimal64Encoding.IsNegative(right);

        if (Decimal64Encoding.IsInfinity(left))
        {
            if (Decimal64Encoding.IsInfinity(right) || isRemainder)
            {
                // One infinity over another has no quotient, and there is nothing left over
                // from an infinity.
                return Invalid(ref status);
            }

            // An infinity over anything finite is infinite, even over a zero.
            return Decimal64Encoding.Infinity(negative);
        }

        // The divisor is the infinity.
        if (isRemainder)
        {
            // Nothing has been taken out, so the whole dividend is left over.
            return Decimal64Encoding.Canonical(left);
        }

        if (kind == Decimal64DivisionKind.DivideInteger)
        {
            // No whole copies of an infinity come out, and an integer result sits at an
            // exponent of zero rather than being clamped down.
            return Decimal64Encoding.Zero(negative, 0);
        }

        // A finite over an infinity is a zero whose exponent wants to be unboundedly small;
        // it comes to rest at the smallest the format holds, which is a clamp.
        status |= Decimal64Status.Clamped;
        return Decimal64Encoding.Zero(negative, Decimal64Encoding.MinQuantumExponent);
    }

    // Comparison.

    /// <summary>
    /// Compares two values numerically, giving -1, 0, 1, or <see cref="int.MinValue"/> with
    /// a NaN in <paramref name="nan"/> when the two are unordered. The two zeros are equal
    /// here, unlike under the total order.
    /// </summary>
    public static int Compare(ulong left, ulong right, bool signaling, ref Decimal64Status status, out ulong nan)
    {
        nan = 0;

        if (Decimal64Encoding.IsSpecial(left) || Decimal64Encoding.IsSpecial(right))
        {
            if (Decimal64Encoding.IsNaN(left) || Decimal64Encoding.IsNaN(right))
            {
                // The quiet comparison lets a quiet NaN through without a condition; the
                // signaling one reports every NaN.
                if (signaling)
                {
                    status |= Decimal64Status.InvalidOperation;
                }

                nan = PropagateNaN(left, right, ref status);
                return int.MinValue;
            }

            return CompareInfinity(left, right);
        }

        var leftCoefficient = Decimal64Encoding.Unpack(left, out var leftExponent);
        var rightCoefficient = Decimal64Encoding.Unpack(right, out var rightExponent);

        return CompareFinite(Decimal64Encoding.IsNegative(left), leftCoefficient, leftExponent,
            Decimal64Encoding.IsNegative(right), rightCoefficient, rightExponent);
    }

    /// <summary>
    /// Orders values where at least one is infinite and neither is a NaN. An infinity is
    /// beyond every finite value on its own side, and two of the same sign are equal.
    /// </summary>
    public static int CompareInfinity(ulong left, ulong right)
    {
        if (Decimal64Encoding.IsInfinity(left) && Decimal64Encoding.IsInfinity(right))
        {
            if (Decimal64Encoding.IsNegative(left) == Decimal64Encoding.IsNegative(right))
            {
                return 0;
            }

            return Decimal64Encoding.IsNegative(left) ? -1 : 1;
        }

        if (Decimal64Encoding.IsInfinity(left))
        {
            return Decimal64Encoding.IsNegative(left) ? -1 : 1;
        }

        return Decimal64Encoding.IsNegative(right) ? 1 : -1;
    }

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
    /// Orders two non-zero coefficients by value, ignoring both signs. Where the leading
    /// digits sit decides it unless they sit in the same place; then the coefficients are
    /// lined up, which stays inside sixteen digits because their leading digits agree.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CompareMagnitude(ulong leftCoefficient, int leftExponent, ulong rightCoefficient,
        int rightExponent)
    {
        var leftTop = leftExponent + Decimal64Tables.CountDigits(leftCoefficient);
        var rightTop = rightExponent + Decimal64Tables.CountDigits(rightCoefficient);

        if (leftTop != rightTop)
        {
            return leftTop < rightTop ? -1 : 1;
        }

        if (leftExponent > rightExponent)
        {
            leftCoefficient *= Decimal64Tables.PowerOfTen(leftExponent - rightExponent);
        }
        else if (rightExponent > leftExponent)
        {
            rightCoefficient *= Decimal64Tables.PowerOfTen(rightExponent - leftExponent);
        }

        if (leftCoefficient == rightCoefficient)
        {
            return 0;
        }

        return leftCoefficient < rightCoefficient ? -1 : 1;
    }
}
