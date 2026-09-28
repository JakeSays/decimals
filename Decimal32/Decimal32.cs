// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using Decimals.Internal;


namespace Decimals;

/// <summary>
/// An IEEE 754 decimal32 value: 7 digits of coefficient, exponents from -101 to +90, held
/// as a binary-integer encoding and computed on with nothing wider than a machine word.
/// </summary>
/// <remarks>
/// <para>
/// This type stands on its own. Its arithmetic, text, conversions, and elementary
/// functions depend on nothing outside this namespace, and nothing in it uses a 128-bit
/// integer or an arbitrary-precision integer type. Seven digits is narrow enough that every
/// intermediate the arithmetic forms -- a product, an aligned sum, a scaled dividend, a
/// radicand -- fits one 64-bit word, so there are no limbs at all; the elementary functions
/// run on a unit-array engine ported from decNumber.
/// </para>
/// <para>
/// The in-memory encoding is BID, the binary-integer form, because that is what makes the
/// arithmetic fast: the coefficient is reached with a mask and a shift. The
/// densely-packed interchange form that decimal hardware and decNumber exchange is a
/// conversion away through <see cref="ToDpdBits"/> and <see cref="FromDpdBits"/>.
/// </para>
/// </remarks>
public readonly struct Decimal32
    : IFloatingPoint<Decimal32>,
      IMinMaxValue<Decimal32>,
      IExponentialFunctions<Decimal32>,
      ILogarithmicFunctions<Decimal32>,
      IPowerFunctions<Decimal32>,
      IRootFunctions<Decimal32>,
      IUtf8SpanFormattable,
      IUtf8SpanParsable<Decimal32>
{
    private readonly uint _bits;

    private Decimal32(uint bits)
    {
        _bits = bits;
    }

    public static Decimal32 Zero => new(Decimal32Encoding.Zero(false, 0));

    public static Decimal32 NegativeZero => new(Decimal32Encoding.Zero(true, 0));

    public static Decimal32 One => new(Decimal32Encoding.Pack(false, 0, 1));

    public static Decimal32 NegativeOne => new(Decimal32Encoding.Pack(true, 0, 1));

    public static Decimal32 MaxValue =>
        new(Decimal32Encoding.Pack(false, Decimal32Encoding.MaxQuantumExponent, Decimal32Encoding.MaxCoefficient));

    public static Decimal32 MinValue =>
        new(Decimal32Encoding.Pack(true, Decimal32Encoding.MaxQuantumExponent, Decimal32Encoding.MaxCoefficient));

    /// <summary>The smallest positive value, which is subnormal: 1E-101.</summary>
    public static Decimal32 Epsilon => new(Decimal32Encoding.Pack(false, Decimal32Encoding.MinQuantumExponent, 1));

    public static Decimal32 PositiveInfinity => new(Decimal32Encoding.Infinity(false));

    public static Decimal32 NegativeInfinity => new(Decimal32Encoding.Infinity(true));

    public static Decimal32 NaN => new(Decimal32Encoding.QuietNaN());

    /// <summary>The raw encoding, which is BID.</summary>
    public uint ToBits() => _bits;

    public static Decimal32 FromBits(uint bits) => new(bits);

    /// <summary>
    /// The binary-integer-decimal encoding, where the coefficient is a plain integer in the
    /// trailing field. This is the in-memory encoding, so it costs nothing.
    /// </summary>
    public uint ToBidBits() => _bits;

    public static Decimal32 FromBidBits(uint bits) => new(bits);

    /// <summary>
    /// The densely-packed-decimal interchange encoding, which is what decimal hardware and
    /// DPD-based libraries exchange.
    /// </summary>
    public uint ToDpdBits() => Decimal32Dpd.ToDpd(_bits);

    public static Decimal32 FromDpdBits(uint bits) => new(Decimal32Dpd.FromDpd(bits));

    public static bool IsNaN(Decimal32 value) => Decimal32Encoding.IsNaN(value._bits);

    public static bool IsSignalingNaN(Decimal32 value) => Decimal32Encoding.IsSignalingNaN(value._bits);

    public static bool IsInfinity(Decimal32 value) => Decimal32Encoding.IsInfinity(value._bits);

    public static bool IsFinite(Decimal32 value) => !Decimal32Encoding.IsSpecial(value._bits);

    public static bool IsNegative(Decimal32 value) => Decimal32Encoding.IsNegative(value._bits);

    public static bool IsPositiveInfinity(Decimal32 value) => IsInfinity(value) && !IsNegative(value);

    public static bool IsNegativeInfinity(Decimal32 value) => IsInfinity(value) && IsNegative(value);

    public static bool IsZero(Decimal32 value) => Decimal32Encoding.IsZero(value._bits);

    public static bool IsSubnormal(Decimal32 value) => Decimal32Ordering.IsSubnormal(value._bits);

    public static bool IsNormal(Decimal32 value) => IsFinite(value) && !IsZero(value) && !IsSubnormal(value);

    /// <summary>
    /// False when the encoding uses a coefficient or payload the format cannot hold, or
    /// sets bits a special value leaves clear. Those decode as zero, so re-encoding does not
    /// give the same bits back.
    /// </summary>
    public static bool IsCanonical(Decimal32 value) => value._bits == Decimal32Encoding.Canonical(value._bits);

    /// <summary>The same value in its canonical encoding.</summary>
    public static Decimal32 Canonical(Decimal32 value) => new(Decimal32Encoding.Canonical(value._bits));

    public static Decimal32Class Class(Decimal32 value) => Decimal32Ordering.Classify(value._bits);

    /// <summary>Copies the value with a cleared sign. Quiet: a signaling NaN stays one.</summary>
    public static Decimal32 CopyAbs(Decimal32 value) => new(value._bits & ~Decimal32Encoding.SignMask);

    /// <summary>Copies the value with its sign flipped. Quiet, unlike arithmetic negation.</summary>
    public static Decimal32 CopyNegate(Decimal32 value) => new(value._bits ^ Decimal32Encoding.SignMask);

    public static Decimal32 CopySign(Decimal32 value, Decimal32 sign) =>
        new((value._bits & ~Decimal32Encoding.SignMask) | (sign._bits & Decimal32Encoding.SignMask));

    /// <summary>
    /// True when both are finite with the same exponent, or both are infinite, or both are
    /// NaN. Never raises a condition, even for a signaling NaN.
    /// </summary>
    public static bool SameQuantum(Decimal32 left, Decimal32 right)
    {
        if (Decimal32Encoding.IsSpecial(left._bits) || Decimal32Encoding.IsSpecial(right._bits))
        {
            return (Decimal32Encoding.IsNaN(left._bits) && Decimal32Encoding.IsNaN(right._bits))
                || (Decimal32Encoding.IsInfinity(left._bits) && Decimal32Encoding.IsInfinity(right._bits));
        }

        Decimal32Encoding.Unpack(left._bits, out var leftExponent);
        Decimal32Encoding.Unpack(right._bits, out var rightExponent);
        return leftExponent == rightExponent;
    }

    public static int CompareTotal(Decimal32 left, Decimal32 right) => Decimal32Ordering.CompareTotal(left._bits, right._bits);

    public static int CompareTotalMagnitude(Decimal32 left, Decimal32 right) =>
        Decimal32Ordering.CompareTotalMagnitude(left._bits, right._bits);

    /// <summary>
    /// The specification's to-number conversion: a bad string gives a quiet NaN and raises
    /// <see cref="Decimal32Status.ConversionSyntax"/> rather than throwing.
    /// </summary>
    public static Decimal32 FromString(ReadOnlySpan<char> text, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var bits = Decimal32Parser.Parse(text, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal32(bits);
    }

    public static Decimal32 Parse(ReadOnlySpan<char> text)
    {
        if (!TryParse(text, out var value))
        {
            throw new FormatException($"'{text}' is not a Decimal32.");
        }

        return value;
    }

    public static Decimal32 Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Parse(text.AsSpan());
    }

    public static bool TryParse(ReadOnlySpan<char> text, out Decimal32 value)
    {
        var status = Decimal32Status.None;
        var bits = Decimal32Parser.Parse(text, Decimal32Rounding.HalfEven, ref status);
        if ((status & Decimal32Status.ConversionSyntax) != 0)
        {
            value = Zero;
            return false;
        }

        value = new Decimal32(bits);
        return true;
    }

    public static bool TryParse(string? text, out Decimal32 value)
    {
        if (text is null)
        {
            value = Zero;
            return false;
        }

        return TryParse(text.AsSpan(), out value);
    }

    public override string ToString() => Decimal32Formatter.ToScientificString(_bits);

    public string ToScientificString() => Decimal32Formatter.ToScientificString(_bits);

    public string ToEngineeringString() => Decimal32Formatter.ToEngineeringString(_bits);

    // Arithmetic. The plain form rounds half to even and drops the conditions, which is
    // IEEE 754 default exception handling; the overload taking a context does neither. An
    // operation decides its conditions from a clean slate and adds them to the context, so
    // one operation's conditions never leak into the next one's decisions.

    public static Decimal32 Add(Decimal32 left, Decimal32 right, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var result = Decimal32Arithmetic.Add(left._bits, right._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal32(result);
    }

    public static Decimal32 Add(Decimal32 left, Decimal32 right)
    {
        var status = Decimal32Status.None;
        return new Decimal32(Decimal32Arithmetic.Add(left._bits, right._bits, Decimal32Rounding.HalfEven, ref status));
    }

    public static Decimal32 Subtract(Decimal32 left, Decimal32 right, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var result = Decimal32Arithmetic.Subtract(left._bits, right._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal32(result);
    }

    public static Decimal32 Subtract(Decimal32 left, Decimal32 right)
    {
        var status = Decimal32Status.None;
        return new Decimal32(Decimal32Arithmetic.Subtract(left._bits, right._bits, Decimal32Rounding.HalfEven, ref status));
    }

    public static Decimal32 Multiply(Decimal32 left, Decimal32 right, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var result = Decimal32Arithmetic.Multiply(left._bits, right._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal32(result);
    }

    public static Decimal32 Multiply(Decimal32 left, Decimal32 right)
    {
        var status = Decimal32Status.None;
        return new Decimal32(Decimal32Arithmetic.Multiply(left._bits, right._bits, Decimal32Rounding.HalfEven, ref status));
    }

    public static Decimal32 Divide(Decimal32 left, Decimal32 right, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var result = Decimal32Arithmetic.Divide(left._bits, right._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal32(result);
    }

    public static Decimal32 Divide(Decimal32 left, Decimal32 right)
    {
        var status = Decimal32Status.None;
        return new Decimal32(Decimal32Arithmetic.Divide(left._bits, right._bits, Decimal32Rounding.HalfEven, ref status));
    }

    /// <summary>The integer part of the quotient, with a zero exponent.</summary>
    public static Decimal32 DivideInteger(Decimal32 left, Decimal32 right, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var result = Decimal32Arithmetic.DivideInteger(left._bits, right._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal32(result);
    }

    public static Decimal32 DivideInteger(Decimal32 left, Decimal32 right)
    {
        var status = Decimal32Status.None;
        return new Decimal32(Decimal32Arithmetic.DivideInteger(left._bits, right._bits, Decimal32Rounding.HalfEven, ref status));
    }

    public static Decimal32 Remainder(Decimal32 left, Decimal32 right, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var result = Decimal32Arithmetic.Remainder(left._bits, right._bits, false, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal32(result);
    }

    public static Decimal32 Remainder(Decimal32 left, Decimal32 right)
    {
        var status = Decimal32Status.None;
        return new Decimal32(Decimal32Arithmetic.Remainder(left._bits, right._bits, false, Decimal32Rounding.HalfEven, ref status));
    }

    /// <summary>IEEE 754's remainder, which takes the quotient to the nearest integer.</summary>
    public static Decimal32 RemainderNear(Decimal32 left, Decimal32 right, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var result = Decimal32Arithmetic.Remainder(left._bits, right._bits, true, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal32(result);
    }

    public static Decimal32 RemainderNear(Decimal32 left, Decimal32 right)
    {
        var status = Decimal32Status.None;
        return new Decimal32(Decimal32Arithmetic.Remainder(left._bits, right._bits, true, Decimal32Rounding.HalfEven, ref status));
    }

    /// <summary>Multiply and add, rounded once rather than twice.</summary>
    public static Decimal32 FusedMultiplyAdd(Decimal32 left, Decimal32 right, Decimal32 addend, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var result = Decimal32Arithmetic.FusedMultiplyAdd(left._bits, right._bits, addend._bits, context.Rounding,
            ref status);

        context.Status |= status;
        return new Decimal32(result);
    }

    public static Decimal32 FusedMultiplyAdd(Decimal32 left, Decimal32 right, Decimal32 addend)
    {
        var status = Decimal32Status.None;
        return new Decimal32(Decimal32Arithmetic.FusedMultiplyAdd(left._bits, right._bits, addend._bits,
            Decimal32Rounding.HalfEven, ref status));
    }

    /// <summary>Zero plus the value, so the format's rounding is applied.</summary>
    public static Decimal32 Plus(Decimal32 value, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var result = Decimal32Arithmetic.AddToZero(value._bits, false, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal32(result);
    }

    public static Decimal32 Plus(Decimal32 value)
    {
        var status = Decimal32Status.None;
        return new Decimal32(Decimal32Arithmetic.AddToZero(value._bits, false, Decimal32Rounding.HalfEven, ref status));
    }

    /// <summary>Zero minus the value. Arithmetic, unlike <see cref="CopyNegate"/>.</summary>
    public static Decimal32 Minus(Decimal32 value, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var result = Decimal32Arithmetic.AddToZero(value._bits, true, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal32(result);
    }

    public static Decimal32 Minus(Decimal32 value)
    {
        var status = Decimal32Status.None;
        return new Decimal32(Decimal32Arithmetic.AddToZero(value._bits, true, Decimal32Rounding.HalfEven, ref status));
    }

    /// <summary>The magnitude, arithmetically. Unlike <see cref="CopyAbs"/> this can signal.</summary>
    public static Decimal32 Abs(Decimal32 value, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var negate = Decimal32Encoding.IsNegative(value._bits) && !Decimal32Encoding.IsNaN(value._bits);
        var result = Decimal32Arithmetic.AddToZero(value._bits, negate, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal32(result);
    }

    /// <summary>
    /// Numeric comparison giving -1, 0, 1, or NaN. Unlike <see cref="CompareTotal"/> the two
    /// zeros are equal and a NaN operand gives a NaN.
    /// </summary>
    public static Decimal32 Compare(Decimal32 left, Decimal32 right, ref Decimal32Context context) =>
        CompareToValue(left, right, false, ref context);

    /// <summary>Comparison that signals on any NaN, not only a signaling one.</summary>
    public static Decimal32 CompareSignal(Decimal32 left, Decimal32 right, ref Decimal32Context context) =>
        CompareToValue(left, right, true, ref context);

    private static Decimal32 CompareToValue(Decimal32 left, Decimal32 right, bool signaling, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var comparison = Decimal32Arithmetic.Compare(left._bits, right._bits, signaling, ref status, out var nan);
        context.Status |= status;

        if (comparison == int.MinValue)
        {
            return new Decimal32(nan);
        }

        return FromComparison(comparison);
    }

    private static Decimal32 FromComparison(int comparison)
    {
        if (comparison < 0)
        {
            return NegativeOne;
        }

        return comparison > 0 ? One : Zero;
    }

    // Digit-wise logical operations. Each operand must be a string of ones and zeros with
    // a zero exponent and no sign; anything else is an invalid operation.

    public static Decimal32 And(Decimal32 left, Decimal32 right, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var result = Decimal32Logical.And(left._bits, right._bits, ref status);
        context.Status |= status;
        return new Decimal32(result);
    }

    public static Decimal32 Or(Decimal32 left, Decimal32 right, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var result = Decimal32Logical.Or(left._bits, right._bits, ref status);
        context.Status |= status;
        return new Decimal32(result);
    }

    public static Decimal32 Xor(Decimal32 left, Decimal32 right, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var result = Decimal32Logical.Xor(left._bits, right._bits, ref status);
        context.Status |= status;
        return new Decimal32(result);
    }

    public static Decimal32 Invert(Decimal32 value, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var result = Decimal32Logical.Invert(value._bits, ref status);
        context.Status |= status;
        return new Decimal32(result);
    }

    /// <summary>The larger of the two. A quiet NaN beside a number loses to the number.</summary>
    public static Decimal32 Max(Decimal32 left, Decimal32 right, ref Decimal32Context context) =>
        Select(left, right, true, false, ref context);

    public static Decimal32 Min(Decimal32 left, Decimal32 right, ref Decimal32Context context) =>
        Select(left, right, false, false, ref context);

    public static Decimal32 MaxMagnitude(Decimal32 left, Decimal32 right, ref Decimal32Context context) =>
        Select(left, right, true, true, ref context);

    public static Decimal32 MinMagnitude(Decimal32 left, Decimal32 right, ref Decimal32Context context) =>
        Select(left, right, false, true, ref context);

    private static Decimal32 Select(Decimal32 left, Decimal32 right, bool wantLarger, bool byMagnitude, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var result = Decimal32Ordering.Select(left._bits, right._bits, wantLarger, byMagnitude, ref status);
        context.Status |= status;
        return new Decimal32(result);
    }

    /// <summary>The adjusted exponent, as an integer.</summary>
    public static Decimal32 LogB(Decimal32 value, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var result = Decimal32Shaping.LogB(value._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal32(result);
    }

    /// <summary>Multiplies by ten raised to the second operand.</summary>
    public static Decimal32 ScaleB(Decimal32 value, Decimal32 scale, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var result = Decimal32Shaping.ScaleB(value._bits, scale._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal32(result);
    }

    /// <summary>Removes trailing zeros, giving the shortest coefficient of the same value.</summary>
    public static Decimal32 Reduce(Decimal32 value, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var result = Decimal32Shaping.Reduce(value._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal32(result);
    }

    /// <summary>Like <see cref="Reduce"/>, but never past a zero exponent.</summary>
    public static Decimal32 Trim(Decimal32 value) => new(Decimal32Shaping.Trim(value._bits));

    /// <summary>Rounds to an integer without reporting that anything was rounded.</summary>
    public static Decimal32 RoundToIntegral(Decimal32 value, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var result = Decimal32Shaping.ToIntegral(value._bits, false, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal32(result);
    }

    /// <summary>Rounds to an integer, reporting inexactness.</summary>
    public static Decimal32 RoundToIntegralExact(Decimal32 value, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var result = Decimal32Shaping.ToIntegral(value._bits, true, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal32(result);
    }

    /// <summary>Rescales to the second operand's exponent.</summary>
    public static Decimal32 Quantize(Decimal32 value, Decimal32 pattern, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var result = Decimal32Shaping.Quantize(value._bits, pattern._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal32(result);
    }

    /// <summary>Rotates the coefficient's digits within the format's full width.</summary>
    public static Decimal32 Rotate(Decimal32 value, Decimal32 places, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var result = Decimal32Shaping.RotateOrShift(value._bits, places._bits, true, ref status);
        context.Status |= status;
        return new Decimal32(result);
    }

    /// <summary>Shifts the coefficient's digits, dropping what falls off the end.</summary>
    public static Decimal32 Shift(Decimal32 value, Decimal32 places, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var result = Decimal32Shaping.RotateOrShift(value._bits, places._bits, false, ref status);
        context.Status |= status;
        return new Decimal32(result);
    }

    /// <summary>The square root, correctly rounded.</summary>
    public static Decimal32 Sqrt(Decimal32 value, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var result = Decimal32SquareRoot.SquareRoot(value._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal32(result);
    }

    public static Decimal32 Sqrt(Decimal32 value)
    {
        var status = Decimal32Status.None;
        return new Decimal32(Decimal32SquareRoot.SquareRoot(value._bits, Decimal32Rounding.HalfEven, ref status));
    }

    // The elementary functions, each in the context form and the plain form.

    private delegate uint Function(uint value, Decimal32Rounding rounding, ref Decimal32Status status);

    private delegate uint FunctionOfTwo(uint left, uint right, Decimal32Rounding rounding, ref Decimal32Status status);

    private static Decimal32 Apply(Function function, Decimal32 value, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var result = function(value._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal32(result);
    }

    private static Decimal32 Apply(Function function, Decimal32 value)
    {
        var status = Decimal32Status.None;
        return new Decimal32(function(value._bits, Decimal32Rounding.HalfEven, ref status));
    }

    private static Decimal32 Apply(FunctionOfTwo function, Decimal32 left, Decimal32 right, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var result = function(left._bits, right._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal32(result);
    }

    private static Decimal32 Apply(FunctionOfTwo function, Decimal32 left, Decimal32 right)
    {
        var status = Decimal32Status.None;
        return new Decimal32(function(left._bits, right._bits, Decimal32Rounding.HalfEven, ref status));
    }

    /// <summary>e raised to the value.</summary>
    public static Decimal32 Exp(Decimal32 value, ref Decimal32Context context) => Apply(Decimal32Math.Exp, value, ref context);

    public static Decimal32 Exp(Decimal32 value) => Apply(Decimal32Math.Exp, value);

    /// <summary>The natural logarithm.</summary>
    public static Decimal32 Log(Decimal32 value, ref Decimal32Context context) => Apply(Decimal32Math.Log, value, ref context);

    public static Decimal32 Log(Decimal32 value) => Apply(Decimal32Math.Log, value);

    /// <summary>The base-ten logarithm.</summary>
    public static Decimal32 Log10(Decimal32 value, ref Decimal32Context context) => Apply(Decimal32Math.Log10, value, ref context);

    public static Decimal32 Log10(Decimal32 value) => Apply(Decimal32Math.Log10, value);

    /// <summary>The first value raised to the second.</summary>
    public static Decimal32 Pow(Decimal32 value, Decimal32 power, ref Decimal32Context context) =>
        Apply(Decimal32Math.Power, value, power, ref context);

    public static Decimal32 Pow(Decimal32 value, Decimal32 power) => Apply(Decimal32Math.Power, value, power);

    /// <summary>Two raised to the value.</summary>
    public static Decimal32 Exp2(Decimal32 value, ref Decimal32Context context) => Apply(Decimal32Math.Exp2, value, ref context);

    public static Decimal32 Exp2(Decimal32 value) => Apply(Decimal32Math.Exp2, value);

    /// <summary>Ten raised to the value.</summary>
    public static Decimal32 Exp10(Decimal32 value, ref Decimal32Context context) => Apply(Decimal32Math.Exp10, value, ref context);

    public static Decimal32 Exp10(Decimal32 value) => Apply(Decimal32Math.Exp10, value);

    // The M1 and P1 names are the ones the generic math interfaces declare, so they stay as
    // spelled there rather than written out.

    /// <summary>e raised to the value, less one.</summary>
    public static Decimal32 ExpM1(Decimal32 value, ref Decimal32Context context) =>
        Apply(Decimal32Math.ExpMinusOne, value, ref context);

    public static Decimal32 ExpM1(Decimal32 value) => Apply(Decimal32Math.ExpMinusOne, value);

    /// <summary>Two raised to the value, less one.</summary>
    public static Decimal32 Exp2M1(Decimal32 value, ref Decimal32Context context) =>
        Apply(Decimal32Math.Exp2MinusOne, value, ref context);

    public static Decimal32 Exp2M1(Decimal32 value) => Apply(Decimal32Math.Exp2MinusOne, value);

    /// <summary>Ten raised to the value, less one.</summary>
    public static Decimal32 Exp10M1(Decimal32 value, ref Decimal32Context context) =>
        Apply(Decimal32Math.Exp10MinusOne, value, ref context);

    public static Decimal32 Exp10M1(Decimal32 value) => Apply(Decimal32Math.Exp10MinusOne, value);

    /// <summary>The logarithm in the given base.</summary>
    public static Decimal32 Log(Decimal32 value, Decimal32 newBase, ref Decimal32Context context) =>
        Apply(Decimal32Math.LogInBase, value, newBase, ref context);

    public static Decimal32 Log(Decimal32 value, Decimal32 newBase) => Apply(Decimal32Math.LogInBase, value, newBase);

    /// <summary>The base-two logarithm.</summary>
    public static Decimal32 Log2(Decimal32 value, ref Decimal32Context context) => Apply(Decimal32Math.Log2, value, ref context);

    public static Decimal32 Log2(Decimal32 value) => Apply(Decimal32Math.Log2, value);

    /// <summary>The natural logarithm of one plus the value.</summary>
    public static Decimal32 LogP1(Decimal32 value, ref Decimal32Context context) =>
        Apply(Decimal32Math.LogPlusOne, value, ref context);

    public static Decimal32 LogP1(Decimal32 value) => Apply(Decimal32Math.LogPlusOne, value);

    /// <summary>The base-two logarithm of one plus the value.</summary>
    public static Decimal32 Log2P1(Decimal32 value, ref Decimal32Context context) =>
        Apply(Decimal32Math.Log2PlusOne, value, ref context);

    public static Decimal32 Log2P1(Decimal32 value) => Apply(Decimal32Math.Log2PlusOne, value);

    /// <summary>The base-ten logarithm of one plus the value.</summary>
    public static Decimal32 Log10P1(Decimal32 value, ref Decimal32Context context) =>
        Apply(Decimal32Math.Log10PlusOne, value, ref context);

    public static Decimal32 Log10P1(Decimal32 value) => Apply(Decimal32Math.Log10PlusOne, value);

    /// <summary>The cube root.</summary>
    public static Decimal32 Cbrt(Decimal32 value, ref Decimal32Context context) => Apply(Decimal32Math.Cbrt, value, ref context);

    public static Decimal32 Cbrt(Decimal32 value) => Apply(Decimal32Math.Cbrt, value);

    /// <summary>The root of the given degree.</summary>
    public static Decimal32 RootN(Decimal32 value, int degree, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var result = Decimal32Math.RootN(value._bits, degree, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal32(result);
    }

    public static Decimal32 RootN(Decimal32 value, int degree)
    {
        var status = Decimal32Status.None;
        return new Decimal32(Decimal32Math.RootN(value._bits, degree, Decimal32Rounding.HalfEven, ref status));
    }

    /// <summary>The square root of the sum of two squares, without overflowing on the way.</summary>
    public static Decimal32 Hypot(Decimal32 left, Decimal32 right, ref Decimal32Context context) =>
        Apply(Decimal32Math.Hypot, left, right, ref context);

    public static Decimal32 Hypot(Decimal32 left, Decimal32 right) => Apply(Decimal32Math.Hypot, left, right);

    /// <summary>The next value toward positive infinity.</summary>
    public static Decimal32 NextPlus(Decimal32 value, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var result = Decimal32Shaping.Next(value._bits, true, true, ref status);
        context.Status |= status;
        return new Decimal32(result);
    }

    /// <summary>The next value toward negative infinity.</summary>
    public static Decimal32 NextMinus(Decimal32 value, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var result = Decimal32Shaping.Next(value._bits, false, true, ref status);
        context.Status |= status;
        return new Decimal32(result);
    }

    /// <summary>The next value from the first operand in the direction of the second.</summary>
    public static Decimal32 NextToward(Decimal32 value, Decimal32 target, ref Decimal32Context context)
    {
        var status = Decimal32Status.None;
        var result = Decimal32Shaping.NextToward(value._bits, target._bits, ref status);
        context.Status |= status;
        return new Decimal32(result);
    }

    // Four operations under the names .NET spells them with. Each is one of the operations
    // above, so code written against the built-in floating-point types finds them here.

    /// <summary>IEEE 754's remainder, which is <see cref="RemainderNear(Decimal32, Decimal32)"/>.</summary>
    public static Decimal32 Ieee754Remainder(Decimal32 left, Decimal32 right) => RemainderNear(left, right);

    /// <summary>
    /// The adjusted exponent as an integer, which is <see cref="LogB"/> narrowed to one.
    /// The results off the end of the range are .NET's rather than the specification's: a
    /// zero gives <see cref="int.MinValue"/> and a NaN or an infinity
    /// <see cref="int.MaxValue"/>, where logb gives -Infinity, a NaN, and +Infinity.
    /// </summary>
    public static int ILogB(Decimal32 value) => Decimal32Shaping.ILogB(value._bits);

    /// <summary>
    /// Multiplies by ten raised to <paramref name="scale"/>. The radix is this format's, so
    /// this is the specification's scaleb and not double's power of two.
    /// </summary>
    public static Decimal32 ScaleB(Decimal32 value, int scale)
    {
        var status = Decimal32Status.None;
        return new Decimal32(Decimal32Shaping.ScaleB(value._bits, scale, Decimal32Rounding.HalfEven, ref status));
    }

    /// <summary>The next value toward positive infinity, which is <see cref="NextPlus"/>.</summary>
    public static Decimal32 BitIncrement(Decimal32 value)
    {
        var status = Decimal32Status.None;
        return new Decimal32(Decimal32Shaping.Next(value._bits, true, true, ref status));
    }

    /// <summary>The next value toward negative infinity, which is <see cref="NextMinus"/>.</summary>
    public static Decimal32 BitDecrement(Decimal32 value)
    {
        var status = Decimal32Status.None;
        return new Decimal32(Decimal32Shaping.Next(value._bits, false, true, ref status));
    }

    public static Decimal32 operator +(Decimal32 left, Decimal32 right) => Add(left, right);

    public static Decimal32 operator -(Decimal32 left, Decimal32 right) => Subtract(left, right);

    public static Decimal32 operator *(Decimal32 left, Decimal32 right) => Multiply(left, right);

    public static Decimal32 operator /(Decimal32 left, Decimal32 right) => Divide(left, right);

    public static Decimal32 operator %(Decimal32 left, Decimal32 right) => Remainder(left, right);

    public static Decimal32 operator -(Decimal32 value) => Minus(value);

    public static Decimal32 operator +(Decimal32 value) => Plus(value);

    // Generic math. Numeric comparison here, not the total order: 1.0 equals 1.00, the two
    // zeros are equal, and a NaN is unordered against everything.

    static Decimal32 INumberBase<Decimal32>.Zero => Zero;

    static Decimal32 INumberBase<Decimal32>.One => One;

    static Decimal32 ISignedNumber<Decimal32>.NegativeOne => NegativeOne;

    /// <summary>Ten. This is a decimal format, not a binary one.</summary>
    public static int Radix => 10;

    /// <summary>Converts from another numeric type, refusing a value the format cannot hold.</summary>
    public static Decimal32 CreateChecked<TOther>(TOther value)
        where TOther : INumberBase<TOther>
    {
        if (TryConvertFrom(value, out var result))
        {
            return result;
        }

        if (TOther.TryConvertToChecked<Decimal32>(value, out var other))
        {
            return other;
        }

        throw new NotSupportedException($"Cannot convert {typeof(TOther)} to Decimal32.");
    }

    /// <summary>Converts from another numeric type, clamping to the format's range.</summary>
    public static Decimal32 CreateSaturating<TOther>(TOther value)
        where TOther : INumberBase<TOther>
    {
        if (TryConvertFrom(value, out var result))
        {
            return result;
        }

        if (TOther.TryConvertToSaturating<Decimal32>(value, out var other))
        {
            return other;
        }

        throw new NotSupportedException($"Cannot convert {typeof(TOther)} to Decimal32.");
    }

    /// <summary>Converts from another numeric type, keeping the low-order part.</summary>
    public static Decimal32 CreateTruncating<TOther>(TOther value)
        where TOther : INumberBase<TOther>
    {
        if (TryConvertFrom(value, out var result))
        {
            return result;
        }

        if (TOther.TryConvertToTruncating<Decimal32>(value, out var other))
        {
            return other;
        }

        throw new NotSupportedException($"Cannot convert {typeof(TOther)} to Decimal32.");
    }

    static Decimal32 IAdditiveIdentity<Decimal32, Decimal32>.AdditiveIdentity => Zero;

    static Decimal32 IMultiplicativeIdentity<Decimal32, Decimal32>.MultiplicativeIdentity => One;

    static Decimal32 IMinMaxValue<Decimal32>.MinValue => MinValue;

    static Decimal32 IMinMaxValue<Decimal32>.MaxValue => MaxValue;

    // The constants are their encodings, not text to be parsed at startup: a seven digit
    // coefficient at an exponent of -6.

    /// <summary>The base of natural logarithms: 2.718282.</summary>
    public static Decimal32 E => new(Decimal32Encoding.Pack(false, -6, 2718282));

    /// <summary>The ratio of a circle's circumference to its diameter: 3.141593.</summary>
    public static Decimal32 Pi => new(Decimal32Encoding.Pack(false, -6, 3141593));

    /// <summary>Two Pi: 6.283185.</summary>
    public static Decimal32 Tau => new(Decimal32Encoding.Pack(false, -6, 6283185));

    public static Decimal32 Abs(Decimal32 value) => CopyAbs(value);

    public static bool IsInteger(Decimal32 value)
    {
        if (Decimal32Encoding.IsSpecial(value._bits))
        {
            return false;
        }

        var coefficient = Decimal32Encoding.Unpack(value._bits, out var exponent);
        return exponent >= 0 || FractionIsZero(coefficient, exponent);
    }

    public static bool IsEvenInteger(Decimal32 value) => HasParity(value, 0);

    public static bool IsOddInteger(Decimal32 value) => HasParity(value, 1);

    private static bool HasParity(Decimal32 value, ulong wanted)
    {
        if (!IsInteger(value))
        {
            return false;
        }

        var coefficient = Decimal32Encoding.Unpack(value._bits, out var exponent);
        if (coefficient == 0 || exponent > 0)
        {
            // A positive exponent means trailing zeros, so the value is even whatever the
            // coefficient's last digit is.
            return wanted == 0;
        }

        var whole = exponent == 0
            ? coefficient
            : Decimal32Tables.DivRemPowerOfTen(coefficient, -exponent, out _);

        return (whole & 1) == wanted;
    }

    private static bool FractionIsZero(ulong coefficient, int exponent)
    {
        if (-exponent > Decimal32Tables.MaxPower)
        {
            return coefficient == 0;
        }

        Decimal32Tables.DivRemPowerOfTen(coefficient, -exponent, out var fraction);
        return fraction == 0;
    }

    public static bool IsPositive(Decimal32 value) => !IsNegative(value);

    public static bool IsRealNumber(Decimal32 value) => !IsNaN(value);

    public static bool IsComplexNumber(Decimal32 value) => false;

    public static bool IsImaginaryNumber(Decimal32 value) => false;

    // Selection, as .NET means it. Max and Min without a context are IEEE 754's maximum
    // and minimum, which give a NaN when either operand is one. The Number forms are the
    // specification's max and min, which hand back the number standing beside a quiet NaN
    // -- and so are the overloads taking a context, which the corpus is written against.

    public static Decimal32 Max(Decimal32 left, Decimal32 right) => SelectOrNaN(left, right, true, false);

    public static Decimal32 MaxNumber(Decimal32 left, Decimal32 right)
    {
        var context = new Decimal32Context();
        return Max(left, right, ref context);
    }

    public static Decimal32 Min(Decimal32 left, Decimal32 right) => SelectOrNaN(left, right, false, false);

    public static Decimal32 MinNumber(Decimal32 left, Decimal32 right)
    {
        var context = new Decimal32Context();
        return Min(left, right, ref context);
    }

    public static Decimal32 MaxMagnitude(Decimal32 left, Decimal32 right) => SelectOrNaN(left, right, true, true);

    public static Decimal32 MaxMagnitudeNumber(Decimal32 left, Decimal32 right)
    {
        var context = new Decimal32Context();
        return MaxMagnitude(left, right, ref context);
    }

    public static Decimal32 MinMagnitude(Decimal32 left, Decimal32 right) => SelectOrNaN(left, right, false, true);

    public static Decimal32 MinMagnitudeNumber(Decimal32 left, Decimal32 right)
    {
        var context = new Decimal32Context();
        return MinMagnitude(left, right, ref context);
    }

    private static Decimal32 SelectOrNaN(Decimal32 left, Decimal32 right, bool wantLarger, bool byMagnitude)
    {
        var status = Decimal32Status.None;
        if (Decimal32Encoding.IsNaN(left._bits) || Decimal32Encoding.IsNaN(right._bits))
        {
            return new Decimal32(Decimal32Arithmetic.PropagateNaN(left._bits, right._bits, ref status));
        }

        return new Decimal32(Decimal32Ordering.Select(left._bits, right._bits, wantLarger, byMagnitude, ref status));
    }

    /// <summary>
    /// Where the value sits against zero: -1, 0, or 1. A NaN has no place on that line and
    /// throws, as it does for the built-in floating-point types.
    /// </summary>
    public static int Sign(Decimal32 value)
    {
        if (IsNaN(value))
        {
            throw new ArithmeticException("A NaN has no sign.");
        }

        if (IsZero(value))
        {
            return 0;
        }

        return IsNegative(value)
            ? -1
            : 1;
    }

    /// <summary>
    /// The value held between two bounds. A NaN passes through unchanged; bounds the wrong
    /// way round are an error rather than a silent swap.
    /// </summary>
    public static Decimal32 Clamp(Decimal32 value, Decimal32 min, Decimal32 max)
    {
        if (min > max)
        {
            throw new ArgumentException($"'{min}' cannot be greater than '{max}'.", nameof(min));
        }

        if (value < min)
        {
            return min;
        }

        if (value > max)
        {
            return max;
        }

        return value;
    }

    // Rounding to an integer and to a given number of places. The mode a caller does not
    // name is half to even, which is what the rest of .NET rounds to.

    public static Decimal32 Round(Decimal32 value) => Round(value, 0, MidpointRounding.ToEven);

    public static Decimal32 Round(Decimal32 value, int digits) => Round(value, digits, MidpointRounding.ToEven);

    public static Decimal32 Round(Decimal32 value, MidpointRounding mode) => Round(value, 0, mode);

    public static Decimal32 Round(Decimal32 value, int digits, MidpointRounding mode) =>
        new(Decimal32Shaping.Round(value._bits, digits, FromMidpointRounding(mode)));

    /// <summary>The value rounded toward positive infinity.</summary>
    public static Decimal32 Ceiling(Decimal32 value) => Round(value, 0, MidpointRounding.ToPositiveInfinity);

    /// <summary>The value rounded toward negative infinity.</summary>
    public static Decimal32 Floor(Decimal32 value) => Round(value, 0, MidpointRounding.ToNegativeInfinity);

    /// <summary>The value with its fractional part dropped, so rounded toward zero.</summary>
    public static Decimal32 Truncate(Decimal32 value) => Round(value, 0, MidpointRounding.ToZero);

    /// <summary>
    /// The value as an integer type, truncated toward zero and clamped to that type's
    /// range. A NaN converts to zero, which is what converting a double gives.
    /// </summary>
    public static TInteger ConvertToInteger<TInteger>(Decimal32 value)
        where TInteger : IBinaryInteger<TInteger> => TInteger.CreateSaturating(value);

    /// <summary>
    /// The same conversion. The two differ for the built-in types, where the native form
    /// leaves an out-of-range value to the hardware; here there is nothing to leave to it.
    /// </summary>
    public static TInteger ConvertToIntegerNative<TInteger>(Decimal32 value)
        where TInteger : IBinaryInteger<TInteger> => TInteger.CreateSaturating(value);

    // Text.

    public string ToString(string? format, IFormatProvider? provider) =>
        Decimal32Formatter.Format(_bits, format, provider);

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format,
        IFormatProvider? provider) =>
        Decimal32Formatter.TryFormat(_bits, destination, out charsWritten, format, provider);

    // The overloads that carry a culture read that culture's separators, signs, and
    // symbols; the ones above carry none and read the specification's grammar, which is
    // invariant by definition. The pair a caller picks has to match on both sides -- the
    // text of a value under a culture is only a number to that same culture.

    public static Decimal32 Parse(string s, IFormatProvider? provider) =>
        Parse(s, Decimal32CultureNormalizer.DefaultStyles, provider);

    public static Decimal32 Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        Parse(s, Decimal32CultureNormalizer.DefaultStyles, provider);

    public static Decimal32 Parse(string s, NumberStyles style, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return Parse(s.AsSpan(), style, provider);
    }

    public static Decimal32 Parse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider)
    {
        if (!TryParse(s, style, provider, out var value))
        {
            throw new FormatException($"'{s}' is not a Decimal32.");
        }

        return value;
    }

    public static bool TryParse(string? s, IFormatProvider? provider, out Decimal32 result) =>
        TryParse(s, Decimal32CultureNormalizer.DefaultStyles, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Decimal32 result) =>
        TryParse(s, Decimal32CultureNormalizer.DefaultStyles, provider, out result);

    public static bool TryParse(string? s, NumberStyles style, IFormatProvider? provider, out Decimal32 result)
    {
        Decimal32CultureNormalizer.ValidateStyles(style, nameof(style));

        if (s is null)
        {
            result = Zero;
            return false;
        }

        return TryParse(s.AsSpan(), style, provider, out result);
    }

    public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider,
        out Decimal32 result)
    {
        Decimal32CultureNormalizer.ValidateStyles(style, nameof(style));

        var status = Decimal32Status.None;
        var bits = Decimal32Parser.Parse(s, style, provider, Decimal32Rounding.HalfEven, ref status);
        return Reported(bits, status, out result);
    }

    private static bool Reported(uint bits, Decimal32Status status, out Decimal32 result)
    {
        if ((status & Decimal32Status.ConversionSyntax) != 0)
        {
            result = Zero;
            return false;
        }

        result = new Decimal32(bits);
        return true;
    }

    // The same text as UTF-8, for callers working in bytes.

    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format,
        IFormatProvider? provider) =>
        Decimal32Formatter.TryFormat(_bits, utf8Destination, out bytesWritten, format, provider);

    public static Decimal32 Parse(ReadOnlySpan<byte> utf8Text)
    {
        if (!TryParse(utf8Text, out var value))
        {
            throw new FormatException($"'{Encoding.UTF8.GetString(utf8Text)}' is not a Decimal32.");
        }

        return value;
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, out Decimal32 value)
    {
        var status = Decimal32Status.None;
        var bits = Decimal32Parser.Parse(utf8Text, Decimal32Rounding.HalfEven, ref status);
        return Reported(bits, status, out value);
    }

    public static Decimal32 Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider)
    {
        if (!TryParse(utf8Text, provider, out var value))
        {
            throw new FormatException($"'{Encoding.UTF8.GetString(utf8Text)}' is not a Decimal32.");
        }

        return value;
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out Decimal32 result)
    {
        var status = Decimal32Status.None;
        var bits = Decimal32Parser.Parse(utf8Text, Decimal32CultureNormalizer.DefaultStyles, provider,
            Decimal32Rounding.HalfEven, ref status);

        return Reported(bits, status, out result);
    }

    // The exponent and the coefficient, for callers that want the parts as bytes.

    int IFloatingPoint<Decimal32>.GetExponentByteCount() => sizeof(int);

    int IFloatingPoint<Decimal32>.GetExponentShortestBitLength()
    {
        var exponent = Exponent(_bits);
        return exponent >= 0
            ? (sizeof(int) * 8) - int.LeadingZeroCount(exponent)
            : (sizeof(int) * 8) + 1 - int.LeadingZeroCount(~exponent);
    }

    int IFloatingPoint<Decimal32>.GetSignificandByteCount() => sizeof(uint);

    int IFloatingPoint<Decimal32>.GetSignificandBitLength() => sizeof(uint) * 8;

    bool IFloatingPoint<Decimal32>.TryWriteExponentBigEndian(Span<byte> destination, out int written) =>
        TryWrite(Exponent(_bits), destination, out written, true);

    bool IFloatingPoint<Decimal32>.TryWriteExponentLittleEndian(Span<byte> destination, out int written) =>
        TryWrite(Exponent(_bits), destination, out written, false);

    bool IFloatingPoint<Decimal32>.TryWriteSignificandBigEndian(Span<byte> destination, out int written) =>
        TryWrite(Coefficient(_bits), destination, out written, true);

    bool IFloatingPoint<Decimal32>.TryWriteSignificandLittleEndian(Span<byte> destination, out int written) =>
        TryWrite(Coefficient(_bits), destination, out written, false);

    private static int Exponent(uint bits)
    {
        if (Decimal32Encoding.IsSpecial(bits))
        {
            return 0;
        }

        Decimal32Encoding.Unpack(bits, out var exponent);
        return exponent;
    }

    private static uint Coefficient(uint bits)
    {
        if (Decimal32Encoding.IsSpecial(bits))
        {
            return Decimal32Encoding.IsNaN(bits) ? Decimal32Encoding.Payload(bits) : 0;
        }

        return Decimal32Encoding.Unpack(bits, out _);
    }

    // Conversions to and from the other numeric types.

    static bool INumberBase<Decimal32>.TryConvertFromChecked<TOther>(TOther value, out Decimal32 result) =>
        TryConvertFrom(value, out result);

    static bool INumberBase<Decimal32>.TryConvertFromSaturating<TOther>(TOther value, out Decimal32 result) =>
        TryConvertFrom(value, out result);

    static bool INumberBase<Decimal32>.TryConvertFromTruncating<TOther>(TOther value, out Decimal32 result) =>
        TryConvertFrom(value, out result);

    static bool INumberBase<Decimal32>.TryConvertToChecked<TOther>(Decimal32 value, out TOther result)
        where TOther : default => TryConvertTo(value, out result);

    static bool INumberBase<Decimal32>.TryConvertToSaturating<TOther>(Decimal32 value, out TOther result)
        where TOther : default => TryConvertTo(value, out result);

    static bool INumberBase<Decimal32>.TryConvertToTruncating<TOther>(Decimal32 value, out TOther result)
        where TOther : default => TryConvertTo(value, out result);

    // Conversions. Seven digits is not much, so only the integer types up to five digits
    // widen implicitly; everything else is explicit, because it can round, overflow to an
    // infinity, or underflow to a subnormal. Conversions to an integer truncate toward zero
    // and throw when the value will not fit, which is what System.Decimal does; the
    // generic-math CreateSaturating and CreateTruncating are there for the other two
    // behaviors.

    public static implicit operator Decimal32(sbyte value) => new(Decimal32Conversions.FromInt64(value));

    public static implicit operator Decimal32(byte value) => new(Decimal32Conversions.FromUInt64(value, false));

    public static implicit operator Decimal32(short value) => new(Decimal32Conversions.FromInt64(value));

    public static implicit operator Decimal32(ushort value) => new(Decimal32Conversions.FromUInt64(value, false));

    public static implicit operator Decimal32(char value) => new(Decimal32Conversions.FromUInt64(value, false));

    // Ten digits and up, against the seven this format holds.
    public static explicit operator Decimal32(int value) => new(Decimal32Conversions.FromInt64(value));

    public static explicit operator Decimal32(uint value) => new(Decimal32Conversions.FromUInt64(value, false));

    public static explicit operator Decimal32(long value) => new(Decimal32Conversions.FromInt64(value));

    public static explicit operator Decimal32(ulong value) => new(Decimal32Conversions.FromUInt64(value, false));

    public static explicit operator Decimal32(Half value) => new(Decimal32Conversions.FromBinary(value));

    public static explicit operator Decimal32(float value) => new(Decimal32Conversions.FromBinary(value));

    public static explicit operator Decimal32(double value) => new(Decimal32Conversions.FromBinary(value));

    /// <summary>
    /// Reads a binary floating-point value the way <paramref name="conversion"/> asks for.
    /// The cast operators above take the shortest reading; this is how to ask for IEEE
    /// 754's convertFormat instead, which gives the binary value itself.
    /// </summary>
    public static Decimal32 FromBinary(double value, Decimal32BinaryConversion conversion) =>
        new(Decimal32Conversions.FromBinary(value, conversion));

    public static Decimal32 FromBinary(float value, Decimal32BinaryConversion conversion) =>
        new(Decimal32Conversions.FromBinary(value, conversion));

    public static Decimal32 FromBinary(Half value, Decimal32BinaryConversion conversion) =>
        new(Decimal32Conversions.FromBinary(value, conversion));

    public static explicit operator sbyte(Decimal32 value) => Decimal32Conversions.ToInteger<sbyte>(value._bits);

    public static explicit operator byte(Decimal32 value) => Decimal32Conversions.ToInteger<byte>(value._bits);

    public static explicit operator short(Decimal32 value) => Decimal32Conversions.ToInteger<short>(value._bits);

    public static explicit operator ushort(Decimal32 value) => Decimal32Conversions.ToInteger<ushort>(value._bits);

    public static explicit operator char(Decimal32 value) => (char)Decimal32Conversions.ToInteger<ushort>(value._bits);

    public static explicit operator int(Decimal32 value) => Decimal32Conversions.ToInteger<int>(value._bits);

    public static explicit operator uint(Decimal32 value) => Decimal32Conversions.ToInteger<uint>(value._bits);

    public static explicit operator long(Decimal32 value) => Decimal32Conversions.ToInteger<long>(value._bits);

    public static explicit operator ulong(Decimal32 value) => Decimal32Conversions.ToInteger<ulong>(value._bits);

    public static explicit operator Half(Decimal32 value) => (Half)Decimal32Formatter.ToDouble(value._bits);

    public static explicit operator float(Decimal32 value) => (float)Decimal32Formatter.ToDouble(value._bits);

    public static explicit operator double(Decimal32 value) => Decimal32Formatter.ToDouble(value._bits);

    private static bool TryConvertFrom<TOther>(TOther value, out Decimal32 result)
        where TOther : INumberBase<TOther>
    {
        var converted = Decimal32Conversions.TryFrom(value, out var bits);
        result = new Decimal32(bits);
        return converted;
    }

    private static bool TryConvertTo<TOther>(Decimal32 value, out TOther result)
        where TOther : INumberBase<TOther>?
    {
        var converted = Decimal32Conversions.TryTo<TOther>(value._bits, out var narrowed);
        result = narrowed!;
        return converted;
    }

    private static bool TryWrite(int exponent, Span<byte> destination, out int written, bool bigEndian)
    {
        written = sizeof(int);
        if (destination.Length < written)
        {
            written = 0;
            return false;
        }

        return bigEndian
            ? BinaryPrimitives.TryWriteInt32BigEndian(destination, exponent)
            : BinaryPrimitives.TryWriteInt32LittleEndian(destination, exponent);
    }

    private static bool TryWrite(uint coefficient, Span<byte> destination, out int written, bool bigEndian)
    {
        written = sizeof(uint);
        if (destination.Length < written)
        {
            written = 0;
            return false;
        }

        return bigEndian
            ? BinaryPrimitives.TryWriteUInt32BigEndian(destination, coefficient)
            : BinaryPrimitives.TryWriteUInt32LittleEndian(destination, coefficient);
    }

    private static Decimal32Rounding FromMidpointRounding(MidpointRounding mode) => mode switch
    {
        MidpointRounding.AwayFromZero => Decimal32Rounding.HalfUp,
        MidpointRounding.ToZero => Decimal32Rounding.Down,
        MidpointRounding.ToPositiveInfinity => Decimal32Rounding.Ceiling,
        MidpointRounding.ToNegativeInfinity => Decimal32Rounding.Floor,
        _ => Decimal32Rounding.HalfEven
    };

    /// <summary>
    /// Numeric equality, so 1.0 equals 1.00 and the two zeros are equal. NaN equals NaN
    /// here, unlike under <c>==</c>, so a value can be found in a collection.
    /// <see cref="CompareTotal"/> is the bit-level ordering; <see cref="ToBits"/> the
    /// bits themselves.
    /// </summary>
    public bool Equals(Decimal32 other)
    {
        var thisNaN = Decimal32Encoding.IsNaN(_bits);
        var otherNaN = Decimal32Encoding.IsNaN(other._bits);
        if (thisNaN || otherNaN)
        {
            return thisNaN && otherNaN;
        }

        return Decimal32Ordering.CompareValues(_bits, other._bits) == 0;
    }

    public override bool Equals(object? other) => other is Decimal32 value && Equals(value);

    /// <summary>Hashes the value, so members of one cohort agree.</summary>
    public override int GetHashCode() => Decimal32Ordering.ValueHashCode(_bits);

    /// <summary>Orders for sorting: NaNs sort below everything and equal each other.</summary>
    public int CompareTo(Decimal32 other)
    {
        var thisNaN = Decimal32Encoding.IsNaN(_bits);
        var otherNaN = Decimal32Encoding.IsNaN(other._bits);
        if (thisNaN || otherNaN)
        {
            if (thisNaN && otherNaN)
            {
                return 0;
            }

            return thisNaN ? -1 : 1;
        }

        return Decimal32Ordering.CompareValues(_bits, other._bits);
    }

    public int CompareTo(object? other)
    {
        if (other is null)
        {
            return 1;
        }

        if (other is not Decimal32 value)
        {
            throw new ArgumentException("Expected a Decimal32.", nameof(other));
        }

        return CompareTo(value);
    }

    // Numeric comparison, which leaves a NaN unordered: every one of these is false when
    // either side is a NaN, including a NaN against itself.

    private static bool Ordered(Decimal32 left, Decimal32 right, out int comparison)
    {
        if (Decimal32Encoding.IsNaN(left._bits) || Decimal32Encoding.IsNaN(right._bits))
        {
            comparison = 0;
            return false;
        }

        comparison = Decimal32Ordering.CompareValues(left._bits, right._bits);
        return true;
    }

    public static bool operator ==(Decimal32 left, Decimal32 right) =>
        Ordered(left, right, out var comparison) && comparison == 0;

    public static bool operator !=(Decimal32 left, Decimal32 right) => !(left == right);

    public static bool operator <(Decimal32 left, Decimal32 right) =>
        Ordered(left, right, out var comparison) && comparison < 0;

    public static bool operator >(Decimal32 left, Decimal32 right) =>
        Ordered(left, right, out var comparison) && comparison > 0;

    public static bool operator <=(Decimal32 left, Decimal32 right) =>
        Ordered(left, right, out var comparison) && comparison <= 0;

    public static bool operator >=(Decimal32 left, Decimal32 right) =>
        Ordered(left, right, out var comparison) && comparison >= 0;

    public static Decimal32 operator ++(Decimal32 value) => value + One;

    public static Decimal32 operator --(Decimal32 value) => value - One;
}
