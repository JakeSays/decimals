// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Numerics;

namespace Decimals;

/// <summary>
/// The elementary functions.
/// </summary>
/// <remarks>
/// The square root runs on the format's own coefficient, since a root needs only twice the
/// digits and <see cref="UInt256"/> holds those. The other four cannot: exp evaluates its
/// series into an accumulator twice the working precision, ln calls exp from inside a
/// Newton iteration carried out wider still, and power calls ln. At Decimal128 the
/// innermost accumulator runs to roughly 190 digits, so those four work on
/// <see cref="BigDecimal"/> and are ports of decNumber's decExpOp, decLnOp, decNumberLog10,
/// and decNumberPower.
/// </remarks>
internal static class DecimalMath
{
    /// <summary>
    /// decNumber's LNnn: an initial estimate of ln(f) for a coefficient truncated to two
    /// digits, 0.10 through 0.99. Each entry packs a four-digit coefficient in the top 14
    /// bits and a two-bit exponent code, giving the value -c * 10**(-e-3).
    /// </summary>
    private static readonly ushort[] NaturalLogEstimates =
    [
        9016, 8652, 8316, 8008, 7724, 7456, 7208, 6972, 6748, 6540,
        6340, 6148, 5968, 5792, 5628, 5464, 5312, 5164, 5020, 4884,
        4748, 4620, 4496, 4376, 4256, 4144, 4032, 39233, 38181, 37157,
        36157, 35181, 34229, 33297, 32389, 31501, 30629, 29777, 28945, 28129,
        27329, 26545, 25777, 25021, 24281, 23553, 22837, 22137, 21445, 20769,
        20101, 19445, 18801, 18165, 17541, 16925, 16321, 15721, 15133, 14553,
        13985, 13421, 12865, 12317, 11777, 11241, 10717, 10197, 9685, 9177,
        8677, 8185, 7697, 7213, 6737, 6269, 5801, 5341, 4889, 4437,
        39930, 35534, 31186, 26886, 22630, 18418, 14254, 10130, 6046, 20055
    ];

    /// <summary>
    /// ln(10) to forty digits. Log10 divides by this every time it is called, and decNumber
    /// carries the constant rather than iterating for it.
    /// </summary>
    private static readonly BigDecimal NaturalLogOfTen = new(DecimalKind.Finite, false, -39,
        BigInteger.Parse("2302585092994045684017991454684364207601"));

    /// <summary>ln(2) to forty digits, carried for the same reason.</summary>
    private static readonly BigDecimal NaturalLogOfTwo = new(DecimalKind.Finite, false, -40,
        BigInteger.Parse("6931471805599453094172321214581765680755"));

    /// <summary>
    /// The square root, correctly rounded.
    /// </summary>
    /// <remarks>
    /// The coefficient is scaled until its integer square root has one digit more than the
    /// precision -- that digit is the guard -- and whatever the root leaves over becomes the
    /// sticky. Scaling has to keep the exponent even, since halving it is what makes the
    /// root's exponent. An exact root is then shortened toward the exponent the
    /// specification prefers, which is half the operand's.
    /// </remarks>
    public static UnpackedDecimal<UInt128> SquareRoot<TFormat>(UnpackedDecimal<UInt128> value,
        DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (value.IsNaN)
        {
            return DecimalArithmetic.PropagateNaN(value, ref status);
        }

        if (value.Kind == DecimalKind.Infinity)
        {
            if (value.IsNegative)
            {
                status |= DecimalStatus.InvalidOperation;
                return QuietNaN();
            }

            return value;
        }

        var idealExponent = FloorHalf(value.Exponent);

        if (value.Coefficient == UInt128.Zero)
        {
            // Both zeros keep their sign: the root of a negative zero is a negative zero.
            return DecimalFinalizer.Finalize<TFormat>(value.IsNegative, UInt128.Zero, idealExponent,
                rounding, ref status);
        }

        if (value.IsNegative)
        {
            status |= DecimalStatus.InvalidOperation;
            return QuietNaN();
        }

        var digits = DecimalRounder.CountDigits(value.Coefficient);
        var shift = (2 * (TFormat.Precision + 1)) - digits;
        if (((value.Exponent - shift) & 1) != 0)
        {
            shift++;
        }

        var radicand = UInt256.MultiplyByPowerOfTen(new UInt256(value.Coefficient), shift);
        var root = UInt256.Sqrt(radicand);
        var exact = UInt256.Multiply(root, root) == radicand;
        var exponent = (value.Exponent - shift) / 2;

        if (exact)
        {
            while (exponent < idealExponent && root % 10 == UInt128.Zero)
            {
                root /= 10;
                exponent++;
            }

            return DecimalFinalizer.Finalize<TFormat>(false, root, exponent, rounding, ref status);
        }

        // A sticky digit below the guard, so the rounding sees that the root was cut short.
        return DecimalFinalizer.Finalize<TFormat>(false, (root * 10) + UInt128.One, exponent - 1,
            rounding, ref status);
    }

    /// <summary>
    /// e raised to the value. Finite results are always full precision and inexact, except
    /// for the two that are not approximations: exp(0) is 1 and exp(-Infinity) is 0.
    /// </summary>
    public static UnpackedDecimal<UInt128> Exp<TFormat>(UnpackedDecimal<UInt128> value,
        DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (value.IsNaN)
        {
            return DecimalArithmetic.PropagateNaN(value, ref status);
        }

        var context = BigDecimalContext.ForFormat<TFormat>(rounding);
        return Exponential(BigDecimal.FromUnpacked(value), context, ref status).ToUnpacked();
    }

    /// <summary>
    /// The natural logarithm. Zero gives -Infinity, a negative is invalid, and ln(1) is an
    /// exact zero.
    /// </summary>
    public static UnpackedDecimal<UInt128> Log<TFormat>(UnpackedDecimal<UInt128> value,
        DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (value.IsNaN)
        {
            return DecimalArithmetic.PropagateNaN(value, ref status);
        }

        var context = BigDecimalContext.ForFormat<TFormat>(rounding);
        return NaturalLog(BigDecimal.FromUnpacked(value), context, ref status).ToUnpacked();
    }

    /// <summary>
    /// The base-ten logarithm, as ln(x)/ln(10). A power of ten gives its exponent exactly.
    /// </summary>
    public static UnpackedDecimal<UInt128> Log10<TFormat>(UnpackedDecimal<UInt128> value,
        DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (value.IsNaN)
        {
            return DecimalArithmetic.PropagateNaN(value, ref status);
        }

        var set = BigDecimalContext.ForFormat<TFormat>(rounding);
        return BaseTenLog(BigDecimal.FromUnpacked(value), set, ref status).ToUnpacked();
    }

    /// <summary>
    /// The left operand raised to the right. An integer exponent that fits is applied by
    /// repeated squaring, which can be exact; anything else goes through exp(ln(x)*y).
    /// </summary>
    public static UnpackedDecimal<UInt128> Power<TFormat>(UnpackedDecimal<UInt128> left,
        UnpackedDecimal<UInt128> right, DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (DecimalArithmetic.TryHandleNaN(left, right, ref status, out var nan))
        {
            return nan;
        }

        var set = BigDecimalContext.ForFormat<TFormat>(rounding);
        var result = PowerCore(BigDecimal.FromUnpacked(left), BigDecimal.FromUnpacked(right),
            set, ref status);
        return result.ToUnpacked();
    }

    /// <summary>Two raised to the value.</summary>
    public static UnpackedDecimal<UInt128> Exp2<TFormat>(UnpackedDecimal<UInt128> value,
        DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        return RaiseWholeBase<TFormat>(2, value, rounding, ref status);
    }

    /// <summary>Ten raised to the value.</summary>
    public static UnpackedDecimal<UInt128> Exp10<TFormat>(UnpackedDecimal<UInt128> value,
        DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        return RaiseWholeBase<TFormat>(10, value, rounding, ref status);
    }

    /// <summary>
    /// e raised to the value, less one. Near zero the answer is about the operand itself,
    /// so the subtraction cancels the leading digits away; the evaluation is widened by as
    /// many digits as the cancellation costs.
    /// </summary>
    public static UnpackedDecimal<UInt128> ExpMinusOne<TFormat>(UnpackedDecimal<UInt128> value,
        DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (value.IsNaN)
        {
            return DecimalArithmetic.PropagateNaN(value, ref status);
        }

        var x = BigDecimal.FromUnpacked(value);

        if (x.Kind == DecimalKind.Infinity)
        {
            return x.IsNegative ? NegativeOne() : value;
        }

        if (x.IsZero)
        {
            return value;
        }

        var lost = LeadingDigitsLost(x);
        if (lost > TFormat.Precision + 3)
        {
            // The x**2/2 term lies entirely below the last digit kept, so the result is the
            // operand with a residue away from zero, that term carrying the operand's sign.
            return RoundWithResidue<TFormat>(x, x.IsNegative ? -1 : 1, rounding, ref status);
        }

        var wide = WideContext<TFormat>(3 + lost);
        var wideStatus = DecimalStatus.None;
        var result = Exponential(x, wide, ref wideStatus);
        result = BigDecimal.Add(result, BigDecimal.One, true, wide, ref wideStatus);
        return Finish<TFormat>(result, wideStatus, rounding, ref status);
    }

    /// <summary>Two raised to the value, less one.</summary>
    public static UnpackedDecimal<UInt128> Exp2MinusOne<TFormat>(UnpackedDecimal<UInt128> value,
        DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        return RaiseWholeBaseMinusOne<TFormat>(2, NaturalLogOfTwo, value, rounding, ref status);
    }

    /// <summary>Ten raised to the value, less one.</summary>
    public static UnpackedDecimal<UInt128> Exp10MinusOne<TFormat>(UnpackedDecimal<UInt128> value,
        DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        return RaiseWholeBaseMinusOne<TFormat>(10, NaturalLogOfTen, value, rounding, ref status);
    }

    /// <summary>
    /// The base-two logarithm. A power of two gives its exponent exactly, the way a power
    /// of ten does for <see cref="Log10{TFormat}"/>.
    /// </summary>
    public static UnpackedDecimal<UInt128> Log2<TFormat>(UnpackedDecimal<UInt128> value,
        DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (value.IsNaN)
        {
            return DecimalArithmetic.PropagateNaN(value, ref status);
        }

        var set = BigDecimalContext.ForFormat<TFormat>(rounding);
        return BaseTwoLog(BigDecimal.FromUnpacked(value), set, ref status).ToUnpacked();
    }

    /// <summary>
    /// The logarithm in an arbitrary base. Two and ten go through their own routines, which
    /// have exact cases worth keeping.
    /// </summary>
    public static UnpackedDecimal<UInt128> LogInBase<TFormat>(UnpackedDecimal<UInt128> value,
        UnpackedDecimal<UInt128> newBase, DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (DecimalArithmetic.TryHandleNaN(value, newBase, ref status, out var nan))
        {
            return nan;
        }

        var set = BigDecimalContext.ForFormat<TFormat>(rounding);
        var x = BigDecimal.FromUnpacked(value);
        var b = BigDecimal.FromUnpacked(newBase);

        if (IsWholeNumber(b, 10))
        {
            return BaseTenLog(x, set, ref status).ToUnpacked();
        }

        if (IsWholeNumber(b, 2))
        {
            return BaseTwoLog(x, set, ref status).ToUnpacked();
        }

        if (TryLogarithmOfSpecial(x, ref status, out var special))
        {
            return special.ToUnpacked();
        }

        return DividedLogarithm(x, b, set, ref status).ToUnpacked();
    }

    /// <summary>
    /// The natural logarithm of one plus the value. For a small operand the sum would round
    /// straight back to one, so it is formed at enough digits to keep the operand whole.
    /// </summary>
    public static UnpackedDecimal<UInt128> LogPlusOne<TFormat>(UnpackedDecimal<UInt128> value,
        DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (value.IsNaN)
        {
            return DecimalArithmetic.PropagateNaN(value, ref status);
        }

        var x = BigDecimal.FromUnpacked(value);

        if (x.Kind == DecimalKind.Infinity)
        {
            if (x.IsNegative)
            {
                status |= DecimalStatus.InvalidOperation;
                return QuietNaN();
            }

            return value;
        }

        if (x.IsZero)
        {
            return value;
        }

        var lost = LeadingDigitsLost(x);
        if (lost > TFormat.Precision + 3)
        {
            // ln(1+x) is x less x**2/2, and that term lies below the last digit kept, so
            // the residue runs toward zero.
            return RoundWithResidue<TFormat>(x, x.IsNegative ? 1 : -1, rounding, ref status);
        }

        var wideStatus = DecimalStatus.None;
        var sum = OnePlus<TFormat>(x, lost);
        var result = NaturalLog(sum, WideContext<TFormat>(3), ref wideStatus);
        return Finish<TFormat>(result, wideStatus, rounding, ref status);
    }

    /// <summary>The base-two logarithm of one plus the value.</summary>
    public static UnpackedDecimal<UInt128> Log2PlusOne<TFormat>(UnpackedDecimal<UInt128> value,
        DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        return LogPlusOneInWholeBase<TFormat>(2, value, rounding, ref status);
    }

    /// <summary>The base-ten logarithm of one plus the value.</summary>
    public static UnpackedDecimal<UInt128> Log10PlusOne<TFormat>(UnpackedDecimal<UInt128> value,
        DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        return LogPlusOneInWholeBase<TFormat>(10, value, rounding, ref status);
    }

    /// <summary>The cube root.</summary>
    public static UnpackedDecimal<UInt128> Cbrt<TFormat>(UnpackedDecimal<UInt128> value,
        DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        return RootN<TFormat>(value, 3, rounding, ref status);
    }

    /// <summary>
    /// The degree-th root, as exp(ln(x)/degree). A root that comes out exact is returned as
    /// such: the rounded root is raised back to the degree and compared with the operand.
    /// </summary>
    public static UnpackedDecimal<UInt128> RootN<TFormat>(UnpackedDecimal<UInt128> value,
        int degree, DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (value.IsNaN)
        {
            return DecimalArithmetic.PropagateNaN(value, ref status);
        }

        if (degree == 0)
        {
            status |= DecimalStatus.InvalidOperation;
            return QuietNaN();
        }

        var x = BigDecimal.FromUnpacked(value);
        var oddDegree = (degree & 1) != 0;

        if (x.IsZero)
        {
            // A zero keeps its sign through any root; a negative degree sends it to an
            // infinity of that same sign.
            return degree < 0
                ? new UnpackedDecimal<UInt128>(DecimalKind.Infinity, x.IsNegative, 0, UInt128.Zero)
                : value;
        }

        if (x.IsNegative && !oddDegree)
        {
            // An even root of a negative has no real value.
            status |= DecimalStatus.InvalidOperation;
            return QuietNaN();
        }

        var negative = x.IsNegative;

        if (x.Kind == DecimalKind.Infinity)
        {
            return degree > 0
                ? new UnpackedDecimal<UInt128>(DecimalKind.Infinity, negative, 0, UInt128.Zero)
                : new UnpackedDecimal<UInt128>(DecimalKind.Finite, negative, 0, UInt128.Zero);
        }

        if (degree == 1)
        {
            return value;
        }

        if (degree == 2)
        {
            return SquareRoot<TFormat>(value, rounding, ref status);
        }

        var wide = WideContext<TFormat>(5);
        var wideStatus = DecimalStatus.None;
        var logarithm = NaturalLog(x.WithSign(false), wide, ref wideStatus);
        var scaled = BigDecimal.Divide(logarithm, BigDecimal.FromInt32(degree), wide, ref wideStatus);
        var root = Exponential(scaled, wide, ref wideStatus);

        var candidateStatus = DecimalStatus.None;
        var candidate = Finish<TFormat>(root.WithSign(negative), wideStatus, rounding, ref candidateStatus);

        if (TryExactRoot(candidate, x, degree, out var exact))
        {
            return exact;
        }

        status |= candidateStatus;
        return candidate;
    }

    /// <summary>
    /// The square root of the sum of two squares, computed wide enough that squaring an
    /// operand cannot overflow on the way.
    /// </summary>
    public static UnpackedDecimal<UInt128> Hypot<TFormat>(UnpackedDecimal<UInt128> left,
        UnpackedDecimal<UInt128> right, DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        // An infinite operand settles the result even when the other one is a NaN.
        if (left.Kind == DecimalKind.Infinity || right.Kind == DecimalKind.Infinity)
        {
            return new UnpackedDecimal<UInt128>(DecimalKind.Infinity, false, 0, UInt128.Zero);
        }

        if (DecimalArithmetic.TryHandleNaN(left, right, ref status, out var nan))
        {
            return nan;
        }

        var wide = WideContext<TFormat>(5);
        var squares = wide;
        squares.Digits = (2 * TFormat.Precision) + 8;

        var wideStatus = DecimalStatus.None;
        var x = BigDecimal.FromUnpacked(left).WithSign(false);
        var y = BigDecimal.FromUnpacked(right).WithSign(false);

        var sum = BigDecimal.Add(
            BigDecimal.Multiply(x, x, squares, ref wideStatus),
            BigDecimal.Multiply(y, y, squares, ref wideStatus),
            false, squares, ref wideStatus);

        var root = BigDecimal.SquareRoot(sum, wide, ref wideStatus);
        return Finish<TFormat>(root, wideStatus, rounding, ref status);
    }

    /// <summary>
    /// decNumber's decExpOp. The series is evaluated on an operand normalized to below one
    /// and the result raised back by a power of ten, which is what keeps the iteration
    /// count down.
    /// </summary>
    private static BigDecimal Exponential(BigDecimal rhs, BigDecimalContext set,
        ref DecimalStatus status)
    {
        if (rhs.IsNaN)
        {
            return rhs;
        }

        if (rhs.IsInfinity)
        {
            return rhs.IsNegative ? BigDecimal.Zero : rhs;
        }

        if (rhs.IsZero)
        {
            return BigDecimal.One;
        }

        // For a small enough operand the result is 1 in every digit the context keeps,
        // since e**x is under 1+3x/2 for 0 < x < 0.66. A negative operand needs one more
        // zero, its result being of the form 0.9999999 rather than 1.0000001.
        var tiny = new BigDecimal(DecimalKind.Finite, false,
            rhs.IsNegative ? -set.Digits - 1 : -set.Digits, 4);
        if (BigDecimal.Compare(tiny, rhs, true) >= 0)
        {
            status |= DecimalStatus.Inexact | DecimalStatus.Rounded;
            return PaddedOne(set.Digits);
        }

        var ignore = DecimalStatus.None;

        var accumulatorContext = BigDecimalContext.Default();
        accumulatorContext.MaxExponent = set.MaxExponent;
        accumulatorContext.MinExponent = set.MinExponent;
        accumulatorContext.Clamp = false;

        var x = rhs;
        var h = rhs.Exponent + rhs.Digits;
        BigDecimal a;
        int p;

        if (h > 8)
        {
            // Ten to a power this large cannot be computed, but it need not be: the result
            // is certain to overflow or underflow to zero, so hand the raise below a value
            // that is bound to take it there.
            a = new BigDecimal(DecimalKind.Finite, false, rhs.IsNegative ? -2 : 0, 2);
            h = 8;
            p = 9;
        }
        else
        {
            // Normalizing further than below one cuts iterations, but the power of ten
            // that undoes it has to stay computable, so the leverage slides with h.
            var maxLever = rhs.Digits > 8 ? 1 : 0;
            var lever = Math.Min(8 - h, maxLever);
            var use = -rhs.Digits - lever;
            h += lever;
            if (h < 0)
            {
                use += h;
                h = 0;
            }

            if (rhs.Exponent != use)
            {
                x = rhs.WithExponent(use);
            }

            // Hull and Abrham's working precision, widened when the operand carries more
            // digits than the result: all of them can reach the last digit kept.
            p = Math.Max(x.Digits, set.Digits) + h + 2;

            var termContext = BigDecimalContext.Default();
            var divisorContext = termContext;
            termContext.Digits = p;
            termContext.MinExponent = BigDecimalContext.SmallestExponent;

            // The accumulator holds twice the working precision so that adding each term
            // is exact and round-off cannot pile up across the iterations.
            accumulatorContext.Digits = p * 2;

            var term = x;
            a = BigDecimal.One;
            var divisor = BigDecimal.FromInt32(2);

            for (;;)
            {
                a = BigDecimal.Add(a, term, false, accumulatorContext, ref status);
                term = BigDecimal.Multiply(term, x, termContext, ref ignore);
                term = BigDecimal.Divide(term, divisor, termContext, ref ignore);

                // Done when the term has fallen so far below the accumulator that it
                // cannot reach the last digit kept, and the accumulator is full length.
                if (a.Digits + a.Exponent >= term.Digits + term.Exponent + p + 1
                    && a.Digits >= p)
                {
                    break;
                }

                divisor = BigDecimal.Add(divisor, BigDecimal.One, false, divisorContext, ref ignore);
            }
        }

        if (h > 0)
        {
            // Undo the normalization: a**(10**h), by squaring down the bits of 10**h. Only
            // the multipliers loop is wanted, not the whole of power.
            var n = (int)BigDecimal.PowerOfTen(h);
            accumulatorContext.Digits = p + 2;

            var raised = BigDecimal.One;
            var seenBit = false;
            for (var i = 1; ; i++)
            {
                // Give up once the answer is settled either way.
                if ((status & (DecimalStatus.Overflow | DecimalStatus.Underflow)) != 0)
                {
                    if ((status & DecimalStatus.Overflow) != 0 || raised.IsZero)
                    {
                        break;
                    }
                }

                n <<= 1;
                if (n < 0)
                {
                    seenBit = true;
                    raised = BigDecimal.Multiply(raised, a, accumulatorContext, ref status);
                }

                if (i == 31)
                {
                    break;
                }

                if (!seenBit)
                {
                    continue;
                }

                raised = BigDecimal.Multiply(raised, raised, accumulatorContext, ref status);
            }

            a = raised;
        }

        // Dirt to the right: the series was cut short, so the result is inexact even where
        // the digits kept do not change.
        var residue = a.IsZero ? 0 : 1;
        return BigDecimal.Round(a, residue, set, ref status);
    }

    /// <summary>
    /// decNumber's decLnOp. Newton's method on a' = a + x*exp(-a) - 1, from a four-digit
    /// estimate off a table, doubling the digits calculated each iteration.
    /// </summary>
    private static BigDecimal NaturalLog(BigDecimal rhs, BigDecimalContext set,
        ref DecimalStatus status)
    {
        if (rhs.IsNaN)
        {
            return rhs;
        }

        if (rhs.IsInfinity)
        {
            if (rhs.IsNegative)
            {
                status |= DecimalStatus.InvalidOperation;
                return QuietNaNValue();
            }

            return rhs;
        }

        if (rhs.IsZero)
        {
            return BigDecimal.Infinity(true);
        }

        if (rhs.IsNegative)
        {
            status |= DecimalStatus.InvalidOperation;
            return QuietNaNValue();
        }

        var ignore = DecimalStatus.None;

        // ln(10) and ln(2) get asked for often enough -- log10 needs the first every time
        // it runs -- to be worth carrying rather than iterating for.
        if (rhs.Exponent == 0 && set.Digits <= 40)
        {
            var literalContext = set;
            literalContext.Rounding = DecimalRounding.HalfEven;

            if (rhs.Digits == 2 && rhs.Coefficient == 10)
            {
                status |= DecimalStatus.Inexact | DecimalStatus.Rounded;
                return BigDecimal.Round(NaturalLogOfTen, literalContext, ref ignore);
            }

            if (rhs.Digits == 1 && rhs.Coefficient == 2)
            {
                status |= DecimalStatus.Inexact | DecimalStatus.Rounded;
                return BigDecimal.Round(NaturalLogOfTwo, literalContext, ref ignore);
            }
        }

        var p = Math.Max(rhs.Digits, Math.Max(set.Digits, 7)) + 2;

        // Read the operand as a fraction f times a power of ten, so that
        // ln(x) = ln(f) + ln(10)*r, and estimate ln(f) from the table.
        var estimateContext = BigDecimalContext.Default();
        var r = rhs.Exponent + rhs.Digits;
        var a = BigDecimal.Multiply(BigDecimal.FromInt32(r),
            new BigDecimal(DecimalKind.Finite, false, -6, 2302585), estimateContext, ref ignore);

        var leading = (int)(rhs.Digits >= 2
            ? rhs.Coefficient / BigDecimal.PowerOfTen(rhs.Digits - 2)
            : rhs.Coefficient * 10);
        var entry = NaturalLogEstimates[leading - 10];
        var b = new BigDecimal(DecimalKind.Finite, true, -(entry & 3) - 3, entry >> 2);
        a = BigDecimal.Add(a, b, false, estimateContext, ref ignore);

        // Four digits of the estimate are good. Near Nmax it comes in low, so the
        // iteration approaches from below and the exp calls below cannot overflow.
        var accumulatorContext = estimateContext;
        accumulatorContext.MaxExponent = set.MaxExponent;
        accumulatorContext.MinExponent = set.MinExponent;
        accumulatorContext.Clamp = false;

        // The adjustment is a catastrophic subtraction, so it is calculated at the sum of
        // the operand's precision and the working precision, over doubled bounds.
        var adjustmentContext = accumulatorContext;
        adjustmentContext.MaxExponent = BigDecimalContext.MaxMathExponent * 2;
        adjustmentContext.MinExponent = -BigDecimalContext.MaxMathExponent * 2;

        // Nine to start, so the sequence runs 7+2, 16+2, 34+2: the standard widths.
        var pp = 9;
        accumulatorContext.Digits = pp;
        adjustmentContext.Digits = pp + rhs.Digits;

        for (;;)
        {
            a = a.Negated();
            b = Exponential(a, adjustmentContext, ref ignore);
            a = a.Negated();
            b = BigDecimal.Multiply(b, rhs, adjustmentContext, ref ignore);
            b = BigDecimal.Add(b, BigDecimal.One, true, adjustmentContext, ref ignore);

            // The iteration ends when the adjustment cannot move the result by half a unit
            // in the last place -- looser than exp needs, since all that follows is the
            // final rounding -- and the accumulator is full length.
            if (b.IsZero || a.Digits + a.Exponent >= b.Digits + b.Exponent + set.Digits + 1)
            {
                if (a.Digits == p)
                {
                    break;
                }

                if (a.IsZero)
                {
                    if (BigDecimal.Compare(rhs, BigDecimal.One, false) == 0)
                    {
                        a = a.WithExponent(0);
                    }
                    else
                    {
                        status |= DecimalStatus.Inexact | DecimalStatus.Rounded;
                    }

                    break;
                }

                if (b.IsZero)
                {
                    // Force the padding when the adjustment reached zero early.
                    b = b.WithExponent(a.Exponent - p);
                }
            }

            a = BigDecimal.Add(a, b, false, accumulatorContext, ref ignore);
            if (pp == p)
            {
                continue;
            }

            pp *= 2;
            if (pp > p)
            {
                pp = p;
            }

            accumulatorContext.Digits = pp;
            adjustmentContext.Digits = pp + rhs.Digits;
        }

        var residue = a.IsZero ? 0 : 1;
        return BigDecimal.Round(a, residue, set, ref status);
    }

    /// <summary>decNumber's decNumberPower, less the NaN handling its caller does.</summary>
    private static BigDecimal PowerCore(BigDecimal lhs, BigDecimal rhs, BigDecimalContext set,
        ref DecimalStatus status)
    {
        if (rhs.IsInfinity)
        {
            if (lhs.IsNegative && !lhs.IsZero)
            {
                status |= DecimalStatus.InvalidOperation;
                return QuietNaNValue();
            }

            var comparison = BigDecimal.Compare(lhs, BigDecimal.One, false);
            if (comparison == 0)
            {
                // One to an infinite power is deemed inexact, so it comes back padded.
                status |= DecimalStatus.Inexact | DecimalStatus.Rounded;
                return PaddedOne(set.Digits);
            }

            var infinite = comparison < 0 ? rhs.IsNegative : !rhs.IsNegative;
            return infinite ? BigDecimal.Infinity(false) : BigDecimal.Zero;
        }

        var integerExponent = rhs.IsIntegerValued;
        var oddExponent = rhs.IsOddIntegerValued;
        var useInteger = rhs.TryGetInt32(out var n);
        var negative = lhs.IsNegative && oddExponent;

        if (lhs.IsInfinity)
        {
            if (rhs.IsZero)
            {
                return BigDecimal.One;
            }

            if (!integerExponent && lhs.IsNegative)
            {
                status |= DecimalStatus.InvalidOperation;
                return QuietNaNValue();
            }

            return rhs.IsNegative
                ? new BigDecimal(DecimalKind.Finite, negative, 0, BigInteger.Zero)
                : BigDecimal.Infinity(negative);
        }

        if (lhs.IsZero)
        {
            if (rhs.IsZero)
            {
                status |= DecimalStatus.InvalidOperation;
                return QuietNaNValue();
            }

            return rhs.IsNegative
                ? BigDecimal.Infinity(negative)
                : new BigDecimal(DecimalKind.Finite, negative, 0, BigInteger.Zero);
        }

        if (!useInteger)
        {
            if (lhs.IsNegative)
            {
                status |= DecimalStatus.InvalidOperation;
                return QuietNaNValue();
            }

            var aset = BigDecimalContext.Default();
            aset.MaxExponent = BigDecimalContext.MaxMathExponent;
            aset.MinExponent = -BigDecimalContext.MaxMathExponent;
            aset.Clamp = false;

            // Enough to hold the whole information content of the left operand, exponent
            // included, plus four; six covers any exponent. The spare digits cost ln
            // almost nothing and cut the cases that land more than half a unit out.
            aset.Digits = Math.Max(lhs.Digits, set.Digits) + 6 + 4;

            var accumulator = NaturalLog(lhs, aset, ref status);
            if (accumulator.IsZero)
            {
                // The left operand was one, which would otherwise reduce to an integer 1.
                accumulator = BigDecimal.One;
                if (!integerExponent)
                {
                    accumulator = PaddedOne(set.Digits);
                    status |= DecimalStatus.Inexact | DecimalStatus.Rounded;
                }
            }
            else
            {
                accumulator = BigDecimal.Multiply(accumulator, rhs, aset, ref status);
                accumulator = Exponential(accumulator, aset, ref status);
            }

            return BigDecimal.Round(accumulator, set, ref status);
        }

        if (rhs.IsZero)
        {
            return BigDecimal.One;
        }

        if (n < 0)
        {
            n = -n;
        }

        var integerSet = set;
        integerSet.Rounding = DecimalRounding.HalfEven;
        integerSet.Digits = set.Digits + (rhs.Digits + rhs.Exponent) + 2;

        var dac = BigDecimal.One;
        if (rhs.IsNegative)
        {
            // Invert the operand now rather than the result later, which keeps the
            // rounding to one place.
            lhs = BigDecimal.Divide(BigDecimal.One, lhs, integerSet, ref status);
        }

        var seen = false;
        for (var i = 1; ; i++)
        {
            if ((status & (DecimalStatus.Overflow | DecimalStatus.Underflow)) != 0)
            {
                if ((status & DecimalStatus.Overflow) != 0 || dac.IsZero)
                {
                    break;
                }
            }

            n <<= 1;
            if (n < 0)
            {
                seen = true;
                dac = BigDecimal.Multiply(dac, lhs, integerSet, ref status);
            }

            if (i == 31)
            {
                break;
            }

            if (!seen)
            {
                continue;
            }

            dac = BigDecimal.Multiply(dac, dac, integerSet, ref status);
        }

        if ((status & (DecimalStatus.Overflow | DecimalStatus.Underflow)) != 0)
        {
            if (!dac.IsFinite)
            {
                return dac.WithSign(negative);
            }

            // Round the subnormal to the requested length rather than the working one.
            return BigDecimal.Finalize(negative, dac.Coefficient, dac.Exponent, 0, set, ref status);
        }

        // The sign came out of the multiplications themselves: a negative base reaches an
        // odd power still negative.
        return BigDecimal.Round(dac, set, ref status);
    }

    /// <summary>
    /// decNumber's decNumberLog10, at the BigDecimal level so that the reductions built on
    /// it keep the exact power-of-ten case.
    /// </summary>
    private static BigDecimal BaseTenLog(BigDecimal rhs, BigDecimalContext set,
        ref DecimalStatus status)
    {
        if (TryLogarithmOfSpecial(rhs, ref status, out var special))
        {
            return special;
        }

        // A coefficient of one followed by zeros makes the value a power of ten, and then
        // the logarithm is the adjusted exponent, exactly.
        if (rhs.Coefficient == BigDecimal.PowerOfTen(rhs.Digits - 1))
        {
            var power = BigDecimal.FromInt32(rhs.Exponent + rhs.Digits - 1);
            return BigDecimal.Round(power, set, ref status);
        }

        return DividedLogarithm(rhs, BigDecimal.FromInt32(10), set, ref status);
    }

    /// <summary>The same shape for base two, whose exact case is a power of two.</summary>
    private static BigDecimal BaseTwoLog(BigDecimal rhs, BigDecimalContext set,
        ref DecimalStatus status)
    {
        if (TryLogarithmOfSpecial(rhs, ref status, out var special))
        {
            return special;
        }

        if (TryPowerOfTwoExponent(rhs, out var exponent))
        {
            return BigDecimal.Round(BigDecimal.FromInt32(exponent), set, ref status);
        }

        return DividedLogarithm(rhs, BigDecimal.FromInt32(2), set, ref status);
    }

    /// <summary>
    /// ln(x)/ln(base), at the precisions decNumber uses for log10: the numerator carries the
    /// whole information content of the operand, the divisor three guard digits, and the
    /// division alone is done at the requested precision.
    /// </summary>
    private static BigDecimal DividedLogarithm(BigDecimal rhs, BigDecimal wholeBase,
        BigDecimalContext set, ref DecimalStatus status)
    {
        var aset = BigDecimalContext.Default();
        aset.MaxExponent = BigDecimalContext.MaxMathExponent;
        aset.MinExponent = -BigDecimalContext.MaxMathExponent;
        aset.Clamp = false;

        // Six digits covers any exponent, and letting all of the operand participate costs
        // ln almost nothing: it doubles its precision each iteration, so a few extra digits
        // rarely buys another one.
        aset.Digits = Math.Max(rhs.Digits + 6, set.Digits) + 3;
        var logarithm = NaturalLog(rhs, aset, ref status);

        if (!logarithm.IsFinite || logarithm.IsZero)
        {
            return logarithm;
        }

        // The divisor is always inexact and always rounded, and saying so would tell the
        // caller nothing; anything else it raises, such as an unusable base, does.
        var baseStatus = DecimalStatus.None;
        aset.Digits = set.Digits + 3;
        var divisor = NaturalLog(wholeBase, aset, ref baseStatus);
        status |= baseStatus & ~(DecimalStatus.Inexact | DecimalStatus.Rounded);

        aset.Digits = set.Digits;
        return BigDecimal.Divide(logarithm, divisor, aset, ref status);
    }

    /// <summary>
    /// The cases every logarithm shares: a NaN passes through, a negative is invalid, zero
    /// gives -Infinity, and +Infinity gives itself.
    /// </summary>
    private static bool TryLogarithmOfSpecial(BigDecimal rhs, ref DecimalStatus status,
        out BigDecimal result)
    {
        result = rhs;

        if (rhs.IsNaN)
        {
            return true;
        }

        if (rhs.IsInfinity)
        {
            if (rhs.IsNegative)
            {
                status |= DecimalStatus.InvalidOperation;
                result = QuietNaNValue();
            }

            return true;
        }

        if (rhs.IsZero)
        {
            result = BigDecimal.Infinity(true);
            return true;
        }

        if (rhs.IsNegative)
        {
            status |= DecimalStatus.InvalidOperation;
            result = QuietNaNValue();
            return true;
        }

        return false;
    }

    private static UnpackedDecimal<UInt128> RaiseWholeBase<TFormat>(int wholeBase,
        UnpackedDecimal<UInt128> value, DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (value.IsNaN)
        {
            return DecimalArithmetic.PropagateNaN(value, ref status);
        }

        var wide = WideContext<TFormat>(3);
        var wideStatus = DecimalStatus.None;
        var result = PowerCore(BigDecimal.FromInt32(wholeBase), BigDecimal.FromUnpacked(value),
            wide, ref wideStatus);
        return Finish<TFormat>(result, wideStatus, rounding, ref status);
    }

    private static UnpackedDecimal<UInt128> RaiseWholeBaseMinusOne<TFormat>(int wholeBase,
        BigDecimal logarithmOfBase, UnpackedDecimal<UInt128> value, DecimalRounding rounding,
        ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (value.IsNaN)
        {
            return DecimalArithmetic.PropagateNaN(value, ref status);
        }

        var x = BigDecimal.FromUnpacked(value);

        if (x.Kind == DecimalKind.Infinity)
        {
            return x.IsNegative ? NegativeOne() : value;
        }

        if (x.IsZero)
        {
            return value;
        }

        var lost = LeadingDigitsLost(x);
        if (lost > TFormat.Precision + 3)
        {
            // Below this the answer is x*ln(base) to well past the last digit kept.
            var narrow = WideContext<TFormat>(3);
            var narrowStatus = DecimalStatus.Inexact;
            var scaled = BigDecimal.Multiply(x, logarithmOfBase, narrow, ref narrowStatus);
            return Finish<TFormat>(scaled, narrowStatus, rounding, ref status);
        }

        var wide = WideContext<TFormat>(4 + lost);
        var wideStatus = DecimalStatus.None;
        var result = PowerCore(BigDecimal.FromInt32(wholeBase), x, wide, ref wideStatus);
        result = BigDecimal.Add(result, BigDecimal.One, true, wide, ref wideStatus);
        return Finish<TFormat>(result, wideStatus, rounding, ref status);
    }

    private static UnpackedDecimal<UInt128> LogPlusOneInWholeBase<TFormat>(int wholeBase,
        UnpackedDecimal<UInt128> value, DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (value.IsNaN)
        {
            return DecimalArithmetic.PropagateNaN(value, ref status);
        }

        var x = BigDecimal.FromUnpacked(value);

        if (x.Kind == DecimalKind.Infinity)
        {
            if (x.IsNegative)
            {
                status |= DecimalStatus.InvalidOperation;
                return QuietNaN();
            }

            return value;
        }

        if (x.IsZero)
        {
            return value;
        }

        var wide = WideContext<TFormat>(3);
        var wideStatus = DecimalStatus.None;
        var lost = LeadingDigitsLost(x);

        if (lost > TFormat.Precision + 3)
        {
            // The answer is x/ln(base) to well past the last digit kept.
            var divisor = NaturalLog(BigDecimal.FromInt32(wholeBase), wide, ref wideStatus);
            var scaled = BigDecimal.Divide(x, divisor, wide, ref wideStatus);
            return Finish<TFormat>(scaled, wideStatus, rounding, ref status);
        }

        var sum = OnePlus<TFormat>(x, lost);
        var result = wholeBase == 10
            ? BaseTenLog(sum, wide, ref wideStatus)
            : BaseTwoLog(sum, wide, ref wideStatus);
        return Finish<TFormat>(result, wideStatus, rounding, ref status);
    }

    /// <summary>
    /// One plus the value, exactly. The context is widened by the digits the operand sits
    /// below the point so that none of it is lost in the sum.
    /// </summary>
    private static BigDecimal OnePlus<TFormat>(BigDecimal value, int lost)
        where TFormat : IDecimalFormat
    {
        var ignore = DecimalStatus.None;
        return BigDecimal.Add(BigDecimal.One, value, false, WideContext<TFormat>(4 + lost),
            ref ignore);
    }

    /// <summary>
    /// How many digits a subtraction from one cancels: the leading digit of the operand
    /// plus the zeros between it and the point.
    /// </summary>
    private static int LeadingDigitsLost(BigDecimal value)
    {
        var adjusted = value.Exponent + value.Digits - 1;
        return adjusted < 0 ? -adjusted : 0;
    }

    /// <summary>
    /// The context the reductions are evaluated in: guard digits above the format, and no
    /// exponent limits, so that only the final rounding into the format can overflow.
    /// </summary>
    private static BigDecimalContext WideContext<TFormat>(int guardDigits)
        where TFormat : IDecimalFormat
    {
        var context = BigDecimalContext.ForFormat<TFormat>(DecimalRounding.HalfEven);
        context.Digits = TFormat.Precision + guardDigits;
        context.MaxExponent = BigDecimalContext.MaxMathExponent;
        context.MinExponent = -BigDecimalContext.MaxMathExponent;
        context.Clamp = false;
        return context;
    }

    /// <summary>
    /// Rounds a value evaluated at guard digits into the format, once. An evaluation that
    /// was inexact hands the rounding a residue, so that a result which happens to end in
    /// zeros is still reported as the approximation it is.
    /// </summary>
    private static UnpackedDecimal<UInt128> Finish<TFormat>(BigDecimal value,
        DecimalStatus wideStatus, DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        status |= wideStatus & (DecimalStatus.InvalidOperation | DecimalStatus.DivisionByZero);

        var residue = value.IsFinite && !value.IsZero
            && (wideStatus & DecimalStatus.Inexact) != 0 ? 1 : 0;

        var set = BigDecimalContext.ForFormat<TFormat>(rounding);
        return BigDecimal.Round(value, residue, set, ref status).ToUnpacked();
    }

    private static UnpackedDecimal<UInt128> RoundWithResidue<TFormat>(BigDecimal value,
        int residue, DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        var set = BigDecimalContext.ForFormat<TFormat>(rounding);
        return BigDecimal.Round(value, residue, set, ref status).ToUnpacked();
    }

    /// <summary>Whether a value is exactly a given small whole number.</summary>
    private static bool IsWholeNumber(BigDecimal value, int whole)
    {
        return value.IsFinite && !value.IsNegative && value.TryGetInt32(out var integer)
            && integer == whole;
    }

    /// <summary>
    /// Whether the value is a power of two, and which. It is read as a fraction in lowest
    /// terms, since a value below one carries its factors in the denominator.
    /// </summary>
    private static bool TryPowerOfTwoExponent(BigDecimal value, out int exponent)
    {
        exponent = 0;

        var numerator = value.Coefficient;
        var denominator = BigInteger.One;
        if (value.Exponent >= 0)
        {
            numerator *= BigDecimal.PowerOfTen(value.Exponent);
        }
        else
        {
            denominator = BigDecimal.PowerOfTen(-value.Exponent);
        }

        var common = BigInteger.GreatestCommonDivisor(numerator, denominator);
        numerator /= common;
        denominator /= common;

        if (!TryBitPosition(numerator, out var high) || !TryBitPosition(denominator, out var low))
        {
            return false;
        }

        exponent = high - low;
        return true;
    }

    /// <summary>Which bit a positive value is, when it is a power of two alone.</summary>
    private static bool TryBitPosition(BigInteger value, out int position)
    {
        position = (int)(value.GetBitLength() - 1);
        return (value & (value - BigInteger.One)).IsZero;
    }

    /// <summary>
    /// Whether a rounded root is the exact one, by raising it back to the degree. An exact
    /// root is then shortened toward the exponent the operand's divided by the degree,
    /// which is what the square root does with its own.
    /// </summary>
    private static bool TryExactRoot(UnpackedDecimal<UInt128> candidate, BigDecimal value,
        int degree, out UnpackedDecimal<UInt128> result)
    {
        result = candidate;

        // Past this the raise costs more than the case is worth, and an exact root of such
        // a degree needs an operand that is a perfect power of it.
        if (Math.Abs(degree) > 40 || candidate.Kind != DecimalKind.Finite
            || candidate.Coefficient == UInt128.Zero)
        {
            return false;
        }

        var magnitude = Math.Abs(degree);
        var raised = BigInteger.Pow(candidate.Coefficient, magnitude);
        var raisedExponent = candidate.Exponent * (long)magnitude;

        // A negative degree gives the reciprocal, so the test becomes whether the raised
        // root times the operand is one.
        var comparand = degree > 0 ? value.Coefficient : BigInteger.One;
        var comparandExponent = degree > 0 ? (long)value.Exponent : 0L;
        if (degree < 0)
        {
            raised *= value.Coefficient;
            raisedExponent += value.Exponent;
        }

        var common = Math.Min(raisedExponent, comparandExponent);
        if (raisedExponent - common > 20000 || comparandExponent - common > 20000)
        {
            return false;
        }

        var left = raised * BigDecimal.PowerOfTen((int)(raisedExponent - common));
        var right = comparand * BigDecimal.PowerOfTen((int)(comparandExponent - common));
        if (left != right)
        {
            return false;
        }

        var ideal = FloorDivide(value.Exponent, degree);
        var coefficient = (BigInteger)candidate.Coefficient;
        var exponent = candidate.Exponent;
        while (exponent < ideal && (coefficient % 10).IsZero)
        {
            coefficient /= 10;
            exponent++;
        }

        result = new UnpackedDecimal<UInt128>(DecimalKind.Finite, candidate.IsNegative, exponent,
            (UInt128)coefficient);
        return true;
    }

    /// <summary>
    /// Division rounding toward negative infinity, which is what an exponent divided by a
    /// root's degree calls for: the preferred exponent goes down, not toward zero.
    /// </summary>
    private static int FloorDivide(int value, int divisor)
    {
        var quotient = value / divisor;
        return quotient * divisor != value && (value < 0) != (divisor < 0) ? quotient - 1 : quotient;
    }

    private static UnpackedDecimal<UInt128> NegativeOne()
    {
        return new UnpackedDecimal<UInt128>(DecimalKind.Finite, true, 0, UInt128.One);
    }

    /// <summary>
    /// One written out to the full width of the context, which is how the specification
    /// asks an inexact one to be presented.
    /// </summary>
    private static BigDecimal PaddedOne(int digits)
    {
        var shift = digits - 1;
        return new BigDecimal(DecimalKind.Finite, false, -shift, BigDecimal.PowerOfTen(shift));
    }

    /// <summary>
    /// Halves an exponent, rounding toward negative infinity so that an odd negative
    /// exponent goes down rather than toward zero.
    /// </summary>
    private static int FloorHalf(int exponent)
    {
        return exponent >= 0 ? exponent / 2 : (exponent - 1) / 2;
    }

    private static UnpackedDecimal<UInt128> QuietNaN()
    {
        return new UnpackedDecimal<UInt128>(DecimalKind.QuietNaN, false, 0, UInt128.Zero);
    }

    private static BigDecimal QuietNaNValue()
    {
        return new BigDecimal(DecimalKind.QuietNaN, false, 0, BigInteger.Zero);
    }
}
