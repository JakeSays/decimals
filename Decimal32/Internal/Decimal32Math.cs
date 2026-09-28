// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// The elementary functions beyond the square root.
/// </summary>
/// <remarks>
/// <para>
/// Exp, ln, log10, and power cannot work on the format's own coefficient: exp evaluates its
/// series into an accumulator twice the working precision, ln calls exp from inside a
/// Newton iteration carried out wider still, and power calls ln. So they work on
/// <see cref="Decimal32WideNumber"/> and are ports of decNumber's decExpOp, decLnOp,
/// decNumberLog10, and decNumberPower, with the same intermediate precisions -- which is
/// what makes their results the reference's, digit for digit.
/// </para>
/// <para>
/// decNumber declares its temporaries as locals sized from the widest case. Here an entry
/// point stack-allocates one block and the functions draw slots from it through
/// <see cref="Decimal32WideArena"/>, which is the same arrangement with the sizes in one place.
/// </para>
/// </remarks>
internal static unsafe class Decimal32Math
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
    /// e raised to the value. Finite results are always full precision and inexact, except
    /// for the two that are not approximations: exp(0) is 1 and exp(-Infinity) is 0.
    /// </summary>
    public static uint Exp(uint value, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsNaN(value))
        {
            return Decimal32Arithmetic.PropagateNaN(value, ref status);
        }

        var scratch = stackalloc ulong[Decimal32WideArena.TotalWords];
        var arena = new Decimal32WideArena((uint*)scratch);
        var context = Decimal32WideContext.ForFormat(rounding);
        var operand = Decimal32WideNumber.FromBits(value, arena.TakeUnits());

        return Exponential(ref arena, operand, context, ref status).ToBits();
    }

    /// <summary>
    /// The natural logarithm. Zero gives -Infinity, a negative is invalid, and ln(1) is an
    /// exact zero.
    /// </summary>
    public static uint Log(uint value, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsNaN(value))
        {
            return Decimal32Arithmetic.PropagateNaN(value, ref status);
        }

        var scratch = stackalloc ulong[Decimal32WideArena.TotalWords];
        var arena = new Decimal32WideArena((uint*)scratch);
        var context = Decimal32WideContext.ForFormat(rounding);
        var operand = Decimal32WideNumber.FromBits(value, arena.TakeUnits());

        return NaturalLog(ref arena, operand, context, ref status).ToBits();
    }

    /// <summary>
    /// The base-ten logarithm, as ln(x)/ln(10). A power of ten gives its exponent exactly.
    /// </summary>
    public static uint Log10(uint value, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsNaN(value))
        {
            return Decimal32Arithmetic.PropagateNaN(value, ref status);
        }

        var scratch = stackalloc ulong[Decimal32WideArena.TotalWords];
        var arena = new Decimal32WideArena((uint*)scratch);
        var set = Decimal32WideContext.ForFormat(rounding);
        var operand = Decimal32WideNumber.FromBits(value, arena.TakeUnits());

        return Settle(ref arena, BaseTenLog(ref arena, operand, set, ref status), set, ref status);
    }

    /// <summary>
    /// Brings a logarithm's result into the format's exponent range. The divisions that
    /// produce one run over the wider mathematical range, and a degenerate base -- zero, or
    /// an infinity -- leaves a zero at an exponent no encoding can carry.
    /// </summary>
    private static uint Settle(ref Decimal32WideArena arena, Decimal32WideNumber value, Decimal32WideContext set,
        ref Decimal32Status status)
    {
        return Decimal32WideMath.Round(ref arena, value, 0, set, ref status).ToBits();
    }

    /// <summary>
    /// The left operand raised to the right. An integer exponent that fits is applied by
    /// repeated squaring, which can be exact; anything else goes through exp(ln(x)*y).
    /// </summary>
    public static uint Power(uint left, uint right, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsNaN(left) || Decimal32Encoding.IsNaN(right))
        {
            return Decimal32Arithmetic.PropagateNaN(left, right, ref status);
        }

        var scratch = stackalloc ulong[Decimal32WideArena.TotalWords];
        var arena = new Decimal32WideArena((uint*)scratch);
        var set = Decimal32WideContext.ForFormat(rounding);
        var baseValue = Decimal32WideNumber.FromBits(left, arena.TakeUnits());
        var exponent = Decimal32WideNumber.FromBits(right, arena.TakeUnits());

        return PowerCore(ref arena, baseValue, exponent, set, ref status).ToBits();
    }

    /// <summary>Two raised to the value.</summary>
    public static uint Exp2(uint value, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        return RaiseWholeBase(2, value, rounding, ref status);
    }

    /// <summary>Ten raised to the value.</summary>
    public static uint Exp10(uint value, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        return RaiseWholeBase(10, value, rounding, ref status);
    }

    /// <summary>
    /// e raised to the value, less one. Near zero the result is about the operand itself,
    /// so the subtraction cancels the leading digits away; the evaluation is widened by as
    /// many digits as the cancellation costs.
    /// </summary>
    public static uint ExpMinusOne(uint value, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsNaN(value))
        {
            return Decimal32Arithmetic.PropagateNaN(value, ref status);
        }

        var scratch = stackalloc ulong[Decimal32WideArena.TotalWords];
        var arena = new Decimal32WideArena((uint*)scratch);
        var x = Decimal32WideNumber.FromBits(value, arena.TakeUnits());

        if (x.Kind == Decimal32Kind.Infinity)
        {
            return x.IsNegative ? NegativeOne() : Decimal32Encoding.Infinity(false);
        }

        if (x.IsZero)
        {
            return Decimal32Encoding.Canonical(value);
        }

        var lost = LeadingDigitsLost(x);
        if (lost > Decimal32Encoding.Precision + 3)
        {
            // The x**2/2 term lies entirely below the last digit kept, so the result is the
            // operand with a residue away from zero, that term carrying the operand's sign.
            return RoundWithResidue(ref arena, x, x.IsNegative ? -1 : 1, rounding, ref status);
        }

        var wide = GuardedContext(3 + lost);
        var wideStatus = Decimal32Status.None;
        var result = Exponential(ref arena, x, wide, ref wideStatus);
        result = Decimal32WideMath.Subtract(ref arena, result, Decimal32WideMath.One(ref arena), wide, ref wideStatus);

        return Finish(ref arena, result, wideStatus, rounding, ref status);
    }

    /// <summary>Two raised to the value, less one.</summary>
    public static uint Exp2MinusOne(uint value, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        return RaiseWholeBaseMinusOne(2, value, rounding, ref status);
    }

    /// <summary>Ten raised to the value, less one.</summary>
    public static uint Exp10MinusOne(uint value, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        return RaiseWholeBaseMinusOne(10, value, rounding, ref status);
    }

    /// <summary>
    /// The base-two logarithm. A power of two gives its exponent exactly, the way a power
    /// of ten does for <see cref="Log10"/>.
    /// </summary>
    public static uint Log2(uint value, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsNaN(value))
        {
            return Decimal32Arithmetic.PropagateNaN(value, ref status);
        }

        var scratch = stackalloc ulong[Decimal32WideArena.TotalWords];
        var arena = new Decimal32WideArena((uint*)scratch);
        var set = Decimal32WideContext.ForFormat(rounding);
        var operand = Decimal32WideNumber.FromBits(value, arena.TakeUnits());

        return Settle(ref arena, BaseTwoLog(ref arena, operand, set, ref status), set, ref status);
    }

    /// <summary>
    /// The logarithm in an arbitrary base. Two and ten go through their own routines, which
    /// have exact cases worth keeping.
    /// </summary>
    public static uint LogInBase(uint value, uint newBase, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsNaN(value) || Decimal32Encoding.IsNaN(newBase))
        {
            return Decimal32Arithmetic.PropagateNaN(value, newBase, ref status);
        }

        var scratch = stackalloc ulong[Decimal32WideArena.TotalWords];
        var arena = new Decimal32WideArena((uint*)scratch);
        var set = Decimal32WideContext.ForFormat(rounding);
        var x = Decimal32WideNumber.FromBits(value, arena.TakeUnits());
        var b = Decimal32WideNumber.FromBits(newBase, arena.TakeUnits());

        if (IsWholeNumber(b, 10))
        {
            return Settle(ref arena, BaseTenLog(ref arena, x, set, ref status), set, ref status);
        }

        if (IsWholeNumber(b, 2))
        {
            return Settle(ref arena, BaseTwoLog(ref arena, x, set, ref status), set, ref status);
        }

        if (TryLogarithmOfSpecial(ref arena, x, ref status, out var special))
        {
            return special.ToBits();
        }

        return Settle(ref arena, DividedLogarithm(ref arena, x, b, set, ref status), set, ref status);
    }

    /// <summary>
    /// The natural logarithm of one plus the value. For a small operand the sum would round
    /// straight back to one, so it is formed at enough digits to keep the operand whole.
    /// </summary>
    public static uint LogPlusOne(uint value, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsNaN(value))
        {
            return Decimal32Arithmetic.PropagateNaN(value, ref status);
        }

        var scratch = stackalloc ulong[Decimal32WideArena.TotalWords];
        var arena = new Decimal32WideArena((uint*)scratch);
        var x = Decimal32WideNumber.FromBits(value, arena.TakeUnits());

        if (x.Kind == Decimal32Kind.Infinity)
        {
            if (x.IsNegative)
            {
                return Decimal32Arithmetic.Invalid(ref status);
            }

            return Decimal32Encoding.Infinity(false);
        }

        if (x.IsZero)
        {
            return Decimal32Encoding.Canonical(value);
        }

        var lost = LeadingDigitsLost(x);
        if (lost > Decimal32Encoding.Precision + 3)
        {
            // ln(1+x) is x less x**2/2, and that term lies below the last digit kept, so
            // the residue runs toward zero.
            return RoundWithResidue(ref arena, x, x.IsNegative ? 1 : -1, rounding, ref status);
        }

        var wideStatus = Decimal32Status.None;
        var sum = OnePlus(ref arena, x, lost);
        var result = NaturalLog(ref arena, sum, GuardedContext(3), ref wideStatus);

        return Finish(ref arena, result, wideStatus, rounding, ref status);
    }

    /// <summary>The base-two logarithm of one plus the value.</summary>
    public static uint Log2PlusOne(uint value, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        return LogPlusOneInWholeBase(2, value, rounding, ref status);
    }

    /// <summary>The base-ten logarithm of one plus the value.</summary>
    public static uint Log10PlusOne(uint value, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        return LogPlusOneInWholeBase(10, value, rounding, ref status);
    }

    /// <summary>The cube root.</summary>
    public static uint Cbrt(uint value, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        return RootN(value, 3, rounding, ref status);
    }

    /// <summary>
    /// The degree-th root, as exp(ln(x)/degree). A root that comes out exact is returned as
    /// such: the rounded root is raised back to the degree and compared with the operand.
    /// </summary>
    public static uint RootN(uint value, int degree, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsNaN(value))
        {
            return Decimal32Arithmetic.PropagateNaN(value, ref status);
        }

        if (degree == 0)
        {
            return Decimal32Arithmetic.Invalid(ref status);
        }

        var scratch = stackalloc ulong[Decimal32WideArena.TotalWords];
        var arena = new Decimal32WideArena((uint*)scratch);
        var x = Decimal32WideNumber.FromBits(value, arena.TakeUnits());
        var oddDegree = (degree & 1) != 0;

        if (x.IsZero)
        {
            // A zero keeps its sign through any root; a negative degree sends it to an
            // infinity of that same sign.
            return degree < 0
                ? Decimal32Encoding.Infinity(x.IsNegative)
                : Decimal32Encoding.Canonical(value);
        }

        if (x.IsNegative && !oddDegree)
        {
            // An even root of a negative has no real value.
            return Decimal32Arithmetic.Invalid(ref status);
        }

        var negative = x.IsNegative;

        if (x.Kind == Decimal32Kind.Infinity)
        {
            return degree > 0 ? Decimal32Encoding.Infinity(negative) : Decimal32Encoding.Zero(negative, 0);
        }

        if (degree == 1)
        {
            return Decimal32Encoding.Canonical(value);
        }

        if (degree == 2)
        {
            return Decimal32SquareRoot.SquareRoot(value, rounding, ref status);
        }

        var wide = GuardedContext(5);
        var wideStatus = Decimal32Status.None;

        var magnitudeOfX = arena.TakeCopy(x);
        magnitudeOfX.IsNegative = false;

        var logarithm = NaturalLog(ref arena, magnitudeOfX, wide, ref wideStatus);
        var scaled = Decimal32WideMath.Divide(ref arena, logarithm, Decimal32WideMath.FromInt32(ref arena, degree),
            wide, ref wideStatus);

        var root = Exponential(ref arena, scaled, wide, ref wideStatus);
        root.IsNegative = negative;

        var candidateStatus = Decimal32Status.None;
        var candidate = Finish(ref arena, root, wideStatus, rounding, ref candidateStatus);

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
    public static uint Hypot(uint left, uint right, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        // An infinite operand settles the result even when the other one is a NaN.
        if (Decimal32Encoding.IsInfinity(left) || Decimal32Encoding.IsInfinity(right))
        {
            return Decimal32Encoding.Infinity(false);
        }

        if (Decimal32Encoding.IsNaN(left) || Decimal32Encoding.IsNaN(right))
        {
            return Decimal32Arithmetic.PropagateNaN(left, right, ref status);
        }

        var scratch = stackalloc ulong[Decimal32WideArena.TotalWords];
        var arena = new Decimal32WideArena((uint*)scratch);

        var wide = GuardedContext(5);
        var squares = wide;
        squares.Digits = (2 * Decimal32Encoding.Precision) + 8;

        var wideStatus = Decimal32Status.None;
        var x = Decimal32WideNumber.FromBits(left, arena.TakeUnits());
        var y = Decimal32WideNumber.FromBits(right, arena.TakeUnits());
        x.IsNegative = false;
        y.IsNegative = false;

        var sum = Decimal32WideMath.Add(ref arena,
            Decimal32WideMath.Multiply(ref arena, x, x, squares, ref wideStatus),
            Decimal32WideMath.Multiply(ref arena, y, y, squares, ref wideStatus),
            squares, ref wideStatus);

        var root = Decimal32WideMath.SquareRoot(ref arena, sum, wide, ref wideStatus);
        return Finish(ref arena, root, wideStatus, rounding, ref status);
    }

    /// <summary>
    /// decNumber's decExpOp. The series is evaluated on an operand normalized to below one
    /// and the result raised back by a power of ten, which is what keeps the iteration
    /// count down.
    /// </summary>
    private static Decimal32WideNumber Exponential(ref Decimal32WideArena arena, Decimal32WideNumber rhs,
        Decimal32WideContext set, ref Decimal32Status status)
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
            result.CopyFrom(Decimal32WideMath.One(ref arena));
            arena.Release(frame + 1);
            return result;
        }

        // For a small enough operand the result is 1 in every digit the context keeps,
        // since e**x is under 1+3x/2 for 0 < x < 0.66. A negative operand needs one more
        // zero, its result being of the form 0.9999999 rather than 1.0000001.
        var tiny = Decimal32WideMath.FromInt32(ref arena, 4);
        tiny.Exponent = rhs.IsNegative ? -set.Digits - 1 : -set.Digits;

        if (Decimal32WideMath.Compare(tiny, rhs, true) >= 0)
        {
            status |= Decimal32Status.Inexact | Decimal32Status.Rounded;
            result.CopyFrom(PaddedOne(ref arena, set.Digits));
            arena.Release(frame + 1);
            return result;
        }

        var ignore = Decimal32Status.None;

        var accumulatorContext = Decimal32WideContext.Default();
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
            a.CopyFrom(Decimal32WideMath.FromInt32(ref arena, 2));
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

            var termContext = Decimal32WideContext.Default();
            var divisorContext = termContext;
            termContext.Digits = p;
            termContext.MinExponent = Decimal32WideContext.SmallestExponent;

            // The accumulator holds twice the working precision so that adding each term is
            // exact and round-off cannot pile up across the iterations.
            accumulatorContext.Digits = p * 2;

            var term = arena.TakeCopy(x);
            var divisor = arena.TakeCopy(Decimal32WideMath.FromInt32(ref arena, 2));
            a.CopyFrom(Decimal32WideMath.One(ref arena));

            // The loop reuses these three slots rather than taking new ones each pass, so
            // everything above the mark goes back at the end of every iteration.
            var loop = arena.Mark;

            for (;;)
            {
                a.CopyFrom(Decimal32WideMath.Add(ref arena, a, term, accumulatorContext, ref status));

                var next = Decimal32WideMath.Multiply(ref arena, term, x, termContext, ref ignore);
                term.CopyFrom(Decimal32WideMath.Divide(ref arena, next, divisor, termContext, ref ignore));

                // Done when the term has fallen so far below the accumulator that it cannot
                // reach the last digit kept, and the accumulator is full length.
                if (a.Digits + a.Exponent >= term.Digits + term.Exponent + p + 1
                    && a.Digits >= p)
                {
                    arena.Release(loop);
                    break;
                }

                divisor.CopyFrom(Decimal32WideMath.Add(ref arena, divisor, Decimal32WideMath.One(ref arena),
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

            var raised = arena.TakeCopy(Decimal32WideMath.One(ref arena));
            var seenBit = false;
            var loop = arena.Mark;

            for (var i = 1; ; i++)
            {
                // Give up once the result is settled either way.
                if ((status & (Decimal32Status.Overflow | Decimal32Status.Underflow)) != 0)
                {
                    if ((status & Decimal32Status.Overflow) != 0 || raised.IsZero)
                    {
                        break;
                    }
                }

                n <<= 1;
                if (n < 0)
                {
                    seenBit = true;
                    raised.CopyFrom(Decimal32WideMath.Multiply(ref arena, raised, a, accumulatorContext,
                        ref status));
                }

                if (i == 31)
                {
                    break;
                }

                if (seenBit)
                {
                    raised.CopyFrom(Decimal32WideMath.Multiply(ref arena, raised, raised,
                        accumulatorContext, ref status));
                }

                arena.Release(loop);
            }

            a.CopyFrom(raised);
        }

        // Dirt to the right: the series was cut short, so the result is inexact even where
        // the digits kept do not change.
        var residue = a.IsZero ? 0 : 1;
        result.CopyFrom(Decimal32WideMath.Round(ref arena, a, residue, set, ref status));
        arena.Release(frame + 1);
        return result;
    }

    /// <summary>
    /// decNumber's decLnOp. Newton's method on a' = a + x*exp(-a) - 1, from a four-digit
    /// estimate off a table, doubling the digits calculated each iteration.
    /// </summary>
    private static Decimal32WideNumber NaturalLog(ref Decimal32WideArena arena, Decimal32WideNumber rhs,
        Decimal32WideContext set, ref Decimal32Status status)
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
                status |= Decimal32Status.InvalidOperation;
                result.SetZero();
                result.Kind = Decimal32Kind.QuietNaN;
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
            result.Kind = Decimal32Kind.Infinity;
            result.IsNegative = true;
            arena.Release(frame + 1);
            return result;
        }

        if (rhs.IsNegative)
        {
            status |= Decimal32Status.InvalidOperation;
            result.SetZero();
            result.Kind = Decimal32Kind.QuietNaN;
            result.IsNegative = false;
            arena.Release(frame + 1);
            return result;
        }

        var ignore = Decimal32Status.None;

        // ln(10) and ln(2) get asked for often enough -- log10 needs the first every time it
        // runs -- to be worth carrying rather than iterating for.
        if (rhs.Exponent == 0 && set.Digits <= 40)
        {
            var literalContext = set;
            literalContext.Rounding = Decimal32Rounding.HalfEven;

            if (rhs.Digits == 2 && rhs.ToUInt64() == 10)
            {
                status |= Decimal32Status.Inexact | Decimal32Status.Rounded;
                var constant = Decimal32WideConstants.NaturalLogOfTen(arena.TakeUnits());
                result.CopyFrom(Decimal32WideMath.Round(ref arena, constant, 0, literalContext, ref ignore));
                arena.Release(frame + 1);
                return result;
            }

            if (rhs.Digits == 1 && rhs.ToUInt64() == 2)
            {
                status |= Decimal32Status.Inexact | Decimal32Status.Rounded;
                var constant = Decimal32WideConstants.NaturalLogOfTwo(arena.TakeUnits());
                result.CopyFrom(Decimal32WideMath.Round(ref arena, constant, 0, literalContext, ref ignore));
                arena.Release(frame + 1);
                return result;
            }
        }

        var p = Math.Max(rhs.Digits, Math.Max(set.Digits, 7)) + 2;

        // Read the operand as a fraction f times a power of ten, so that
        // ln(x) = ln(f) + ln(10)*r, and estimate ln(f) from the table.
        var estimateContext = Decimal32WideContext.Default();
        var r = rhs.Exponent + rhs.Digits;

        var logTenApproximation = Decimal32WideMath.FromInt32(ref arena, 2302585);
        logTenApproximation.Exponent = -6;

        var a = arena.TakeCopy(Decimal32WideMath.Multiply(ref arena, Decimal32WideMath.FromInt32(ref arena, r),
            logTenApproximation, estimateContext, ref ignore));

        // The leading two digits of the coefficient index the table.
        var leading = (int)(rhs.Digits >= 2
            ? (Decimal32WideUnits.DigitAt(rhs.Lsu, rhs.Units, rhs.Digits - 1) * 10)
                + Decimal32WideUnits.DigitAt(rhs.Lsu, rhs.Units, rhs.Digits - 2)
            : Decimal32WideUnits.DigitAt(rhs.Lsu, rhs.Units, 0) * 10);

        var entry = NaturalLogEstimates[leading - 10];
        var b = arena.Take();
        b.CopyFrom(Decimal32WideMath.FromInt32(ref arena, entry >> 2));
        b.Exponent = -(entry & 3) - 3;
        b.IsNegative = true;

        a.CopyFrom(Decimal32WideMath.Add(ref arena, a, b, estimateContext, ref ignore));

        // Four digits of the estimate are good. Near Nmax it comes in low, so the iteration
        // approaches from below and the exp calls below cannot overflow.
        var accumulatorContext = estimateContext;
        accumulatorContext.MaxExponent = set.MaxExponent;
        accumulatorContext.MinExponent = set.MinExponent;
        accumulatorContext.Clamp = false;

        // The adjustment is a catastrophic subtraction, so it is calculated at the sum of
        // the operand's precision and the working precision, over doubled bounds.
        var adjustmentContext = accumulatorContext;
        adjustmentContext.MaxExponent = Decimal32WideContext.MaxMathExponent * 2;
        adjustmentContext.MinExponent = -Decimal32WideContext.MaxMathExponent * 2;

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

            var scaled = Decimal32WideMath.Multiply(ref arena, exponential, rhs, adjustmentContext,
                ref ignore);

            b.CopyFrom(Decimal32WideMath.Subtract(ref arena, scaled, Decimal32WideMath.One(ref arena),
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
                    if (Decimal32WideMath.Compare(rhs, Decimal32WideMath.One(ref arena), false) == 0)
                    {
                        a.Exponent = 0;
                    }
                    else
                    {
                        status |= Decimal32Status.Inexact | Decimal32Status.Rounded;
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

            a.CopyFrom(Decimal32WideMath.Add(ref arena, a, b, accumulatorContext, ref ignore));
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
        result.CopyFrom(Decimal32WideMath.Round(ref arena, a, residue, set, ref status));
        arena.Release(frame + 1);
        return result;
    }

    /// <summary>decNumber's decNumberPower, less the NaN handling its caller does.</summary>
    private static Decimal32WideNumber PowerCore(ref Decimal32WideArena arena, Decimal32WideNumber lhs, Decimal32WideNumber rhs,
        Decimal32WideContext set, ref Decimal32Status status)
    {
        var frame = arena.Mark;
        var result = arena.Take();

        if (rhs.IsInfinity)
        {
            if (lhs.IsNegative && !lhs.IsZero)
            {
                status |= Decimal32Status.InvalidOperation;
                SetQuietNaN(ref result);
                arena.Release(frame + 1);
                return result;
            }

            var comparison = Decimal32WideMath.Compare(lhs, Decimal32WideMath.One(ref arena), false);
            if (comparison == 0)
            {
                // One to an infinite power is deemed inexact, so it comes back padded.
                status |= Decimal32Status.Inexact | Decimal32Status.Rounded;
                result.CopyFrom(PaddedOne(ref arena, set.Digits));
                arena.Release(frame + 1);
                return result;
            }

            var infinite = comparison < 0 ? rhs.IsNegative : !rhs.IsNegative;
            result.SetZero();
            if (infinite)
            {
                result.Kind = Decimal32Kind.Infinity;
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
                result.CopyFrom(Decimal32WideMath.One(ref arena));
                arena.Release(frame + 1);
                return result;
            }

            if (!integerExponent && lhs.IsNegative)
            {
                status |= Decimal32Status.InvalidOperation;
                SetQuietNaN(ref result);
                arena.Release(frame + 1);
                return result;
            }

            result.SetZero();
            result.IsNegative = negative;
            if (!rhs.IsNegative)
            {
                result.Kind = Decimal32Kind.Infinity;
            }

            arena.Release(frame + 1);
            return result;
        }

        if (lhs.IsZero)
        {
            if (rhs.IsZero)
            {
                status |= Decimal32Status.InvalidOperation;
                SetQuietNaN(ref result);
                arena.Release(frame + 1);
                return result;
            }

            result.SetZero();
            result.IsNegative = negative;
            if (rhs.IsNegative)
            {
                result.Kind = Decimal32Kind.Infinity;
            }

            arena.Release(frame + 1);
            return result;
        }

        if (!useInteger)
        {
            if (lhs.IsNegative)
            {
                status |= Decimal32Status.InvalidOperation;
                SetQuietNaN(ref result);
                arena.Release(frame + 1);
                return result;
            }

            var aset = Decimal32WideContext.Default();
            aset.MaxExponent = Decimal32WideContext.MaxMathExponent;
            aset.MinExponent = -Decimal32WideContext.MaxMathExponent;
            aset.Clamp = false;

            // Enough to hold the whole information content of the left operand, exponent
            // included, plus four; six covers any exponent. The spare digits cost ln almost
            // nothing and cut the cases that land more than half a unit out.
            aset.Digits = Math.Max(lhs.Digits, set.Digits) + 6 + 4;

            var accumulator = arena.TakeCopy(NaturalLog(ref arena, lhs, aset, ref status));
            if (accumulator.IsZero)
            {
                // The left operand was one, which would otherwise reduce to an integer 1.
                accumulator.CopyFrom(Decimal32WideMath.One(ref arena));
                if (!integerExponent)
                {
                    accumulator.CopyFrom(PaddedOne(ref arena, set.Digits));
                    status |= Decimal32Status.Inexact | Decimal32Status.Rounded;
                }
            }
            else
            {
                accumulator.CopyFrom(Decimal32WideMath.Multiply(ref arena, accumulator, rhs, aset,
                    ref status));

                accumulator.CopyFrom(Exponential(ref arena, accumulator, aset, ref status));
            }

            result.CopyFrom(Decimal32WideMath.Round(ref arena, accumulator, 0, set, ref status));
            arena.Release(frame + 1);
            return result;
        }

        if (rhs.IsZero)
        {
            result.CopyFrom(Decimal32WideMath.One(ref arena));
            arena.Release(frame + 1);
            return result;
        }

        if (n < 0)
        {
            n = -n;
        }

        var integerSet = set;
        integerSet.Rounding = Decimal32Rounding.HalfEven;
        integerSet.Digits = set.Digits + (rhs.Digits + rhs.Exponent) + 2;

        var dac = arena.TakeCopy(Decimal32WideMath.One(ref arena));
        var multiplicand = arena.TakeCopy(lhs);

        if (rhs.IsNegative)
        {
            // Invert the operand now rather than the result later, which keeps the rounding
            // to one place.
            multiplicand.CopyFrom(Decimal32WideMath.Divide(ref arena, Decimal32WideMath.One(ref arena), lhs,
                integerSet, ref status));
        }

        var seen = false;
        var loop = arena.Mark;

        for (var i = 1; ; i++)
        {
            if ((status & (Decimal32Status.Overflow | Decimal32Status.Underflow)) != 0)
            {
                if ((status & Decimal32Status.Overflow) != 0 || dac.IsZero)
                {
                    break;
                }
            }

            n <<= 1;
            if (n < 0)
            {
                seen = true;
                dac.CopyFrom(Decimal32WideMath.Multiply(ref arena, dac, multiplicand, integerSet,
                    ref status));
            }

            if (i == 31)
            {
                break;
            }

            if (seen)
            {
                dac.CopyFrom(Decimal32WideMath.Multiply(ref arena, dac, dac, integerSet, ref status));
            }

            arena.Release(loop);
        }

        arena.Release(loop);

        if ((status & (Decimal32Status.Overflow | Decimal32Status.Underflow)) != 0)
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
            Decimal32WideRounding.Finalize(ref result, 0, set, ref status);
            arena.Release(frame + 1);
            return result;
        }

        // The sign came out of the multiplications themselves: a negative base reaches an
        // odd power still negative.
        result.CopyFrom(Decimal32WideMath.Round(ref arena, dac, 0, set, ref status));
        arena.Release(frame + 1);
        return result;
    }

    /// <summary>
    /// decNumber's decNumberLog10, at the engine level so that the reductions built on it
    /// keep the exact power-of-ten case.
    /// </summary>
    private static Decimal32WideNumber BaseTenLog(ref Decimal32WideArena arena, Decimal32WideNumber rhs, Decimal32WideContext set,
        ref Decimal32Status status)
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
        if (Decimal32WideUnits.IsPowerOfTen(rhs.Lsu, rhs.Units, rhs.Digits))
        {
            var power = Decimal32WideMath.FromInt32(ref arena, rhs.Exponent + rhs.Digits - 1);
            result.CopyFrom(Decimal32WideMath.Round(ref arena, power, 0, set, ref status));
            arena.Release(frame + 1);
            return result;
        }

        result.CopyFrom(DividedLogarithm(ref arena, rhs, Decimal32WideMath.FromInt32(ref arena, 10),
            set, ref status));

        arena.Release(frame + 1);
        return result;
    }

    /// <summary>The same shape for base two, whose exact case is a power of two.</summary>
    private static Decimal32WideNumber BaseTwoLog(ref Decimal32WideArena arena, Decimal32WideNumber rhs, Decimal32WideContext set,
        ref Decimal32Status status)
    {
        var frame = arena.Mark;
        var result = arena.Take();

        if (TryLogarithmOfSpecial(ref arena, rhs, ref status, out var special))
        {
            result.CopyFrom(special);
            arena.Release(frame + 1);
            return result;
        }

        if (Decimal32WideConstants.TryPowerOfTwoExponent(rhs, arena.TakeUnits(), out var exponent))
        {
            result.CopyFrom(Decimal32WideMath.Round(ref arena, Decimal32WideMath.FromInt32(ref arena, exponent),
                0, set, ref status));

            arena.Release(frame + 1);
            return result;
        }

        result.CopyFrom(DividedLogarithm(ref arena, rhs, Decimal32WideMath.FromInt32(ref arena, 2),
            set, ref status));

        arena.Release(frame + 1);
        return result;
    }

    /// <summary>
    /// ln(x)/ln(base), at the precisions decNumber uses for log10: the numerator carries the
    /// whole information content of the operand, the divisor three guard digits, and the
    /// division alone is done at the requested precision.
    /// </summary>
    private static Decimal32WideNumber DividedLogarithm(ref Decimal32WideArena arena, Decimal32WideNumber rhs,
        Decimal32WideNumber wholeBase, Decimal32WideContext set, ref Decimal32Status status)
    {
        var frame = arena.Mark;
        var result = arena.Take();

        var aset = Decimal32WideContext.Default();
        aset.MaxExponent = Decimal32WideContext.MaxMathExponent;
        aset.MinExponent = -Decimal32WideContext.MaxMathExponent;
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
        var baseStatus = Decimal32Status.None;
        aset.Digits = set.Digits + 3;
        var divisor = NaturalLog(ref arena, wholeBase, aset, ref baseStatus);
        status |= baseStatus & ~(Decimal32Status.Inexact | Decimal32Status.Rounded);

        aset.Digits = set.Digits;
        result.CopyFrom(Decimal32WideMath.Divide(ref arena, logarithm, divisor, aset, ref status));
        arena.Release(frame + 1);
        return result;
    }

    /// <summary>
    /// The cases every logarithm shares: a NaN passes through, a negative is invalid, zero
    /// gives -Infinity, and +Infinity gives itself.
    /// </summary>
    private static bool TryLogarithmOfSpecial(ref Decimal32WideArena arena, Decimal32WideNumber rhs,
        ref Decimal32Status status, out Decimal32WideNumber result)
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
                status |= Decimal32Status.InvalidOperation;
                result = Decimal32WideMath.QuietNaN(ref arena);
            }

            return true;
        }

        if (rhs.IsZero)
        {
            result = Decimal32WideMath.Infinity(ref arena, true);
            return true;
        }

        if (rhs.IsNegative)
        {
            status |= Decimal32Status.InvalidOperation;
            result = Decimal32WideMath.QuietNaN(ref arena);
            return true;
        }

        return false;
    }

    private static uint RaiseWholeBase(int wholeBase, uint value, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsNaN(value))
        {
            return Decimal32Arithmetic.PropagateNaN(value, ref status);
        }

        var scratch = stackalloc ulong[Decimal32WideArena.TotalWords];
        var arena = new Decimal32WideArena((uint*)scratch);

        var wide = GuardedContext(3);
        var wideStatus = Decimal32Status.None;
        var exponent = Decimal32WideNumber.FromBits(value, arena.TakeUnits());

        var result = PowerCore(ref arena, Decimal32WideMath.FromInt32(ref arena, wholeBase), exponent,
            wide, ref wideStatus);

        return Finish(ref arena, result, wideStatus, rounding, ref status);
    }

    private static uint RaiseWholeBaseMinusOne(int wholeBase, uint value, Decimal32Rounding rounding,
        ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsNaN(value))
        {
            return Decimal32Arithmetic.PropagateNaN(value, ref status);
        }

        var scratch = stackalloc ulong[Decimal32WideArena.TotalWords];
        var arena = new Decimal32WideArena((uint*)scratch);
        var x = Decimal32WideNumber.FromBits(value, arena.TakeUnits());

        if (x.Kind == Decimal32Kind.Infinity)
        {
            return x.IsNegative ? NegativeOne() : Decimal32Encoding.Infinity(false);
        }

        if (x.IsZero)
        {
            return Decimal32Encoding.Canonical(value);
        }

        var lost = LeadingDigitsLost(x);
        if (lost > Decimal32Encoding.Precision + 3)
        {
            // Below this the result is x*ln(base) to well past the last digit kept.
            var narrow = GuardedContext(3);
            var narrowStatus = Decimal32Status.Inexact;

            var logarithmOfBase = wholeBase == 10
                ? Decimal32WideConstants.NaturalLogOfTen(arena.TakeUnits())
                : Decimal32WideConstants.NaturalLogOfTwo(arena.TakeUnits());

            var scaled = Decimal32WideMath.Multiply(ref arena, x, logarithmOfBase, narrow, ref narrowStatus);
            return Finish(ref arena, scaled, narrowStatus, rounding, ref status);
        }

        var wide = GuardedContext(4 + lost);
        var wideStatus = Decimal32Status.None;

        var result = PowerCore(ref arena, Decimal32WideMath.FromInt32(ref arena, wholeBase), x, wide,
            ref wideStatus);

        result = Decimal32WideMath.Subtract(ref arena, result, Decimal32WideMath.One(ref arena), wide, ref wideStatus);
        return Finish(ref arena, result, wideStatus, rounding, ref status);
    }

    private static uint LogPlusOneInWholeBase(int wholeBase, uint value, Decimal32Rounding rounding,
        ref Decimal32Status status)
    {
        if (Decimal32Encoding.IsNaN(value))
        {
            return Decimal32Arithmetic.PropagateNaN(value, ref status);
        }

        var scratch = stackalloc ulong[Decimal32WideArena.TotalWords];
        var arena = new Decimal32WideArena((uint*)scratch);
        var x = Decimal32WideNumber.FromBits(value, arena.TakeUnits());

        if (x.Kind == Decimal32Kind.Infinity)
        {
            if (x.IsNegative)
            {
                return Decimal32Arithmetic.Invalid(ref status);
            }

            return Decimal32Encoding.Infinity(false);
        }

        if (x.IsZero)
        {
            return Decimal32Encoding.Canonical(value);
        }

        var wide = GuardedContext(3);
        var wideStatus = Decimal32Status.None;
        var lost = LeadingDigitsLost(x);

        if (lost > Decimal32Encoding.Precision + 3)
        {
            // The result is x/ln(base) to well past the last digit kept.
            var divisor = NaturalLog(ref arena, Decimal32WideMath.FromInt32(ref arena, wholeBase), wide,
                ref wideStatus);

            var scaled = Decimal32WideMath.Divide(ref arena, x, divisor, wide, ref wideStatus);
            return Finish(ref arena, scaled, wideStatus, rounding, ref status);
        }

        var sum = OnePlus(ref arena, x, lost);
        var result = wholeBase == 10
            ? BaseTenLog(ref arena, sum, wide, ref wideStatus)
            : BaseTwoLog(ref arena, sum, wide, ref wideStatus);

        return Finish(ref arena, result, wideStatus, rounding, ref status);
    }

    /// <summary>
    /// One plus the value, exactly. The context is widened by the digits the operand sits
    /// below the point so that none of it is lost in the sum.
    /// </summary>
    private static Decimal32WideNumber OnePlus(ref Decimal32WideArena arena, Decimal32WideNumber value, int lost)
    {
        var ignore = Decimal32Status.None;
        return Decimal32WideMath.Add(ref arena, Decimal32WideMath.One(ref arena), value,
            GuardedContext(4 + lost), ref ignore);
    }

    /// <summary>
    /// How many digits a subtraction from one cancels: the leading digit of the operand plus
    /// the zeros between it and the point.
    /// </summary>
    private static int LeadingDigitsLost(Decimal32WideNumber value)
    {
        var adjusted = value.Exponent + value.Digits - 1;
        return adjusted < 0 ? -adjusted : 0;
    }

    /// <summary>
    /// The context the reductions are evaluated in: guard digits above the format, and no
    /// exponent limits, so that only the final rounding into the format can overflow.
    /// </summary>
    private static Decimal32WideContext GuardedContext(int guardDigits)
    {
        var context = Decimal32WideContext.ForFormat(Decimal32Rounding.HalfEven);
        context.Digits = Decimal32Encoding.Precision + guardDigits;
        context.MaxExponent = Decimal32WideContext.MaxMathExponent;
        context.MinExponent = -Decimal32WideContext.MaxMathExponent;
        context.Clamp = false;
        return context;
    }

    /// <summary>
    /// Rounds a value evaluated at guard digits into the format, once. An evaluation that
    /// was inexact hands the rounding a residue, so that a result which happens to end in
    /// zeros is still reported as the approximation it is.
    /// </summary>
    private static uint Finish(ref Decimal32WideArena arena, Decimal32WideNumber value, Decimal32Status wideStatus,
        Decimal32Rounding rounding, ref Decimal32Status status)
    {
        status |= wideStatus & (Decimal32Status.InvalidOperation | Decimal32Status.DivisionByZero);

        var residue = value.IsFinite && !value.IsZero
            && (wideStatus & Decimal32Status.Inexact) != 0 ? 1 : 0;

        var set = Decimal32WideContext.ForFormat(rounding);
        return Decimal32WideMath.Round(ref arena, value, residue, set, ref status).ToBits();
    }

    private static uint RoundWithResidue(ref Decimal32WideArena arena, Decimal32WideNumber value, int residue,
        Decimal32Rounding rounding, ref Decimal32Status status)
    {
        var set = Decimal32WideContext.ForFormat(rounding);
        return Decimal32WideMath.Round(ref arena, value, residue, set, ref status).ToBits();
    }

    /// <summary>Whether a value is exactly a given small whole number.</summary>
    private static bool IsWholeNumber(Decimal32WideNumber value, int whole)
    {
        return value.IsFinite && !value.IsNegative && value.TryGetInt32(out var integer)
            && integer == whole;
    }

    /// <summary>
    /// Whether a rounded root is the exact one, by raising it back to the degree. An exact
    /// root is then shortened toward the operand's exponent divided by the degree, which is
    /// what the square root does with its own.
    /// </summary>
    private static bool TryExactRoot(ref Decimal32WideArena arena, uint candidate, Decimal32WideNumber value, int degree,
        out uint result)
    {
        result = candidate;

        // Past this the raise costs more than the case is worth, and an exact root of such a
        // degree needs an operand that is a perfect power of it.
        if (Math.Abs(degree) > 40 || Decimal32Encoding.IsSpecial(candidate))
        {
            return false;
        }

        var candidateCoefficient = Decimal32Encoding.Unpack(candidate, out var candidateExponent);
        if (candidateCoefficient == 0)
        {
            return false;
        }

        var frame = arena.Mark;
        var exact = Decimal32WideContext.Default();
        exact.Digits = Decimal32WideArena.SlotUnits * Decimal32WideNumber.DigitsPerUnit / 4;
        exact.MaxExponent = Decimal32WideContext.MaxMathExponent;
        exact.MinExponent = -Decimal32WideContext.MaxMathExponent;
        exact.Clamp = false;

        var ignore = Decimal32Status.None;
        var magnitude = Math.Abs(degree);

        var root = Decimal32WideNumber.FromParts(Decimal32Kind.Finite, false, 0, candidateCoefficient, arena.TakeUnits());

        var raised = arena.TakeCopy(Decimal32WideMath.One(ref arena));
        var loop = arena.Mark;

        for (var index = 0; index < magnitude; index++)
        {
            raised.CopyFrom(Decimal32WideMath.Multiply(ref arena, raised, root, exact, ref ignore));
            arena.Release(loop);

            if (raised.Digits > exact.Digits - 40)
            {
                // The raise has outgrown what an exact comparison can carry, which means
                // the operand cannot be a perfect power of this degree anyway.
                arena.Release(frame);
                return false;
            }
        }

        var raisedExponent = (long)candidateExponent * magnitude;

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

            raised.CopyFrom(Decimal32WideMath.Multiply(ref arena, raised, magnitudeOfValue, exact,
                ref ignore));

            raisedExponent += value.Exponent;
            comparand.CopyFrom(Decimal32WideMath.One(ref arena));
        }

        // Two equal values have digit counts that differ by at most the widest exact
        // intermediate, so a larger gap says they differ without any shifting -- and a
        // shift that large would outrun the slot.
        var common = Math.Min(raisedExponent, comparandExponent);
        if (raisedExponent - common > 500 || comparandExponent - common > 500)
        {
            arena.Release(frame);
            return false;
        }

        raised.Units = Decimal32WideUnits.ShiftUp(raised.Lsu, raised.Units, (int)(raisedExponent - common));
        raised.CountDigits();

        comparand.Units = Decimal32WideUnits.ShiftUp(comparand.Lsu, comparand.Units,
            (int)(comparandExponent - common));

        comparand.CountDigits();

        if (Decimal32WideUnits.Compare(raised.Lsu, raised.Units, comparand.Lsu, comparand.Units) != 0)
        {
            arena.Release(frame);
            return false;
        }

        var ideal = FloorDivide(value.Exponent, degree);
        var shortened = (ulong)candidateCoefficient;
        var shortenedExponent = candidateExponent;
        Decimal32Shaping.StripTrailingZeros(ref shortened, ref shortenedExponent, ideal);

        result = Decimal32Encoding.Pack(Decimal32Encoding.IsNegative(candidate), shortenedExponent, (uint)shortened);
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

    private static uint NegativeOne()
    {
        return Decimal32Encoding.Pack(true, 0, 1);
    }

    /// <summary>
    /// One written out to the full width of the context, which is how the specification asks
    /// an inexact one to be presented.
    /// </summary>
    private static Decimal32WideNumber PaddedOne(ref Decimal32WideArena arena, int digits)
    {
        var shift = digits - 1;
        var result = arena.Take();
        result.Units = Decimal32WideUnits.SetPowerOfTen(result.Lsu, shift);
        result.Exponent = -shift;
        result.CountDigits();
        return result;
    }

    private static void SetQuietNaN(ref Decimal32WideNumber value)
    {
        value.SetZero();
        value.Kind = Decimal32Kind.QuietNaN;
        value.IsNegative = false;
    }
}
