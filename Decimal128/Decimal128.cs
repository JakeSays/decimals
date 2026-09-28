// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using Decimals.Internal;

namespace Decimals;

/// <summary>
/// An IEEE 754 decimal128 value: 34 digits of coefficient, exponents from -6176 to +6111,
/// held as a binary-integer encoding and computed on with two machine words.
/// </summary>
/// <remarks>
/// <para>
/// This type stands on its own. Its arithmetic, text, conversions, and elementary
/// functions depend on nothing outside this namespace, and nothing in it computes with a
/// 128-bit integer type or an arbitrary-precision integer type: the coefficient is two
/// words, the intermediates that outgrow them are four, and the elementary functions run
/// on a unit-array engine ported from decNumber. The one place a <see cref="UInt128"/>
/// appears is at the edge, as the shape the raw bits are handed in and out in.
/// </para>
/// <para>
/// The in-memory encoding is BID, the binary-integer form, because that is what makes the
/// arithmetic fast: the coefficient is reached with a mask. The densely-packed interchange
/// form that decimal hardware and decNumber exchange is a conversion away through
/// <see cref="ToDpdBits"/> and <see cref="FromDpdBits"/>.
/// </para>
/// </remarks>
public readonly struct Decimal128
    : IFloatingPoint<Decimal128>,
      IMinMaxValue<Decimal128>,
      IExponentialFunctions<Decimal128>,
      ILogarithmicFunctions<Decimal128>,
      IPowerFunctions<Decimal128>,
      IRootFunctions<Decimal128>,
      IUtf8SpanFormattable,
      IUtf8SpanParsable<Decimal128>
{
    private readonly Decimal128Integer _bits;

    private Decimal128(Decimal128Integer bits)
    {
        _bits = bits;
    }

    /// <summary>The bits as the arithmetic holds them, for the code in this assembly.</summary>
    internal Decimal128Integer Bits => _bits;

    internal static Decimal128 FromInternal(Decimal128Integer bits)
    {
        return new Decimal128(bits);
    }

    public static Decimal128 Zero => new(Decimal128Encoding.Zero(false, 0));

    public static Decimal128 NegativeZero => new(Decimal128Encoding.Zero(true, 0));

    public static Decimal128 One => new(Decimal128Encoding.Pack(false, 0, Decimal128Integer.One));

    public static Decimal128 NegativeOne => new(Decimal128Encoding.Pack(true, 0, Decimal128Integer.One));

    public static Decimal128 MaxValue =>
        new(Decimal128Encoding.Pack(false, Decimal128Encoding.MaxQuantumExponent, Decimal128Encoding.MaxCoefficient));

    public static Decimal128 MinValue =>
        new(Decimal128Encoding.Pack(true, Decimal128Encoding.MaxQuantumExponent, Decimal128Encoding.MaxCoefficient));

    /// <summary>The smallest positive value, which is subnormal: 1E-6176.</summary>
    public static Decimal128 Epsilon =>
        new(Decimal128Encoding.Pack(false, Decimal128Encoding.MinQuantumExponent, Decimal128Integer.One));

    public static Decimal128 PositiveInfinity => new(Decimal128Encoding.Infinity(false));

    public static Decimal128 NegativeInfinity => new(Decimal128Encoding.Infinity(true));

    public static Decimal128 NaN => new(Decimal128Encoding.QuietNaN());

    /// <summary>The raw encoding, which is BID.</summary>
    public UInt128 ToBits() => new(_bits.High, _bits.Low);

    public static Decimal128 FromBits(UInt128 bits) => new(new Decimal128Integer((ulong)(bits >> 64), (ulong)bits));

    /// <summary>
    /// The binary-integer-decimal encoding, where the coefficient is a plain integer in the
    /// trailing field. This is the in-memory encoding, so it costs nothing.
    /// </summary>
    public UInt128 ToBidBits() => ToBits();

    public static Decimal128 FromBidBits(UInt128 bits) => FromBits(bits);

    /// <summary>
    /// The densely-packed-decimal interchange encoding, which is what decimal hardware and
    /// DPD-based libraries exchange.
    /// </summary>
    public UInt128 ToDpdBits()
    {
        var dpd = Decimal128Dpd.ToDpd(_bits);
        return new UInt128(dpd.High, dpd.Low);
    }

    public static Decimal128 FromDpdBits(UInt128 bits) =>
        new(Decimal128Dpd.FromDpd(new Decimal128Integer((ulong)(bits >> 64), (ulong)bits)));

    public static bool IsNaN(Decimal128 value) => Decimal128Encoding.IsNaN(value._bits);

    public static bool IsSignalingNaN(Decimal128 value) => Decimal128Encoding.IsSignalingNaN(value._bits);

    public static bool IsInfinity(Decimal128 value) => Decimal128Encoding.IsInfinity(value._bits);

    public static bool IsFinite(Decimal128 value) => !Decimal128Encoding.IsSpecial(value._bits);

    public static bool IsNegative(Decimal128 value) => Decimal128Encoding.IsNegative(value._bits);

    public static bool IsPositiveInfinity(Decimal128 value) => IsInfinity(value) && !IsNegative(value);

    public static bool IsNegativeInfinity(Decimal128 value) => IsInfinity(value) && IsNegative(value);

    public static bool IsZero(Decimal128 value) => Decimal128Encoding.IsZero(value._bits);

    public static bool IsSubnormal(Decimal128 value) => Decimal128Ordering.IsSubnormal(value._bits);

    public static bool IsNormal(Decimal128 value) => IsFinite(value) && !IsZero(value) && !IsSubnormal(value);

    /// <summary>
    /// False when the encoding uses a coefficient or payload the format cannot hold, or
    /// sets bits a special value leaves clear. Those decode as zero, so re-encoding does not
    /// give the same bits back.
    /// </summary>
    public static bool IsCanonical(Decimal128 value) => value._bits == Decimal128Encoding.Canonical(value._bits);

    /// <summary>The same value in its canonical encoding.</summary>
    public static Decimal128 Canonical(Decimal128 value) => new(Decimal128Encoding.Canonical(value._bits));

    public static Decimal128Class Class(Decimal128 value) => Decimal128Ordering.Classify(value._bits);

    /// <summary>Copies the value with a cleared sign. Quiet: a signaling NaN stays one.</summary>
    public static Decimal128 CopyAbs(Decimal128 value) => new(Decimal128Ordering.Magnitude(value._bits));

    /// <summary>Copies the value with its sign flipped. Quiet, unlike arithmetic negation.</summary>
    public static Decimal128 CopyNegate(Decimal128 value) =>
        new(new Decimal128Integer(value._bits.High ^ Decimal128Encoding.SignMask, value._bits.Low));

    public static Decimal128 CopySign(Decimal128 value, Decimal128 sign) =>
        new(new Decimal128Integer(
            (value._bits.High & ~Decimal128Encoding.SignMask) | (sign._bits.High & Decimal128Encoding.SignMask),
            value._bits.Low));

    /// <summary>
    /// True when both are finite with the same exponent, or both are infinite, or both are
    /// NaN. Never raises a condition, even for a signaling NaN.
    /// </summary>
    public static bool SameQuantum(Decimal128 left, Decimal128 right)
    {
        if (Decimal128Encoding.IsSpecial(left._bits) || Decimal128Encoding.IsSpecial(right._bits))
        {
            return (Decimal128Encoding.IsNaN(left._bits) && Decimal128Encoding.IsNaN(right._bits))
                || (Decimal128Encoding.IsInfinity(left._bits) && Decimal128Encoding.IsInfinity(right._bits));
        }

        Decimal128Encoding.Unpack(left._bits, out var leftExponent);
        Decimal128Encoding.Unpack(right._bits, out var rightExponent);
        return leftExponent == rightExponent;
    }

    public static int CompareTotal(Decimal128 left, Decimal128 right) =>
        Decimal128Ordering.CompareTotal(left._bits, right._bits);

    public static int CompareTotalMagnitude(Decimal128 left, Decimal128 right) =>
        Decimal128Ordering.CompareTotalMagnitude(left._bits, right._bits);

    /// <summary>
    /// The specification's to-number conversion: a bad string gives a quiet NaN and raises
    /// <see cref="Decimal128Status.ConversionSyntax"/> rather than throwing.
    /// </summary>
    public static Decimal128 FromString(ReadOnlySpan<char> text, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var bits = Decimal128Parser.Parse(text, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(bits);
    }

    public static Decimal128 Parse(ReadOnlySpan<char> text)
    {
        if (!TryParse(text, out var value))
        {
            throw new FormatException($"'{text}' is not a Decimal128.");
        }

        return value;
    }

    public static Decimal128 Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Parse(text.AsSpan());
    }

    public static bool TryParse(ReadOnlySpan<char> text, out Decimal128 value)
    {
        var status = Decimal128Status.None;
        var bits = Decimal128Parser.Parse(text, Decimal128Rounding.HalfEven, ref status);
        if ((status & Decimal128Status.ConversionSyntax) != 0)
        {
            value = Zero;
            return false;
        }

        value = new Decimal128(bits);
        return true;
    }

    public static bool TryParse(string? text, out Decimal128 value)
    {
        if (text is null)
        {
            value = Zero;
            return false;
        }

        return TryParse(text.AsSpan(), out value);
    }

    public override string ToString() => Decimal128Formatter.ToScientificString(_bits);

    public string ToScientificString() => Decimal128Formatter.ToScientificString(_bits);

    public string ToEngineeringString() => Decimal128Formatter.ToEngineeringString(_bits);

    // Arithmetic. The plain form rounds half to even and drops the conditions, which is
    // IEEE 754 default exception handling; the overload taking a context does neither. An
    // operation decides its conditions from a clean slate and adds them to the context, so
    // one operation's conditions never leak into the next one's decisions.

    public static Decimal128 Add(Decimal128 left, Decimal128 right, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Arithmetic.Add(left._bits, right._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    public static Decimal128 Add(Decimal128 left, Decimal128 right)
    {
        var status = Decimal128Status.None;
        return new Decimal128(Decimal128Arithmetic.Add(left._bits, right._bits, Decimal128Rounding.HalfEven, ref status));
    }

    public static Decimal128 Subtract(Decimal128 left, Decimal128 right, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Arithmetic.Subtract(left._bits, right._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    public static Decimal128 Subtract(Decimal128 left, Decimal128 right)
    {
        var status = Decimal128Status.None;
        return new Decimal128(Decimal128Arithmetic.Subtract(left._bits, right._bits, Decimal128Rounding.HalfEven,
            ref status));
    }

    public static Decimal128 Multiply(Decimal128 left, Decimal128 right, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Arithmetic.Multiply(left._bits, right._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    public static Decimal128 Multiply(Decimal128 left, Decimal128 right)
    {
        var status = Decimal128Status.None;
        return new Decimal128(Decimal128Arithmetic.Multiply(left._bits, right._bits, Decimal128Rounding.HalfEven,
            ref status));
    }

    public static Decimal128 Divide(Decimal128 left, Decimal128 right, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Arithmetic.Divide(left._bits, right._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    public static Decimal128 Divide(Decimal128 left, Decimal128 right)
    {
        var status = Decimal128Status.None;
        return new Decimal128(Decimal128Arithmetic.Divide(left._bits, right._bits, Decimal128Rounding.HalfEven,
            ref status));
    }

    /// <summary>The integer part of the quotient, with a zero exponent.</summary>
    public static Decimal128 DivideInteger(Decimal128 left, Decimal128 right, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Arithmetic.DivideInteger(left._bits, right._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    public static Decimal128 DivideInteger(Decimal128 left, Decimal128 right)
    {
        var status = Decimal128Status.None;
        return new Decimal128(Decimal128Arithmetic.DivideInteger(left._bits, right._bits, Decimal128Rounding.HalfEven,
            ref status));
    }

    public static Decimal128 Remainder(Decimal128 left, Decimal128 right, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Arithmetic.Remainder(left._bits, right._bits, false, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    public static Decimal128 Remainder(Decimal128 left, Decimal128 right)
    {
        var status = Decimal128Status.None;
        return new Decimal128(Decimal128Arithmetic.Remainder(left._bits, right._bits, false,
            Decimal128Rounding.HalfEven, ref status));
    }

    /// <summary>IEEE 754's remainder, which takes the quotient to the nearest integer.</summary>
    public static Decimal128 RemainderNear(Decimal128 left, Decimal128 right, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Arithmetic.Remainder(left._bits, right._bits, true, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    public static Decimal128 RemainderNear(Decimal128 left, Decimal128 right)
    {
        var status = Decimal128Status.None;
        return new Decimal128(Decimal128Arithmetic.Remainder(left._bits, right._bits, true,
            Decimal128Rounding.HalfEven, ref status));
    }

    /// <summary>Multiply and add, rounded once rather than twice.</summary>
    public static Decimal128 FusedMultiplyAdd(Decimal128 left, Decimal128 right, Decimal128 addend,
        ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Arithmetic.FusedMultiplyAdd(left._bits, right._bits, addend._bits, context.Rounding,
            ref status);

        context.Status |= status;
        return new Decimal128(result);
    }

    public static Decimal128 FusedMultiplyAdd(Decimal128 left, Decimal128 right, Decimal128 addend)
    {
        var status = Decimal128Status.None;
        return new Decimal128(Decimal128Arithmetic.FusedMultiplyAdd(left._bits, right._bits, addend._bits,
            Decimal128Rounding.HalfEven, ref status));
    }

    /// <summary>Zero plus the value, so the format's rounding is applied.</summary>
    public static Decimal128 Plus(Decimal128 value, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Arithmetic.AddToZero(value._bits, false, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    public static Decimal128 Plus(Decimal128 value)
    {
        var status = Decimal128Status.None;
        return new Decimal128(Decimal128Arithmetic.AddToZero(value._bits, false, Decimal128Rounding.HalfEven, ref status));
    }

    /// <summary>Zero minus the value. Arithmetic, unlike <see cref="CopyNegate"/>.</summary>
    public static Decimal128 Minus(Decimal128 value, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Arithmetic.AddToZero(value._bits, true, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    public static Decimal128 Minus(Decimal128 value)
    {
        var status = Decimal128Status.None;
        return new Decimal128(Decimal128Arithmetic.AddToZero(value._bits, true, Decimal128Rounding.HalfEven, ref status));
    }

    /// <summary>The magnitude, arithmetically. Unlike <see cref="CopyAbs"/> this can signal.</summary>
    public static Decimal128 Abs(Decimal128 value, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var negate = Decimal128Encoding.IsNegative(value._bits) && !Decimal128Encoding.IsNaN(value._bits);
        var result = Decimal128Arithmetic.AddToZero(value._bits, negate, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>
    /// Numeric comparison giving -1, 0, 1, or NaN. Unlike <see cref="CompareTotal"/> the two
    /// zeros are equal and a NaN operand gives a NaN.
    /// </summary>
    public static Decimal128 Compare(Decimal128 left, Decimal128 right, ref Decimal128Context context) =>
        CompareToValue(left, right, false, ref context);

    /// <summary>Comparison that signals on any NaN, not only a signaling one.</summary>
    public static Decimal128 CompareSignal(Decimal128 left, Decimal128 right, ref Decimal128Context context) =>
        CompareToValue(left, right, true, ref context);

    private static Decimal128 CompareToValue(Decimal128 left, Decimal128 right, bool signaling,
        ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var comparison = Decimal128Arithmetic.Compare(left._bits, right._bits, signaling, ref status, out var nan);
        context.Status |= status;

        if (comparison == int.MinValue)
        {
            return new Decimal128(nan);
        }

        return FromComparison(comparison);
    }

    private static Decimal128 FromComparison(int comparison)
    {
        if (comparison < 0)
        {
            return NegativeOne;
        }

        return comparison > 0 ? One : Zero;
    }

    // Digit-wise logical operations. Each operand must be a string of ones and zeros with
    // a zero exponent and no sign; anything else is an invalid operation.

    public static Decimal128 And(Decimal128 left, Decimal128 right, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Logical.And(left._bits, right._bits, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    public static Decimal128 Or(Decimal128 left, Decimal128 right, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Logical.Or(left._bits, right._bits, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    public static Decimal128 Xor(Decimal128 left, Decimal128 right, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Logical.Xor(left._bits, right._bits, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    public static Decimal128 Invert(Decimal128 value, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Logical.Invert(value._bits, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>The larger of the two. A quiet NaN beside a number loses to the number.</summary>
    public static Decimal128 Max(Decimal128 left, Decimal128 right, ref Decimal128Context context) =>
        Select(left, right, true, false, ref context);

    public static Decimal128 Min(Decimal128 left, Decimal128 right, ref Decimal128Context context) =>
        Select(left, right, false, false, ref context);

    public static Decimal128 MaxMagnitude(Decimal128 left, Decimal128 right, ref Decimal128Context context) =>
        Select(left, right, true, true, ref context);

    public static Decimal128 MinMagnitude(Decimal128 left, Decimal128 right, ref Decimal128Context context) =>
        Select(left, right, false, true, ref context);

    private static Decimal128 Select(Decimal128 left, Decimal128 right, bool wantLarger, bool byMagnitude,
        ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Ordering.Select(left._bits, right._bits, wantLarger, byMagnitude, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>The adjusted exponent, as an integer.</summary>
    public static Decimal128 LogB(Decimal128 value, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Shaping.LogB(value._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>Multiplies by ten raised to the second operand.</summary>
    public static Decimal128 ScaleB(Decimal128 value, Decimal128 scale, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Shaping.ScaleB(value._bits, scale._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>Removes trailing zeros, giving the shortest coefficient of the same value.</summary>
    public static Decimal128 Reduce(Decimal128 value, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Shaping.Reduce(value._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>Like <see cref="Reduce"/>, but never past a zero exponent.</summary>
    public static Decimal128 Trim(Decimal128 value) => new(Decimal128Shaping.Trim(value._bits));

    /// <summary>Rounds to an integer without reporting that anything was rounded.</summary>
    public static Decimal128 RoundToIntegral(Decimal128 value, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Shaping.ToIntegral(value._bits, false, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>Rounds to an integer, reporting inexactness.</summary>
    public static Decimal128 RoundToIntegralExact(Decimal128 value, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Shaping.ToIntegral(value._bits, true, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>Rescales to the second operand's exponent.</summary>
    public static Decimal128 Quantize(Decimal128 value, Decimal128 pattern, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Shaping.Quantize(value._bits, pattern._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>Rotates the coefficient's digits within the format's full width.</summary>
    public static Decimal128 Rotate(Decimal128 value, Decimal128 places, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Shaping.RotateOrShift(value._bits, places._bits, true, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>Shifts the coefficient's digits, dropping what falls off the end.</summary>
    public static Decimal128 Shift(Decimal128 value, Decimal128 places, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Shaping.RotateOrShift(value._bits, places._bits, false, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>The square root, correctly rounded.</summary>
    public static Decimal128 Sqrt(Decimal128 value, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128SquareRoot.SquareRoot(value._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    public static Decimal128 Sqrt(Decimal128 value)
    {
        var status = Decimal128Status.None;
        return new Decimal128(Decimal128SquareRoot.SquareRoot(value._bits, Decimal128Rounding.HalfEven, ref status));
    }

    // The elementary functions, each in the context form and the plain form.

    private delegate Decimal128Integer Function(Decimal128Integer value, Decimal128Rounding rounding,
        ref Decimal128Status status);

    private delegate Decimal128Integer FunctionOfTwo(Decimal128Integer left, Decimal128Integer right,
        Decimal128Rounding rounding, ref Decimal128Status status);

    private static Decimal128 Apply(Function function, Decimal128 value, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = function(value._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    private static Decimal128 Apply(Function function, Decimal128 value)
    {
        var status = Decimal128Status.None;
        return new Decimal128(function(value._bits, Decimal128Rounding.HalfEven, ref status));
    }

    private static Decimal128 Apply(FunctionOfTwo function, Decimal128 left, Decimal128 right,
        ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = function(left._bits, right._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    private static Decimal128 Apply(FunctionOfTwo function, Decimal128 left, Decimal128 right)
    {
        var status = Decimal128Status.None;
        return new Decimal128(function(left._bits, right._bits, Decimal128Rounding.HalfEven, ref status));
    }

    /// <summary>e raised to the value.</summary>
    public static Decimal128 Exp(Decimal128 value, ref Decimal128Context context) =>
        Apply(Decimal128Math.Exp, value, ref context);

    public static Decimal128 Exp(Decimal128 value) => Apply(Decimal128Math.Exp, value);

    /// <summary>The natural logarithm.</summary>
    public static Decimal128 Log(Decimal128 value, ref Decimal128Context context) =>
        Apply(Decimal128Math.Log, value, ref context);

    public static Decimal128 Log(Decimal128 value) => Apply(Decimal128Math.Log, value);

    /// <summary>The base-ten logarithm.</summary>
    public static Decimal128 Log10(Decimal128 value, ref Decimal128Context context) =>
        Apply(Decimal128Math.Log10, value, ref context);

    public static Decimal128 Log10(Decimal128 value) => Apply(Decimal128Math.Log10, value);

    /// <summary>The first value raised to the second.</summary>
    public static Decimal128 Pow(Decimal128 value, Decimal128 power, ref Decimal128Context context) =>
        Apply(Decimal128Math.Power, value, power, ref context);

    public static Decimal128 Pow(Decimal128 value, Decimal128 power) => Apply(Decimal128Math.Power, value, power);

    /// <summary>Two raised to the value.</summary>
    public static Decimal128 Exp2(Decimal128 value, ref Decimal128Context context) =>
        Apply(Decimal128Math.Exp2, value, ref context);

    public static Decimal128 Exp2(Decimal128 value) => Apply(Decimal128Math.Exp2, value);

    /// <summary>Ten raised to the value.</summary>
    public static Decimal128 Exp10(Decimal128 value, ref Decimal128Context context) =>
        Apply(Decimal128Math.Exp10, value, ref context);

    public static Decimal128 Exp10(Decimal128 value) => Apply(Decimal128Math.Exp10, value);

    // The M1 and P1 names are the ones the generic math interfaces declare, so they stay as
    // spelled there rather than written out.

    /// <summary>e raised to the value, less one.</summary>
    public static Decimal128 ExpM1(Decimal128 value, ref Decimal128Context context) =>
        Apply(Decimal128Math.ExpMinusOne, value, ref context);

    public static Decimal128 ExpM1(Decimal128 value) => Apply(Decimal128Math.ExpMinusOne, value);

    /// <summary>Two raised to the value, less one.</summary>
    public static Decimal128 Exp2M1(Decimal128 value, ref Decimal128Context context) =>
        Apply(Decimal128Math.Exp2MinusOne, value, ref context);

    public static Decimal128 Exp2M1(Decimal128 value) => Apply(Decimal128Math.Exp2MinusOne, value);

    /// <summary>Ten raised to the value, less one.</summary>
    public static Decimal128 Exp10M1(Decimal128 value, ref Decimal128Context context) =>
        Apply(Decimal128Math.Exp10MinusOne, value, ref context);

    public static Decimal128 Exp10M1(Decimal128 value) => Apply(Decimal128Math.Exp10MinusOne, value);

    /// <summary>The logarithm in the given base.</summary>
    public static Decimal128 Log(Decimal128 value, Decimal128 newBase, ref Decimal128Context context) =>
        Apply(Decimal128Math.LogInBase, value, newBase, ref context);

    public static Decimal128 Log(Decimal128 value, Decimal128 newBase) => Apply(Decimal128Math.LogInBase, value, newBase);

    /// <summary>The base-two logarithm.</summary>
    public static Decimal128 Log2(Decimal128 value, ref Decimal128Context context) =>
        Apply(Decimal128Math.Log2, value, ref context);

    public static Decimal128 Log2(Decimal128 value) => Apply(Decimal128Math.Log2, value);

    /// <summary>The natural logarithm of one plus the value.</summary>
    public static Decimal128 LogP1(Decimal128 value, ref Decimal128Context context) =>
        Apply(Decimal128Math.LogPlusOne, value, ref context);

    public static Decimal128 LogP1(Decimal128 value) => Apply(Decimal128Math.LogPlusOne, value);

    /// <summary>The base-two logarithm of one plus the value.</summary>
    public static Decimal128 Log2P1(Decimal128 value, ref Decimal128Context context) =>
        Apply(Decimal128Math.Log2PlusOne, value, ref context);

    public static Decimal128 Log2P1(Decimal128 value) => Apply(Decimal128Math.Log2PlusOne, value);

    /// <summary>The base-ten logarithm of one plus the value.</summary>
    public static Decimal128 Log10P1(Decimal128 value, ref Decimal128Context context) =>
        Apply(Decimal128Math.Log10PlusOne, value, ref context);

    public static Decimal128 Log10P1(Decimal128 value) => Apply(Decimal128Math.Log10PlusOne, value);

    /// <summary>The cube root.</summary>
    public static Decimal128 Cbrt(Decimal128 value, ref Decimal128Context context) =>
        Apply(Decimal128Math.Cbrt, value, ref context);

    public static Decimal128 Cbrt(Decimal128 value) => Apply(Decimal128Math.Cbrt, value);

    /// <summary>The root of the given degree.</summary>
    public static Decimal128 RootN(Decimal128 value, int degree, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Math.RootN(value._bits, degree, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    public static Decimal128 RootN(Decimal128 value, int degree)
    {
        var status = Decimal128Status.None;
        return new Decimal128(Decimal128Math.RootN(value._bits, degree, Decimal128Rounding.HalfEven, ref status));
    }

    /// <summary>The square root of the sum of two squares, without overflowing on the way.</summary>
    public static Decimal128 Hypot(Decimal128 left, Decimal128 right, ref Decimal128Context context) =>
        Apply(Decimal128Math.Hypot, left, right, ref context);

    public static Decimal128 Hypot(Decimal128 left, Decimal128 right) => Apply(Decimal128Math.Hypot, left, right);

    /// <summary>The next value toward positive infinity.</summary>
    public static Decimal128 NextPlus(Decimal128 value, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Shaping.Next(value._bits, true, true, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>The next value toward negative infinity.</summary>
    public static Decimal128 NextMinus(Decimal128 value, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Shaping.Next(value._bits, false, true, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>The next value from the first operand in the direction of the second.</summary>
    public static Decimal128 NextToward(Decimal128 value, Decimal128 target, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Shaping.NextToward(value._bits, target._bits, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    // Four operations under the names .NET spells them with. Each is one of the operations
    // above, so code written against the built-in floating-point types finds them here.

    /// <summary>IEEE 754's remainder, which is <see cref="RemainderNear(Decimal128, Decimal128)"/>.</summary>
    public static Decimal128 Ieee754Remainder(Decimal128 left, Decimal128 right) => RemainderNear(left, right);

    /// <summary>
    /// The adjusted exponent as an integer, which is <see cref="LogB"/> narrowed to one.
    /// The results off the end of the range are .NET's rather than the specification's: a
    /// zero gives <see cref="int.MinValue"/> and a NaN or an infinity
    /// <see cref="int.MaxValue"/>, where logb gives -Infinity, a NaN, and +Infinity.
    /// </summary>
    public static int ILogB(Decimal128 value) => Decimal128Shaping.ILogB(value._bits);

    /// <summary>
    /// Multiplies by ten raised to <paramref name="scale"/>. The radix is this format's, so
    /// this is the specification's scaleb and not double's power of two.
    /// </summary>
    public static Decimal128 ScaleB(Decimal128 value, int scale)
    {
        var status = Decimal128Status.None;
        return new Decimal128(Decimal128Shaping.ScaleB(value._bits, scale, Decimal128Rounding.HalfEven, ref status));
    }

    /// <summary>The next value toward positive infinity, which is <see cref="NextPlus"/>.</summary>
    public static Decimal128 BitIncrement(Decimal128 value)
    {
        var status = Decimal128Status.None;
        return new Decimal128(Decimal128Shaping.Next(value._bits, true, true, ref status));
    }

    /// <summary>The next value toward negative infinity, which is <see cref="NextMinus"/>.</summary>
    public static Decimal128 BitDecrement(Decimal128 value)
    {
        var status = Decimal128Status.None;
        return new Decimal128(Decimal128Shaping.Next(value._bits, false, true, ref status));
    }

    public static Decimal128 operator +(Decimal128 left, Decimal128 right) => Add(left, right);

    public static Decimal128 operator -(Decimal128 left, Decimal128 right) => Subtract(left, right);

    public static Decimal128 operator *(Decimal128 left, Decimal128 right) => Multiply(left, right);

    public static Decimal128 operator /(Decimal128 left, Decimal128 right) => Divide(left, right);

    public static Decimal128 operator %(Decimal128 left, Decimal128 right) => Remainder(left, right);

    public static Decimal128 operator -(Decimal128 value) => Minus(value);

    public static Decimal128 operator +(Decimal128 value) => Plus(value);

    // Generic math. Numeric comparison here, not the total order: 1.0 equals 1.00, the two
    // zeros are equal, and a NaN is unordered against everything.

    static Decimal128 INumberBase<Decimal128>.Zero => Zero;

    static Decimal128 INumberBase<Decimal128>.One => One;

    static Decimal128 ISignedNumber<Decimal128>.NegativeOne => NegativeOne;

    /// <summary>Ten. This is a decimal format, not a binary one.</summary>
    public static int Radix => 10;

    /// <summary>Converts from another numeric type, refusing a value the format cannot hold.</summary>
    public static Decimal128 CreateChecked<TOther>(TOther value)
        where TOther : INumberBase<TOther>
    {
        if (TryConvertFrom(value, out var result))
        {
            return result;
        }

        if (TOther.TryConvertToChecked<Decimal128>(value, out var other))
        {
            return other;
        }

        throw new NotSupportedException($"Cannot convert {typeof(TOther)} to Decimal128.");
    }

    /// <summary>Converts from another numeric type, clamping to the format's range.</summary>
    public static Decimal128 CreateSaturating<TOther>(TOther value)
        where TOther : INumberBase<TOther>
    {
        if (TryConvertFrom(value, out var result))
        {
            return result;
        }

        if (TOther.TryConvertToSaturating<Decimal128>(value, out var other))
        {
            return other;
        }

        throw new NotSupportedException($"Cannot convert {typeof(TOther)} to Decimal128.");
    }

    /// <summary>Converts from another numeric type, keeping the low-order part.</summary>
    public static Decimal128 CreateTruncating<TOther>(TOther value)
        where TOther : INumberBase<TOther>
    {
        if (TryConvertFrom(value, out var result))
        {
            return result;
        }

        if (TOther.TryConvertToTruncating<Decimal128>(value, out var other))
        {
            return other;
        }

        throw new NotSupportedException($"Cannot convert {typeof(TOther)} to Decimal128.");
    }

    static Decimal128 IAdditiveIdentity<Decimal128, Decimal128>.AdditiveIdentity => Zero;

    static Decimal128 IMultiplicativeIdentity<Decimal128, Decimal128>.MultiplicativeIdentity => One;

    static Decimal128 IMinMaxValue<Decimal128>.MinValue => MinValue;

    static Decimal128 IMinMaxValue<Decimal128>.MaxValue => MaxValue;

    // The constants are their encodings, not text to be parsed at startup: a thirty-four
    // digit coefficient at an exponent of -33.

    /// <summary>The base of natural logarithms: 2.718281828459045235360287471352662.</summary>
    public static Decimal128 E =>
        new(Decimal128Encoding.Pack(false, -33, new Decimal128Integer(0x000086058A4BF4DE, 0x4E906ACCB26ABB56)));

    /// <summary>The ratio of a circle's circumference to its diameter: 3.141592653589793238462643383279503.</summary>
    public static Decimal128 Pi =>
        new(Decimal128Encoding.Pack(false, -33, new Decimal128Integer(0x00009AE4795796A7, 0xBABE5564E6F39F8F)));

    /// <summary>Two Pi: 6.283185307179586476925286766559006.</summary>
    public static Decimal128 Tau =>
        new(Decimal128Encoding.Pack(false, -33, new Decimal128Integer(0x000135C8F2AF2D4F, 0x757CAAC9CDE73F1E)));

    public static Decimal128 Abs(Decimal128 value) => CopyAbs(value);

    public static bool IsInteger(Decimal128 value)
    {
        if (Decimal128Encoding.IsSpecial(value._bits))
        {
            return false;
        }

        var coefficient = Decimal128Encoding.Unpack(value._bits, out var exponent);
        return exponent >= 0 || FractionIsZero(coefficient, exponent);
    }

    public static bool IsEvenInteger(Decimal128 value) => HasParity(value, 0);

    public static bool IsOddInteger(Decimal128 value) => HasParity(value, 1);

    private static bool HasParity(Decimal128 value, ulong wanted)
    {
        if (!IsInteger(value))
        {
            return false;
        }

        var coefficient = Decimal128Encoding.Unpack(value._bits, out var exponent);
        if (coefficient.IsZero || exponent > 0)
        {
            // A positive exponent means trailing zeros, so the value is even whatever the
            // coefficient's last digit is.
            return wanted == 0;
        }

        var whole = exponent == 0
            ? coefficient
            : Decimal128Tables.DivRemWidePowerOfTen(coefficient, -exponent, out _);

        return (whole.Low & 1) == wanted;
    }

    private static bool FractionIsZero(Decimal128Integer coefficient, int exponent)
    {
        if (-exponent > Decimal128Tables.MaxWidePower)
        {
            return coefficient.IsZero;
        }

        Decimal128Tables.DivRemWidePowerOfTen(coefficient, -exponent, out var fraction);
        return fraction.IsZero;
    }

    public static bool IsPositive(Decimal128 value) => !IsNegative(value);

    public static bool IsRealNumber(Decimal128 value) => !IsNaN(value);

    public static bool IsComplexNumber(Decimal128 value) => false;

    public static bool IsImaginaryNumber(Decimal128 value) => false;

    // Selection, as .NET means it. Max and Min without a context are IEEE 754's maximum
    // and minimum, which give a NaN when either operand is one. The Number forms are the
    // specification's max and min, which hand back the number standing beside a quiet NaN
    // -- and so are the overloads taking a context, which the corpus is written against.

    public static Decimal128 Max(Decimal128 left, Decimal128 right) => SelectOrNaN(left, right, true, false);

    public static Decimal128 MaxNumber(Decimal128 left, Decimal128 right)
    {
        var context = new Decimal128Context();
        return Max(left, right, ref context);
    }

    public static Decimal128 Min(Decimal128 left, Decimal128 right) => SelectOrNaN(left, right, false, false);

    public static Decimal128 MinNumber(Decimal128 left, Decimal128 right)
    {
        var context = new Decimal128Context();
        return Min(left, right, ref context);
    }

    public static Decimal128 MaxMagnitude(Decimal128 left, Decimal128 right) => SelectOrNaN(left, right, true, true);

    public static Decimal128 MaxMagnitudeNumber(Decimal128 left, Decimal128 right)
    {
        var context = new Decimal128Context();
        return MaxMagnitude(left, right, ref context);
    }

    public static Decimal128 MinMagnitude(Decimal128 left, Decimal128 right) => SelectOrNaN(left, right, false, true);

    public static Decimal128 MinMagnitudeNumber(Decimal128 left, Decimal128 right)
    {
        var context = new Decimal128Context();
        return MinMagnitude(left, right, ref context);
    }

    private static Decimal128 SelectOrNaN(Decimal128 left, Decimal128 right, bool wantLarger, bool byMagnitude)
    {
        var status = Decimal128Status.None;
        if (Decimal128Encoding.IsNaN(left._bits) || Decimal128Encoding.IsNaN(right._bits))
        {
            return new Decimal128(Decimal128Arithmetic.PropagateNaN(left._bits, right._bits, ref status));
        }

        return new Decimal128(Decimal128Ordering.Select(left._bits, right._bits, wantLarger, byMagnitude, ref status));
    }

    /// <summary>
    /// Where the value sits against zero: -1, 0, or 1. A NaN has no place on that line and
    /// throws, as it does for the built-in floating-point types.
    /// </summary>
    public static int Sign(Decimal128 value)
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
    public static Decimal128 Clamp(Decimal128 value, Decimal128 min, Decimal128 max)
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

    public static Decimal128 Round(Decimal128 value) => Round(value, 0, MidpointRounding.ToEven);

    public static Decimal128 Round(Decimal128 value, int digits) => Round(value, digits, MidpointRounding.ToEven);

    public static Decimal128 Round(Decimal128 value, MidpointRounding mode) => Round(value, 0, mode);

    public static Decimal128 Round(Decimal128 value, int digits, MidpointRounding mode) =>
        new(Decimal128Shaping.Round(value._bits, digits, FromMidpointRounding(mode)));

    /// <summary>The value rounded toward positive infinity.</summary>
    public static Decimal128 Ceiling(Decimal128 value) => Round(value, 0, MidpointRounding.ToPositiveInfinity);

    /// <summary>The value rounded toward negative infinity.</summary>
    public static Decimal128 Floor(Decimal128 value) => Round(value, 0, MidpointRounding.ToNegativeInfinity);

    /// <summary>The value with its fractional part dropped, so rounded toward zero.</summary>
    public static Decimal128 Truncate(Decimal128 value) => Round(value, 0, MidpointRounding.ToZero);

    /// <summary>
    /// The value as an integer type, truncated toward zero and clamped to that type's
    /// range. A NaN converts to zero, which is what converting a double gives.
    /// </summary>
    public static TInteger ConvertToInteger<TInteger>(Decimal128 value)
        where TInteger : IBinaryInteger<TInteger> => TInteger.CreateSaturating(value);

    /// <summary>
    /// The same conversion. The two differ for the built-in types, where the native form
    /// leaves an out-of-range value to the hardware; here there is nothing to leave to it.
    /// </summary>
    public static TInteger ConvertToIntegerNative<TInteger>(Decimal128 value)
        where TInteger : IBinaryInteger<TInteger> => TInteger.CreateSaturating(value);

    // Text.

    public string ToString(string? format, IFormatProvider? provider) =>
        Decimal128Formatter.Format(_bits, format, provider);

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format,
        IFormatProvider? provider) =>
        Decimal128Formatter.TryFormat(_bits, destination, out charsWritten, format, provider);

    // The overloads that carry a culture read that culture's separators, signs, and
    // symbols; the ones above carry none and read the specification's grammar, which is
    // invariant by definition. The pair a caller picks has to match on both sides -- the
    // text of a value under a culture is only a number to that same culture.

    public static Decimal128 Parse(string s, IFormatProvider? provider) =>
        Parse(s, Decimal128CultureNormalizer.DefaultStyles, provider);

    public static Decimal128 Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        Parse(s, Decimal128CultureNormalizer.DefaultStyles, provider);

    public static Decimal128 Parse(string s, NumberStyles style, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return Parse(s.AsSpan(), style, provider);
    }

    public static Decimal128 Parse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider)
    {
        if (!TryParse(s, style, provider, out var value))
        {
            throw new FormatException($"'{s}' is not a Decimal128.");
        }

        return value;
    }

    public static bool TryParse(string? s, IFormatProvider? provider, out Decimal128 result) =>
        TryParse(s, Decimal128CultureNormalizer.DefaultStyles, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Decimal128 result) =>
        TryParse(s, Decimal128CultureNormalizer.DefaultStyles, provider, out result);

    public static bool TryParse(string? s, NumberStyles style, IFormatProvider? provider, out Decimal128 result)
    {
        Decimal128CultureNormalizer.ValidateStyles(style, nameof(style));

        if (s is null)
        {
            result = Zero;
            return false;
        }

        return TryParse(s.AsSpan(), style, provider, out result);
    }

    public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider,
        out Decimal128 result)
    {
        Decimal128CultureNormalizer.ValidateStyles(style, nameof(style));

        var status = Decimal128Status.None;
        var bits = Decimal128Parser.Parse(s, style, provider, Decimal128Rounding.HalfEven, ref status);
        return Reported(bits, status, out result);
    }

    private static bool Reported(Decimal128Integer bits, Decimal128Status status, out Decimal128 result)
    {
        if ((status & Decimal128Status.ConversionSyntax) != 0)
        {
            result = Zero;
            return false;
        }

        result = new Decimal128(bits);
        return true;
    }

    // The same text as UTF-8, for callers working in bytes.

    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format,
        IFormatProvider? provider) =>
        Decimal128Formatter.TryFormat(_bits, utf8Destination, out bytesWritten, format, provider);

    public static Decimal128 Parse(ReadOnlySpan<byte> utf8Text)
    {
        if (!TryParse(utf8Text, out var value))
        {
            throw new FormatException($"'{Encoding.UTF8.GetString(utf8Text)}' is not a Decimal128.");
        }

        return value;
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, out Decimal128 value)
    {
        var status = Decimal128Status.None;
        var bits = Decimal128Parser.Parse(utf8Text, Decimal128Rounding.HalfEven, ref status);
        return Reported(bits, status, out value);
    }

    public static Decimal128 Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider)
    {
        if (!TryParse(utf8Text, provider, out var value))
        {
            throw new FormatException($"'{Encoding.UTF8.GetString(utf8Text)}' is not a Decimal128.");
        }

        return value;
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out Decimal128 result)
    {
        var status = Decimal128Status.None;
        var bits = Decimal128Parser.Parse(utf8Text, Decimal128CultureNormalizer.DefaultStyles, provider,
            Decimal128Rounding.HalfEven, ref status);

        return Reported(bits, status, out result);
    }

    // The exponent and the coefficient, for callers that want the parts as bytes.

    int IFloatingPoint<Decimal128>.GetExponentByteCount() => sizeof(int);

    int IFloatingPoint<Decimal128>.GetExponentShortestBitLength()
    {
        var exponent = Exponent(_bits);
        return exponent >= 0
            ? (sizeof(int) * 8) - int.LeadingZeroCount(exponent)
            : (sizeof(int) * 8) + 1 - int.LeadingZeroCount(~exponent);
    }

    int IFloatingPoint<Decimal128>.GetSignificandByteCount() => 2 * sizeof(ulong);

    int IFloatingPoint<Decimal128>.GetSignificandBitLength() => 2 * sizeof(ulong) * 8;

    bool IFloatingPoint<Decimal128>.TryWriteExponentBigEndian(Span<byte> destination, out int written) =>
        TryWrite(Exponent(_bits), destination, out written, true);

    bool IFloatingPoint<Decimal128>.TryWriteExponentLittleEndian(Span<byte> destination, out int written) =>
        TryWrite(Exponent(_bits), destination, out written, false);

    bool IFloatingPoint<Decimal128>.TryWriteSignificandBigEndian(Span<byte> destination, out int written) =>
        TryWrite(Coefficient(_bits), destination, out written, true);

    bool IFloatingPoint<Decimal128>.TryWriteSignificandLittleEndian(Span<byte> destination, out int written) =>
        TryWrite(Coefficient(_bits), destination, out written, false);

    private static int Exponent(Decimal128Integer bits)
    {
        if (Decimal128Encoding.IsSpecial(bits))
        {
            return 0;
        }

        Decimal128Encoding.Unpack(bits, out var exponent);
        return exponent;
    }

    private static Decimal128Integer Coefficient(Decimal128Integer bits)
    {
        if (Decimal128Encoding.IsSpecial(bits))
        {
            return Decimal128Encoding.IsNaN(bits) ? Decimal128Encoding.Payload(bits) : Decimal128Integer.Zero;
        }

        return Decimal128Encoding.Unpack(bits, out _);
    }

    // Conversions to and from the other numeric types.

    static bool INumberBase<Decimal128>.TryConvertFromChecked<TOther>(TOther value, out Decimal128 result) =>
        TryConvertFrom(value, out result);

    static bool INumberBase<Decimal128>.TryConvertFromSaturating<TOther>(TOther value, out Decimal128 result) =>
        TryConvertFrom(value, out result);

    static bool INumberBase<Decimal128>.TryConvertFromTruncating<TOther>(TOther value, out Decimal128 result) =>
        TryConvertFrom(value, out result);

    static bool INumberBase<Decimal128>.TryConvertToChecked<TOther>(Decimal128 value, out TOther result)
        where TOther : default => TryConvertTo(value, out result);

    static bool INumberBase<Decimal128>.TryConvertToSaturating<TOther>(Decimal128 value, out TOther result)
        where TOther : default => TryConvertTo(value, out result);

    static bool INumberBase<Decimal128>.TryConvertToTruncating<TOther>(Decimal128 value, out TOther result)
        where TOther : default => TryConvertTo(value, out result);

    // Conversions. Widening is implicit: every value of a built-in integer type is held
    // here exactly, since thirty-four digits cover twenty. Narrowing is explicit, since it
    // can round, overflow to an infinity, or underflow to a subnormal. Conversions to an
    // integer truncate toward zero and throw when the value will not fit, which is what
    // System.Decimal does; the generic-math CreateSaturating and CreateTruncating are there
    // for the other two behaviors.

    public static implicit operator Decimal128(sbyte value) => new(Decimal128Conversions.FromInt64(value));

    public static implicit operator Decimal128(byte value) => new(Decimal128Conversions.FromUInt64(value, false));

    public static implicit operator Decimal128(short value) => new(Decimal128Conversions.FromInt64(value));

    public static implicit operator Decimal128(ushort value) => new(Decimal128Conversions.FromUInt64(value, false));

    public static implicit operator Decimal128(char value) => new(Decimal128Conversions.FromUInt64(value, false));

    public static implicit operator Decimal128(int value) => new(Decimal128Conversions.FromInt64(value));

    public static implicit operator Decimal128(uint value) => new(Decimal128Conversions.FromUInt64(value, false));

    public static implicit operator Decimal128(long value) => new(Decimal128Conversions.FromInt64(value));

    public static implicit operator Decimal128(ulong value) => new(Decimal128Conversions.FromUInt64(value, false));

    public static explicit operator Decimal128(Half value) => new(Decimal128Conversions.FromBinary(value));

    public static explicit operator Decimal128(float value) => new(Decimal128Conversions.FromBinary(value));

    public static explicit operator Decimal128(double value) => new(Decimal128Conversions.FromBinary(value));

    /// <summary>
    /// Reads a binary floating-point value the way <paramref name="conversion"/> asks for.
    /// The cast operators above take the shortest reading; this is how to ask for IEEE
    /// 754's convertFormat instead, which gives the binary value itself.
    /// </summary>
    public static Decimal128 FromBinary(double value, Decimal128BinaryConversion conversion) =>
        new(Decimal128Conversions.FromBinary(value, conversion));

    public static Decimal128 FromBinary(float value, Decimal128BinaryConversion conversion) =>
        new(Decimal128Conversions.FromBinary(value, conversion));

    public static Decimal128 FromBinary(Half value, Decimal128BinaryConversion conversion) =>
        new(Decimal128Conversions.FromBinary(value, conversion));

    public static explicit operator sbyte(Decimal128 value) => Decimal128Conversions.ToInteger<sbyte>(value._bits);

    public static explicit operator byte(Decimal128 value) => Decimal128Conversions.ToInteger<byte>(value._bits);

    public static explicit operator short(Decimal128 value) => Decimal128Conversions.ToInteger<short>(value._bits);

    public static explicit operator ushort(Decimal128 value) => Decimal128Conversions.ToInteger<ushort>(value._bits);

    public static explicit operator char(Decimal128 value) => (char)Decimal128Conversions.ToInteger<ushort>(value._bits);

    public static explicit operator int(Decimal128 value) => Decimal128Conversions.ToInteger<int>(value._bits);

    public static explicit operator uint(Decimal128 value) => Decimal128Conversions.ToInteger<uint>(value._bits);

    public static explicit operator long(Decimal128 value) => Decimal128Conversions.ToInteger<long>(value._bits);

    public static explicit operator ulong(Decimal128 value) => Decimal128Conversions.ToInteger<ulong>(value._bits);

    public static explicit operator Half(Decimal128 value) => (Half)Decimal128Formatter.ToDouble(value._bits);

    public static explicit operator float(Decimal128 value) => (float)Decimal128Formatter.ToDouble(value._bits);

    public static explicit operator double(Decimal128 value) => Decimal128Formatter.ToDouble(value._bits);

    private static bool TryConvertFrom<TOther>(TOther value, out Decimal128 result)
        where TOther : INumberBase<TOther>
    {
        var converted = Decimal128Conversions.TryFrom(value, out var bits);
        result = new Decimal128(bits);
        return converted;
    }

    private static bool TryConvertTo<TOther>(Decimal128 value, out TOther result)
        where TOther : INumberBase<TOther>?
    {
        var converted = Decimal128Conversions.TryTo<TOther>(value._bits, out var narrowed);
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

    private static bool TryWrite(Decimal128Integer coefficient, Span<byte> destination, out int written, bool bigEndian)
    {
        written = 2 * sizeof(ulong);
        if (destination.Length < written)
        {
            written = 0;
            return false;
        }

        if (bigEndian)
        {
            BinaryPrimitives.WriteUInt64BigEndian(destination, coefficient.High);
            BinaryPrimitives.WriteUInt64BigEndian(destination[sizeof(ulong)..], coefficient.Low);
            return true;
        }

        BinaryPrimitives.WriteUInt64LittleEndian(destination, coefficient.Low);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[sizeof(ulong)..], coefficient.High);
        return true;
    }

    private static Decimal128Rounding FromMidpointRounding(MidpointRounding mode) => mode switch
    {
        MidpointRounding.AwayFromZero => Decimal128Rounding.HalfUp,
        MidpointRounding.ToZero => Decimal128Rounding.Down,
        MidpointRounding.ToPositiveInfinity => Decimal128Rounding.Ceiling,
        MidpointRounding.ToNegativeInfinity => Decimal128Rounding.Floor,
        _ => Decimal128Rounding.HalfEven
    };

    /// <summary>
    /// Numeric equality, so 1.0 equals 1.00 and the two zeros are equal. NaN equals NaN
    /// here, unlike under <c>==</c>, so a value can be found in a collection.
    /// <see cref="CompareTotal"/> is the bit-level ordering; <see cref="ToBits"/> the
    /// bits themselves.
    /// </summary>
    public bool Equals(Decimal128 other)
    {
        var thisNaN = Decimal128Encoding.IsNaN(_bits);
        var otherNaN = Decimal128Encoding.IsNaN(other._bits);
        if (thisNaN || otherNaN)
        {
            return thisNaN && otherNaN;
        }

        return Decimal128Ordering.CompareValues(_bits, other._bits) == 0;
    }

    public override bool Equals(object? other) => other is Decimal128 value && Equals(value);

    /// <summary>Hashes the value, so members of one cohort agree.</summary>
    public override int GetHashCode() => Decimal128Ordering.ValueHashCode(_bits);

    /// <summary>Orders for sorting: NaNs sort below everything and equal each other.</summary>
    public int CompareTo(Decimal128 other)
    {
        var thisNaN = Decimal128Encoding.IsNaN(_bits);
        var otherNaN = Decimal128Encoding.IsNaN(other._bits);
        if (thisNaN || otherNaN)
        {
            if (thisNaN && otherNaN)
            {
                return 0;
            }

            return thisNaN ? -1 : 1;
        }

        return Decimal128Ordering.CompareValues(_bits, other._bits);
    }

    public int CompareTo(object? other)
    {
        if (other is null)
        {
            return 1;
        }

        if (other is not Decimal128 value)
        {
            throw new ArgumentException("Expected a Decimal128.", nameof(other));
        }

        return CompareTo(value);
    }

    // Numeric comparison, which leaves a NaN unordered: every one of these is false when
    // either side is a NaN, including a NaN against itself.

    private static bool Ordered(Decimal128 left, Decimal128 right, out int comparison)
    {
        if (Decimal128Encoding.IsNaN(left._bits) || Decimal128Encoding.IsNaN(right._bits))
        {
            comparison = 0;
            return false;
        }

        comparison = Decimal128Ordering.CompareValues(left._bits, right._bits);
        return true;
    }

    public static bool operator ==(Decimal128 left, Decimal128 right) =>
        Ordered(left, right, out var comparison) && comparison == 0;

    public static bool operator !=(Decimal128 left, Decimal128 right) => !(left == right);

    public static bool operator <(Decimal128 left, Decimal128 right) =>
        Ordered(left, right, out var comparison) && comparison < 0;

    public static bool operator >(Decimal128 left, Decimal128 right) =>
        Ordered(left, right, out var comparison) && comparison > 0;

    public static bool operator <=(Decimal128 left, Decimal128 right) =>
        Ordered(left, right, out var comparison) && comparison <= 0;

    public static bool operator >=(Decimal128 left, Decimal128 right) =>
        Ordered(left, right, out var comparison) && comparison >= 0;

    public static Decimal128 operator ++(Decimal128 value) => value + One;

    public static Decimal128 operator --(Decimal128 value) => value - One;
}
