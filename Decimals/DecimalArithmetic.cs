// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// The arithmetic operations, written once and instantiated per format.
/// </summary>
/// <remarks>
/// <para>
/// The shape follows decNumber's fixed-format sources -- one routine behind the four
/// division forms, multiply feeding the same finalization path as add, specials handled
/// before anything else -- while the representation is a binary coefficient rather than a
/// string of BCD digits.
/// </para>
/// <para>
/// Wide intermediates go through <see cref="UInt256"/>. Aligning two operands can reach
/// about forty digits, a product of two 34-digit coefficients reaches sixty-eight, and a
/// division scales its dividend to sixty-nine before dividing.
/// </para>
/// </remarks>
internal static class DecimalArithmetic
{
    /// <summary>
    /// How far below the result's leading digit anything can still matter. Two digits past
    /// the precision would do; the third is slack so no path has to think about it.
    /// </summary>
    private const int GuardDigits = 3;

    public static UnpackedDecimal<UInt128> Add<TFormat>(UnpackedDecimal<UInt128> left,
        UnpackedDecimal<UInt128> right, DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (TryHandleNaN(left, right, ref status, out var nan))
        {
            return nan;
        }

        if (left.Kind == DecimalKind.Infinity || right.Kind == DecimalKind.Infinity)
        {
            return AddInfinity(left, right, ref status);
        }

        if (FitsNarrowAdd<TFormat>())
        {
            return AddNarrow<TFormat>(
                left.IsNegative, left.Coefficient, left.Exponent,
                right.IsNegative, right.Coefficient, right.Exponent,
                rounding, ref status);
        }

        return AddFinite<TFormat>(
            left.IsNegative, new UInt256(left.Coefficient), left.Exponent,
            right.IsNegative, new UInt256(right.Coefficient), right.Exponent,
            rounding, ref status);
    }

    /// <summary>
    /// Whether a format's addition fits 128 bits throughout. Folding caps an operand at
    /// precision + <see cref="GuardDigits"/> + 1 digits, and the sum carries one more, so
    /// Decimal32 needs 13 and Decimal64 needs 22 -- against the 38 a
    /// <see cref="UInt128"/> holds. Decimal128 needs 39 and does not fit, so it keeps the
    /// wider intermediate.
    /// </summary>
    /// <remarks>
    /// <see cref="IDecimalFormat.Precision"/> is a compile-time constant of the
    /// instantiation, so this test costs nothing at run time: the JIT folds it and emits
    /// only the branch that format takes.
    /// </remarks>
    private static bool FitsNarrowAdd<TFormat>()
        where TFormat : IDecimalFormat
    {
        return TFormat.Precision + GuardDigits + 2 <= PowersOfTen.MaxUInt128Power;
    }

    public static UnpackedDecimal<UInt128> Subtract<TFormat>(UnpackedDecimal<UInt128> left,
        UnpackedDecimal<UInt128> right, DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (TryHandleNaN(left, right, ref status, out var nan))
        {
            return nan;
        }

        return Add<TFormat>(left, Negated(right), rounding, ref status);
    }

    /// <summary>Plus is add with a zero of the operand's own exponent, which is what makes
    /// <c>plus -0</c> a positive zero.</summary>
    public static UnpackedDecimal<UInt128> Plus<TFormat>(UnpackedDecimal<UInt128> value,
        DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        return Add<TFormat>(ZeroLike(value), value, rounding, ref status);
    }

    public static UnpackedDecimal<UInt128> Minus<TFormat>(UnpackedDecimal<UInt128> value,
        DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        return Subtract<TFormat>(ZeroLike(value), value, rounding, ref status);
    }

    public static UnpackedDecimal<UInt128> Abs<TFormat>(UnpackedDecimal<UInt128> value,
        DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        return value.IsNegative && !value.IsNaN
            ? Minus<TFormat>(value, rounding, ref status)
            : Plus<TFormat>(value, rounding, ref status);
    }

    public static UnpackedDecimal<UInt128> Multiply<TFormat>(UnpackedDecimal<UInt128> left,
        UnpackedDecimal<UInt128> right, DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (TryHandleNaN(left, right, ref status, out var nan))
        {
            return nan;
        }

        if (!TryMultiplyExactly(left, right, ref status, out var negative, out var product,
            out var exponent, out var isInfinite, out var special))
        {
            return special;
        }

        if (isInfinite)
        {
            return new UnpackedDecimal<UInt128>(DecimalKind.Infinity, negative, 0, UInt128.Zero);
        }

        return DecimalFinalizer.Finalize<TFormat>(negative, product, exponent, rounding, ref status);
    }

    /// <summary>
    /// Multiply and add with only one rounding, so the product reaches the addition exact.
    /// </summary>
    public static UnpackedDecimal<UInt128> FusedMultiplyAdd<TFormat>(UnpackedDecimal<UInt128> left,
        UnpackedDecimal<UInt128> right, UnpackedDecimal<UInt128> addend, DecimalRounding rounding,
        ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (TryQuietSignalingNaN(left, right, addend, ref status, out var signaling))
        {
            return signaling;
        }

        // An invalid multiplication is reported even when the addend is a quiet NaN, so the
        // product is settled before the addend's NaN is looked at.
        if (!TryMultiplyExactly(left, right, ref status, out var negative, out var product,
            out var exponent, out var isInfinite, out var special))
        {
            return special;
        }

        if (left.IsNaN || right.IsNaN || addend.IsNaN)
        {
            return Quieted(left.IsNaN ? left : right.IsNaN ? right : addend);
        }

        // An infinite product still has to meet the addend: two infinities of opposite sign
        // are invalid, and everything else keeps the infinity.
        if (isInfinite || addend.Kind == DecimalKind.Infinity)
        {
            var asNumber = isInfinite
                ? new UnpackedDecimal<UInt128>(DecimalKind.Infinity, negative, 0, UInt128.Zero)
                : new UnpackedDecimal<UInt128>(DecimalKind.Finite, negative, exponent, UInt128.Zero);

            return AddInfinity(asNumber, addend, ref status);
        }

        // A Decimal64 product is at most thirty-two digits and a Decimal32 product
        // fourteen, so both meet their addend inside 128 bits; only Decimal128 has to
        // carry the product wide.
        if (FitsNarrowAdd<TFormat>() && product.FitsUInt128)
        {
            return AddNarrow<TFormat>(
                negative, product.ToUInt128(), exponent,
                addend.IsNegative, addend.Coefficient, addend.Exponent,
                rounding, ref status);
        }

        return AddFinite<TFormat>(
            negative, product, exponent,
            addend.IsNegative, new UInt256(addend.Coefficient), addend.Exponent,
            rounding, ref status);
    }

    public static UnpackedDecimal<UInt128> Divide<TFormat>(UnpackedDecimal<UInt128> left,
        UnpackedDecimal<UInt128> right, DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (TryHandleNaN(left, right, ref status, out var nan))
        {
            return nan;
        }

        var negative = left.IsNegative ^ right.IsNegative;

        if (left.Kind == DecimalKind.Infinity)
        {
            if (right.Kind == DecimalKind.Infinity)
            {
                return Invalid(ref status);
            }

            return new UnpackedDecimal<UInt128>(DecimalKind.Infinity, negative, 0, UInt128.Zero);
        }

        if (right.Kind == DecimalKind.Infinity)
        {
            // A finite over an infinity is a zero whose ideal exponent runs off to negative
            // infinity, so it settles at the smallest the format holds and says it clamped.
            status |= DecimalStatus.Clamped;
            return new UnpackedDecimal<UInt128>(DecimalKind.Finite, negative,
                TFormat.MinQuantumExponent, UInt128.Zero);
        }

        if (right.Coefficient == UInt128.Zero)
        {
            if (left.Coefficient == UInt128.Zero)
            {
                status |= DecimalStatus.DivisionUndefined;
                return QuietNaN();
            }

            status |= DecimalStatus.DivisionByZero;
            return new UnpackedDecimal<UInt128>(DecimalKind.Infinity, negative, 0, UInt128.Zero);
        }

        var idealExponent = left.Exponent - right.Exponent;

        if (left.Coefficient == UInt128.Zero)
        {
            return DecimalFinalizer.Finalize<TFormat>(negative, UInt128.Zero, idealExponent,
                rounding, ref status);
        }

        // Scale the dividend so the quotient lands on one digit more than the precision:
        // that is the guard digit, and whatever the division leaves over is the sticky.
        var shift = TFormat.Precision + 1 - DecimalRounder.CountDigits(left.Coefficient)
            + DecimalRounder.CountDigits(right.Coefficient);

        var numerator = UInt256.MultiplyByPowerOfTen(new UInt256(left.Coefficient), shift);
        var quotient = UInt256.DivRem(numerator, right.Coefficient, out var remainder);
        var exponent = idealExponent - shift;

        if (remainder == UInt128.Zero)
        {
            // Exact, so the specification asks for the exponent closest to dividend minus
            // divisor: strip the trailing zeros the scaling introduced, and no more.
            while (exponent < idealExponent)
            {
                var stripped = UInt256.DivRem(quotient, 10, out var digit);
                if (digit != 0)
                {
                    break;
                }

                quotient = stripped;
                exponent++;
            }

            return DecimalFinalizer.Finalize<TFormat>(negative, quotient, exponent, rounding, ref status);
        }

        quotient = UInt256.MultiplyByUInt64(quotient, 10) + UInt256.One;
        return DecimalFinalizer.Finalize<TFormat>(negative, quotient, exponent - 1, rounding, ref status);
    }

    public static UnpackedDecimal<UInt128> DivideInteger<TFormat>(UnpackedDecimal<UInt128> left,
        UnpackedDecimal<UInt128> right, DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (TryHandleNaN(left, right, ref status, out var nan))
        {
            return nan;
        }

        if (!TryIntegerQuotient<TFormat>(left, right, false, ref status, out var quotient,
            out var negative, out var special))
        {
            return special;
        }

        return DecimalFinalizer.Finalize<TFormat>(negative, quotient, 0, rounding, ref status);
    }

    public static UnpackedDecimal<UInt128> Remainder<TFormat>(UnpackedDecimal<UInt128> left,
        UnpackedDecimal<UInt128> right, DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        return RemainderCore<TFormat>(left, right, false, rounding, ref status);
    }

    public static UnpackedDecimal<UInt128> RemainderNear<TFormat>(UnpackedDecimal<UInt128> left,
        UnpackedDecimal<UInt128> right, DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        return RemainderCore<TFormat>(left, right, true, rounding, ref status);
    }

    /// <summary>
    /// Numeric comparison. Returns null when the result is a NaN; <paramref name="nan"/>
    /// then carries it, because a NaN operand is propagated sign and payload intact rather
    /// than replaced by a fresh one.
    /// </summary>
    public static int? Compare(UnpackedDecimal<UInt128> left, UnpackedDecimal<UInt128> right,
        bool signaling, ref DecimalStatus status, out UnpackedDecimal<UInt128> nan)
    {
        nan = default;

        if (left.IsNaN || right.IsNaN)
        {
            if (signaling || left.Kind == DecimalKind.SignalingNaN
                || right.Kind == DecimalKind.SignalingNaN)
            {
                status |= DecimalStatus.InvalidOperation;
            }

            var propagated = left.Kind == DecimalKind.SignalingNaN ? left
                : right.Kind == DecimalKind.SignalingNaN ? right
                : left.IsNaN ? left
                : right;

            nan = Quieted(propagated);
            return null;
        }

        if (left.Kind == DecimalKind.Infinity || right.Kind == DecimalKind.Infinity)
        {
            var leftRank = left.Kind == DecimalKind.Infinity ? (left.IsNegative ? -2 : 2) : 0;
            var rightRank = right.Kind == DecimalKind.Infinity ? (right.IsNegative ? -2 : 2) : 0;
            if (leftRank != rightRank)
            {
                return leftRank < rightRank ? -1 : 1;
            }

            return 0;
        }

        var leftIsZero = left.Coefficient == UInt128.Zero;
        var rightIsZero = right.Coefficient == UInt128.Zero;
        if (leftIsZero && rightIsZero)
        {
            // Negative zero and positive zero are numerically equal, unlike under the total
            // order that compare-total imposes.
            return 0;
        }

        if (left.IsNegative != right.IsNegative)
        {
            return left.IsNegative ? -1 : 1;
        }

        var magnitude = DecimalOperations.CompareFiniteValue(left, right);
        return left.IsNegative ? -magnitude : magnitude;
    }

    private static UnpackedDecimal<UInt128> RemainderCore<TFormat>(UnpackedDecimal<UInt128> left,
        UnpackedDecimal<UInt128> right, bool toNearest, DecimalRounding rounding,
        ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (TryHandleNaN(left, right, ref status, out var nan))
        {
            return nan;
        }

        if (left.Kind == DecimalKind.Infinity)
        {
            return Invalid(ref status);
        }

        if (right.Kind == DecimalKind.Infinity)
        {
            return DecimalFinalizer.Finalize<TFormat>(left.IsNegative, left.Coefficient,
                left.Exponent, rounding, ref status);
        }

        if (right.Coefficient == UInt128.Zero)
        {
            status |= left.Coefficient == UInt128.Zero
                ? DecimalStatus.DivisionUndefined
                : DecimalStatus.InvalidOperation;

            return QuietNaN();
        }

        if (!TryIntegerQuotient<TFormat>(left, right, toNearest, ref status, out var quotient,
            out var quotientNegative, out var special))
        {
            return special;
        }

        // remainder = dividend - quotient x divisor, which is exact and smaller than the
        // divisor, so it always fits once both sides are lined up on the lower exponent.
        var exponent = Math.Min(left.Exponent, right.Exponent);
        var dividend = UInt256.MultiplyByPowerOfTen(new UInt256(left.Coefficient), left.Exponent - exponent);
        var scaled = UInt256.MultiplyByPowerOfTen(
            UInt256.Multiply(quotient.ToUInt128(), right.Coefficient), right.Exponent - exponent);

        var negative = left.IsNegative;
        UInt256 magnitude;
        if (dividend >= scaled)
        {
            magnitude = dividend - scaled;
        }
        else
        {
            magnitude = scaled - dividend;
            negative = !negative;
        }

        if (magnitude.IsZero)
        {
            negative = left.IsNegative;
        }

        Unused(quotientNegative);
        return DecimalFinalizer.Finalize<TFormat>(negative, magnitude, exponent, rounding, ref status);
    }

    /// <summary>
    /// The integer quotient shared by divide-integer, remainder, and remainder-near. Signals
    /// division-impossible when it would need more digits than the format holds.
    /// </summary>
    private static bool TryIntegerQuotient<TFormat>(UnpackedDecimal<UInt128> left,
        UnpackedDecimal<UInt128> right, bool toNearest, ref DecimalStatus status,
        out UInt256 quotient, out bool negative, out UnpackedDecimal<UInt128> special)
        where TFormat : IDecimalFormat
    {
        quotient = UInt256.Zero;
        negative = left.IsNegative ^ right.IsNegative;
        special = default;

        if (left.Kind == DecimalKind.Infinity)
        {
            if (right.Kind == DecimalKind.Infinity)
            {
                special = Invalid(ref status);
                return false;
            }

            special = new UnpackedDecimal<UInt128>(DecimalKind.Infinity, negative, 0, UInt128.Zero);
            return false;
        }

        if (right.Kind == DecimalKind.Infinity)
        {
            special = new UnpackedDecimal<UInt128>(DecimalKind.Finite, negative, 0, UInt128.Zero);
            return false;
        }

        if (right.Coefficient == UInt128.Zero)
        {
            if (left.Coefficient == UInt128.Zero)
            {
                status |= DecimalStatus.DivisionUndefined;
            }
            else
            {
                status |= DecimalStatus.DivisionByZero;
                special = new UnpackedDecimal<UInt128>(DecimalKind.Infinity, negative, 0, UInt128.Zero);
                return false;
            }

            special = QuietNaN();
            return false;
        }

        if (left.Coefficient == UInt128.Zero)
        {
            return true;
        }

        var shift = left.Exponent - right.Exponent;
        var leftDigits = DecimalRounder.CountDigits(left.Coefficient);

        UInt256 numerator;
        UInt128 denominator;
        if (shift >= 0)
        {
            if (leftDigits + shift > 70)
            {
                // The quotient would run past seventy digits, which is far beyond any
                // format, so there is no need to compute it to know it does not fit.
                status |= DecimalStatus.DivisionImpossible;
                special = QuietNaN();
                return false;
            }

            numerator = UInt256.MultiplyByPowerOfTen(new UInt256(left.Coefficient), shift);
            denominator = right.Coefficient;
        }
        else
        {
            var scale = -shift;
            if (DecimalRounder.CountDigits(right.Coefficient) + scale > 38)
            {
                // The divisor outruns the dividend by more than any coefficient spans, so
                // the integer quotient is zero.
                return true;
            }

            numerator = new UInt256(left.Coefficient);
            denominator = right.Coefficient * PowersOfTen.UInt128(scale);
        }

        quotient = UInt256.DivRem(numerator, denominator, out var remainder);

        if (toNearest)
        {
            // remainder-near rounds the quotient half to even rather than truncating.
            var twice = remainder * 2;
            var roundUp = twice > denominator
                || (twice == denominator && (quotient.Limb0 & 1) != 0);

            if (roundUp)
            {
                quotient += UInt256.One;
            }
        }

        if (quotient.CountDigits() > TFormat.Precision)
        {
            status |= DecimalStatus.DivisionImpossible;
            special = QuietNaN();
            quotient = UInt256.Zero;
            return false;
        }

        return true;
    }

    /// <summary>
    /// The exact product, or false when multiplying is invalid. An infinite product is not
    /// a failure -- it is reported through <paramref name="isInfinite"/>, because fused
    /// multiply-add still has to add the addend to it.
    /// </summary>
    private static bool TryMultiplyExactly(UnpackedDecimal<UInt128> left, UnpackedDecimal<UInt128> right,
        ref DecimalStatus status, out bool negative, out UInt256 product, out int exponent,
        out bool isInfinite, out UnpackedDecimal<UInt128> special)
    {
        negative = left.IsNegative ^ right.IsNegative;
        product = UInt256.Zero;
        exponent = 0;
        isInfinite = false;
        special = default;

        var leftIsInfinite = left.Kind == DecimalKind.Infinity;
        var rightIsInfinite = right.Kind == DecimalKind.Infinity;

        if (leftIsInfinite || rightIsInfinite)
        {
            var otherIsZero = leftIsInfinite
                ? right.Kind == DecimalKind.Finite && right.Coefficient == UInt128.Zero
                : left.Kind == DecimalKind.Finite && left.Coefficient == UInt128.Zero;

            if (otherIsZero)
            {
                special = Invalid(ref status);
                return false;
            }

            if (left.IsNaN || right.IsNaN)
            {
                return true;
            }

            isInfinite = true;
            return true;
        }

        if (left.IsNaN || right.IsNaN)
        {
            return true;
        }

        product = UInt256.Multiply(left.Coefficient, right.Coefficient);
        exponent = left.Exponent + right.Exponent;
        return true;
    }

    /// <summary>
    /// Addition for the formats whose working values fit 128 bits, which is Decimal32 and
    /// Decimal64. Same algorithm as the wide version below; what differs is that every
    /// intermediate is a <see cref="UInt128"/>, so lining the operands up is one multiply
    /// against a table entry rather than a loop over four limbs.
    /// </summary>
    private static UnpackedDecimal<UInt128> AddNarrow<TFormat>(
        bool leftNegative, UInt128 leftCoefficient, int leftExponent,
        bool rightNegative, UInt128 rightCoefficient, int rightExponent,
        DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (leftCoefficient == UInt128.Zero && rightCoefficient == UInt128.Zero)
        {
            var bothNegative = leftNegative == rightNegative
                ? leftNegative
                : rounding == DecimalRounding.Floor;

            return DecimalFinalizer.Finalize<TFormat>(bothNegative, UInt128.Zero,
                Math.Min(leftExponent, rightExponent), rounding, ref status);
        }

        var leftAdjusted = NarrowAdjusted(leftCoefficient, leftExponent);
        var rightAdjusted = NarrowAdjusted(rightCoefficient, rightExponent);

        var mayCancel = leftNegative != rightNegative
            && leftCoefficient != UInt128.Zero
            && rightCoefficient != UInt128.Zero
            && Math.Abs(leftAdjusted - rightAdjusted) <= 1;

        if (!mayCancel)
        {
            var floor = Math.Max(leftAdjusted, rightAdjusted) + 1 - (TFormat.Precision + GuardDigits);
            FoldToSticky(ref leftCoefficient, ref leftExponent, floor);
            FoldToSticky(ref rightCoefficient, ref rightExponent, floor);
        }

        // Operands that may cancel are within an order of magnitude of each other, so
        // aligning them spans at most one digit more than the precision -- there is no
        // case here that needs more room than the fold leaves.
        var exponent = Math.Min(leftExponent, rightExponent);
        leftCoefficient = ScaleUp(leftCoefficient, leftExponent - exponent);
        rightCoefficient = ScaleUp(rightCoefficient, rightExponent - exponent);

        bool negative;
        UInt128 magnitude;
        if (leftNegative == rightNegative)
        {
            negative = leftNegative;
            magnitude = leftCoefficient + rightCoefficient;
        }
        else if (leftCoefficient == rightCoefficient)
        {
            // Opposite signs canceling exactly gives a positive zero, except when the
            // rounding runs toward negative infinity.
            negative = rounding == DecimalRounding.Floor;
            magnitude = UInt128.Zero;
        }
        else if (leftCoefficient > rightCoefficient)
        {
            negative = leftNegative;
            magnitude = leftCoefficient - rightCoefficient;
        }
        else
        {
            negative = rightNegative;
            magnitude = rightCoefficient - leftCoefficient;
        }

        return DecimalFinalizer.Finalize<TFormat>(negative, magnitude, exponent, rounding, ref status);
    }

    private static UInt128 ScaleUp(UInt128 coefficient, int shift)
    {
        // A zero shifts to a zero however far it goes, and it can have far to go: a zero
        // takes no part in setting the fold window -- it has no leading digit to set it
        // from -- so a zero operand's exponent can sit hundreds of decades above the
        // other's. Every non-zero shift is bounded by the window.
        if (shift == 0 || coefficient == UInt128.Zero)
        {
            return coefficient;
        }

        return coefficient * PowersOfTen.UInt128(shift);
    }

    /// <summary>
    /// Drops whatever sits below the window into a single sticky digit. One division,
    /// where the wide form needs a loop, and none at all when the whole coefficient is
    /// below the window.
    /// </summary>
    private static void FoldToSticky(ref UInt128 coefficient, ref int exponent, int floor)
    {
        if (exponent >= floor)
        {
            return;
        }

        var drop = floor - exponent;
        UInt128 kept;
        bool discarded;
        if (drop > PowersOfTen.MaxUInt128Power)
        {
            kept = UInt128.Zero;
            discarded = coefficient != UInt128.Zero;
        }
        else
        {
            var power = PowersOfTen.UInt128(drop);
            kept = coefficient / power;
            discarded = coefficient != kept * power;
        }

        exponent = floor - 1;
        coefficient = (kept * 10) + (discarded ? UInt128.One : UInt128.Zero);
    }

    private static int NarrowAdjusted(UInt128 coefficient, int exponent)
    {
        return coefficient == UInt128.Zero
            ? int.MinValue
            : exponent + DecimalRounder.CountDigits(coefficient) - 1;
    }

    private static UnpackedDecimal<UInt128> AddFinite<TFormat>(
        bool leftNegative, UInt256 leftCoefficient, int leftExponent,
        bool rightNegative, UInt256 rightCoefficient, int rightExponent,
        DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (leftCoefficient.IsZero && rightCoefficient.IsZero)
        {
            // Two zeros have no digits to line up, and only their exponents matter. Signs
            // that disagree give a positive zero, except when rounding runs to minus
            // infinity.
            var bothNegative = leftNegative == rightNegative
                ? leftNegative
                : rounding == DecimalRounding.Floor;

            return DecimalFinalizer.Finalize<TFormat>(bothNegative, UInt128.Zero,
                Math.Min(leftExponent, rightExponent), rounding, ref status);
        }

        // Anything more than a few digits below the result's leading digit can only make it
        // inexact, so operands reaching further down are folded to a sticky digit first.
        // Without that, lining up 1E+6144 with 1E-6176 would need a coefficient nothing can
        // hold. A zero operand is left out of this reckoning: it has no leading digit, and
        // letting its exponent set the window would push the other operand out of it.
        var leftAdjusted = Adjusted(leftCoefficient, leftExponent);
        var rightAdjusted = Adjusted(rightCoefficient, rightExponent);

        // Folding is only safe when the result is known to stay near the larger operand.
        // Opposite signs within one order of magnitude can cancel almost everything away
        // and leave the answer sitting on digits the fold would have thrown out, so those
        // line up in full. They can afford to: two operands that close together span at
        // most twice the precision once aligned.
        var mayCancel = leftNegative != rightNegative
            && !leftCoefficient.IsZero
            && !rightCoefficient.IsZero
            && Math.Abs(leftAdjusted - rightAdjusted) <= 1;

        if (!mayCancel)
        {
            var floor = Math.Max(leftAdjusted, rightAdjusted) + 1 - (TFormat.Precision + GuardDigits);
            FoldToSticky(ref leftCoefficient, ref leftExponent, floor);
            FoldToSticky(ref rightCoefficient, ref rightExponent, floor);
        }

        var exponent = Math.Min(leftExponent, rightExponent);
        leftCoefficient = UInt256.MultiplyByPowerOfTen(leftCoefficient, leftExponent - exponent);
        rightCoefficient = UInt256.MultiplyByPowerOfTen(rightCoefficient, rightExponent - exponent);

        bool negative;
        UInt256 magnitude;
        if (leftNegative == rightNegative)
        {
            negative = leftNegative;
            magnitude = leftCoefficient + rightCoefficient;
        }
        else
        {
            var comparison = leftCoefficient.CompareTo(rightCoefficient);
            if (comparison == 0)
            {
                // Opposite signs canceling exactly gives a positive zero, except when the
                // rounding runs toward negative infinity.
                negative = rounding == DecimalRounding.Floor;
                magnitude = UInt256.Zero;
            }
            else if (comparison > 0)
            {
                negative = leftNegative;
                magnitude = leftCoefficient - rightCoefficient;
            }
            else
            {
                negative = rightNegative;
                magnitude = rightCoefficient - leftCoefficient;
            }
        }

        return DecimalFinalizer.Finalize<TFormat>(negative, magnitude, exponent, rounding, ref status);
    }

    private static void FoldToSticky(ref UInt256 coefficient, ref int exponent, int floor)
    {
        if (exponent >= floor)
        {
            return;
        }

        var drop = floor - exponent;
        coefficient = UInt256.DivideByPowerOfTen(coefficient, drop, out var discarded);
        exponent = floor - 1;
        coefficient = UInt256.MultiplyByUInt64(coefficient, 10);
        if (discarded)
        {
            coefficient += UInt256.One;
        }
    }

    /// <summary>
    /// Where an operand's leading digit sits. A zero has none, so it takes no part in
    /// deciding the window; the caller has already ruled out both operands being zero.
    /// </summary>
    private static int Adjusted(UInt256 coefficient, int exponent)
    {
        return coefficient.IsZero ? int.MinValue : exponent + coefficient.CountDigits() - 1;
    }

    private static UnpackedDecimal<UInt128> AddInfinity(UnpackedDecimal<UInt128> left,
        UnpackedDecimal<UInt128> right, ref DecimalStatus status)
    {
        if (left.Kind == DecimalKind.Infinity && right.Kind == DecimalKind.Infinity)
        {
            if (left.IsNegative != right.IsNegative)
            {
                return Invalid(ref status);
            }

            return left;
        }

        return left.Kind == DecimalKind.Infinity ? left : right;
    }

    /// <summary>
    /// Passes a NaN operand through, left before right, with a signaling one winning from
    /// either side.
    /// </summary>
    public static bool TryHandleNaN(UnpackedDecimal<UInt128> left, UnpackedDecimal<UInt128> right,
        ref DecimalStatus status, out UnpackedDecimal<UInt128> result)
    {
        if (left.Kind == DecimalKind.SignalingNaN || right.Kind == DecimalKind.SignalingNaN)
        {
            status |= DecimalStatus.InvalidOperation;
            result = Quieted(left.Kind == DecimalKind.SignalingNaN ? left : right);
            return true;
        }

        if (left.IsNaN || right.IsNaN)
        {
            result = Quieted(left.IsNaN ? left : right);
            return true;
        }

        result = default;
        return false;
    }

    private static bool TryQuietSignalingNaN(UnpackedDecimal<UInt128> first,
        UnpackedDecimal<UInt128> second, UnpackedDecimal<UInt128> third, ref DecimalStatus status,
        out UnpackedDecimal<UInt128> result)
    {
        foreach (var value in new[] { first, second, third })
        {
            if (value.Kind == DecimalKind.SignalingNaN)
            {
                status |= DecimalStatus.InvalidOperation;
                result = Quieted(value);
                return true;
            }
        }

        result = default;
        return false;
    }

    /// <summary>Turns a NaN into a quiet one, keeping its sign and payload.</summary>
    public static UnpackedDecimal<UInt128> Quiet(UnpackedDecimal<UInt128> value)
    {
        return new UnpackedDecimal<UInt128>(DecimalKind.QuietNaN, value.IsNegative, 0, value.Coefficient);
    }

    /// <summary>
    /// Passes a NaN operand through, signaling invalid operation if it was a signaling one.
    /// </summary>
    public static UnpackedDecimal<UInt128> PropagateNaN(UnpackedDecimal<UInt128> value,
        ref DecimalStatus status)
    {
        if (value.Kind == DecimalKind.SignalingNaN)
        {
            status |= DecimalStatus.InvalidOperation;
        }

        return Quiet(value);
    }

    private static UnpackedDecimal<UInt128> Quieted(UnpackedDecimal<UInt128> value) => Quiet(value);

    private static UnpackedDecimal<UInt128> Invalid(ref DecimalStatus status)
    {
        status |= DecimalStatus.InvalidOperation;
        return QuietNaN();
    }

    private static UnpackedDecimal<UInt128> QuietNaN()
    {
        return new UnpackedDecimal<UInt128>(DecimalKind.QuietNaN, false, 0, UInt128.Zero);
    }

    private static UnpackedDecimal<UInt128> Negated(UnpackedDecimal<UInt128> value)
    {
        return new UnpackedDecimal<UInt128>(value.Kind, !value.IsNegative, value.Exponent, value.Coefficient);
    }

    private static UnpackedDecimal<UInt128> ZeroLike(UnpackedDecimal<UInt128> value)
    {
        var exponent = value.Kind == DecimalKind.Finite ? value.Exponent : 0;
        return new UnpackedDecimal<UInt128>(DecimalKind.Finite, false, exponent, UInt128.Zero);
    }

    private static void Unused<T>(T value)
    {
    }
}
