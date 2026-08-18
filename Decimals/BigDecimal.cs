// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Numerics;

namespace Decimals;

/// <summary>
/// A decimal whose coefficient grows as wide as it has to. This is decNumber's decNumber:
/// exp, ln, log10, and power evaluate series and Newton iterations at working precisions
/// several times the width of any interchange format, so they cannot run on the
/// fixed-width coefficients the rest of the library uses.
/// </summary>
/// <remarks>
/// <para>
/// The arithmetic reproduces decNumber's, condition flags included. That is not a
/// preference: the expected results for those four functions come from decNumber, and
/// every intermediate rounding it does is visible in the last digit of the result.
/// </para>
/// <para>
/// Nothing on the fixed-format paths reaches this type, so its allocation is confined to
/// the elementary functions. Should they ever need to be fast, the backing integer is the
/// thing to replace; the algorithms above it would not change.
/// </para>
/// </remarks>
internal readonly struct BigDecimal
{
    /// <summary>
    /// decNumber's resmap, which turns a guard digit into a residue code: 0 for an exact
    /// discard, 3 for below the halfway point, 5 for exactly half, 7 for above it.
    /// </summary>
    private static readonly int[] ResidueMap = [0, 3, 3, 3, 3, 5, 7, 7, 7, 7];

    private static readonly BigInteger[] CachedPowersOfTen = BuildPowersOfTen();

    private readonly int _digits;

    public BigDecimal(DecimalKind kind, bool isNegative, int exponent, BigInteger coefficient)
    {
        Kind = kind;
        IsNegative = isNegative;
        Exponent = exponent;
        Coefficient = coefficient;
        _digits = CountDigits(coefficient);
    }

    public DecimalKind Kind { get; }

    public bool IsNegative { get; }

    public int Exponent { get; }

    /// <summary>The coefficient's magnitude; the sign lives in <see cref="IsNegative"/>.</summary>
    public BigInteger Coefficient { get; }

    /// <summary>How many digits the coefficient is written with. A zero has one.</summary>
    public int Digits => _digits;

    public bool IsFinite => Kind == DecimalKind.Finite;

    public bool IsNaN => Kind is DecimalKind.QuietNaN or DecimalKind.SignalingNaN;

    public bool IsInfinity => Kind == DecimalKind.Infinity;

    public bool IsZero => Kind == DecimalKind.Finite && Coefficient.IsZero;

    public static BigDecimal Zero => new(DecimalKind.Finite, false, 0, BigInteger.Zero);

    public static BigDecimal One => new(DecimalKind.Finite, false, 0, BigInteger.One);

    public static BigDecimal Infinity(bool isNegative) =>
        new(DecimalKind.Infinity, isNegative, 0, BigInteger.Zero);

    public static BigDecimal FromInt32(int value)
    {
        return value < 0
            ? new BigDecimal(DecimalKind.Finite, true, 0, -(BigInteger)value)
            : new BigDecimal(DecimalKind.Finite, false, 0, value);
    }

    public static BigDecimal FromUnpacked(UnpackedDecimal<UInt128> value) =>
        new(value.Kind, value.IsNegative, value.Exponent, value.Coefficient);

    /// <summary>
    /// Back to the width-bounded form. The caller is responsible for having finalized the
    /// value into a format first, since only then is the coefficient known to fit.
    /// </summary>
    public UnpackedDecimal<UInt128> ToUnpacked() =>
        new(Kind, IsNegative, Exponent, (UInt128)Coefficient);

    /// <summary>
    /// The same coefficient read at a different exponent, which is how decNumber
    /// normalizes an operand before a series evaluation. This changes the value.
    /// </summary>
    public BigDecimal WithExponent(int exponent) =>
        new(Kind, IsNegative, exponent, Coefficient);

    public BigDecimal WithSign(bool isNegative) =>
        new(Kind, isNegative, Exponent, Coefficient);

    public BigDecimal Negated() => WithSign(!IsNegative);

    /// <summary>
    /// Whether the value is a whole number. An exponent at or above zero always is; below
    /// zero the discarded digits have to be zeros.
    /// </summary>
    public bool IsIntegerValued
    {
        get
        {
            if (!IsFinite)
            {
                return false;
            }

            if (Coefficient.IsZero || Exponent >= 0)
            {
                return true;
            }

            return (Coefficient % PowerOfTen(-Exponent)).IsZero;
        }
    }

    /// <summary>Whether the value is a whole number and that number is odd.</summary>
    public bool IsOddIntegerValued
    {
        get
        {
            if (!IsIntegerValued || Coefficient.IsZero)
            {
                return false;
            }

            // A positive exponent means trailing zeros, so the integer ends in one.
            if (Exponent > 0)
            {
                return false;
            }

            var integral = Exponent == 0 ? Coefficient : Coefficient / PowerOfTen(-Exponent);
            return !integral.IsEven;
        }
    }

    /// <summary>
    /// The value as a 32-bit integer, over the range decNumber's decGetInt accepts: whole
    /// numbers up to 999999999, or to 1999999997 when negative.
    /// </summary>
    public bool TryGetInt32(out int value)
    {
        value = 0;
        if (!IsIntegerValued)
        {
            return false;
        }

        if (Coefficient.IsZero)
        {
            return true;
        }

        // Beyond eleven integral digits the magnitude is past the window regardless, and
        // the multiply below would be pointless work.
        if (Digits + Exponent > 11)
        {
            return false;
        }

        var integral = Exponent >= 0
            ? Coefficient * PowerOfTen(Exponent)
            : Coefficient / PowerOfTen(-Exponent);

        var limit = IsNegative ? 1999999997 : 999999999;
        if (integral > limit)
        {
            return false;
        }

        value = IsNegative ? -(int)integral : (int)integral;
        return true;
    }

    /// <summary>Ten raised to a non-negative power.</summary>
    public static BigInteger PowerOfTen(int power)
    {
        return power < CachedPowersOfTen.Length
            ? CachedPowersOfTen[power]
            : BigInteger.Pow(10, power);
    }

    public static int CountDigits(BigInteger value)
    {
        if (value.IsZero)
        {
            return 1;
        }

        // Bits times log10(2), which lands on the right count or one past it.
        var digits = (int)(value.GetBitLength() * 30103 / 100000) + 1;
        while (digits > 1 && value < PowerOfTen(digits - 1))
        {
            digits--;
        }

        while (value >= PowerOfTen(digits))
        {
            digits++;
        }

        return digits;
    }

    /// <summary>
    /// Compares two finite values numerically, ignoring the signs when asked to. Returns
    /// the sign of left minus right.
    /// </summary>
    public static int Compare(BigDecimal left, BigDecimal right, bool ignoreSigns)
    {
        var leftNegative = !ignoreSigns && left.IsNegative;
        var rightNegative = !ignoreSigns && right.IsNegative;

        if (left.IsZero && right.IsZero)
        {
            return 0;
        }

        // With both zeros already out of the way, differing signs settle it: whichever side
        // is negative is the smaller, even when it is a negative zero against a positive
        // value.
        if (leftNegative != rightNegative)
        {
            return leftNegative ? -1 : 1;
        }

        var magnitude = CompareMagnitudes(left, right);
        return leftNegative ? -magnitude : magnitude;
    }

    private static int CompareMagnitudes(BigDecimal left, BigDecimal right)
    {
        if (left.IsInfinity || right.IsInfinity)
        {
            if (left.IsInfinity && right.IsInfinity)
            {
                return 0;
            }

            return left.IsInfinity ? 1 : -1;
        }

        if (left.Coefficient.IsZero)
        {
            return right.Coefficient.IsZero ? 0 : -1;
        }

        if (right.Coefficient.IsZero)
        {
            return 1;
        }

        // Adjusted exponents settle all but the case where the two overlap, and then the
        // shift needed to line them up is no wider than the difference in length.
        var leftAdjusted = left.Exponent + left.Digits;
        var rightAdjusted = right.Exponent + right.Digits;
        if (leftAdjusted != rightAdjusted)
        {
            return leftAdjusted > rightAdjusted ? 1 : -1;
        }

        var exponent = Math.Min(left.Exponent, right.Exponent);
        var leftCoefficient = left.Coefficient * PowerOfTen(left.Exponent - exponent);
        var rightCoefficient = right.Coefficient * PowerOfTen(right.Exponent - exponent);
        return leftCoefficient.CompareTo(rightCoefficient);
    }

    /// <summary>
    /// Adds, optionally flipping the right operand's sign first, which is how decNumber
    /// spells subtraction.
    /// </summary>
    public static BigDecimal Add(BigDecimal left, BigDecimal right, bool negateRight,
        BigDecimalContext context, ref DecimalStatus status)
    {
        var rightNegative = right.IsNegative ^ negateRight;
        var differingSigns = left.IsNegative != rightNegative;

        if (left.IsNaN)
        {
            return left;
        }

        if (right.IsNaN)
        {
            return right;
        }

        if (left.IsInfinity || right.IsInfinity)
        {
            if (left.IsInfinity && right.IsInfinity && differingSigns)
            {
                status |= DecimalStatus.InvalidOperation;
                return QuietNaN();
            }

            return Infinity(left.IsInfinity ? left.IsNegative : rightNegative);
        }

        if (left.IsZero || right.IsZero)
        {
            return AddZero(left, right, rightNegative, differingSigns, context, ref status);
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

        // When the lower operand falls entirely past the digit after the last one kept,
        // it cannot reach the result except to tip the rounding. Say so with a residue
        // rather than materializing an alignment hundreds of digits wide.
        if (padding > 0 && upper.Digits + padding > lower.Digits + context.Digits + 1)
        {
            var residue = differingSigns ? -1 : 1;
            var shift = context.Digits - upper.Digits;
            var coefficient = upper.Coefficient;
            var exponent = upper.Exponent;
            SetCoefficient(ref coefficient, ref exponent, context.Digits, ref residue, ref status);
            if (shift > 0)
            {
                coefficient *= PowerOfTen(shift);
                exponent -= shift;
            }

            return Finalize(upperNegative, coefficient, exponent, residue, context, ref status);
        }

        var scaled = upper.Coefficient * PowerOfTen(padding);
        var sum = (lowerNegative ? -lower.Coefficient : lower.Coefficient)
            + (upperNegative ? -scaled : scaled);

        var negative = sum.Sign < 0;
        if (sum.IsZero)
        {
            // A sum that cancels exactly is positive in every rounding mode but one.
            negative = differingSigns && context.Rounding == DecimalRounding.Floor;
        }

        var sumResidue = 0;
        var sumCoefficient = BigInteger.Abs(sum);
        var sumExponent = lower.Exponent;
        SetCoefficient(ref sumCoefficient, ref sumExponent, context.Digits, ref sumResidue, ref status);
        return Finalize(negative, sumCoefficient, sumExponent, sumResidue, context, ref status);
    }

    /// <summary>
    /// The result is the other operand, except that a zero still contributes its exponent
    /// when that is the lower of the two.
    /// </summary>
    private static BigDecimal AddZero(BigDecimal left, BigDecimal right, bool rightNegative,
        bool differingSigns, BigDecimalContext context, ref DecimalStatus status)
    {
        var zeroExponent = left.IsZero ? left.Exponent : right.Exponent;
        var other = left.IsZero ? right : left;
        var negative = left.IsZero ? rightNegative : left.IsNegative;

        var residue = 0;
        var coefficient = other.Coefficient;
        var exponent = other.Exponent;
        SetCoefficient(ref coefficient, ref exponent, context.Digits, ref residue, ref status);

        if (coefficient.IsZero)
        {
            if (zeroExponent < exponent)
            {
                exponent = zeroExponent;
            }

            if (differingSigns)
            {
                negative = context.Rounding == DecimalRounding.Floor;
            }
        }
        else
        {
            var pad = exponent - zeroExponent;
            if (pad > 0)
            {
                var digits = CountDigits(coefficient);
                if (digits + pad > context.Digits)
                {
                    // Only so many zeros fit; the value is unchanged, but digits went.
                    pad = context.Digits - digits;
                    status |= DecimalStatus.Rounded;
                }

                coefficient *= PowerOfTen(pad);
                exponent -= pad;
            }
        }

        return Finalize(negative, coefficient, exponent, residue, context, ref status);
    }

    public static BigDecimal Multiply(BigDecimal left, BigDecimal right,
        BigDecimalContext context, ref DecimalStatus status)
    {
        if (left.IsNaN)
        {
            return left;
        }

        if (right.IsNaN)
        {
            return right;
        }

        var negative = left.IsNegative ^ right.IsNegative;

        if (left.IsInfinity || right.IsInfinity)
        {
            if (left.IsZero || right.IsZero)
            {
                status |= DecimalStatus.InvalidOperation;
                return QuietNaN();
            }

            return Infinity(negative);
        }

        var residue = 0;
        var coefficient = left.Coefficient * right.Coefficient;
        var exponent = left.Exponent + right.Exponent;
        SetCoefficient(ref coefficient, ref exponent, context.Digits, ref residue, ref status);
        return Finalize(negative, coefficient, exponent, residue, context, ref status);
    }

    public static BigDecimal Divide(BigDecimal left, BigDecimal right,
        BigDecimalContext context, ref DecimalStatus status)
    {
        if (left.IsNaN)
        {
            return left;
        }

        if (right.IsNaN)
        {
            return right;
        }

        var negative = left.IsNegative ^ right.IsNegative;

        if (left.IsInfinity)
        {
            if (right.IsInfinity)
            {
                status |= DecimalStatus.InvalidOperation;
                return QuietNaN();
            }

            return Infinity(negative);
        }

        if (right.IsInfinity)
        {
            // The ideal exponent runs off to negative infinity, so the zero settles at the
            // smallest exponent the context allows and says it was clamped.
            status |= DecimalStatus.Clamped;
            return new BigDecimal(DecimalKind.Finite, negative, context.TinyExponent, BigInteger.Zero);
        }

        if (right.Coefficient.IsZero)
        {
            if (left.Coefficient.IsZero)
            {
                status |= DecimalStatus.DivisionUndefined;
                return QuietNaN();
            }

            status |= DecimalStatus.DivisionByZero;
            return Infinity(negative);
        }

        var idealExponent = left.Exponent - right.Exponent;

        if (left.Coefficient.IsZero)
        {
            var zeroResidue = 0;
            var zeroCoefficient = BigInteger.Zero;
            var zeroExponent = idealExponent;
            SetCoefficient(ref zeroCoefficient, ref zeroExponent, context.Digits, ref zeroResidue, ref status);
            return Finalize(negative, zeroCoefficient, zeroExponent, zeroResidue, context, ref status);
        }

        // Scale so the quotient lands on the requested number of digits, give or take one.
        var shift = context.Digits - left.Digits + right.Digits;
        var numerator = left.Coefficient;
        var denominator = right.Coefficient;
        if (shift >= 0)
        {
            numerator *= PowerOfTen(shift);
        }
        else
        {
            denominator *= PowerOfTen(-shift);
        }

        var quotient = BigInteger.DivRem(numerator, denominator, out var remainder);
        var exponent = idealExponent - shift;
        var residue = 0;

        if (remainder.IsZero)
        {
            // Exact, so the specification wants the exponent nearest dividend minus
            // divisor: give back the trailing zeros the scaling introduced, and no more.
            while (exponent < idealExponent)
            {
                var stripped = BigInteger.DivRem(quotient, 10, out var digit);
                if (!digit.IsZero)
                {
                    break;
                }

                quotient = stripped;
                exponent++;
            }
        }
        else
        {
            var twice = remainder * 2;
            residue = twice.CompareTo(denominator) switch
            {
                < 0 => 3,
                0 => 5,
                _ => 7
            };
        }

        SetCoefficient(ref quotient, ref exponent, context.Digits, ref residue, ref status);
        return Finalize(negative, quotient, exponent, residue, context, ref status);
    }

    /// <summary>
    /// The square root, correctly rounded. Same method as the fixed-width one: scale the
    /// coefficient until its integer root carries a guard digit, and let whatever the root
    /// leaves over be the sticky.
    /// </summary>
    public static BigDecimal SquareRoot(BigDecimal value, BigDecimalContext context,
        ref DecimalStatus status)
    {
        if (!value.IsFinite)
        {
            if (value.IsInfinity && value.IsNegative)
            {
                status |= DecimalStatus.InvalidOperation;
                return QuietNaN();
            }

            return value;
        }

        var idealExponent = value.Exponent >= 0 ? value.Exponent / 2 : (value.Exponent - 1) / 2;

        if (value.Coefficient.IsZero)
        {
            return Finalize(value.IsNegative, BigInteger.Zero, idealExponent, 0, context, ref status);
        }

        if (value.IsNegative)
        {
            status |= DecimalStatus.InvalidOperation;
            return QuietNaN();
        }

        // Halving the exponent is what makes the root's, so the scaling has to leave it even.
        var shift = (2 * (context.Digits + 1)) - value.Digits;
        if (((value.Exponent - shift) & 1) != 0)
        {
            shift++;
        }

        var radicand = value.Coefficient * PowerOfTen(shift);
        var root = IntegerSquareRoot(radicand);
        var exponent = (value.Exponent - shift) / 2;
        var residue = 0;

        if (root * root == radicand)
        {
            // Exact, so shorten toward the exponent the specification prefers, which is
            // half the operand's.
            while (exponent < idealExponent && (root % 10).IsZero)
            {
                root /= 10;
                exponent++;
            }
        }
        else
        {
            // The root carries a guard digit, so all the remainder says is that something
            // non-zero lies below it: a sticky bit, which is residue 1.
            residue = 1;
        }

        SetCoefficient(ref root, ref exponent, context.Digits, ref residue, ref status);
        return Finalize(false, root, exponent, residue, context, ref status);
    }

    /// <summary>
    /// The largest integer whose square does not exceed the value, by Newton's method from
    /// a power-of-two start that is certain to be above the root.
    /// </summary>
    private static BigInteger IntegerSquareRoot(BigInteger value)
    {
        if (value.IsZero)
        {
            return BigInteger.Zero;
        }

        var guess = BigInteger.One << (int)((value.GetBitLength() + 1) / 2);
        for (;;)
        {
            var next = (guess + (value / guess)) >> 1;
            if (next >= guess)
            {
                return guess;
            }

            guess = next;
        }
    }

    /// <summary>
    /// Rounds a value to the context's precision and brings it into range, which is
    /// decNumber's decCopyFit followed by decFinish.
    /// </summary>
    public static BigDecimal Round(BigDecimal value, BigDecimalContext context,
        ref DecimalStatus status)
    {
        return Round(value, 0, context, ref status);
    }

    /// <summary>
    /// The same, starting from a residue the caller already holds: exp and ln finish this
    /// way, having computed far more digits than they return.
    /// </summary>
    public static BigDecimal Round(BigDecimal value, int residue, BigDecimalContext context,
        ref DecimalStatus status)
    {
        if (!value.IsFinite)
        {
            if (residue != 0)
            {
                status |= DecimalStatus.Inexact | DecimalStatus.Rounded;
            }

            return value;
        }

        var coefficient = value.Coefficient;
        var exponent = value.Exponent;
        SetCoefficient(ref coefficient, ref exponent, context.Digits, ref residue, ref status);
        return Finalize(value.IsNegative, coefficient, exponent, residue, context, ref status);
    }

    /// <summary>
    /// decNumber's decSetCoeff: shortens a coefficient to the requested number of digits,
    /// folding what goes with it into the residue.
    /// </summary>
    /// <param name="coefficient">The coefficient, shortened in place.</param>
    /// <param name="exponent">The exponent, raised to keep the value.</param>
    /// <param name="digits">Digits to end up with; zero or less means the result is zero.</param>
    /// <param name="residue">Rounding residue, read as well as written.</param>
    /// <param name="status">Conditions raised.</param>
    public static void SetCoefficient(ref BigInteger coefficient, ref int exponent, int digits,
        ref int residue, ref DecimalStatus status)
    {
        var length = CountDigits(coefficient);
        var discard = length - digits;
        if (discard <= 0)
        {
            if (residue != 0)
            {
                status |= DecimalStatus.Inexact | DecimalStatus.Rounded;
            }

            return;
        }

        exponent += discard;
        status |= DecimalStatus.Rounded;
        if (residue > 1)
        {
            // Whatever the residue described is now further to the right than the digits
            // about to go, so it can only be a sticky bit.
            residue = 1;
        }

        if (discard > length)
        {
            // Everything goes, and then some: the guard digit is a zero the value never had.
            if (residue <= 0 && !coefficient.IsZero)
            {
                residue = 1;
            }

            if (residue != 0)
            {
                status |= DecimalStatus.Inexact;
            }

            coefficient = BigInteger.Zero;
            return;
        }

        var guardPower = PowerOfTen(discard - 1);
        var kept = BigInteger.DivRem(coefficient, guardPower * 10, out var dropped);
        var guard = (int)BigInteger.Divide(dropped, guardPower);
        if (!(dropped - (guard * guardPower)).IsZero)
        {
            residue = 1;
        }

        residue += ResidueMap[guard];
        coefficient = digits <= 0 ? BigInteger.Zero : kept;

        if (residue != 0)
        {
            status |= DecimalStatus.Inexact;
        }
    }

    /// <summary>
    /// decNumber's decFinalize: settles a coefficient of final length into the context,
    /// applying any pending round and then the exponent limits.
    /// </summary>
    public static BigDecimal Finalize(bool isNegative, BigInteger coefficient, int exponent,
        int residue, BigDecimalContext context, ref DecimalStatus status)
    {
        var digits = CountDigits(coefficient);
        var tinyExponent = context.MinExponent - digits + 1;

        // Subnormal is decided before the pending round, since that round could carry the
        // value up to Nmin or drop it to zero and cover the fact up.
        if (exponent <= tinyExponent)
        {
            if (exponent < tinyExponent)
            {
                return Subnormal(isNegative, coefficient, exponent, residue, context, ref status);
            }

            // Equal leaves one case: the value is exactly Nmin and the residue is
            // subtractive, so rounding can still take it below.
            if (residue < 0 && coefficient == PowerOfTen(digits - 1))
            {
                ApplyRound(ref coefficient, ref exponent, ref digits, isNegative, residue,
                    context, ref status, out _);
                return Subnormal(isNegative, coefficient, exponent, residue, context, ref status);
            }
        }

        if (residue != 0)
        {
            ApplyRound(ref coefficient, ref exponent, ref digits, isNegative, residue,
                context, ref status, out var overflowed);
            if (overflowed)
            {
                return Overflowed(isNegative, coefficient, exponent, context, ref status);
            }
        }

        if (exponent <= context.MaxExponent - context.Digits + 1)
        {
            return new BigDecimal(DecimalKind.Finite, isNegative, exponent, coefficient);
        }

        if (exponent > context.MaxExponent - digits + 1)
        {
            return Overflowed(isNegative, coefficient, exponent, context, ref status);
        }

        if (!context.Clamp)
        {
            return new BigDecimal(DecimalKind.Finite, isNegative, exponent, coefficient);
        }

        // In range, but only if the coefficient carries the extra magnitude as trailing
        // zeros rather than the exponent.
        var shift = exponent - (context.MaxExponent - context.Digits + 1);
        if (!coefficient.IsZero)
        {
            coefficient *= PowerOfTen(shift);
        }

        status |= DecimalStatus.Clamped;
        return new BigDecimal(DecimalKind.Finite, isNegative, exponent - shift, coefficient);
    }

    /// <summary>decNumber's decSetSubnormal.</summary>
    private static BigDecimal Subnormal(bool isNegative, BigInteger coefficient, int exponent,
        int residue, BigDecimalContext context, ref DecimalStatus status)
    {
        var tiny = context.TinyExponent;

        if (coefficient.IsZero)
        {
            // A zero is never subnormal, whatever its exponent; it just gets clamped.
            if (exponent < tiny)
            {
                exponent = tiny;
                status |= DecimalStatus.Clamped;
            }

            return new BigDecimal(DecimalKind.Finite, isNegative, exponent, coefficient);
        }

        status |= DecimalStatus.Subnormal;

        var digits = CountDigits(coefficient);
        var adjust = tiny - exponent;
        if (adjust <= 0)
        {
            // 754's default rule: a subnormal underflows exactly when it is inexact.
            if ((status & DecimalStatus.Inexact) != 0)
            {
                status |= DecimalStatus.Underflow;
            }

            return new BigDecimal(DecimalKind.Finite, isNegative, exponent, coefficient);
        }

        var workContext = context;
        workContext.Digits = digits - adjust;
        workContext.MinExponent = context.MinExponent - adjust;

        SetCoefficient(ref coefficient, ref exponent, workContext.Digits, ref residue, ref status);
        var workDigits = CountDigits(coefficient);
        ApplyRound(ref coefficient, ref exponent, ref workDigits, isNegative, residue,
            workContext, ref status, out _);

        if ((status & DecimalStatus.Inexact) != 0)
        {
            status |= DecimalStatus.Underflow;
        }

        // Rounding a run of nines up lengthens the coefficient by one; it fits, because
        // the value was shortened a moment ago.
        if (exponent > tiny)
        {
            coefficient *= 10;
            exponent--;
        }

        if (coefficient.IsZero)
        {
            // Rounded to nothing, which by definition means the exponent was clamped.
            status |= DecimalStatus.Clamped;
        }

        return new BigDecimal(DecimalKind.Finite, isNegative, exponent, coefficient);
    }

    /// <summary>
    /// decNumber's decApplyRound: acts on a pending residue while keeping the
    /// coefficient's length, except for the two carries that cannot.
    /// </summary>
    private static void ApplyRound(ref BigInteger coefficient, ref int exponent, ref int digits,
        bool isNegative, int residue, BigDecimalContext context, ref DecimalStatus status,
        out bool overflowed)
    {
        overflowed = false;
        if (residue == 0)
        {
            return;
        }

        var bump = 0;
        switch (context.Rounding)
        {
            case DecimalRounding.ZeroFiveUp:
                // Down, unless the last digit is one the mode moves; a subtractive
                // residue takes it down instead, unless that would be a no-op.
                var lastDigit = (int)(coefficient % 5);
                if (residue < 0 && lastDigit != 1)
                {
                    bump = -1;
                }
                else if (residue > 0 && lastDigit == 0)
                {
                    bump = 1;
                }

                break;
            case DecimalRounding.Down:
                if (residue < 0)
                {
                    bump = -1;
                }

                break;
            case DecimalRounding.HalfDown:
                if (residue > 5)
                {
                    bump = 1;
                }

                break;
            case DecimalRounding.HalfEven:
                if (residue > 5)
                {
                    bump = 1;
                }
                else if (residue == 5 && !coefficient.IsEven)
                {
                    bump = 1;
                }

                break;
            case DecimalRounding.HalfUp:
                if (residue >= 5)
                {
                    bump = 1;
                }

                break;
            case DecimalRounding.Up:
                if (residue > 0)
                {
                    bump = 1;
                }

                break;
            case DecimalRounding.Ceiling:
                if (isNegative)
                {
                    if (residue < 0)
                    {
                        bump = -1;
                    }
                }
                else if (residue > 0)
                {
                    bump = 1;
                }

                break;
            default:
                if (isNegative)
                {
                    if (residue > 0)
                    {
                        bump = 1;
                    }
                }
                else if (residue < 0)
                {
                    bump = -1;
                }

                break;
        }

        if (bump == 0)
        {
            return;
        }

        if (bump > 0)
        {
            if (coefficient == PowerOfTen(digits) - BigInteger.One)
            {
                // All nines: 999 becomes 100 one decade up, rather than growing a digit.
                coefficient = PowerOfTen(digits - 1);
                exponent++;
                if (exponent + digits > context.MaxExponent + 1)
                {
                    overflowed = true;
                }

                return;
            }

            coefficient += BigInteger.One;
            return;
        }

        if (coefficient == PowerOfTen(digits - 1))
        {
            // The mirror case: 100 becomes 999 one decade down.
            coefficient = PowerOfTen(digits) - BigInteger.One;
            exponent--;

            if (exponent + 1 == context.MinExponent - context.Digits + 1)
            {
                // The decade below was Etiny, so the value gets clamped back up and the
                // last nine drops off again.
                if (digits == 1)
                {
                    coefficient = BigInteger.Zero;
                }
                else
                {
                    coefficient = PowerOfTen(digits - 1) - BigInteger.One;
                    digits--;
                }

                exponent++;
                status |= DecimalStatus.Underflow | DecimalStatus.Subnormal
                    | DecimalStatus.Inexact | DecimalStatus.Rounded;
            }

            return;
        }

        coefficient -= BigInteger.One;
    }

    /// <summary>decNumber's decSetOverflow.</summary>
    private static BigDecimal Overflowed(bool isNegative, BigInteger coefficient, int exponent,
        BigDecimalContext context, ref DecimalStatus status)
    {
        if (coefficient.IsZero)
        {
            // A zero has no magnitude to overflow; only its exponent needs bringing back.
            var limit = context.Clamp ? context.MaxExponent - (context.Digits - 1) : context.MaxExponent;
            if (exponent > limit)
            {
                exponent = limit;
                status |= DecimalStatus.Clamped;
            }

            return new BigDecimal(DecimalKind.Finite, isNegative, exponent, BigInteger.Zero);
        }

        status |= DecimalStatus.Overflow | DecimalStatus.Inexact | DecimalStatus.Rounded;

        var givesLargestFinite = context.Rounding switch
        {
            DecimalRounding.Down => true,
            DecimalRounding.ZeroFiveUp => true,
            DecimalRounding.Ceiling => isNegative,
            DecimalRounding.Floor => !isNegative,
            _ => false
        };

        if (!givesLargestFinite)
        {
            return Infinity(isNegative);
        }

        return new BigDecimal(DecimalKind.Finite, isNegative,
            context.MaxExponent - context.Digits + 1,
            PowerOfTen(context.Digits) - BigInteger.One);
    }

    private static BigDecimal QuietNaN() =>
        new(DecimalKind.QuietNaN, false, 0, BigInteger.Zero);

    private static BigInteger[] BuildPowersOfTen()
    {
        var powers = new BigInteger[256];
        powers[0] = BigInteger.One;
        for (var i = 1; i < powers.Length; i++)
        {
            powers[i] = powers[i - 1] * 10;
        }

        return powers;
    }
}
