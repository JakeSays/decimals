// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// The elementary functions.
/// </summary>
/// <remarks>
/// <para>
/// The square root runs on the format's own coefficient, since a root needs only twice the
/// digits and <see cref="UInt256"/> holds those. The other four cannot: exp evaluates its
/// series into an accumulator twice the working precision, ln calls exp from inside a
/// Newton iteration carried out wider still, and power calls ln. At Decimal128 the
/// innermost accumulator runs to roughly 190 digits, so those four work on
/// <see cref="WideNumber"/> and are ports of decNumber's decExpOp, decLnOp, decNumberLog10,
/// and decNumberPower.
/// </para>
/// <para>
/// decNumber declares its temporaries as locals sized from the widest case. Here an entry
/// point stack-allocates one block and the functions draw slots from it through
/// <see cref="WideArena"/>, which is the same arrangement with the sizes in one place.
/// </para>
/// </remarks>
internal static unsafe class DecimalMath
{
    /// <summary>
    /// decNumber's LNnn: an initial estimate of ln(f) for a coefficient truncated to two
    /// digits, 0.10 through 0.99. Each entry packs a four-digit coefficient in the top 14
    /// bits and a two-bit exponent code, giving the value -c * 10**(-e-3).
    /// </summary>
    private static ReadOnlySpan<ushort> NaturalLogEstimates =>
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

        var scratch = stackalloc ulong[WideArena.TotalWords];
        var arena = new WideArena((uint*)scratch);
        var context = WideContext.ForFormat<TFormat>(rounding);
        var operand = WideNumber.FromUnpacked(value, arena.TakeUnits());

        return Exponential(ref arena, operand, context, ref status).ToUnpacked();
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

        var scratch = stackalloc ulong[WideArena.TotalWords];
        var arena = new WideArena((uint*)scratch);
        var context = WideContext.ForFormat<TFormat>(rounding);
        var operand = WideNumber.FromUnpacked(value, arena.TakeUnits());

        return NaturalLog(ref arena, operand, context, ref status).ToUnpacked();
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

        var scratch = stackalloc ulong[WideArena.TotalWords];
        var arena = new WideArena((uint*)scratch);
        var set = WideContext.ForFormat<TFormat>(rounding);
        var operand = WideNumber.FromUnpacked(value, arena.TakeUnits());

        return BaseTenLog(ref arena, operand, set, ref status).ToUnpacked();
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

        var scratch = stackalloc ulong[WideArena.TotalWords];
        var arena = new WideArena((uint*)scratch);
        var set = WideContext.ForFormat<TFormat>(rounding);
        var baseValue = WideNumber.FromUnpacked(left, arena.TakeUnits());
        var exponent = WideNumber.FromUnpacked(right, arena.TakeUnits());

        return PowerCore(ref arena, baseValue, exponent, set, ref status).ToUnpacked();
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

        var scratch = stackalloc ulong[WideArena.TotalWords];
        var arena = new WideArena((uint*)scratch);
        var x = WideNumber.FromUnpacked(value, arena.TakeUnits());

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
            return RoundWithResidue<TFormat>(ref arena, x, x.IsNegative ? -1 : 1, rounding,
                ref status);
        }

        var wide = GuardedContext<TFormat>(3 + lost);
        var wideStatus = DecimalStatus.None;
        var result = Exponential(ref arena, x, wide, ref wideStatus);
        result = WideMath.Subtract(ref arena, result, WideMath.One(ref arena), wide, ref wideStatus);

        return Finish<TFormat>(ref arena, result, wideStatus, rounding, ref status);
    }

    /// <summary>Two raised to the value, less one.</summary>
    public static UnpackedDecimal<UInt128> Exp2MinusOne<TFormat>(UnpackedDecimal<UInt128> value,
        DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        return RaiseWholeBaseMinusOne<TFormat>(2, value, rounding, ref status);
    }

    /// <summary>Ten raised to the value, less one.</summary>
    public static UnpackedDecimal<UInt128> Exp10MinusOne<TFormat>(UnpackedDecimal<UInt128> value,
        DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        return RaiseWholeBaseMinusOne<TFormat>(10, value, rounding, ref status);
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

        var scratch = stackalloc ulong[WideArena.TotalWords];
        var arena = new WideArena((uint*)scratch);
        var set = WideContext.ForFormat<TFormat>(rounding);
        var operand = WideNumber.FromUnpacked(value, arena.TakeUnits());

        return BaseTwoLog(ref arena, operand, set, ref status).ToUnpacked();
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

        var scratch = stackalloc ulong[WideArena.TotalWords];
        var arena = new WideArena((uint*)scratch);
        var set = WideContext.ForFormat<TFormat>(rounding);
        var x = WideNumber.FromUnpacked(value, arena.TakeUnits());
        var b = WideNumber.FromUnpacked(newBase, arena.TakeUnits());

        if (IsWholeNumber(b, 10))
        {
            return BaseTenLog(ref arena, x, set, ref status).ToUnpacked();
        }

        if (IsWholeNumber(b, 2))
        {
            return BaseTwoLog(ref arena, x, set, ref status).ToUnpacked();
        }

        if (TryLogarithmOfSpecial(ref arena, x, ref status, out var special))
        {
            return special.ToUnpacked();
        }

        return DividedLogarithm(ref arena, x, b, set, ref status).ToUnpacked();
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

        var scratch = stackalloc ulong[WideArena.TotalWords];
        var arena = new WideArena((uint*)scratch);
        var x = WideNumber.FromUnpacked(value, arena.TakeUnits());

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
            return RoundWithResidue<TFormat>(ref arena, x, x.IsNegative ? 1 : -1, rounding,
                ref status);
        }

        var wideStatus = DecimalStatus.None;
        var sum = OnePlus<TFormat>(ref arena, x, lost);
        var result = NaturalLog(ref arena, sum, GuardedContext<TFormat>(3), ref wideStatus);

        return Finish<TFormat>(ref arena, result, wideStatus, rounding, ref status);
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

        var scratch = stackalloc ulong[WideArena.TotalWords];
        var arena = new WideArena((uint*)scratch);
        var x = WideNumber.FromUnpacked(value, arena.TakeUnits());
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

        var wide = GuardedContext<TFormat>(5);
        var wideStatus = DecimalStatus.None;

        var magnitudeOfX = arena.TakeCopy(x);
        magnitudeOfX.IsNegative = false;

        var logarithm = NaturalLog(ref arena, magnitudeOfX, wide, ref wideStatus);
        var scaled = WideMath.Divide(ref arena, logarithm, WideMath.FromInt32(ref arena, degree),
            wide, ref wideStatus);

        var root = Exponential(ref arena, scaled, wide, ref wideStatus);
        root.IsNegative = negative;

        var candidateStatus = DecimalStatus.None;
        var candidate = Finish<TFormat>(ref arena, root, wideStatus, rounding, ref candidateStatus);

        if (TryExactRoot(ref arena, candidate, x, degree, out var exact))
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

        var scratch = stackalloc ulong[WideArena.TotalWords];
        var arena = new WideArena((uint*)scratch);

        var wide = GuardedContext<TFormat>(5);
        var squares = wide;
        squares.Digits = (2 * TFormat.Precision) + 8;

        var wideStatus = DecimalStatus.None;
        var x = WideNumber.FromUnpacked(left, arena.TakeUnits());
        var y = WideNumber.FromUnpacked(right, arena.TakeUnits());
        x.IsNegative = false;
        y.IsNegative = false;

        var sum = WideMath.Add(ref arena,
            WideMath.Multiply(ref arena, x, x, squares, ref wideStatus),
            WideMath.Multiply(ref arena, y, y, squares, ref wideStatus),
            squares, ref wideStatus);

        var root = WideMath.SquareRoot(ref arena, sum, wide, ref wideStatus);
        return Finish<TFormat>(ref arena, root, wideStatus, rounding, ref status);
    }

    /// <summary>
    /// decNumber's decExpOp. The series is evaluated on an operand normalized to below one
    /// and the result raised back by a power of ten, which is what keeps the iteration
    /// count down.
    /// </summary>
    private static WideNumber Exponential(ref WideArena arena, WideNumber rhs, WideContext set,
        ref DecimalStatus status)
    {
        var frame = arena.Mark;
        var result = arena.Take();

        if (rhs.IsNaN)
        {
            result.CopyFrom(rhs);
            arena.Release(frame + 1);
            return result;
        }

        if (rhs.IsInfinity)
        {
            if (rhs.IsNegative)
            {
                result.SetZero();
            }
            else
            {
                result.CopyFrom(rhs);
            }

            arena.Release(frame + 1);
            return result;
        }

        if (rhs.IsZero)
        {
            result.CopyFrom(WideMath.One(ref arena));
            arena.Release(frame + 1);
            return result;
        }

        // For a small enough operand the result is 1 in every digit the context keeps,
        // since e**x is under 1+3x/2 for 0 < x < 0.66. A negative operand needs one more
        // zero, its result being of the form 0.9999999 rather than 1.0000001.
        var tiny = WideMath.FromInt32(ref arena, 4);
        tiny.Exponent = rhs.IsNegative ? -set.Digits - 1 : -set.Digits;

        if (WideMath.Compare(tiny, rhs, true) >= 0)
        {
            status |= DecimalStatus.Inexact | DecimalStatus.Rounded;
            result.CopyFrom(PaddedOne(ref arena, set.Digits));
            arena.Release(frame + 1);
            return result;
        }

        var ignore = DecimalStatus.None;

        var accumulatorContext = WideContext.Default();
        accumulatorContext.MaxExponent = set.MaxExponent;
        accumulatorContext.MinExponent = set.MinExponent;
        accumulatorContext.Clamp = false;

        var x = arena.TakeCopy(rhs);
        var h = rhs.Exponent + rhs.Digits;
        var a = arena.Take();
        int p;

        if (h > 8)
        {
            // Ten to a power this large cannot be computed, but it need not be: the result
            // is certain to overflow or underflow to zero, so hand the raise below a value
            // that is bound to take it there.
            a.CopyFrom(WideMath.FromInt32(ref arena, 2));
            a.Exponent = rhs.IsNegative ? -2 : 0;
            h = 8;
            p = 9;
        }
        else
        {
            // Normalizing further than below one cuts iterations, but the power of ten that
            // undoes it has to stay computable, so the leverage slides with h.
            var maxLever = rhs.Digits > 8 ? 1 : 0;
            var lever = Math.Min(8 - h, maxLever);
            var use = -rhs.Digits - lever;
            h += lever;
            if (h < 0)
            {
                use += h;
                h = 0;
            }

            x.Exponent = use;

            // Hull and Abrham's working precision, widened when the operand carries more
            // digits than the result: all of them can reach the last digit kept.
            p = Math.Max(x.Digits, set.Digits) + h + 2;

            var termContext = WideContext.Default();
            var divisorContext = termContext;
            termContext.Digits = p;
            termContext.MinExponent = WideContext.SmallestExponent;

            // The accumulator holds twice the working precision so that adding each term is
            // exact and round-off cannot pile up across the iterations.
            accumulatorContext.Digits = p * 2;

            var term = arena.TakeCopy(x);
            var divisor = arena.TakeCopy(WideMath.FromInt32(ref arena, 2));
            a.CopyFrom(WideMath.One(ref arena));

            // The loop reuses these three slots rather than taking new ones each pass, so
            // everything above the mark goes back at the end of every iteration.
            var loop = arena.Mark;

            for (;;)
            {
                a.CopyFrom(WideMath.Add(ref arena, a, term, accumulatorContext, ref status));

                var next = WideMath.Multiply(ref arena, term, x, termContext, ref ignore);
                term.CopyFrom(WideMath.Divide(ref arena, next, divisor, termContext, ref ignore));

                // Done when the term has fallen so far below the accumulator that it cannot
                // reach the last digit kept, and the accumulator is full length.
                if (a.Digits + a.Exponent >= term.Digits + term.Exponent + p + 1
                    && a.Digits >= p)
                {
                    arena.Release(loop);
                    break;
                }

                divisor.CopyFrom(WideMath.Add(ref arena, divisor, WideMath.One(ref arena),
                    divisorContext, ref ignore));

                arena.Release(loop);
            }
        }

        if (h > 0)
        {
            // Undo the normalization: a**(10**h), by squaring down the bits of 10**h. Only
            // the multipliers loop is wanted, not the whole of power.
            var n = 1;
            for (var index = 0; index < h; index++)
            {
                n *= 10;
            }

            accumulatorContext.Digits = p + 2;

            var raised = arena.TakeCopy(WideMath.One(ref arena));
            var seenBit = false;
            var loop = arena.Mark;

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
                    raised.CopyFrom(WideMath.Multiply(ref arena, raised, a, accumulatorContext,
                        ref status));
                }

                if (i == 31)
                {
                    break;
                }

                if (seenBit)
                {
                    raised.CopyFrom(WideMath.Multiply(ref arena, raised, raised,
                        accumulatorContext, ref status));
                }

                arena.Release(loop);
            }

            a.CopyFrom(raised);
        }

        // Dirt to the right: the series was cut short, so the result is inexact even where
        // the digits kept do not change.
        var residue = a.IsZero ? 0 : 1;
        result.CopyFrom(WideMath.Round(ref arena, a, residue, set, ref status));
        arena.Release(frame + 1);
        return result;
    }

    /// <summary>
    /// decNumber's decLnOp. Newton's method on a' = a + x*exp(-a) - 1, from a four-digit
    /// estimate off a table, doubling the digits calculated each iteration.
    /// </summary>
    private static WideNumber NaturalLog(ref WideArena arena, WideNumber rhs, WideContext set,
        ref DecimalStatus status)
    {
        var frame = arena.Mark;
        var result = arena.Take();

        if (rhs.IsNaN)
        {
            result.CopyFrom(rhs);
            arena.Release(frame + 1);
            return result;
        }

        if (rhs.IsInfinity)
        {
            if (rhs.IsNegative)
            {
                status |= DecimalStatus.InvalidOperation;
                result.SetZero();
                result.Kind = DecimalKind.QuietNaN;
                result.IsNegative = false;
            }
            else
            {
                result.CopyFrom(rhs);
            }

            arena.Release(frame + 1);
            return result;
        }

        if (rhs.IsZero)
        {
            result.SetZero();
            result.Kind = DecimalKind.Infinity;
            result.IsNegative = true;
            arena.Release(frame + 1);
            return result;
        }

        if (rhs.IsNegative)
        {
            status |= DecimalStatus.InvalidOperation;
            result.SetZero();
            result.Kind = DecimalKind.QuietNaN;
            result.IsNegative = false;
            arena.Release(frame + 1);
            return result;
        }

        var ignore = DecimalStatus.None;

        // ln(10) and ln(2) get asked for often enough -- log10 needs the first every time it
        // runs -- to be worth carrying rather than iterating for.
        if (rhs.Exponent == 0 && set.Digits <= 40)
        {
            var literalContext = set;
            literalContext.Rounding = DecimalRounding.HalfEven;

            if (rhs.Digits == 2 && rhs.ToUInt64() == 10)
            {
                status |= DecimalStatus.Inexact | DecimalStatus.Rounded;
                var constant = WideConstants.NaturalLogOfTen(arena.TakeUnits());
                result.CopyFrom(WideMath.Round(ref arena, constant, 0, literalContext, ref ignore));
                arena.Release(frame + 1);
                return result;
            }

            if (rhs.Digits == 1 && rhs.ToUInt64() == 2)
            {
                status |= DecimalStatus.Inexact | DecimalStatus.Rounded;
                var constant = WideConstants.NaturalLogOfTwo(arena.TakeUnits());
                result.CopyFrom(WideMath.Round(ref arena, constant, 0, literalContext, ref ignore));
                arena.Release(frame + 1);
                return result;
            }
        }

        var p = Math.Max(rhs.Digits, Math.Max(set.Digits, 7)) + 2;

        // Read the operand as a fraction f times a power of ten, so that
        // ln(x) = ln(f) + ln(10)*r, and estimate ln(f) from the table.
        var estimateContext = WideContext.Default();
        var r = rhs.Exponent + rhs.Digits;

        var logTenApproximation = WideMath.FromInt32(ref arena, 2302585);
        logTenApproximation.Exponent = -6;

        var a = arena.TakeCopy(WideMath.Multiply(ref arena, WideMath.FromInt32(ref arena, r),
            logTenApproximation, estimateContext, ref ignore));

        // The leading two digits of the coefficient index the table.
        var leading = (int)(rhs.Digits >= 2
            ? (WideUnits.DigitAt(rhs.Lsu, rhs.Units, rhs.Digits - 1) * 10)
                + WideUnits.DigitAt(rhs.Lsu, rhs.Units, rhs.Digits - 2)
            : WideUnits.DigitAt(rhs.Lsu, rhs.Units, 0) * 10);

        var entry = NaturalLogEstimates[leading - 10];
        var b = arena.Take();
        b.CopyFrom(WideMath.FromInt32(ref arena, entry >> 2));
        b.Exponent = -(entry & 3) - 3;
        b.IsNegative = true;

        a.CopyFrom(WideMath.Add(ref arena, a, b, estimateContext, ref ignore));

        // Four digits of the estimate are good. Near Nmax it comes in low, so the iteration
        // approaches from below and the exp calls below cannot overflow.
        var accumulatorContext = estimateContext;
        accumulatorContext.MaxExponent = set.MaxExponent;
        accumulatorContext.MinExponent = set.MinExponent;
        accumulatorContext.Clamp = false;

        // The adjustment is a catastrophic subtraction, so it is calculated at the sum of
        // the operand's precision and the working precision, over doubled bounds.
        var adjustmentContext = accumulatorContext;
        adjustmentContext.MaxExponent = WideContext.MaxMathExponent * 2;
        adjustmentContext.MinExponent = -WideContext.MaxMathExponent * 2;

        // Nine to start, so the sequence runs 7+2, 16+2, 34+2: the standard widths.
        var pp = 9;
        accumulatorContext.Digits = pp;
        adjustmentContext.Digits = pp + rhs.Digits;

        var loop = arena.Mark;

        for (;;)
        {
            a.Negate();
            var exponential = Exponential(ref arena, a, adjustmentContext, ref ignore);
            a.Negate();

            var scaled = WideMath.Multiply(ref arena, exponential, rhs, adjustmentContext,
                ref ignore);

            b.CopyFrom(WideMath.Subtract(ref arena, scaled, WideMath.One(ref arena),
                adjustmentContext, ref ignore));

            // The iteration ends when the adjustment cannot move the result by half a unit
            // in the last place -- looser than exp needs, since all that follows is the
            // final rounding -- and the accumulator is full length.
            if (b.IsZero || a.Digits + a.Exponent >= b.Digits + b.Exponent + set.Digits + 1)
            {
                if (a.Digits == p)
                {
                    arena.Release(loop);
                    break;
                }

                if (a.IsZero)
                {
                    if (WideMath.Compare(rhs, WideMath.One(ref arena), false) == 0)
                    {
                        a.Exponent = 0;
                    }
                    else
                    {
                        status |= DecimalStatus.Inexact | DecimalStatus.Rounded;
                    }

                    arena.Release(loop);
                    break;
                }

                if (b.IsZero)
                {
                    // Force the padding when the adjustment reached zero early.
                    b.Exponent = a.Exponent - p;
                }
            }

            a.CopyFrom(WideMath.Add(ref arena, a, b, accumulatorContext, ref ignore));
            arena.Release(loop);

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
        result.CopyFrom(WideMath.Round(ref arena, a, residue, set, ref status));
        arena.Release(frame + 1);
        return result;
    }

    /// <summary>decNumber's decNumberPower, less the NaN handling its caller does.</summary>
    private static WideNumber PowerCore(ref WideArena arena, WideNumber lhs, WideNumber rhs,
        WideContext set, ref DecimalStatus status)
    {
        var frame = arena.Mark;
        var result = arena.Take();

        if (rhs.IsInfinity)
        {
            if (lhs.IsNegative && !lhs.IsZero)
            {
                status |= DecimalStatus.InvalidOperation;
                SetQuietNaN(ref result);
                arena.Release(frame + 1);
                return result;
            }

            var comparison = WideMath.Compare(lhs, WideMath.One(ref arena), false);
            if (comparison == 0)
            {
                // One to an infinite power is deemed inexact, so it comes back padded.
                status |= DecimalStatus.Inexact | DecimalStatus.Rounded;
                result.CopyFrom(PaddedOne(ref arena, set.Digits));
                arena.Release(frame + 1);
                return result;
            }

            var infinite = comparison < 0 ? rhs.IsNegative : !rhs.IsNegative;
            result.SetZero();
            if (infinite)
            {
                result.Kind = DecimalKind.Infinity;
            }

            arena.Release(frame + 1);
            return result;
        }

        var integerExponent = rhs.IsIntegerValued;
        var oddExponent = rhs.IsOddIntegerValued;
        var useInteger = rhs.TryGetInt32(out var n);
        var negative = lhs.IsNegative && oddExponent;

        if (lhs.IsInfinity)
        {
            if (rhs.IsZero)
            {
                result.CopyFrom(WideMath.One(ref arena));
                arena.Release(frame + 1);
                return result;
            }

            if (!integerExponent && lhs.IsNegative)
            {
                status |= DecimalStatus.InvalidOperation;
                SetQuietNaN(ref result);
                arena.Release(frame + 1);
                return result;
            }

            result.SetZero();
            result.IsNegative = negative;
            if (!rhs.IsNegative)
            {
                result.Kind = DecimalKind.Infinity;
            }

            arena.Release(frame + 1);
            return result;
        }

        if (lhs.IsZero)
        {
            if (rhs.IsZero)
            {
                status |= DecimalStatus.InvalidOperation;
                SetQuietNaN(ref result);
                arena.Release(frame + 1);
                return result;
            }

            result.SetZero();
            result.IsNegative = negative;
            if (rhs.IsNegative)
            {
                result.Kind = DecimalKind.Infinity;
            }

            arena.Release(frame + 1);
            return result;
        }

        if (!useInteger)
        {
            if (lhs.IsNegative)
            {
                status |= DecimalStatus.InvalidOperation;
                SetQuietNaN(ref result);
                arena.Release(frame + 1);
                return result;
            }

            var aset = WideContext.Default();
            aset.MaxExponent = WideContext.MaxMathExponent;
            aset.MinExponent = -WideContext.MaxMathExponent;
            aset.Clamp = false;

            // Enough to hold the whole information content of the left operand, exponent
            // included, plus four; six covers any exponent. The spare digits cost ln almost
            // nothing and cut the cases that land more than half a unit out.
            aset.Digits = Math.Max(lhs.Digits, set.Digits) + 6 + 4;

            var accumulator = arena.TakeCopy(NaturalLog(ref arena, lhs, aset, ref status));
            if (accumulator.IsZero)
            {
                // The left operand was one, which would otherwise reduce to an integer 1.
                accumulator.CopyFrom(WideMath.One(ref arena));
                if (!integerExponent)
                {
                    accumulator.CopyFrom(PaddedOne(ref arena, set.Digits));
                    status |= DecimalStatus.Inexact | DecimalStatus.Rounded;
                }
            }
            else
            {
                accumulator.CopyFrom(WideMath.Multiply(ref arena, accumulator, rhs, aset,
                    ref status));

                accumulator.CopyFrom(Exponential(ref arena, accumulator, aset, ref status));
            }

            result.CopyFrom(WideMath.Round(ref arena, accumulator, 0, set, ref status));
            arena.Release(frame + 1);
            return result;
        }

        if (rhs.IsZero)
        {
            result.CopyFrom(WideMath.One(ref arena));
            arena.Release(frame + 1);
            return result;
        }

        if (n < 0)
        {
            n = -n;
        }

        var integerSet = set;
        integerSet.Rounding = DecimalRounding.HalfEven;
        integerSet.Digits = set.Digits + (rhs.Digits + rhs.Exponent) + 2;

        var dac = arena.TakeCopy(WideMath.One(ref arena));
        var multiplicand = arena.TakeCopy(lhs);

        if (rhs.IsNegative)
        {
            // Invert the operand now rather than the result later, which keeps the rounding
            // to one place.
            multiplicand.CopyFrom(WideMath.Divide(ref arena, WideMath.One(ref arena), lhs,
                integerSet, ref status));
        }

        var seen = false;
        var loop = arena.Mark;

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
                dac.CopyFrom(WideMath.Multiply(ref arena, dac, multiplicand, integerSet,
                    ref status));
            }

            if (i == 31)
            {
                break;
            }

            if (seen)
            {
                dac.CopyFrom(WideMath.Multiply(ref arena, dac, dac, integerSet, ref status));
            }

            arena.Release(loop);
        }

        arena.Release(loop);

        if ((status & (DecimalStatus.Overflow | DecimalStatus.Underflow)) != 0)
        {
            if (!dac.IsFinite)
            {
                result.CopyFrom(dac);
                result.IsNegative = negative;
                arena.Release(frame + 1);
                return result;
            }

            // Round the subnormal to the requested length rather than the working one.
            result.CopyFrom(dac);
            result.IsNegative = negative;
            WideRounding.Finalize(ref result, 0, set, ref status);
            arena.Release(frame + 1);
            return result;
        }

        // The sign came out of the multiplications themselves: a negative base reaches an
        // odd power still negative.
        result.CopyFrom(WideMath.Round(ref arena, dac, 0, set, ref status));
        arena.Release(frame + 1);
        return result;
    }

    /// <summary>
    /// decNumber's decNumberLog10, at the engine level so that the reductions built on it
    /// keep the exact power-of-ten case.
    /// </summary>
    private static WideNumber BaseTenLog(ref WideArena arena, WideNumber rhs, WideContext set,
        ref DecimalStatus status)
    {
        var frame = arena.Mark;
        var result = arena.Take();

        if (TryLogarithmOfSpecial(ref arena, rhs, ref status, out var special))
        {
            result.CopyFrom(special);
            arena.Release(frame + 1);
            return result;
        }

        // A coefficient of one followed by zeros makes the value a power of ten, and then
        // the logarithm is the adjusted exponent, exactly.
        if (WideUnits.IsPowerOfTen(rhs.Lsu, rhs.Units, rhs.Digits))
        {
            var power = WideMath.FromInt32(ref arena, rhs.Exponent + rhs.Digits - 1);
            result.CopyFrom(WideMath.Round(ref arena, power, 0, set, ref status));
            arena.Release(frame + 1);
            return result;
        }

        result.CopyFrom(DividedLogarithm(ref arena, rhs, WideMath.FromInt32(ref arena, 10),
            set, ref status));

        arena.Release(frame + 1);
        return result;
    }

    /// <summary>The same shape for base two, whose exact case is a power of two.</summary>
    private static WideNumber BaseTwoLog(ref WideArena arena, WideNumber rhs, WideContext set,
        ref DecimalStatus status)
    {
        var frame = arena.Mark;
        var result = arena.Take();

        if (TryLogarithmOfSpecial(ref arena, rhs, ref status, out var special))
        {
            result.CopyFrom(special);
            arena.Release(frame + 1);
            return result;
        }

        if (WideConstants.TryPowerOfTwoExponent(rhs, arena.TakeUnits(), out var exponent))
        {
            result.CopyFrom(WideMath.Round(ref arena, WideMath.FromInt32(ref arena, exponent),
                0, set, ref status));

            arena.Release(frame + 1);
            return result;
        }

        result.CopyFrom(DividedLogarithm(ref arena, rhs, WideMath.FromInt32(ref arena, 2),
            set, ref status));

        arena.Release(frame + 1);
        return result;
    }

    /// <summary>
    /// ln(x)/ln(base), at the precisions decNumber uses for log10: the numerator carries the
    /// whole information content of the operand, the divisor three guard digits, and the
    /// division alone is done at the requested precision.
    /// </summary>
    private static WideNumber DividedLogarithm(ref WideArena arena, WideNumber rhs,
        WideNumber wholeBase, WideContext set, ref DecimalStatus status)
    {
        var frame = arena.Mark;
        var result = arena.Take();

        var aset = WideContext.Default();
        aset.MaxExponent = WideContext.MaxMathExponent;
        aset.MinExponent = -WideContext.MaxMathExponent;
        aset.Clamp = false;

        // Six digits covers any exponent, and letting all of the operand participate costs
        // ln almost nothing: it doubles its precision each iteration, so a few extra digits
        // rarely buys another one.
        aset.Digits = Math.Max(rhs.Digits + 6, set.Digits) + 3;
        var logarithm = NaturalLog(ref arena, rhs, aset, ref status);

        if (!logarithm.IsFinite || logarithm.IsZero)
        {
            result.CopyFrom(logarithm);
            arena.Release(frame + 1);
            return result;
        }

        // The divisor is always inexact and always rounded, and saying so would tell the
        // caller nothing; anything else it raises, such as an unusable base, does.
        var baseStatus = DecimalStatus.None;
        aset.Digits = set.Digits + 3;
        var divisor = NaturalLog(ref arena, wholeBase, aset, ref baseStatus);
        status |= baseStatus & ~(DecimalStatus.Inexact | DecimalStatus.Rounded);

        aset.Digits = set.Digits;
        result.CopyFrom(WideMath.Divide(ref arena, logarithm, divisor, aset, ref status));
        arena.Release(frame + 1);
        return result;
    }

    /// <summary>
    /// The cases every logarithm shares: a NaN passes through, a negative is invalid, zero
    /// gives -Infinity, and +Infinity gives itself.
    /// </summary>
    private static bool TryLogarithmOfSpecial(ref WideArena arena, WideNumber rhs,
        ref DecimalStatus status, out WideNumber result)
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
                result = WideMath.QuietNaN(ref arena);
            }

            return true;
        }

        if (rhs.IsZero)
        {
            result = WideMath.Infinity(ref arena, true);
            return true;
        }

        if (rhs.IsNegative)
        {
            status |= DecimalStatus.InvalidOperation;
            result = WideMath.QuietNaN(ref arena);
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

        var scratch = stackalloc ulong[WideArena.TotalWords];
        var arena = new WideArena((uint*)scratch);

        var wide = GuardedContext<TFormat>(3);
        var wideStatus = DecimalStatus.None;
        var exponent = WideNumber.FromUnpacked(value, arena.TakeUnits());

        var result = PowerCore(ref arena, WideMath.FromInt32(ref arena, wholeBase), exponent,
            wide, ref wideStatus);

        return Finish<TFormat>(ref arena, result, wideStatus, rounding, ref status);
    }

    private static UnpackedDecimal<UInt128> RaiseWholeBaseMinusOne<TFormat>(int wholeBase,
        UnpackedDecimal<UInt128> value, DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (value.IsNaN)
        {
            return DecimalArithmetic.PropagateNaN(value, ref status);
        }

        var scratch = stackalloc ulong[WideArena.TotalWords];
        var arena = new WideArena((uint*)scratch);
        var x = WideNumber.FromUnpacked(value, arena.TakeUnits());

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
            var narrow = GuardedContext<TFormat>(3);
            var narrowStatus = DecimalStatus.Inexact;

            var logarithmOfBase = wholeBase == 10
                ? WideConstants.NaturalLogOfTen(arena.TakeUnits())
                : WideConstants.NaturalLogOfTwo(arena.TakeUnits());

            var scaled = WideMath.Multiply(ref arena, x, logarithmOfBase, narrow, ref narrowStatus);
            return Finish<TFormat>(ref arena, scaled, narrowStatus, rounding, ref status);
        }

        var wide = GuardedContext<TFormat>(4 + lost);
        var wideStatus = DecimalStatus.None;

        var result = PowerCore(ref arena, WideMath.FromInt32(ref arena, wholeBase), x, wide,
            ref wideStatus);

        result = WideMath.Subtract(ref arena, result, WideMath.One(ref arena), wide, ref wideStatus);
        return Finish<TFormat>(ref arena, result, wideStatus, rounding, ref status);
    }

    private static UnpackedDecimal<UInt128> LogPlusOneInWholeBase<TFormat>(int wholeBase,
        UnpackedDecimal<UInt128> value, DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        if (value.IsNaN)
        {
            return DecimalArithmetic.PropagateNaN(value, ref status);
        }

        var scratch = stackalloc ulong[WideArena.TotalWords];
        var arena = new WideArena((uint*)scratch);
        var x = WideNumber.FromUnpacked(value, arena.TakeUnits());

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

        var wide = GuardedContext<TFormat>(3);
        var wideStatus = DecimalStatus.None;
        var lost = LeadingDigitsLost(x);

        if (lost > TFormat.Precision + 3)
        {
            // The answer is x/ln(base) to well past the last digit kept.
            var divisor = NaturalLog(ref arena, WideMath.FromInt32(ref arena, wholeBase), wide,
                ref wideStatus);

            var scaled = WideMath.Divide(ref arena, x, divisor, wide, ref wideStatus);
            return Finish<TFormat>(ref arena, scaled, wideStatus, rounding, ref status);
        }

        var sum = OnePlus<TFormat>(ref arena, x, lost);
        var result = wholeBase == 10
            ? BaseTenLog(ref arena, sum, wide, ref wideStatus)
            : BaseTwoLog(ref arena, sum, wide, ref wideStatus);

        return Finish<TFormat>(ref arena, result, wideStatus, rounding, ref status);
    }

    /// <summary>
    /// One plus the value, exactly. The context is widened by the digits the operand sits
    /// below the point so that none of it is lost in the sum.
    /// </summary>
    private static WideNumber OnePlus<TFormat>(ref WideArena arena, WideNumber value, int lost)
        where TFormat : IDecimalFormat
    {
        var ignore = DecimalStatus.None;
        return WideMath.Add(ref arena, WideMath.One(ref arena), value,
            GuardedContext<TFormat>(4 + lost), ref ignore);
    }

    /// <summary>
    /// How many digits a subtraction from one cancels: the leading digit of the operand plus
    /// the zeros between it and the point.
    /// </summary>
    private static int LeadingDigitsLost(WideNumber value)
    {
        var adjusted = value.Exponent + value.Digits - 1;
        return adjusted < 0 ? -adjusted : 0;
    }

    /// <summary>
    /// The context the reductions are evaluated in: guard digits above the format, and no
    /// exponent limits, so that only the final rounding into the format can overflow.
    /// </summary>
    private static WideContext GuardedContext<TFormat>(int guardDigits)
        where TFormat : IDecimalFormat
    {
        var context = WideContext.ForFormat<TFormat>(DecimalRounding.HalfEven);
        context.Digits = TFormat.Precision + guardDigits;
        context.MaxExponent = WideContext.MaxMathExponent;
        context.MinExponent = -WideContext.MaxMathExponent;
        context.Clamp = false;
        return context;
    }

    /// <summary>
    /// Rounds a value evaluated at guard digits into the format, once. An evaluation that
    /// was inexact hands the rounding a residue, so that a result which happens to end in
    /// zeros is still reported as the approximation it is.
    /// </summary>
    private static UnpackedDecimal<UInt128> Finish<TFormat>(ref WideArena arena, WideNumber value,
        DecimalStatus wideStatus, DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        status |= wideStatus & (DecimalStatus.InvalidOperation | DecimalStatus.DivisionByZero);

        var residue = value.IsFinite && !value.IsZero
            && (wideStatus & DecimalStatus.Inexact) != 0 ? 1 : 0;

        var set = WideContext.ForFormat<TFormat>(rounding);
        return WideMath.Round(ref arena, value, residue, set, ref status).ToUnpacked();
    }

    private static UnpackedDecimal<UInt128> RoundWithResidue<TFormat>(ref WideArena arena,
        WideNumber value, int residue, DecimalRounding rounding, ref DecimalStatus status)
        where TFormat : IDecimalFormat
    {
        var set = WideContext.ForFormat<TFormat>(rounding);
        return WideMath.Round(ref arena, value, residue, set, ref status).ToUnpacked();
    }

    /// <summary>Whether a value is exactly a given small whole number.</summary>
    private static bool IsWholeNumber(WideNumber value, int whole)
    {
        return value.IsFinite && !value.IsNegative && value.TryGetInt32(out var integer)
            && integer == whole;
    }

    /// <summary>
    /// Whether a rounded root is the exact one, by raising it back to the degree. An exact
    /// root is then shortened toward the operand's exponent divided by the degree, which is
    /// what the square root does with its own.
    /// </summary>
    private static bool TryExactRoot(ref WideArena arena, UnpackedDecimal<UInt128> candidate,
        WideNumber value, int degree, out UnpackedDecimal<UInt128> result)
    {
        result = candidate;

        // Past this the raise costs more than the case is worth, and an exact root of such a
        // degree needs an operand that is a perfect power of it.
        if (Math.Abs(degree) > 40 || candidate.Kind != DecimalKind.Finite
            || candidate.Coefficient == UInt128.Zero)
        {
            return false;
        }

        var frame = arena.Mark;
        var exact = WideContext.Default();
        exact.Digits = WideArena.SlotUnits * WideNumber.DigitsPerUnit / 4;
        exact.MaxExponent = WideContext.MaxMathExponent;
        exact.MinExponent = -WideContext.MaxMathExponent;
        exact.Clamp = false;

        var ignore = DecimalStatus.None;
        var magnitude = Math.Abs(degree);

        var root = WideNumber.FromUnpacked(candidate, arena.TakeUnits());
        root.IsNegative = false;
        root.Exponent = 0;

        var raised = arena.TakeCopy(WideMath.One(ref arena));
        var loop = arena.Mark;

        for (var index = 0; index < magnitude; index++)
        {
            raised.CopyFrom(WideMath.Multiply(ref arena, raised, root, exact, ref ignore));
            arena.Release(loop);

            if (raised.Digits > exact.Digits - 40)
            {
                // The raise has outgrown what an exact comparison can carry, which means
                // the operand cannot be a perfect power of this degree anyway.
                arena.Release(frame);
                return false;
            }
        }

        var raisedExponent = (long)candidate.Exponent * magnitude;

        // A negative degree gives the reciprocal, so the test becomes whether the raised
        // root times the operand is one.
        var comparandExponent = degree > 0 ? (long)value.Exponent : 0L;
        var comparand = arena.Take();

        if (degree > 0)
        {
            comparand.CopyFrom(value);
            comparand.IsNegative = false;
            comparand.Exponent = 0;
        }
        else
        {
            var magnitudeOfValue = arena.TakeCopy(value);
            magnitudeOfValue.IsNegative = false;
            magnitudeOfValue.Exponent = 0;

            raised.CopyFrom(WideMath.Multiply(ref arena, raised, magnitudeOfValue, exact,
                ref ignore));

            raisedExponent += value.Exponent;
            comparand.CopyFrom(WideMath.One(ref arena));
        }

        var common = Math.Min(raisedExponent, comparandExponent);
        if (raisedExponent - common > 20000 || comparandExponent - common > 20000)
        {
            arena.Release(frame);
            return false;
        }

        raised.Units = WideUnits.ShiftUp(raised.Lsu, raised.Units, (int)(raisedExponent - common));
        raised.CountDigits();

        comparand.Units = WideUnits.ShiftUp(comparand.Lsu, comparand.Units,
            (int)(comparandExponent - common));

        comparand.CountDigits();

        if (WideUnits.Compare(raised.Lsu, raised.Units, comparand.Lsu, comparand.Units) != 0)
        {
            arena.Release(frame);
            return false;
        }

        var ideal = FloorDivide(value.Exponent, degree);
        var shortened = WideNumber.FromUnpacked(candidate, arena.TakeUnits());

        while (shortened.Exponent < ideal
            && WideUnits.DigitAt(shortened.Lsu, shortened.Units, 0) == 0
            && !shortened.IsZero)
        {
            shortened.Units = WideUnits.ShiftDown(shortened.Lsu, shortened.Units, 1);
            shortened.Exponent++;
            shortened.CountDigits();
        }

        result = shortened.ToUnpacked();
        arena.Release(frame);
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
    /// One written out to the full width of the context, which is how the specification asks
    /// an inexact one to be presented.
    /// </summary>
    private static WideNumber PaddedOne(ref WideArena arena, int digits)
    {
        var shift = digits - 1;
        var result = arena.Take();
        result.Units = WideUnits.SetPowerOfTen(result.Lsu, shift);
        result.Exponent = -shift;
        result.CountDigits();
        return result;
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

    private static void SetQuietNaN(ref WideNumber value)
    {
        value.SetZero();
        value.Kind = DecimalKind.QuietNaN;
        value.IsNegative = false;
    }
}
