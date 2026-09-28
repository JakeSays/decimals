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
/// A product of two seven-digit coefficients has fourteen digits and fits the word, so the
/// one intermediate that does not fit is an operand aligned far above the other, which is
/// cut down to nineteen digits and a <see cref="Decimal32Residue"/> before it is added; the
/// finalizer then rounds once.
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
internal static class Decimal32Arithmetic
{
    private const int WideDigits = Decimal32Tables.MaxPower;

    /// <summary>Digits of quotient a division computes: the precision and one to round on.</summary>
    private const int QuotientDigits = Decimal32Encoding.Precision + 1;

    // The special values, settled the same way in every operation: a signaling NaN is
    // invalid and comes out quiet, a quiet NaN passes through, left before right.

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

    public static uint PropagateNaN(uint value, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsSignalingNaN(value))
        {
            status |= Decimal32Status.InvalidOperation;
        }

        return Decimal32Encoding.Quiet(value);
    }

    public static uint Invalid(ref Decimal32Status status)
    {
        status |= Decimal32Status.InvalidOperation;
        return Decimal32Encoding.QuietNaN();
    }

    // Addition and subtraction.

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
    /// Subtraction is addition with the right operand's sign flipped -- after the NaNs are
    /// settled, since a NaN keeps the sign it arrived with.
    /// </summary>
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
                // Infinities of opposite sign have no sum.
                return Invalid(ref status);
            }

            return Decimal32Encoding.Infinity(Decimal32Encoding.IsNegative(left));
        }

        return Decimal32Encoding.Infinity(Decimal32Encoding.IsNegative(right));
    }

    /// <summary>
    /// Adds two finite values given as sign, coefficient of at most seven digits, and
    /// exponent.
    /// </summary>
    public static uint AddFinite(bool leftNegative, ulong leftCoefficient, int leftExponent,
        bool rightNegative, ulong rightCoefficient, int rightExponent, Decimal32Rounding rounding,
        ref Decimal32Status status)
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

        // Too far apart to align exactly: the higher operand goes to nineteen digits and the
        // lower one is folded up to that exponent, which loses nothing that rounding needs.
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

        // Subtracting an inexact operand takes one more unit off, and the fraction of that
        // unit left behind is the flipped residue.
        return Decimal32Finalizer.Finalize(leftNegative, wide - folded - 1, exponent, Decimal32Rounder.Flip(residue),
            rounding, ref status);
    }

    /// <summary>
    /// The difference of two coefficients at the same exponent and opposite signs, which
    /// takes the sign of the larger.
    /// </summary>
    private static uint SubtractAligned(bool leftNegative, ulong left, bool rightNegative, ulong right,
        int exponent, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        if (left == right)
        {
            // Opposite signs canceling exactly gives a positive zero, except when the
            // rounding runs toward negative infinity.
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
    /// A zero operand contributes only its exponent, and only when that is the lower of the
    /// two: the other operand is padded down to meet it as far as the precision allows.
    /// </summary>
    private static uint AddWithZero(bool leftNegative, ulong leftCoefficient, int leftExponent,
        bool rightNegative, ulong rightCoefficient, int rightExponent, Decimal32Rounding rounding,
        ref Decimal32Status status)
    {
        if (leftCoefficient == 0 && rightCoefficient == 0)
        {
            // Two zeros keep the lower exponent, and the sign is theirs only when they
            // agree; otherwise it follows the rounding.
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
                // Only so many zeros fit; the value is unchanged, but digits went.
                pad = Decimal32Encoding.Precision - digits;
                status |= Decimal32Status.Rounded;
            }

            coefficient *= Decimal32Tables.PowerOfTen(pad);
            exponent -= pad;
        }

        return Decimal32Finalizer.Finalize(otherNegative, coefficient, exponent, Decimal32Residue.Exact, rounding, ref status);
    }

    /// <summary>
    /// Applies the context to a value without otherwise changing it, which is addition to a
    /// zero of the value's own exponent. That zero is what makes <c>plus -0</c> a positive
    /// zero under every rounding but one. <paramref name="negate"/> makes it a subtraction.
    /// </summary>
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
    /// The product of two coefficients of at most seven digits has at most fourteen, so it
    /// is exact in the word and the finalizer rounds it.
    /// </summary>
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

    /// <summary>At least one operand is infinite and neither is a NaN.</summary>
    private static uint MultiplyInfinity(uint left, uint right, ref Decimal32Status status)
    {
        var leftInfinite = Decimal32Encoding.IsInfinity(left);
        var rightInfinite = Decimal32Encoding.IsInfinity(right);

        if ((leftInfinite && Decimal32Encoding.IsZero(right)) || (rightInfinite && Decimal32Encoding.IsZero(left)))
        {
            // An infinity times a zero has no product.
            return Invalid(ref status);
        }

        return Decimal32Encoding.Infinity(Decimal32Encoding.IsNegative(left) != Decimal32Encoding.IsNegative(right));
    }

    // Fused multiply-add.

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
            // A product within the format is an ordinary addition, which pads it out to
            // the precision when the addend lies below it.
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
        // product is settled before the addend's NaN is looked at.
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
                // An infinite product still has to meet the addend, which can be the other
                // infinity and make the addition invalid in its own right.
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

        // Everything else is finite, so the addend is the infinity.
        return Decimal32Encoding.Infinity(Decimal32Encoding.IsNegative(addend));
    }

    // Division and its relatives.

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
    /// The quotient to seven or eight digits, with the remainder deciding the residue. The
    /// dividend is scaled by ten to <c>7 + divisor digits - dividend digits</c>, which puts
    /// the quotient between 10^6 and 10^8: at the precision, when the remainder alone
    /// decides the rounding, or one digit over, which the finalizer drops with the
    /// remainder's residue under it. The scaled dividend has at most fourteen digits.
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
            // Exact, so the specification wants the exponent nearest the ideal: give back
            // the trailing zeros the scaling introduced, and no more.
            Decimal32Shaping.StripTrailingZeros(ref quotient, ref exponent, idealExponent);
            return Decimal32Finalizer.Finalize(negative, quotient, exponent, Decimal32Residue.Exact, rounding, ref status);
        }

        // Twice the remainder against the divisor says which side of half it falls on.
        var doubled = remainder * 2;
        var residue = doubled < divisor
            ? Decimal32Residue.BelowHalf
            : doubled == divisor ? Decimal32Residue.Half : Decimal32Residue.AboveHalf;

        return Decimal32Finalizer.Finalize(negative, quotient, exponent, residue, rounding, ref status);
    }

    /// <summary>The integer part of the quotient, with a zero exponent.</summary>
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

        if (!TryIntegerDivide(leftCoefficient, leftExponent, rightCoefficient, rightExponent,
            out var quotient, out _, out _, out _))
        {
            status |= Decimal32Status.DivisionImpossible;
            return Decimal32Encoding.QuietNaN();
        }

        return Decimal32Finalizer.Finalize(negative, quotient, 0, Decimal32Residue.Exact, rounding, ref status);
    }

    /// <summary>
    /// What is left after taking out the integer quotient, which the division leaves behind
    /// exactly. The near form takes the quotient to the nearest integer instead, so the
    /// remainder can come out the other side of zero.
    /// </summary>
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
            // Nothing is taken out of a zero, so the dividend is what is left: its own
            // sign, at the lower of the two exponents.
            return Decimal32Finalizer.Zero(negative, Math.Min(leftExponent, rightExponent), ref status);
        }

        if (!TryIntegerDivide(leftCoefficient, leftExponent, rightCoefficient, rightExponent,
            out var quotient, out var remainder, out var remainderExponent, out var scaledDivisor))
        {
            status |= Decimal32Status.DivisionImpossible;
            return Decimal32Encoding.QuietNaN();
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
    /// The integer quotient of two finite non-zero values and what it leaves, exactly. The
    /// quotient has to fit the precision; one that would not makes the division impossible.
    /// The remainder comes back at the lower of the two exponents, with the divisor scaled
    /// to that exponent beside it when it fits a word, and zero when it does not -- in which
    /// case the remainder is the whole dividend and is below half of it.
    /// </summary>
    private static bool TryIntegerDivide(ulong dividend, int dividendExponent, ulong divisor, int divisorExponent,
        out ulong quotient, out ulong remainder, out int remainderExponent, out ulong scaledDivisor)
    {
        var dividendDigits = Decimal32Tables.CountDigits(dividend);
        var divisorDigits = Decimal32Tables.CountDigits(divisor);

        if (dividendExponent >= divisorExponent)
        {
            var scale = dividendExponent - divisorExponent;
            remainderExponent = divisorExponent;
            scaledDivisor = divisor;

            // The dividend scaled to the divisor's exponent has this many digits; past
            // seven more than the divisor the quotient cannot fit. Within that it has at
            // most fourteen digits, which the divider takes whole.
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
            // The divisor lifted to the dividend's exponent is past any word, so it is past
            // the dividend too: nothing divides out.
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

        // Both are below the dividend's seven digits here, so the divider takes them.
        quotient = Decimal32Divider.Divide(dividend, scaledDivisor, out remainder);
        return true;
    }

    /// <summary>
    /// The infinities and NaNs, for all four operations built on division.
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
                // One infinity over another has no quotient, and there is nothing left over
                // from an infinity.
                return Invalid(ref status);
            }

            // An infinity over anything finite is infinite, even over a zero.
            return Decimal32Encoding.Infinity(negative);
        }

        // The divisor is the infinity.
        if (isRemainder)
        {
            // Nothing has been taken out, so the whole dividend is left over.
            return Decimal32Encoding.Canonical(left);
        }

        if (kind == Decimal32DivisionKind.DivideInteger)
        {
            // No whole copies of an infinity come out, and an integer result sits at an
            // exponent of zero rather than being clamped down.
            return Decimal32Encoding.Zero(negative, 0);
        }

        // A finite over an infinity is a zero whose exponent wants to be unboundedly small;
        // it comes to rest at the smallest the format holds, which is a clamp.
        status |= Decimal32Status.Clamped;
        return Decimal32Encoding.Zero(negative, Decimal32Encoding.MinQuantumExponent);
    }

    // Comparison.

    /// <summary>
    /// Compares two values numerically, giving -1, 0, 1, or <see cref="int.MinValue"/> with
    /// a NaN in <paramref name="nan"/> when the two are unordered. The two zeros are equal
    /// here, unlike under the total order.
    /// </summary>
    public static int Compare(uint left, uint right, bool signaling, ref Decimal32Status status, out uint nan)
    {
        nan = 0;

        if (Decimal32Encoding.IsSpecial(left) || Decimal32Encoding.IsSpecial(right))
        {
            if (Decimal32Encoding.IsNaN(left) || Decimal32Encoding.IsNaN(right))
            {
                // The quiet comparison lets a quiet NaN through without a condition; the
                // signaling one reports every NaN.
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
    /// Orders values where at least one is infinite and neither is a NaN. An infinity is
    /// beyond every finite value on its own side, and two of the same sign are equal.
    /// </summary>
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
    /// lined up, which stays inside seven digits because their leading digits agree.
    /// </summary>
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
