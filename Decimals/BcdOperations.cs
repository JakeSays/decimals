// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// The digit-form operations as the core calls them: encoding in, encoding out, with the
/// special values settled before the arithmetic sees anything.
/// </summary>
/// <remarks>
/// decNumber settles specials at the top of each decFloat operation for the same reason
/// they are settled here -- a NaN or an infinity has no coefficient to compute on, and
/// letting one reach the digit paths would mean every one of them checking.
/// </remarks>
internal static unsafe class BcdOperations
{
    public static UInt128 Add<TFormat>(UInt128 leftBits, UInt128 rightBits,
        DecimalRounding rounding, ref DecimalStatus status, ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        var left = BcdCodec.Decode<TFormat>(leftBits, work.Left);
        var right = BcdCodec.Decode<TFormat>(rightBits, work.Right);

        if (TryHandleNaN<TFormat>(left, right, ref status, out var nan))
        {
            return nan;
        }

        if (left.Kind == DecimalKind.Infinity || right.Kind == DecimalKind.Infinity)
        {
            return AddInfinity<TFormat>(left, right, ref status);
        }

        var result = BcdArithmetic.Add<TFormat>(left, right, work.Result, rounding,
            work.LeftWork, work.RightWork);

        BcdFinalizer.Finalize<TFormat>(ref result, rounding, ref status);
        return BcdCodec.Encode<TFormat>(result);
    }

    public static UInt128 Subtract<TFormat>(UInt128 leftBits, UInt128 rightBits,
        DecimalRounding rounding, ref DecimalStatus status, ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        var left = BcdCodec.Decode<TFormat>(leftBits, work.Left);
        var right = BcdCodec.Decode<TFormat>(rightBits, work.Right);

        if (TryHandleNaN<TFormat>(left, right, ref status, out var nan))
        {
            return nan;
        }

        // Subtraction is addition with the right operand's sign flipped, which is true of
        // infinities too.
        right.IsNegative = !right.IsNegative;

        if (left.Kind == DecimalKind.Infinity || right.Kind == DecimalKind.Infinity)
        {
            return AddInfinity<TFormat>(left, right, ref status);
        }

        var result = BcdArithmetic.Add<TFormat>(left, right, work.Result, rounding,
            work.LeftWork, work.RightWork);

        BcdFinalizer.Finalize<TFormat>(ref result, rounding, ref status);
        return BcdCodec.Encode<TFormat>(result);
    }

    public static UInt128 Multiply<TFormat>(UInt128 leftBits, UInt128 rightBits,
        DecimalRounding rounding, ref DecimalStatus status, ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        var left = BcdCodec.Decode<TFormat>(leftBits, work.Left);
        var right = BcdCodec.Decode<TFormat>(rightBits, work.Right);

        if (TryHandleNaN<TFormat>(left, right, ref status, out var nan))
        {
            return nan;
        }

        if (TryMultiplySpecial<TFormat>(left, right, ref status, out var special))
        {
            return special;
        }

        var result = BcdMultiplier.Multiply<TFormat>(left, right, work.Product,
            work.LeftLimbs, work.RightLimbs, work.Accumulator);

        BcdFinalizer.Finalize<TFormat>(ref result, rounding, ref status);
        return BcdCodec.Encode<TFormat>(result);
    }

    public static UInt128 Divide<TFormat>(UInt128 leftBits, UInt128 rightBits,
        DecimalRounding rounding, ref DecimalStatus status, ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        var left = BcdCodec.Decode<TFormat>(leftBits, work.Left);
        var right = BcdCodec.Decode<TFormat>(rightBits, work.Right);

        if (TryHandleNaN<TFormat>(left, right, ref status, out var nan))
        {
            return nan;
        }

        if (TryDivisionSpecial<TFormat>(left, right, BcdDivisionKind.Divide, ref status,
            out var special))
        {
            return special;
        }

        var result = BcdDivider.Divide<TFormat>(left, right, work.Result,
            work.WideAccumulator, work.LeftLimbs, work.Quotient, ref status);

        if (result.Kind != DecimalKind.Finite)
        {
            return BcdCodec.Encode<TFormat>(result);
        }

        BcdFinalizer.Finalize<TFormat>(ref result, rounding, ref status);
        return BcdCodec.Encode<TFormat>(result);
    }

    /// <summary>
    /// The integer part of the quotient, with no remainder kept.
    /// </summary>
    public static UInt128 DivideInteger<TFormat>(UInt128 leftBits, UInt128 rightBits,
        DecimalRounding rounding, ref DecimalStatus status, ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        if (!TryIntegerQuotient<TFormat>(leftBits, rightBits, false, false, rounding, ref status,
            ref work, out var quotient, out var special))
        {
            return special;
        }

        BcdFinalizer.Finalize<TFormat>(ref quotient, rounding, ref status);
        return BcdCodec.Encode<TFormat>(quotient);
    }

    public static UInt128 Remainder<TFormat>(UInt128 leftBits, UInt128 rightBits,
        DecimalRounding rounding, ref DecimalStatus status, ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        return RemainderCore<TFormat>(leftBits, rightBits, false, rounding, ref status, ref work);
    }

    public static UInt128 RemainderNear<TFormat>(UInt128 leftBits, UInt128 rightBits,
        DecimalRounding rounding, ref DecimalStatus status, ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        return RemainderCore<TFormat>(leftBits, rightBits, true, rounding, ref status, ref work);
    }

    /// <summary>
    /// What is left after taking out the integer quotient. decNumber forms it as a fused
    /// multiply-add rather than a subtraction, which is exact and needs no second rounding:
    /// the remainder is <c>lhs - quotient * rhs</c>.
    /// </summary>
    private static UInt128 RemainderCore<TFormat>(UInt128 leftBits, UInt128 rightBits,
        bool roundNear, DecimalRounding rounding, ref DecimalStatus status, ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        if (!TryIntegerQuotient<TFormat>(leftBits, rightBits, true, roundNear, rounding,
            ref status, ref work, out var quotient, out var special))
        {
            return special;
        }

        BcdFinalizer.Finalize<TFormat>(ref quotient, rounding, ref status);

        // Negated, so that the fused multiply-add subtracts.
        quotient.IsNegative = !quotient.IsNegative;
        var negatedBits = BcdCodec.Encode<TFormat>(quotient);

        var remainderStatus = status;
        var result = FusedMultiplyAdd<TFormat>(negatedBits, rightBits, leftBits, rounding,
            ref remainderStatus, ref work);

        status = remainderStatus;

        // A zero remainder takes the dividend's sign, which the multiply-add's own sign
        // rules do not give it.
        var settled = BcdCodec.Decode<TFormat>(result, work.Integer);
        if (settled.IsZero)
        {
            var dividend = BcdCodec.Decode<TFormat>(leftBits, work.Addend);
            settled.IsNegative = dividend.IsNegative;
            return BcdCodec.Encode<TFormat>(settled);
        }

        return result;
    }

    /// <summary>
    /// Divides and shapes the quotient into an integer, truncating or rounding half to even
    /// as the operation asks. An integer that will not fit the format has no representation
    /// and the division is impossible.
    /// </summary>
    private static bool TryIntegerQuotient<TFormat>(UInt128 leftBits, UInt128 rightBits,
        bool forRemainder, bool roundNear, DecimalRounding rounding, ref DecimalStatus status,
        ref BcdWorkspace work, out BcdNumber quotient, out UInt128 special)
        where TFormat : IDecimalFormat
    {
        var left = BcdCodec.Decode<TFormat>(leftBits, work.Left);
        var right = BcdCodec.Decode<TFormat>(rightBits, work.Right);

        quotient = default;

        if (TryHandleNaN<TFormat>(left, right, ref status, out var nan))
        {
            special = nan;
            return false;
        }

        var kind = roundNear ? BcdDivisionKind.RemainderNear
            : forRemainder ? BcdDivisionKind.Remainder
            : BcdDivisionKind.DivideInteger;

        if (TryDivisionSpecial<TFormat>(left, right, kind, ref status, out special))
        {
            return false;
        }

        var divided = BcdDivider.Divide<TFormat>(left, right, work.Result,
            work.WideAccumulator, work.LeftLimbs, work.Quotient, ref status);

        if (divided.Kind != DecimalKind.Finite)
        {
            special = BcdCodec.Encode<TFormat>(divided);
            return false;
        }

        // An integer needs as many digits as the value has above the point; past the
        // precision there is nowhere to put them.
        if (divided.DigitCount + divided.Exponent > TFormat.Precision)
        {
            status |= DecimalStatus.DivisionImpossible;
            special = Special<TFormat>(DecimalKind.QuietNaN, false);
            return false;
        }

        ShapeToInteger(ref divided, roundNear);
        quotient = divided;
        return true;
    }

    /// <summary>
    /// Brings a quotient to an exponent of zero: zeros appended when it is already an
    /// integer scaled up, and digits dropped when it is not.
    /// </summary>
    private static void ShapeToInteger(ref BcdNumber value, bool roundNear)
    {
        if (value.Exponent >= 0)
        {
            value.AppendZeros(value.Exponent);
            value.Exponent = 0;
            return;
        }

        var drop = -value.Exponent;

        if (!roundNear)
        {
            // Truncation, which is what an integer division discards.
            if (drop >= value.DigitCount)
            {
                value.SetZero(0);
                return;
            }

            value.DropDigits(drop);
            value.Exponent = 0;
            return;
        }

        // remainderNear takes the closest integer, breaking a tie toward the even one.
        BcdRounder.Round(ref value, drop, DecimalRounding.HalfEven, out _);
        value.Exponent = 0;
    }

    /// <summary>
    /// The infinities and the zero divisor, for all four operations built on division. They
    /// do not agree on what these mean, so the operation has to say which it is.
    /// </summary>
    private static bool TryDivisionSpecial<TFormat>(BcdNumber left, BcdNumber right,
        BcdDivisionKind kind, ref DecimalStatus status, out UInt128 result)
        where TFormat : IDecimalFormat
    {
        var isRemainder = kind is BcdDivisionKind.Remainder or BcdDivisionKind.RemainderNear;
        var negative = left.IsNegative != right.IsNegative;

        if (left.Kind == DecimalKind.Infinity)
        {
            if (right.Kind == DecimalKind.Infinity)
            {
                // One infinity over another has no quotient.
                result = Invalid<TFormat>(ref status);
                return true;
            }

            if (isRemainder)
            {
                // There is nothing left over from an infinity.
                result = Invalid<TFormat>(ref status);
                return true;
            }

            // An infinity over anything finite is infinite, even over a zero.
            result = Special<TFormat>(DecimalKind.Infinity, negative);
            return true;
        }

        if (right.Kind == DecimalKind.Infinity)
        {
            if (isRemainder)
            {
                // Nothing has been taken out, so the whole dividend is left over.
                result = BcdCodec.Encode<TFormat>(left);
                return true;
            }

            if (kind == BcdDivisionKind.DivideInteger)
            {
                // No whole copies of an infinity come out, and an integer result sits at
                // an exponent of zero rather than being clamped down.
                result = ZeroAt<TFormat>(negative, 0);
                return true;
            }

            // A finite over an infinity is a zero whose exponent wants to be unboundedly
            // small; it comes to rest at the smallest the format holds, which is a clamp.
            status |= DecimalStatus.Clamped;
            result = ZeroAt<TFormat>(negative, TFormat.MinQuantumExponent);
            return true;
        }

        if (right.IsZero)
        {
            if (left.IsZero)
            {
                status |= DecimalStatus.DivisionUndefined;
                result = Special<TFormat>(DecimalKind.QuietNaN, false);
                return true;
            }

            if (isRemainder)
            {
                result = Invalid<TFormat>(ref status);
                return true;
            }

            status |= DecimalStatus.DivisionByZero;
            result = Special<TFormat>(DecimalKind.Infinity, negative);
            return true;
        }

        if (left.IsZero)
        {
            if (kind == BcdDivisionKind.DivideInteger)
            {
                // No whole copies come out of a zero, and an integer sits at an exponent
                // of zero rather than at the quotient's ideal one.
                result = ZeroAt<TFormat>(negative, 0);
                return true;
            }

            if (isRemainder)
            {
                // Nothing is taken out of a zero, so the dividend is what is left: its own
                // sign, at the lower of the two exponents.
                result = ZeroAt<TFormat>(left.IsNegative,
                    Math.Min(left.Exponent, right.Exponent));
                return true;
            }

            // A plain divide keeps the ideal exponent, which the divider works out.
        }

        result = UInt128.Zero;
        return false;
    }

    public static UInt128 FusedMultiplyAdd<TFormat>(UInt128 leftBits, UInt128 rightBits,
        UInt128 addendBits, DecimalRounding rounding, ref DecimalStatus status,
        ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        var left = BcdCodec.Decode<TFormat>(leftBits, work.Left);
        var right = BcdCodec.Decode<TFormat>(rightBits, work.Right);
        var addend = BcdCodec.Decode<TFormat>(addendBits, work.Addend);

        if (TryQuietSignalingNaN<TFormat>(left, right, addend, ref status, out var signaling))
        {
            return signaling;
        }

        // An invalid multiplication is reported even when the addend is a quiet NaN, so the
        // product is settled before the addend's NaN is looked at.
        if (!left.IsNaN && !right.IsNaN
            && TryMultiplySpecial<TFormat>(left, right, ref status, out var productSpecial))
        {
            if (addend.IsNaN)
            {
                return QuietedBits<TFormat>(addend);
            }

            // An infinite product still has to meet the addend, which can be the other
            // infinity and make the addition invalid in its own right.
            var infinite = BcdCodec.Decode<TFormat>(productSpecial, work.Product);
            if (infinite.Kind != DecimalKind.Finite)
            {
                return AddInfinity<TFormat>(infinite, addend, ref status);
            }

            return productSpecial;
        }

        if (left.IsNaN || right.IsNaN || addend.IsNaN)
        {
            return QuietedBits<TFormat>(left.IsNaN ? left : right.IsNaN ? right : addend);
        }

        if (addend.Kind == DecimalKind.Infinity)
        {
            return BcdCodec.Encode<TFormat>(addend);
        }

        var result = BcdArithmetic.FusedMultiplyAdd<TFormat>(left, right, addend, rounding,
            work.Product, work.LeftLimbs, work.RightLimbs, work.Accumulator,
            work.Result, work.LeftWork, work.RightWork);

        BcdFinalizer.Finalize<TFormat>(ref result, rounding, ref status);
        return BcdCodec.Encode<TFormat>(result);
    }

    public static DecimalClass Classify<TFormat>(UInt128 valueBits, ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        return BcdOrdering.Classify<TFormat>(BcdCodec.Decode<TFormat>(valueBits, work.Left));
    }

    public static bool IsSubnormal<TFormat>(UInt128 valueBits, ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        return BcdOrdering.IsSubnormal<TFormat>(BcdCodec.Decode<TFormat>(valueBits, work.Left));
    }

    public static int CompareTotal<TFormat>(UInt128 leftBits, UInt128 rightBits,
        ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        return BcdOrdering.CompareTotal(
            BcdCodec.Decode<TFormat>(leftBits, work.Left),
            BcdCodec.Decode<TFormat>(rightBits, work.Right));
    }

    public static int CompareTotalMagnitude<TFormat>(UInt128 leftBits, UInt128 rightBits,
        ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        return BcdOrdering.CompareTotalMagnitude(
            BcdCodec.Decode<TFormat>(leftBits, work.Left),
            BcdCodec.Decode<TFormat>(rightBits, work.Right));
    }

    /// <summary>
    /// The min and max family. A quiet NaN beside a number loses: these hand back the
    /// number. Two values that compare equal still differ, so the total order settles which
    /// member of the pair the caller gets -- negative zero loses to positive zero for max,
    /// and 1.0 loses to 1.
    /// </summary>
    public static UInt128 Select<TFormat>(UInt128 leftBits, UInt128 rightBits, bool wantLarger,
        bool byMagnitude, ref DecimalStatus status, ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        var left = BcdCodec.Decode<TFormat>(leftBits, work.Left);
        var right = BcdCodec.Decode<TFormat>(rightBits, work.Right);

        if (left.Kind == DecimalKind.SignalingNaN || right.Kind == DecimalKind.SignalingNaN)
        {
            status |= DecimalStatus.InvalidOperation;
            return QuietedBits<TFormat>(
                left.Kind == DecimalKind.SignalingNaN ? left : right);
        }

        if (left.IsNaN && right.IsNaN)
        {
            return QuietedBits<TFormat>(left);
        }

        if (left.IsNaN || right.IsNaN)
        {
            var number = left.IsNaN ? right : left;
            if (BcdOrdering.IsSubnormal<TFormat>(number))
            {
                status |= DecimalStatus.Subnormal;
            }

            return BcdCodec.Encode<TFormat>(number);
        }

        int comparison;
        if (byMagnitude)
        {
            var leftMagnitude = left;
            var rightMagnitude = right;
            leftMagnitude.IsNegative = false;
            rightMagnitude.IsNegative = false;
            comparison = CompareValues(leftMagnitude, rightMagnitude);
        }
        else
        {
            comparison = CompareValues(left, right);
        }

        var chooseLeft = comparison == 0
            ? BcdOrdering.CompareTotal(left, right) > 0 == wantLarger
            : comparison > 0 == wantLarger;

        var chosen = chooseLeft ? left : right;
        if (BcdOrdering.IsSubnormal<TFormat>(chosen))
        {
            status |= DecimalStatus.Subnormal;
        }

        return BcdCodec.Encode<TFormat>(chosen);
    }

    /// <summary>
    /// The numeric comparison of two values neither of which is a NaN, which is what the
    /// selection above needs and what the infinities make more than a magnitude question.
    /// </summary>
    private static int CompareValues(BcdNumber left, BcdNumber right)
    {
        if (left.Kind == DecimalKind.Infinity || right.Kind == DecimalKind.Infinity)
        {
            return CompareInfinity(left, right);
        }

        return BcdArithmetic.Compare(left, right);
    }

    /// <summary>
    /// Rescales the value to the pattern's exponent. Needing more digits than the format
    /// holds is invalid rather than rounded: the value cannot be said at that exponent.
    /// </summary>
    public static UInt128 Quantize<TFormat>(UInt128 valueBits, UInt128 patternBits,
        DecimalRounding rounding, ref DecimalStatus status, ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        var value = BcdCodec.Decode<TFormat>(valueBits, work.Left);
        var pattern = BcdCodec.Decode<TFormat>(patternBits, work.Right);

        if (TryHandleNaN<TFormat>(value, pattern, ref status, out var nan))
        {
            return nan;
        }

        var valueIsInfinite = value.Kind == DecimalKind.Infinity;
        var patternIsInfinite = pattern.Kind == DecimalKind.Infinity;
        if (valueIsInfinite || patternIsInfinite)
        {
            // Only two infinities quantize to anything; one of each is invalid.
            return valueIsInfinite && patternIsInfinite
                ? BcdCodec.Encode<TFormat>(value)
                : Invalid<TFormat>(ref status);
        }

        return RescaleTo<TFormat>(ref value, pattern.Exponent, rounding, ref status);
    }

    /// <summary>Rescales to an exponent given directly rather than by a pattern value.</summary>
    public static UInt128 Rescale<TFormat>(UInt128 valueBits, int exponent,
        DecimalRounding rounding, ref DecimalStatus status, ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        var value = BcdCodec.Decode<TFormat>(valueBits, work.Left);

        if (value.IsNaN)
        {
            return PropagateNaN<TFormat>(value, ref status);
        }

        if (value.Kind == DecimalKind.Infinity)
        {
            return Invalid<TFormat>(ref status);
        }

        return RescaleTo<TFormat>(ref value, exponent, rounding, ref status);
    }

    private static UInt128 RescaleTo<TFormat>(ref BcdNumber value, int exponent,
        DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (!BcdShaping.TryRescale<TFormat>(ref value, exponent, rounding, ref status))
        {
            return Invalid<TFormat>(ref status);
        }

        return BcdCodec.Encode<TFormat>(value);
    }

    /// <summary>Rounds to an integer.</summary>
    public static UInt128 ToIntegral<TFormat>(UInt128 valueBits, bool exact,
        DecimalRounding rounding, ref DecimalStatus status, ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        var value = BcdCodec.Decode<TFormat>(valueBits, work.Left);

        if (value.IsNaN)
        {
            return PropagateNaN<TFormat>(value, ref status);
        }

        if (value.Kind == DecimalKind.Infinity)
        {
            // Re-encoded rather than handed back: the operand's own bits may be
            // non-canonical, and only the copy family is defined to preserve that.
            return BcdCodec.Encode<TFormat>(value);
        }

        BcdShaping.ToIntegral<TFormat>(ref value, exact, rounding, ref status);
        return BcdCodec.Encode<TFormat>(value);
    }

    /// <summary>
    /// Removes trailing zeros, leaving the shortest coefficient of the same value.
    /// </summary>
    public static UInt128 Reduce<TFormat>(UInt128 valueBits, DecimalRounding rounding,
        ref DecimalStatus status, ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        var probe = BcdCodec.Decode<TFormat>(valueBits, work.Left);

        if (probe.IsNaN)
        {
            return PropagateNaN<TFormat>(probe, ref status);
        }

        if (probe.Kind == DecimalKind.Infinity)
        {
            return BcdCodec.Encode<TFormat>(probe);
        }

        if (probe.IsZero)
        {
            // Every zero reduces to the same one, whatever exponent it arrived with.
            return ZeroAt<TFormat>(probe.IsNegative, 0);
        }

        // Applying the context first is what rounds an over-long coefficient before its
        // zeros are counted.
        var applied = Plus<TFormat>(valueBits, rounding, ref status, ref work);
        var value = BcdCodec.Decode<TFormat>(applied, work.Left);

        if (value.Kind != DecimalKind.Finite)
        {
            return applied;
        }

        if (value.IsZero)
        {
            return ZeroAt<TFormat>(value.IsNegative, 0);
        }

        BcdShaping.StripZeros(ref value, TFormat.MaxQuantumExponent);
        return BcdCodec.Encode<TFormat>(value);
    }

    /// <summary>
    /// Like <see cref="Reduce{TFormat}"/> but stopping at a zero exponent, so an integer
    /// keeps the zeros that are part of its magnitude.
    /// </summary>
    public static UInt128 Trim<TFormat>(UInt128 valueBits, ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        var value = BcdCodec.Decode<TFormat>(valueBits, work.Left);

        if (value.Kind != DecimalKind.Finite || value.IsZero)
        {
            return BcdCodec.Encode<TFormat>(value);
        }

        BcdShaping.StripZeros(ref value, 0);
        return BcdCodec.Encode<TFormat>(value);
    }

    /// <summary>The adjusted exponent, as an integer value.</summary>
    public static UInt128 LogB<TFormat>(UInt128 valueBits, DecimalRounding rounding,
        ref DecimalStatus status, ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        var value = BcdCodec.Decode<TFormat>(valueBits, work.Left);

        if (value.IsNaN)
        {
            return PropagateNaN<TFormat>(value, ref status);
        }

        return BcdShaping.LogB<TFormat>(value, work.Result, rounding, ref status);
    }

    public static UInt128 And<TFormat>(UInt128 leftBits, UInt128 rightBits,
        ref DecimalStatus status, ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        var left = BcdCodec.Decode<TFormat>(leftBits, work.Left);
        var right = BcdCodec.Decode<TFormat>(rightBits, work.Right);

        return BcdLogical.TryAnd<TFormat>(left, right, work.Result, out var result)
            ? BcdCodec.Encode<TFormat>(result)
            : Invalid<TFormat>(ref status);
    }

    public static UInt128 Or<TFormat>(UInt128 leftBits, UInt128 rightBits,
        ref DecimalStatus status, ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        var left = BcdCodec.Decode<TFormat>(leftBits, work.Left);
        var right = BcdCodec.Decode<TFormat>(rightBits, work.Right);

        return BcdLogical.TryOr<TFormat>(left, right, work.Result, out var result)
            ? BcdCodec.Encode<TFormat>(result)
            : Invalid<TFormat>(ref status);
    }

    public static UInt128 Xor<TFormat>(UInt128 leftBits, UInt128 rightBits,
        ref DecimalStatus status, ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        var left = BcdCodec.Decode<TFormat>(leftBits, work.Left);
        var right = BcdCodec.Decode<TFormat>(rightBits, work.Right);

        return BcdLogical.TryXor<TFormat>(left, right, work.Result, out var result)
            ? BcdCodec.Encode<TFormat>(result)
            : Invalid<TFormat>(ref status);
    }

    public static UInt128 Invert<TFormat>(UInt128 valueBits, ref DecimalStatus status,
        ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        var value = BcdCodec.Decode<TFormat>(valueBits, work.Left);

        return BcdLogical.TryInvert<TFormat>(value, work.Result, out var result)
            ? BcdCodec.Encode<TFormat>(result)
            : Invalid<TFormat>(ref status);
    }

    /// <summary>
    /// Moves the coefficient's digits within the format's full width. Rotating carries
    /// digits round the ends; shifting drops them and brings zeros in.
    /// </summary>
    public static UInt128 RotateOrShift<TFormat>(UInt128 valueBits, UInt128 placesBits,
        bool rotate, ref DecimalStatus status, ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        var value = BcdCodec.Decode<TFormat>(valueBits, work.Left);
        var places = BcdCodec.Decode<TFormat>(placesBits, work.Right);

        if (TryHandleNaN<TFormat>(value, places, ref status, out var nan))
        {
            return nan;
        }

        // The count has to be a plain integer no further than the width in either
        // direction; anything else is invalid rather than clamped.
        if (!TryReadInteger(places, TFormat.Precision, out var count))
        {
            return Invalid<TFormat>(ref status);
        }

        if (value.Kind == DecimalKind.Infinity)
        {
            return BcdCodec.Encode<TFormat>(value);
        }

        BcdShaping.RotateOrShift<TFormat>(ref value, count, rotate, work.Product);
        return BcdCodec.Encode<TFormat>(value);
    }

    /// <summary>Multiplies by a power of ten given as a second operand.</summary>
    public static UInt128 ScaleB<TFormat>(UInt128 valueBits, UInt128 scaleBits,
        DecimalRounding rounding, ref DecimalStatus status, ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        var value = BcdCodec.Decode<TFormat>(valueBits, work.Left);
        var scale = BcdCodec.Decode<TFormat>(scaleBits, work.Right);

        if (TryHandleNaN<TFormat>(value, scale, ref status, out var nan))
        {
            return nan;
        }

        // The shift has to be a plain integer, and no larger than could move any value from
        // one end of the format's range to the other.
        var limit = 2 * (TFormat.MaxExponent + TFormat.Precision);
        if (!TryReadInteger(scale, limit, out var shift))
        {
            return Invalid<TFormat>(ref status);
        }

        if (value.Kind == DecimalKind.Infinity)
        {
            return BcdCodec.Encode<TFormat>(value);
        }

        value.Exponent += shift;
        BcdFinalizer.Finalize<TFormat>(ref value, rounding, ref status);
        return BcdCodec.Encode<TFormat>(value);
    }

    /// <summary>
    /// Reads an operand as a plain integer, which the shift-like operations take rather
    /// than a general value: it has to be finite, have an exponent of zero, and fall inside
    /// the limit.
    /// </summary>
    private static bool TryReadInteger(BcdNumber value, int limit, out int result)
    {
        result = 0;

        if (value.Kind != DecimalKind.Finite || value.Exponent != 0)
        {
            return false;
        }

        // A value inside the limit cannot be longer than the limit's own digits, so a
        // coefficient past that is out of range whatever it says.
        if (value.DigitCount > 10)
        {
            return false;
        }

        var magnitude = 0L;
        for (var digit = value.Msd; digit <= value.Lsd; digit++)
        {
            magnitude = (magnitude * 10) + *digit;
        }

        if (magnitude > limit)
        {
            return false;
        }

        result = (int)(value.IsNegative ? -magnitude : magnitude);
        return true;
    }

    /// <summary>The next value above, which is a step of one in the last place.</summary>
    public static UInt128 NextPlus<TFormat>(UInt128 valueBits, ref DecimalStatus status,
        ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        return Next<TFormat>(valueBits, true, true, ref status, ref work);
    }

    public static UInt128 NextMinus<TFormat>(UInt128 valueBits, ref DecimalStatus status,
        ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        return Next<TFormat>(valueBits, false, true, ref status, ref work);
    }

    /// <summary>
    /// The next value from the first operand in the direction of the second. Unlike
    /// next-plus and next-minus this one reports a subnormal result.
    /// </summary>
    public static UInt128 NextToward<TFormat>(UInt128 valueBits, UInt128 targetBits,
        ref DecimalStatus status, ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        var value = BcdCodec.Decode<TFormat>(valueBits, work.Left);
        var target = BcdCodec.Decode<TFormat>(targetBits, work.Right);

        if (value.IsNaN || target.IsNaN)
        {
            if (value.Kind == DecimalKind.SignalingNaN || target.Kind == DecimalKind.SignalingNaN)
            {
                status |= DecimalStatus.InvalidOperation;
                return QuietedBits<TFormat>(
                    value.Kind == DecimalKind.SignalingNaN ? value : target);
            }

            return QuietedBits<TFormat>(value.IsNaN ? value : target);
        }

        var comparison = CompareValues(value, target);
        if (comparison == 0)
        {
            // Already there. The result keeps the first operand's digits and takes the
            // second's sign, which is the only thing left to move.
            value.IsNegative = target.IsNegative;
            return BcdCodec.Encode<TFormat>(value);
        }

        return Next<TFormat>(valueBits, comparison < 0, false, ref status, ref work);
    }

    private static UInt128 Next<TFormat>(UInt128 valueBits, bool toward, bool quiet,
        ref DecimalStatus status, ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        var value = BcdCodec.Decode<TFormat>(valueBits, work.Left);

        if (value.IsNaN)
        {
            return PropagateNaN<TFormat>(value, ref status);
        }

        if (value.Kind == DecimalKind.Infinity)
        {
            if (value.IsNegative == toward)
            {
                // Stepping inward from an infinity lands on the largest finite.
                var largest = new BcdNumber(DecimalKind.Finite, value.IsNegative,
                    TFormat.MaxQuantumExponent, work.Addend, work.Addend);

                largest.SetNines(TFormat.Precision, TFormat.MaxQuantumExponent);
                return BcdCodec.Encode<TFormat>(largest);
            }

            // Stepping outward stays put.
            return BcdCodec.Encode<TFormat>(value);
        }

        // Smaller than the smallest subnormal, so the rounding is what moves the value and
        // the amount added never shows up in the result.
        *work.Addend = 1;
        var step = new BcdNumber(DecimalKind.Finite, !toward,
            TFormat.MinQuantumExponent - 1, work.Addend, work.Addend);

        var rounding = toward ? DecimalRounding.Ceiling : DecimalRounding.Floor;
        var raised = DecimalStatus.None;

        var result = BcdArithmetic.Add<TFormat>(value, step, work.Result, rounding,
            work.LeftWork, work.RightWork);

        BcdFinalizer.Finalize<TFormat>(ref result, rounding, ref raised);

        // next-plus and next-minus report nothing about how they got there; next-toward is
        // an arithmetic operation and reports underflow like one.
        status |= raised & DecimalStatus.InvalidOperation;
        if (quiet)
        {
            return BcdCodec.Encode<TFormat>(result);
        }

        if ((raised & DecimalStatus.Overflow) != 0)
        {
            status |= DecimalStatus.Overflow | DecimalStatus.Inexact | DecimalStatus.Rounded;
        }
        else if ((raised & DecimalStatus.Underflow) != 0 || BcdOrdering.IsSubnormal<TFormat>(result))
        {
            // Stepping into or through the subnormal range is reported; a step that lands
            // on an ordinary value says nothing, however much rounding it took to get there.
            status |= raised & (DecimalStatus.Underflow | DecimalStatus.Inexact
                | DecimalStatus.Subnormal | DecimalStatus.Rounded | DecimalStatus.Clamped);
        }

        return BcdCodec.Encode<TFormat>(result);
    }

    /// <summary>
    /// The NaN one of two operands carries, quieted and passed on. The caller has already
    /// established that there is one.
    /// </summary>
    public static UInt128 PropagatePairNaN<TFormat>(UInt128 leftBits, UInt128 rightBits,
        ref DecimalStatus status, ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        var left = BcdCodec.Decode<TFormat>(leftBits, work.Left);
        var right = BcdCodec.Decode<TFormat>(rightBits, work.Right);

        TryHandleNaN<TFormat>(left, right, ref status, out var nan);
        return nan;
    }

    /// <summary>
    /// A NaN travelling out of an operation that takes one operand: quieted, with its sign
    /// and payload intact, and reported when it was signaling.
    /// </summary>
    private static UInt128 PropagateNaN<TFormat>(BcdNumber value, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (value.Kind == DecimalKind.SignalingNaN)
        {
            status |= DecimalStatus.InvalidOperation;
        }

        return QuietedBits<TFormat>(value);
    }

    /// <summary>
    /// Applies the context to a value without otherwise changing it, which is addition to a
    /// zero of the value's own exponent. That zero is what makes <c>plus -0</c> a positive
    /// zero under every rounding but one.
    /// </summary>
    public static UInt128 Plus<TFormat>(UInt128 valueBits, DecimalRounding rounding,
        ref DecimalStatus status, ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        return AddToZero<TFormat>(valueBits, false, rounding, ref status, ref work);
    }

    /// <summary>Arithmetic negation, which is a zero minus the value.</summary>
    public static UInt128 Minus<TFormat>(UInt128 valueBits, DecimalRounding rounding,
        ref DecimalStatus status, ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        return AddToZero<TFormat>(valueBits, true, rounding, ref status, ref work);
    }

    /// <summary>
    /// Arithmetic absolute value, which negates a negative and applies the context to
    /// everything else. Unlike the copy family this is not quiet.
    /// </summary>
    public static UInt128 Abs<TFormat>(UInt128 valueBits, DecimalRounding rounding,
        ref DecimalStatus status, ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        var probe = BcdCodec.Decode<TFormat>(valueBits, work.Addend);
        var negate = probe.IsNegative && !probe.IsNaN;
        return AddToZero<TFormat>(valueBits, negate, rounding, ref status, ref work);
    }

    private static UInt128 AddToZero<TFormat>(UInt128 valueBits, bool negate,
        DecimalRounding rounding, ref DecimalStatus status, ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        var value = BcdCodec.Decode<TFormat>(valueBits, work.Right);

        if (TryHandleNaN<TFormat>(value, value, ref status, out var nan))
        {
            return nan;
        }

        if (negate)
        {
            value.IsNegative = !value.IsNegative;
        }

        if (value.Kind == DecimalKind.Infinity)
        {
            return BcdCodec.Encode<TFormat>(value);
        }

        *work.Left = 0;
        var zero = new BcdNumber(DecimalKind.Finite, false, value.Exponent, work.Left, work.Left);

        var result = BcdArithmetic.Add<TFormat>(zero, value, work.Result, rounding,
            work.LeftWork, work.RightWork);

        BcdFinalizer.Finalize<TFormat>(ref result, rounding, ref status);
        return BcdCodec.Encode<TFormat>(result);
    }

    /// <summary>
    /// Compares two values numerically, giving <see cref="int.MinValue"/> and a NaN in
    /// <paramref name="nan"/> when the two are unordered. Unlike the total order, the two
    /// zeros are equal here and a NaN is unordered against everything.
    /// </summary>
    public static int Compare<TFormat>(UInt128 leftBits, UInt128 rightBits, bool signaling,
        ref DecimalStatus status, out UInt128 nan, ref BcdWorkspace work)
        where TFormat : IDecimalFormat
    {
        var left = BcdCodec.Decode<TFormat>(leftBits, work.Left);
        var right = BcdCodec.Decode<TFormat>(rightBits, work.Right);

        nan = UInt128.Zero;

        if (left.IsNaN || right.IsNaN)
        {
            // The quiet comparison lets a quiet NaN through without a condition; the
            // signaling one reports every NaN.
            if (signaling || left.Kind == DecimalKind.SignalingNaN
                || right.Kind == DecimalKind.SignalingNaN)
            {
                status |= DecimalStatus.InvalidOperation;
            }

            var propagated = left.Kind == DecimalKind.SignalingNaN ? left
                : right.Kind == DecimalKind.SignalingNaN ? right
                : left.IsNaN ? left
                : right;

            nan = QuietedBits<TFormat>(propagated);
            return int.MinValue;
        }

        if (left.Kind == DecimalKind.Infinity || right.Kind == DecimalKind.Infinity)
        {
            return CompareInfinity(left, right);
        }

        return BcdArithmetic.Compare(left, right);
    }

    /// <summary>
    /// Orders values where at least one is infinite. An infinity is beyond every finite
    /// value on its own side, and two of the same sign are equal.
    /// </summary>
    private static int CompareInfinity(BcdNumber left, BcdNumber right)
    {
        if (left.Kind == DecimalKind.Infinity && right.Kind == DecimalKind.Infinity)
        {
            if (left.IsNegative == right.IsNegative)
            {
                return 0;
            }

            return left.IsNegative ? -1 : 1;
        }

        if (left.Kind == DecimalKind.Infinity)
        {
            return left.IsNegative ? -1 : 1;
        }

        return right.IsNegative ? 1 : -1;
    }

    /// <summary>
    /// Passes a NaN operand through, left before right, with a signaling one winning from
    /// either side.
    /// </summary>
    private static bool TryHandleNaN<TFormat>(BcdNumber left, BcdNumber right,
        ref DecimalStatus status, out UInt128 result)
        where TFormat : IDecimalFormat
    {
        if (left.Kind == DecimalKind.SignalingNaN || right.Kind == DecimalKind.SignalingNaN)
        {
            status |= DecimalStatus.InvalidOperation;
            result = QuietedBits<TFormat>(left.Kind == DecimalKind.SignalingNaN ? left : right);
            return true;
        }

        if (left.IsNaN || right.IsNaN)
        {
            result = QuietedBits<TFormat>(left.IsNaN ? left : right);
            return true;
        }

        result = UInt128.Zero;
        return false;
    }

    private static bool TryQuietSignalingNaN<TFormat>(BcdNumber first, BcdNumber second,
        BcdNumber third, ref DecimalStatus status, out UInt128 result)
        where TFormat : IDecimalFormat
    {
        if (first.Kind == DecimalKind.SignalingNaN)
        {
            status |= DecimalStatus.InvalidOperation;
            result = QuietedBits<TFormat>(first);
            return true;
        }

        if (second.Kind == DecimalKind.SignalingNaN)
        {
            status |= DecimalStatus.InvalidOperation;
            result = QuietedBits<TFormat>(second);
            return true;
        }

        if (third.Kind == DecimalKind.SignalingNaN)
        {
            status |= DecimalStatus.InvalidOperation;
            result = QuietedBits<TFormat>(third);
            return true;
        }

        result = UInt128.Zero;
        return false;
    }

    private static UInt128 AddInfinity<TFormat>(BcdNumber left, BcdNumber right,
        ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (left.Kind == DecimalKind.Infinity && right.Kind == DecimalKind.Infinity)
        {
            if (left.IsNegative != right.IsNegative)
            {
                // Infinities of opposite sign have no sum.
                return Invalid<TFormat>(ref status);
            }

            return BcdCodec.Encode<TFormat>(left);
        }

        return BcdCodec.Encode<TFormat>(left.Kind == DecimalKind.Infinity ? left : right);
    }

    private static bool TryMultiplySpecial<TFormat>(BcdNumber left, BcdNumber right,
        ref DecimalStatus status, out UInt128 result)
        where TFormat : IDecimalFormat
    {
        var leftInfinite = left.Kind == DecimalKind.Infinity;
        var rightInfinite = right.Kind == DecimalKind.Infinity;

        if (!leftInfinite && !rightInfinite)
        {
            result = UInt128.Zero;
            return false;
        }

        if ((leftInfinite && right.IsZero) || (rightInfinite && left.IsZero))
        {
            // An infinity times a zero has no product.
            result = Invalid<TFormat>(ref status);
            return true;
        }

        result = Special<TFormat>(DecimalKind.Infinity, left.IsNegative != right.IsNegative);
        return true;
    }

    private static UInt128 Invalid<TFormat>(ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        status |= DecimalStatus.InvalidOperation;
        return Special<TFormat>(DecimalKind.QuietNaN, false);
    }

    /// <summary>
    /// A NaN or an infinity with no payload, which is what an operation produces when it
    /// makes one rather than passing one through.
    /// </summary>
    private static UInt128 Special<TFormat>(DecimalKind kind, bool isNegative)
        where TFormat : IDecimalFormat
    {
        byte* digit = stackalloc byte[1];
        *digit = 0;
        return BcdCodec.Encode<TFormat>(new BcdNumber(kind, isNegative, 0, digit, digit));
    }

    private static UInt128 ZeroAt<TFormat>(bool isNegative, int exponent)
        where TFormat : IDecimalFormat
    {
        byte* digit = stackalloc byte[1];
        *digit = 0;
        return BcdCodec.Encode<TFormat>(new BcdNumber(DecimalKind.Finite, isNegative,
            exponent, digit, digit));
    }

    /// <summary>
    /// A NaN passed through as quiet, keeping its payload and sign, which is what makes a
    /// signaling NaN's payload survive the operation that reported it.
    /// </summary>
    private static UInt128 QuietedBits<TFormat>(BcdNumber value)
        where TFormat : IDecimalFormat
    {
        value.Kind = DecimalKind.QuietNaN;
        return BcdCodec.Encode<TFormat>(value);
    }
}
