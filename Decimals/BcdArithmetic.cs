// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// Arithmetic on the digit form, following decNumber's <c>decBasic.c</c>.
/// </summary>
/// <remarks>
/// <para>
/// Aligning two operands is what addition mostly is, and on digits it is a copy at an
/// offset: a coefficient scaled by a power of ten is the same digits with zeros written
/// after them. The binary form pays a multiply or a divide for the same step, which is what
/// this representation exists to avoid.
/// </para>
/// <para>
/// Every operation takes the buffers it works in from its caller. The sizes are compile-time
/// constants of the format, the way decNumber sizes from <c>DECPMAX</c>.
/// </para>
/// </remarks>
internal static unsafe class BcdArithmetic
{
    /// <summary>
    /// How far below the result's leading digit anything can still matter. Two digits past
    /// the precision would do; the third is slack so no path has to think about it.
    /// </summary>
    public const int GuardDigits = 3;

    /// <summary>
    /// Digits an operand can hold once folded: the precision, the guard digits, and the
    /// sticky digit that stands in for everything below them.
    /// </summary>
    public static int FoldedLength<TFormat>()
        where TFormat : IDecimalFormat
    {
        return TFormat.Precision + GuardDigits + 1;
    }

    /// <summary>
    /// The buffer an addition's result needs: room for the aligned sum, a digit for the
    /// carry out of it, and two before the leading digit -- one taken by that carry and one
    /// left for a rounding carry in the finalizer.
    /// </summary>
    public static int AddBufferLength<TFormat>()
        where TFormat : IDecimalFormat
    {
        return FoldedLength<TFormat>() + 4;
    }

    /// <summary>
    /// The buffer each operand is copied into to be folded and aligned.
    /// </summary>
    /// <remarks>
    /// Folding puts the lower operand's exponent at the window's floor, and aligning brings
    /// the higher one down to meet it, so either way an aligned coefficient spans the
    /// window: the precision, the guard digits, and the sticky digit. One more for the
    /// leading headroom.
    /// </remarks>
    public static int OperandBufferLength<TFormat>()
        where TFormat : IDecimalFormat
    {
        return FoldedLength<TFormat>() + 2;
    }

    /// <summary>
    /// The buffer an operand of a fused multiply-add is copied into. The product reaching
    /// the addition is exact and so twice the precision, before the fold cuts it back.
    /// </summary>
    public static int FusedOperandBufferLength<TFormat>()
        where TFormat : IDecimalFormat
    {
        return (TFormat.Precision * 2) + FoldedLength<TFormat>() + 2;
    }

    /// <summary>
    /// Multiplies and then adds, with nothing rounded in between: what reaches the addition
    /// is the exact product, which is the whole point of the operation.
    /// </summary>
    public static BcdNumber FusedMultiplyAdd<TFormat>(BcdNumber left, BcdNumber right,
        BcdNumber addend, DecimalRounding rounding, byte* productBuffer, uint* leftLimbs,
        uint* rightLimbs, ulong* accumulator, byte* buffer, byte* leftWork, byte* rightWork)
        where TFormat : IDecimalFormat
    {
        var product = BcdMultiplier.Multiply<TFormat>(left, right, productBuffer,
            leftLimbs, rightLimbs, accumulator);

        return Add<TFormat>(product, addend, buffer, rounding, leftWork, rightWork);
    }

    /// <summary>
    /// Compares two finite values. Neither is modified and no buffer is needed: the
    /// comparison is settled from the leading digits and the adjusted exponents, and only
    /// falls through to the digits when those agree.
    /// </summary>
    public static int Compare(BcdNumber left, BcdNumber right)
    {
        var leftZero = left.IsZero;
        var rightZero = right.IsZero;

        if (leftZero && rightZero)
        {
            return 0;
        }

        if (leftZero)
        {
            return right.IsNegative ? 1 : -1;
        }

        if (rightZero)
        {
            return left.IsNegative ? -1 : 1;
        }

        if (left.IsNegative != right.IsNegative)
        {
            return left.IsNegative ? -1 : 1;
        }

        var magnitude = CompareMagnitudes(left, right);
        return left.IsNegative ? -magnitude : magnitude;
    }

    /// <summary>
    /// Orders two non-zero coefficients by value, ignoring both signs. Where the leading
    /// digits sit decides it unless they sit in the same place.
    /// </summary>
    private static int CompareMagnitudes(BcdNumber left, BcdNumber right)
    {
        if (left.AdjustedExponent != right.AdjustedExponent)
        {
            return left.AdjustedExponent < right.AdjustedExponent ? -1 : 1;
        }

        // The leading digits line up, so the values compare digit by digit from there --
        // whichever runs out first is padded with zeros, which cannot win a comparison.
        var length = Math.Min(left.DigitCount, right.DigitCount);
        var leftDigit = left.Msd;
        var rightDigit = right.Msd;

        for (var end = leftDigit + length; leftDigit < end; leftDigit++, rightDigit++)
        {
            if (*leftDigit != *rightDigit)
            {
                return *leftDigit < *rightDigit ? -1 : 1;
            }
        }

        if (left.DigitCount == right.DigitCount)
        {
            return 0;
        }

        // The longer one has digits below where the shorter one stopped; it is greater
        // unless every one of them is zero.
        var longerIsLeft = left.DigitCount > right.DigitCount;
        var longer = longerIsLeft ? left : right;

        for (var digit = longer.Msd + length; digit <= longer.Lsd; digit++)
        {
            if (*digit != 0)
            {
                return longerIsLeft ? 1 : -1;
            }
        }

        return 0;
    }

    /// <summary>
    /// Adds two finite values into <paramref name="buffer"/>, which must hold
    /// <see cref="AddBufferLength{TFormat}"/> digits. The result is exact except for what
    /// the fold below discards, and the finalizer is what rounds it.
    /// </summary>
    public static BcdNumber Add<TFormat>(BcdNumber left, BcdNumber right, byte* buffer,
        DecimalRounding rounding, byte* leftWork, byte* rightWork)
        where TFormat : IDecimalFormat
    {
        var leftFolded = CopyFor(left, leftWork);
        var rightFolded = CopyFor(right, rightWork);

        if (leftFolded.IsZero && rightFolded.IsZero)
        {
            // Two zeros keep the smaller exponent, and the sign is theirs only when they
            // agree; otherwise it follows the rounding.
            var negative = leftFolded.IsNegative == rightFolded.IsNegative
                ? leftFolded.IsNegative
                : rounding == DecimalRounding.Floor;

            var result = new BcdNumber(DecimalKind.Finite, negative,
                Math.Min(leftFolded.Exponent, rightFolded.Exponent), buffer + 1, buffer + 1);
            *result.Msd = 0;
            return result;
        }

        // Anything this far below the larger operand's leading digit cannot reach the
        // result except as a sticky digit, so it is folded away before the operands are
        // aligned -- which is what stops the alignment from needing unbounded room.
        var mayCancel = leftFolded.IsNegative != rightFolded.IsNegative
            && !leftFolded.IsZero
            && !rightFolded.IsZero
            && Math.Abs(leftFolded.AdjustedExponent - rightFolded.AdjustedExponent) <= 1;

        if (!mayCancel)
        {
            var highest = Math.Max(
                leftFolded.IsZero ? int.MinValue : leftFolded.AdjustedExponent,
                rightFolded.IsZero ? int.MinValue : rightFolded.AdjustedExponent);

            var floor = highest + 1 - (TFormat.Precision + GuardDigits);
            FoldToSticky(ref leftFolded, floor);
            FoldToSticky(ref rightFolded, floor);
        }

        // Aligning is writing zeros after the digits of whichever operand sits higher.
        var exponent = Math.Min(leftFolded.Exponent, rightFolded.Exponent);
        AlignTo(ref leftFolded, exponent);
        AlignTo(ref rightFolded, exponent);

        return leftFolded.IsNegative == rightFolded.IsNegative
            ? AddMagnitudes(leftFolded, rightFolded, buffer, exponent)
            : SubtractMagnitudes(leftFolded, rightFolded, buffer, exponent, rounding);
    }

    /// <summary>
    /// Copies an operand into a work buffer, since aligning and folding rewrite it and the
    /// caller's digits are not ours. The copy sits one digit in, leaving the headroom an
    /// increment needs.
    /// </summary>
    private static BcdNumber CopyFor(BcdNumber value, byte* work)
    {
        var msd = work + 1;
        var destination = msd;
        for (var digit = value.Msd; digit <= value.Lsd; digit++, destination++)
        {
            *destination = *digit;
        }

        return new BcdNumber(value.Kind, value.IsNegative, value.Exponent,
            msd, destination - 1);
    }

    /// <summary>
    /// Brings an operand down to a common exponent by writing zeros after its digits.
    /// </summary>
    /// <remarks>
    /// A zero scales to a zero however far it goes, and it can have far to go: a zero takes
    /// no part in setting the fold window -- it has no leading digit to set one from -- so a
    /// zero operand's exponent can sit hundreds of decades above the other's. Writing that
    /// many zeros would run past any buffer, and every one of them would be a zero anyway.
    /// Every non-zero shift is bounded by the window.
    /// </remarks>
    private static void AlignTo(ref BcdNumber value, int exponent)
    {
        if (value.Exponent == exponent)
        {
            return;
        }

        if (value.IsZero)
        {
            value.SetZero(exponent);
            return;
        }

        value.AppendZeros(value.Exponent - exponent);
    }

    /// <summary>
    /// Collapses everything below <paramref name="floor"/> into a single sticky digit,
    /// which is enough to round correctly: the sticky can only break a tie, never move the
    /// value across one.
    /// </summary>
    private static void FoldToSticky(ref BcdNumber value, int floor)
    {
        if (value.Exponent >= floor)
        {
            return;
        }

        var drop = floor - value.Exponent;
        bool discarded;
        if (drop >= value.DigitCount)
        {
            discarded = !value.IsZero;
            value.SetZero(floor);
        }
        else
        {
            discarded = value.AnyNonZeroInLast(drop);
            value.DropDigits(drop);
        }

        // A sticky digit below everything the coefficient holds; a one there keeps the
        // value strictly between the representable neighbours so rounding sees it.
        value.AppendZeros(1);
        if (discarded)
        {
            *value.Lsd = 1;
        }
    }

    /// <summary>
    /// Adds two aligned coefficients of the same sign. Digits are added from the least
    /// significant end with a carry, which is the whole of it.
    /// </summary>
    private static BcdNumber AddMagnitudes(BcdNumber left, BcdNumber right, byte* buffer,
        int exponent)
    {
        var length = Math.Max(left.DigitCount, right.DigitCount);

        // One position for a carry out of the leading digit, and one before that for the
        // finalizer's own carry.
        var msd = buffer + 2;
        var lsd = msd + length - 1;
        var carry = 0;

        var leftDigit = left.Lsd;
        var rightDigit = right.Lsd;

        for (var destination = lsd; destination >= msd; destination--)
        {
            var sum = carry;
            if (leftDigit >= left.Msd)
            {
                sum += *leftDigit;
                leftDigit--;
            }

            if (rightDigit >= right.Msd)
            {
                sum += *rightDigit;
                rightDigit--;
            }

            carry = sum >= 10 ? 1 : 0;
            *destination = (byte)(sum >= 10 ? sum - 10 : sum);
        }

        var result = new BcdNumber(DecimalKind.Finite, left.IsNegative, exponent, msd, lsd);

        if (carry != 0)
        {
            result.Msd--;
            *result.Msd = 1;
        }

        return result;
    }

    /// <summary>
    /// Subtracts the smaller aligned coefficient from the larger and takes the larger's
    /// sign. Borrowing runs the same way carrying does.
    /// </summary>
    private static BcdNumber SubtractMagnitudes(BcdNumber left, BcdNumber right, byte* buffer,
        int exponent, DecimalRounding rounding)
    {
        var order = CompareAligned(left, right);
        if (order == 0)
        {
            // Opposite signs canceling exactly gives a positive zero, except when the
            // rounding runs toward negative infinity.
            var zero = new BcdNumber(DecimalKind.Finite, rounding == DecimalRounding.Floor,
                exponent, buffer + 2, buffer + 2);
            *zero.Msd = 0;
            return zero;
        }

        var larger = order > 0 ? left : right;
        var smaller = order > 0 ? right : left;
        var length = Math.Max(larger.DigitCount, smaller.DigitCount);

        var msd = buffer + 2;
        var lsd = msd + length - 1;
        var borrow = 0;

        var largerDigit = larger.Lsd;
        var smallerDigit = smaller.Lsd;

        for (var destination = lsd; destination >= msd; destination--)
        {
            var difference = -borrow;
            if (largerDigit >= larger.Msd)
            {
                difference += *largerDigit;
                largerDigit--;
            }

            if (smallerDigit >= smaller.Msd)
            {
                difference -= *smallerDigit;
                smallerDigit--;
            }

            borrow = difference < 0 ? 1 : 0;
            *destination = (byte)(difference < 0 ? difference + 10 : difference);
        }

        var result = new BcdNumber(DecimalKind.Finite, larger.IsNegative, exponent, msd, lsd);

        result.Trim();
        return result;
    }

    /// <summary>
    /// Orders two coefficients already aligned to a common exponent, which is a plain
    /// magnitude comparison of their digits.
    /// </summary>
    private static int CompareAligned(BcdNumber left, BcdNumber right)
    {
        return BcdNumber.CompareDigits(left.Msd, left.Lsd, right.Msd, right.Lsd);
    }

}
