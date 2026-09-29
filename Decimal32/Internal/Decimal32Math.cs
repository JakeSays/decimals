// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// The elementary functions other than the square root.
/// </summary>
/// <remarks>
/// <para>
/// Exp, ln, log10, and power cannot work on the format's own coefficient. Exp evaluates
/// its series in an accumulator of twice the working precision, ln calls exp inside a
/// Newton iteration at an even wider precision, and power calls ln. So they work on
/// <see cref="Decimal32WideNumber"/>. They are ports of decNumber's decExpOp, decLnOp,
/// decNumberLog10, and decNumberPower, with the same intermediate precisions, so their
/// results match the reference digit for digit.
/// </para>
/// <para>
/// decNumber declares its temporaries as locals sized for the widest case. Here an entry
/// point allocates one block on the stack, and the functions take slots from it through
/// <see cref="Decimal32WideArena"/>. The arrangement is the same, with the sizes in one
/// place.
/// </para>
/// </remarks>
internal static unsafe class Decimal32Math
{
    /// <summary>
    /// decNumber's LNnn table: an initial estimate of ln(f) for a coefficient truncated to
    /// two digits, 0.10 through 0.99. Each entry packs a four-digit coefficient in the top
    /// 14 bits and a two-bit exponent code, and represents -c * 10^(-e-3).
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
    /// e raised to the value. A finite result always has full precision and is inexact,
    /// except in two exact cases: exp(0) is 1 and exp(-Infinity) is 0.
    /// </summary>
    /// <param name="value">The encoded exponent.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded result, rounded to the format.</returns>
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
    /// The natural logarithm. Zero gives -Infinity, a negative value is invalid, and ln(1)
    /// is an exact zero.
    /// </summary>
    /// <param name="value">The encoded operand.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded result, rounded to the format.</returns>
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
    /// The base-10 logarithm, computed as ln(x)/ln(10). A power of ten gives its exponent
    /// exactly.
    /// </summary>
    /// <param name="value">The encoded operand.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded result, rounded to the format.</returns>
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
    /// compute it use the wider mathematical range, and a degenerate base (zero or an
    /// infinity) can leave a zero with an exponent no encoding can represent.
    /// </summary>
    private static uint Settle(ref Decimal32WideArena arena, Decimal32WideNumber value, Decimal32WideContext set,
        ref Decimal32Status status)
    {
        return Decimal32WideMath.Round(ref arena, value, 0, set, ref status).ToBits();
    }

    /// <summary>
    /// The left operand raised to the power of the right operand. An integer exponent that
    /// fits is applied by repeated squaring, which can be exact. Any other exponent uses
    /// exp(ln(x) * y).
    /// </summary>
    /// <param name="left">The encoded base.</param>
    /// <param name="right">The encoded exponent.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded result, rounded to the format.</returns>
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

    /// <summary>2 raised to the value.</summary>
    /// <param name="value">The encoded exponent.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded result, rounded to the format.</returns>
    public static uint Exp2(uint value, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        return RaiseWholeBase(2, value, rounding, ref status);
    }

    /// <summary>10 raised to the value.</summary>
    /// <param name="value">The encoded exponent.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded result, rounded to the format.</returns>
    public static uint Exp10(uint value, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        return RaiseWholeBase(10, value, rounding, ref status);
    }

    /// <summary>
    /// e raised to the value, minus one. Near zero the result is close to the operand
    /// itself, so the subtraction cancels the leading digits. The evaluation is widened by
    /// as many digits as the cancellation removes.
    /// </summary>
    /// <param name="value">The encoded exponent.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded result, rounded to the format.</returns>
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
            // The x^2/2 term is entirely below the last digit kept, so the result is the
            // operand with a residue away from zero. The term has the operand's sign.
            return RoundWithResidue(ref arena, x, x.IsNegative ? -1 : 1, rounding, ref status);
        }

        var wide = GuardedContext(3 + lost);
        var wideStatus = Decimal32Status.None;
        var result = Exponential(ref arena, x, wide, ref wideStatus);
        result = Decimal32WideMath.Subtract(ref arena, result, Decimal32WideMath.One(ref arena), wide, ref wideStatus);

        return Finish(ref arena, result, wideStatus, rounding, ref status);
    }

    /// <summary>2 raised to the value, minus one.</summary>
    /// <param name="value">The encoded exponent.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded result, rounded to the format.</returns>
    public static uint Exp2MinusOne(uint value, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        return RaiseWholeBaseMinusOne(2, value, rounding, ref status);
    }

    /// <summary>10 raised to the value, minus one.</summary>
    /// <param name="value">The encoded exponent.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded result, rounded to the format.</returns>
    public static uint Exp10MinusOne(uint value, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        return RaiseWholeBaseMinusOne(10, value, rounding, ref status);
    }

    /// <summary>
    /// The base-2 logarithm. A power of two gives its exponent exactly, as a power of ten
    /// does for <see cref="Log10"/>.
    /// </summary>
    /// <param name="value">The encoded operand.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded result, rounded to the format.</returns>
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
    /// The logarithm in an arbitrary base. Bases 2 and 10 use their own methods, which keep
    /// their exact cases.
    /// </summary>
    /// <param name="value">The encoded operand.</param>
    /// <param name="newBase">The encoded base of the logarithm.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded result, rounded to the format.</returns>
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

        if (LogarithmOfSpecial(ref arena, x, ref status) is { } special)
        {
            return special.ToBits();
        }

        return Settle(ref arena, DividedLogarithm(ref arena, x, b, set, ref status), set, ref status);
    }

    /// <summary>
    /// The natural logarithm of one plus the value. For a small operand, the sum would round
    /// back to exactly one, so it is computed with enough digits to keep the whole operand.
    /// </summary>
    /// <param name="value">The encoded operand.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded result, rounded to the format.</returns>
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
            // ln(1+x) is x minus x^2/2, and that term is below the last digit kept, so the
            // residue is toward zero.
            return RoundWithResidue(ref arena, x, x.IsNegative ? 1 : -1, rounding, ref status);
        }

        var wideStatus = Decimal32Status.None;
        var sum = OnePlus(ref arena, x, lost);
        var result = NaturalLog(ref arena, sum, GuardedContext(3), ref wideStatus);

        return Finish(ref arena, result, wideStatus, rounding, ref status);
    }

    /// <summary>The base-2 logarithm of one plus the value.</summary>
    /// <param name="value">The encoded operand.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded result, rounded to the format.</returns>
    public static uint Log2PlusOne(uint value, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        return LogPlusOneInWholeBase(2, value, rounding, ref status);
    }

    /// <summary>The base-10 logarithm of one plus the value.</summary>
    /// <param name="value">The encoded operand.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded result, rounded to the format.</returns>
    public static uint Log10PlusOne(uint value, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        return LogPlusOneInWholeBase(10, value, rounding, ref status);
    }

    /// <summary>The cube root.</summary>
    /// <param name="value">The encoded operand. It can be negative.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded result, rounded to the format.</returns>
    public static uint Cbrt(uint value, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        return RootN(value, 3, rounding, ref status);
    }

    /// <summary>
    /// The root of the given degree, computed as exp(ln(x)/degree). An exact root is
    /// returned as exact: the rounded root is raised back to the degree and compared with
    /// the operand.
    /// </summary>
    /// <param name="value">The encoded operand.</param>
    /// <param name="degree">The degree of the root. A negative degree gives the reciprocal of the root.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded result, rounded to the format, or a quiet NaN for an even root of a negative value.</returns>
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
            // A zero keeps its sign through any root. A negative degree gives an infinity of
            // the same sign.
            return degree < 0
                ? Decimal32Encoding.Infinity(x.IsNegative)
                : Decimal32Encoding.Canonical(value);
        }

        if (x.IsNegative && !oddDegree)
        {
            // An even root of a negative value has no real result.
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

        if (ExactRoot(ref arena, candidate, x, degree) is { } exact)
        {
            return exact;
        }

        status |= candidateStatus;
        return candidate;
    }

    /// <summary>
    /// The square root of the sum of two squares, computed wide enough that squaring an
    /// operand cannot overflow.
    /// </summary>
    /// <param name="left">The first encoded value.</param>
    /// <param name="right">The second encoded value.</param>
    /// <param name="rounding">The rounding mode.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <returns>The encoded result, rounded to the format.</returns>
    public static uint Hypot(uint left, uint right, Decimal32Rounding rounding, ref Decimal32Status status)
    {
        // An infinite operand decides the result even when the other operand is a NaN.
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
    /// decNumber's decExpOp. The series is evaluated on an operand reduced to below one,
    /// and the result is raised back by a power of ten. This keeps the iteration count low.
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

        // For a small enough operand, the result is 1 in every digit the context keeps,
        // because e^x is below 1+3x/2 for 0 < x < 0.66. A negative operand needs one more
        // zero, because its result has the form 0.9999999 instead of 1.0000001.
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
            // 10 to a power this large cannot be computed, but it is not needed. The result
            // will certainly overflow or underflow to zero, so pass the raise below a value
            // that produces that result.
            a.CopyFrom(Decimal32WideMath.FromInt32(ref arena, 2));
            a.Exponent = rhs.IsNegative ? -2 : 0;
            h = 8;
            p = 9;
        }
        else
        {
            // Reducing the operand further below one cuts iterations, but the power of ten
            // that undoes the reduction must stay computable, so the reduction depends on h.
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

            // Hull and Abrham's working precision, widened when the operand has more digits
            // than the result, because all of them can affect the last digit kept.
            p = Math.Max(x.Digits, set.Digits) + h + 2;

            var termContext = Decimal32WideContext.Default();
            var divisorContext = termContext;
            termContext.Digits = p;
            termContext.MinExponent = Decimal32WideContext.SmallestExponent;

            // The accumulator holds twice the working precision, so adding each term is exact
            // and rounding errors cannot build up across iterations.
            accumulatorContext.Digits = p * 2;

            var term = arena.TakeCopy(x);
            var divisor = arena.TakeCopy(Decimal32WideMath.FromInt32(ref arena, 2));
            a.CopyFrom(Decimal32WideMath.One(ref arena));

            // The loop reuses these three slots instead of taking new ones on each pass, so
            // everything above the mark is released at the end of every iteration.
            var loop = arena.Mark;

            for (;;)
            {
                a.CopyFrom(Decimal32WideMath.Add(ref arena, a, term, accumulatorContext, ref status));

                var next = Decimal32WideMath.Multiply(ref arena, term, x, termContext, ref ignore);
                term.CopyFrom(Decimal32WideMath.Divide(ref arena, next, divisor, termContext, ref ignore));

                // Stop when the term is so far below the accumulator that it cannot affect the
                // last digit kept, and the accumulator is at full length.
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
            // Undo the reduction: compute a^(10^h) by squaring over the bits of 10^h. Only
            // the multiplication loop of power is needed, not the whole function.
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
                // Stop once the result has overflowed, or has underflowed to zero.
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

        // The series was truncated, so the result is inexact even when the kept digits do
        // not change.
        var residue = a.IsZero ? 0 : 1;
        result.CopyFrom(Decimal32WideMath.Round(ref arena, a, residue, set, ref status));
        arena.Release(frame + 1);
        return result;
    }

    /// <summary>
    /// decNumber's decLnOp. Newton's method on a' = a + x*exp(-a) - 1, starting from a
    /// four-digit estimate from a table. The number of digits computed doubles on each
    /// iteration.
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

        // ln(10) and ln(2) are requested often (log10 needs ln(10) on every call), so they
        // are stored as constants instead of computed.
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

        // Write the operand as a fraction f times a power of ten r, so that
        // ln(x) = ln(f) + ln(10)*r, and estimate ln(f) from the table.
        var estimateContext = Decimal32WideContext.Default();
        var r = rhs.Exponent + rhs.Digits;

        var logTenApproximation = Decimal32WideMath.FromInt32(ref arena, 2302585);
        logTenApproximation.Exponent = -6;

        var a = arena.TakeCopy(Decimal32WideMath.Multiply(ref arena, Decimal32WideMath.FromInt32(ref arena, r),
            logTenApproximation, estimateContext, ref ignore));

        // The first two digits of the coefficient index the table.
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

        // The estimate has four correct digits. Near Nmax it is low, so the iteration
        // approaches from below and the exp calls below cannot overflow.
        var accumulatorContext = estimateContext;
        accumulatorContext.MaxExponent = set.MaxExponent;
        accumulatorContext.MinExponent = set.MinExponent;
        accumulatorContext.Clamp = false;

        // The adjustment is a subtraction with catastrophic cancellation, so it is computed
        // at the operand's precision plus the working precision, with twice the normal
        // exponent range.
        var adjustmentContext = accumulatorContext;
        adjustmentContext.MaxExponent = Decimal32WideContext.MaxMathExponent * 2;
        adjustmentContext.MinExponent = -Decimal32WideContext.MaxMathExponent * 2;

        // Start at 9 digits, so the sequence is 7+2, 16+2, 34+2: the standard format
        // precisions, each plus 2.
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

            // The iteration ends when the adjustment cannot change the result by half a unit
            // in the last place, and the accumulator is at full length. This is looser than
            // exp needs, because only the final rounding follows.
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
                    // Force the padding if the adjustment reached zero early.
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

    /// <summary>decNumber's decNumberPower, without the NaN handling that its caller does.</summary>
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
                // One raised to an infinite power is treated as inexact, so the result is
                // padded to full precision.
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
        var integer = rhs.ToInt32();
        var useInteger = integer.HasValue;
        var n = integer.GetValueOrDefault();
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

            // Enough digits to hold all the information in the left operand, including its
            // exponent, plus four. Six digits cover any exponent. The extra digits cost ln
            // almost nothing and reduce the cases that end up more than half a unit off.
            aset.Digits = Math.Max(lhs.Digits, set.Digits) + 6 + 4;

            var accumulator = arena.TakeCopy(NaturalLog(ref arena, lhs, aset, ref status));
            if (accumulator.IsZero)
            {
                // ln(x) is zero, so the left operand was 1 and the result is 1. For a
                // non-integer exponent the result is padded to full precision and inexact.
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
            // Invert the operand now instead of the result later, so there is only one
            // rounding.
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

            // Round the subnormal result to the requested precision, not the working one.
            result.CopyFrom(dac);
            result.IsNegative = negative;
            Decimal32WideRounding.Finalize(ref result, 0, set, ref status);
            arena.Release(frame + 1);
            return result;
        }

        // The sign comes from the multiplications: a negative base raised to an odd power
        // stays negative.
        result.CopyFrom(Decimal32WideMath.Round(ref arena, dac, 0, set, ref status));
        arena.Release(frame + 1);
        return result;
    }

    /// <summary>
    /// decNumber's decNumberLog10, on the wide number, so that the operations built on it
    /// keep the exact power-of-ten case.
    /// </summary>
    private static Decimal32WideNumber BaseTenLog(ref Decimal32WideArena arena, Decimal32WideNumber rhs, Decimal32WideContext set,
        ref Decimal32Status status)
    {
        var frame = arena.Mark;
        var result = arena.Take();

        if (LogarithmOfSpecial(ref arena, rhs, ref status) is { } special)
        {
            result.CopyFrom(special);
            arena.Release(frame + 1);
            return result;
        }

        // A coefficient of 1 followed by zeros means the value is a power of ten, and the
        // logarithm is exactly the adjusted exponent.
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

    /// <summary>The same method for base 2, whose exact case is a power of two.</summary>
    private static Decimal32WideNumber BaseTwoLog(ref Decimal32WideArena arena, Decimal32WideNumber rhs, Decimal32WideContext set,
        ref Decimal32Status status)
    {
        var frame = arena.Mark;
        var result = arena.Take();

        if (LogarithmOfSpecial(ref arena, rhs, ref status) is { } special)
        {
            result.CopyFrom(special);
            arena.Release(frame + 1);
            return result;
        }

        if (Decimal32WideConstants.PowerOfTwoExponent(rhs, arena.TakeUnits()) is { } exponent)
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
    /// ln(x)/ln(base), at the precisions decNumber uses for log10. The numerator keeps all
    /// the information in the operand, the divisor has three guard digits, and only the
    /// division is done at the requested precision.
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

        // Six digits cover any exponent. Using the whole operand costs ln almost nothing:
        // ln doubles its precision on each iteration, so a few extra digits rarely add an
        // iteration.
        aset.Digits = Math.Max(rhs.Digits + 6, set.Digits) + 3;
        var logarithm = NaturalLog(ref arena, rhs, aset, ref status);

        if (!logarithm.IsFinite || logarithm.IsZero)
        {
            result.CopyFrom(logarithm);
            arena.Release(frame + 1);
            return result;
        }

        // The divisor is always inexact and rounded, so reporting that tells the caller
        // nothing. Any other condition it raises, such as an unusable base, is reported.
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
    /// Handles the cases all logarithms share: a NaN passes through, a negative value is
    /// invalid, zero gives -Infinity, and +Infinity gives +Infinity. The result is null for
    /// a positive finite value, which needs the full computation.
    /// </summary>
    private static Decimal32WideNumber? LogarithmOfSpecial(ref Decimal32WideArena arena, Decimal32WideNumber rhs,
        ref Decimal32Status status)
    {
        if (rhs.IsNaN)
        {
            return rhs;
        }

        if (rhs.IsInfinity)
        {
            if (rhs.IsNegative)
            {
                status |= Decimal32Status.InvalidOperation;
                return Decimal32WideMath.QuietNaN(ref arena);
            }

            return rhs;
        }

        if (rhs.IsZero)
        {
            return Decimal32WideMath.Infinity(ref arena, true);
        }

        if (rhs.IsNegative)
        {
            status |= Decimal32Status.InvalidOperation;
            return Decimal32WideMath.QuietNaN(ref arena);
        }

        return null;
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
            // Below this, the result equals x*ln(base) well past the last digit kept.
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
            // The result equals x/ln(base) well past the last digit kept.
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
    /// One plus the value, exactly. The context is widened by the number of digits the
    /// operand has below the decimal point, so none of them are lost in the sum.
    /// </summary>
    private static Decimal32WideNumber OnePlus(ref Decimal32WideArena arena, Decimal32WideNumber value, int lost)
    {
        var ignore = Decimal32Status.None;
        return Decimal32WideMath.Add(ref arena, Decimal32WideMath.One(ref arena), value,
            GuardedContext(4 + lost), ref ignore);
    }

    /// <summary>
    /// The number of digits a subtraction from one cancels: the operand's leading digit plus
    /// the zeros between it and the decimal point.
    /// </summary>
    private static int LeadingDigitsLost(Decimal32WideNumber value)
    {
        var adjusted = value.Exponent + value.Digits - 1;
        return adjusted < 0 ? -adjusted : 0;
    }

    /// <summary>
    /// The context the functions are evaluated in: guard digits beyond the format's
    /// precision, and no exponent limits, so only the final rounding into the format can
    /// overflow.
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
    /// Rounds a value evaluated with guard digits into the format, once. If the evaluation
    /// was inexact, the rounding gets a residue, so a result that happens to end in zeros is
    /// still reported as inexact.
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

    /// <summary>Whether a value is exactly the given small integer.</summary>
    private static bool IsWholeNumber(Decimal32WideNumber value, int whole)
    {
        return value.IsFinite && !value.IsNegative && value.ToInt32() == whole;
    }

    /// <summary>
    /// Whether a rounded root is exact, checked by raising it back to the degree. An exact
    /// root is then shortened toward the operand's exponent divided by the degree, as the
    /// square root does. The result is the encoded exact root, or null if the candidate is
    /// not exact.
    /// </summary>
    private static uint? ExactRoot(ref Decimal32WideArena arena, uint candidate, Decimal32WideNumber value, int degree)
    {
        // Beyond this degree, the raise costs more than the case is worth, and an exact root
        // needs an operand that is a perfect power of that degree.
        if (Math.Abs(degree) > 40 || Decimal32Encoding.IsSpecial(candidate))
        {
            return null;
        }

        var candidateCoefficient = Decimal32Encoding.Unpack(candidate, out var candidateExponent);
        if (candidateCoefficient == 0)
        {
            return null;
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
                // The raised value is too wide for an exact comparison, which means the
                // operand cannot be a perfect power of this degree.
                arena.Release(frame);
                return null;
            }
        }

        var raisedExponent = (long)candidateExponent * magnitude;

        // A negative degree gives the reciprocal, so the test is whether the raised root
        // times the operand is one.
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
        // intermediate. A larger gap means they differ, without any shifting. A shift that
        // large would also overflow the slot.
        var common = Math.Min(raisedExponent, comparandExponent);
        if (raisedExponent - common > 500 || comparandExponent - common > 500)
        {
            arena.Release(frame);
            return null;
        }

        raised.Units = Decimal32WideUnits.ShiftUp(raised.Lsu, raised.Units, (int)(raisedExponent - common));
        raised.CountDigits();

        comparand.Units = Decimal32WideUnits.ShiftUp(comparand.Lsu, comparand.Units,
            (int)(comparandExponent - common));

        comparand.CountDigits();

        if (Decimal32WideUnits.Compare(raised.Lsu, raised.Units, comparand.Lsu, comparand.Units) != 0)
        {
            arena.Release(frame);
            return null;
        }

        var ideal = FloorDivide(value.Exponent, degree);
        var shortened = (ulong)candidateCoefficient;
        var shortenedExponent = candidateExponent;
        Decimal32Shaping.StripTrailingZeros(ref shortened, ref shortenedExponent, ideal);

        arena.Release(frame);
        return Decimal32Encoding.Pack(Decimal32Encoding.IsNegative(candidate), shortenedExponent, (uint)shortened);
    }

    /// <summary>
    /// Integer division rounding toward negative infinity, which an exponent divided by a
    /// root's degree requires: the preferred exponent rounds down, not toward zero.
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
    /// One written to the full precision of the context, which is how the specification
    /// presents an inexact one.
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
