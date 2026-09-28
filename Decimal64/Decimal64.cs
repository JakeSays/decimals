// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using Decimals.Internal;


namespace Decimals;

/// <summary>
/// An IEEE 754 decimal64 value: 16 digits of coefficient, exponents from -398 to +369,
/// held as a binary-integer encoding and computed on with nothing wider than a machine
/// word.
/// </summary>
/// <remarks>
/// <para>
/// This type stands on its own. Its arithmetic, text, conversions, and elementary
/// functions depend on nothing outside this namespace, and nothing in it uses a 128-bit
/// integer or an arbitrary-precision integer type: the two intermediates that outgrow a
/// word are handled as limbs of sixteen digits, and the elementary functions run on a
/// unit-array engine ported from decNumber.
/// </para>
/// <para>
/// The in-memory encoding is BID, the binary-integer form, because that is what makes the
/// arithmetic fast: the coefficient is reached with a mask and a shift. The
/// densely-packed interchange form that decimal hardware and decNumber exchange is a
/// conversion away through <see cref="ToDpdBits"/> and <see cref="FromDpdBits"/>.
/// </para>
/// </remarks>
public readonly struct Decimal64
    : IFloatingPoint<Decimal64>,
      IMinMaxValue<Decimal64>,
      IExponentialFunctions<Decimal64>,
      ILogarithmicFunctions<Decimal64>,
      IPowerFunctions<Decimal64>,
      IRootFunctions<Decimal64>,
      IUtf8SpanFormattable,
      IUtf8SpanParsable<Decimal64>
{
    private readonly ulong _bits;

    private Decimal64(ulong bits)
    {
        _bits = bits;
    }

    public static Decimal64 Zero => new(Decimal64Encoding.Zero(false, 0));

    public static Decimal64 NegativeZero => new(Decimal64Encoding.Zero(true, 0));

    public static Decimal64 One => new(Decimal64Encoding.Pack(false, 0, 1));

    public static Decimal64 NegativeOne => new(Decimal64Encoding.Pack(true, 0, 1));

    public static Decimal64 MaxValue =>
        new(Decimal64Encoding.Pack(false, Decimal64Encoding.MaxQuantumExponent, Decimal64Encoding.MaxCoefficient));

    public static Decimal64 MinValue =>
        new(Decimal64Encoding.Pack(true, Decimal64Encoding.MaxQuantumExponent, Decimal64Encoding.MaxCoefficient));

    /// <summary>The smallest positive value, which is subnormal: 1E-398.</summary>
    public static Decimal64 Epsilon => new(Decimal64Encoding.Pack(false, Decimal64Encoding.MinQuantumExponent, 1));

    public static Decimal64 PositiveInfinity => new(Decimal64Encoding.Infinity(false));

    public static Decimal64 NegativeInfinity => new(Decimal64Encoding.Infinity(true));

    public static Decimal64 NaN => new(Decimal64Encoding.QuietNaN());

    /// <summary>The raw encoding, which is BID.</summary>
    public ulong ToBits() => _bits;

    public static Decimal64 FromBits(ulong bits) => new(bits);

    /// <summary>
    /// The binary-integer-decimal encoding, where the coefficient is a plain integer in the
    /// trailing field. This is the in-memory encoding, so it costs nothing.
    /// </summary>
    public ulong ToBidBits() => _bits;

    public static Decimal64 FromBidBits(ulong bits) => new(bits);

    /// <summary>
    /// The densely-packed-decimal interchange encoding, which is what decimal hardware and
    /// DPD-based libraries exchange.
    /// </summary>
    public ulong ToDpdBits() => Decimal64Dpd.ToDpd(_bits);

    public static Decimal64 FromDpdBits(ulong bits) => new(Decimal64Dpd.FromDpd(bits));

    public static bool IsNaN(Decimal64 value) => Decimal64Encoding.IsNaN(value._bits);

    public static bool IsSignalingNaN(Decimal64 value) => Decimal64Encoding.IsSignalingNaN(value._bits);

    public static bool IsInfinity(Decimal64 value) => Decimal64Encoding.IsInfinity(value._bits);

    public static bool IsFinite(Decimal64 value) => !Decimal64Encoding.IsSpecial(value._bits);

    public static bool IsNegative(Decimal64 value) => Decimal64Encoding.IsNegative(value._bits);

    public static bool IsPositiveInfinity(Decimal64 value) => IsInfinity(value) && !IsNegative(value);

    public static bool IsNegativeInfinity(Decimal64 value) => IsInfinity(value) && IsNegative(value);

    public static bool IsZero(Decimal64 value) => Decimal64Encoding.IsZero(value._bits);

    public static bool IsSubnormal(Decimal64 value) => Decimal64Ordering.IsSubnormal(value._bits);

    public static bool IsNormal(Decimal64 value) => IsFinite(value) && !IsZero(value) && !IsSubnormal(value);

    /// <summary>
    /// False when the encoding uses a coefficient or payload the format cannot hold, or
    /// sets bits a special value leaves clear. Those decode as zero, so re-encoding does not
    /// give the same bits back.
    /// </summary>
    public static bool IsCanonical(Decimal64 value) => value._bits == Decimal64Encoding.Canonical(value._bits);

    /// <summary>The same value in its canonical encoding.</summary>
    public static Decimal64 Canonical(Decimal64 value) => new(Decimal64Encoding.Canonical(value._bits));

    public static Decimal64Class Class(Decimal64 value) => Decimal64Ordering.Classify(value._bits);

    /// <summary>Copies the value with a cleared sign. Quiet: a signaling NaN stays one.</summary>
    public static Decimal64 CopyAbs(Decimal64 value) => new(value._bits & ~Decimal64Encoding.SignMask);

    /// <summary>Copies the value with its sign flipped. Quiet, unlike arithmetic negation.</summary>
    public static Decimal64 CopyNegate(Decimal64 value) => new(value._bits ^ Decimal64Encoding.SignMask);

    public static Decimal64 CopySign(Decimal64 value, Decimal64 sign) =>
        new((value._bits & ~Decimal64Encoding.SignMask) | (sign._bits & Decimal64Encoding.SignMask));

    /// <summary>
    /// True when both are finite with the same exponent, or both are infinite, or both are
    /// NaN. Never raises a condition, even for a signaling NaN.
    /// </summary>
    public static bool SameQuantum(Decimal64 left, Decimal64 right)
    {
        if (Decimal64Encoding.IsSpecial(left._bits) || Decimal64Encoding.IsSpecial(right._bits))
        {
            return (Decimal64Encoding.IsNaN(left._bits) && Decimal64Encoding.IsNaN(right._bits))
                || (Decimal64Encoding.IsInfinity(left._bits) && Decimal64Encoding.IsInfinity(right._bits));
        }

        Decimal64Encoding.Unpack(left._bits, out var leftExponent);
        Decimal64Encoding.Unpack(right._bits, out var rightExponent);
        return leftExponent == rightExponent;
    }

    public static int CompareTotal(Decimal64 left, Decimal64 right) => Decimal64Ordering.CompareTotal(left._bits, right._bits);

    public static int CompareTotalMagnitude(Decimal64 left, Decimal64 right) =>
        Decimal64Ordering.CompareTotalMagnitude(left._bits, right._bits);

    /// <summary>
    /// The specification's to-number conversion: a bad string gives a quiet NaN and raises
    /// <see cref="Decimal64Status.ConversionSyntax"/> rather than throwing.
    /// </summary>
    public static Decimal64 FromString(ReadOnlySpan<char> text, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var bits = Decimal64Parser.Parse(text, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal64(bits);
    }

    public static Decimal64 Parse(ReadOnlySpan<char> text)
    {
        if (!TryParse(text, out var value))
        {
            throw new FormatException($"'{text}' is not a Decimal64.");
        }

        return value;
    }

    public static Decimal64 Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Parse(text.AsSpan());
    }

    public static bool TryParse(ReadOnlySpan<char> text, out Decimal64 value)
    {
        var status = Decimal64Status.None;
        var bits = Decimal64Parser.Parse(text, Decimal64Rounding.HalfEven, ref status);
        if ((status & Decimal64Status.ConversionSyntax) != 0)
        {
            value = Zero;
            return false;
        }

        value = new Decimal64(bits);
        return true;
    }

    public static bool TryParse(string? text, out Decimal64 value)
    {
        if (text is null)
        {
            value = Zero;
            return false;
        }

        return TryParse(text.AsSpan(), out value);
    }

    public override string ToString() => Decimal64Formatter.ToScientificString(_bits);

    public string ToScientificString() => Decimal64Formatter.ToScientificString(_bits);

    public string ToEngineeringString() => Decimal64Formatter.ToEngineeringString(_bits);

    // Arithmetic. The plain form rounds half to even and drops the conditions, which is
    // IEEE 754 default exception handling; the overload taking a context does neither. An
    // operation decides its conditions from a clean slate and adds them to the context, so
    // one operation's conditions never leak into the next one's decisions.

    public static Decimal64 Add(Decimal64 left, Decimal64 right, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var result = Decimal64Arithmetic.Add(left._bits, right._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal64(result);
    }

    public static Decimal64 Add(Decimal64 left, Decimal64 right)
    {
        var status = Decimal64Status.None;
        return new Decimal64(Decimal64Arithmetic.Add(left._bits, right._bits, Decimal64Rounding.HalfEven, ref status));
    }

    public static Decimal64 Subtract(Decimal64 left, Decimal64 right, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var result = Decimal64Arithmetic.Subtract(left._bits, right._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal64(result);
    }

    public static Decimal64 Subtract(Decimal64 left, Decimal64 right)
    {
        var status = Decimal64Status.None;
        return new Decimal64(Decimal64Arithmetic.Subtract(left._bits, right._bits, Decimal64Rounding.HalfEven, ref status));
    }

    public static Decimal64 Multiply(Decimal64 left, Decimal64 right, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var result = Decimal64Arithmetic.Multiply(left._bits, right._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal64(result);
    }

    public static Decimal64 Multiply(Decimal64 left, Decimal64 right)
    {
        var status = Decimal64Status.None;
        return new Decimal64(Decimal64Arithmetic.Multiply(left._bits, right._bits, Decimal64Rounding.HalfEven, ref status));
    }

    public static Decimal64 Divide(Decimal64 left, Decimal64 right, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var result = Decimal64Arithmetic.Divide(left._bits, right._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal64(result);
    }

    public static Decimal64 Divide(Decimal64 left, Decimal64 right)
    {
        var status = Decimal64Status.None;
        return new Decimal64(Decimal64Arithmetic.Divide(left._bits, right._bits, Decimal64Rounding.HalfEven, ref status));
    }

    /// <summary>The integer part of the quotient, with a zero exponent.</summary>
    public static Decimal64 DivideInteger(Decimal64 left, Decimal64 right, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var result = Decimal64Arithmetic.DivideInteger(left._bits, right._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal64(result);
    }

    public static Decimal64 DivideInteger(Decimal64 left, Decimal64 right)
    {
        var status = Decimal64Status.None;
        return new Decimal64(Decimal64Arithmetic.DivideInteger(left._bits, right._bits, Decimal64Rounding.HalfEven, ref status));
    }

    public static Decimal64 Remainder(Decimal64 left, Decimal64 right, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var result = Decimal64Arithmetic.Remainder(left._bits, right._bits, false, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal64(result);
    }

    public static Decimal64 Remainder(Decimal64 left, Decimal64 right)
    {
        var status = Decimal64Status.None;
        return new Decimal64(Decimal64Arithmetic.Remainder(left._bits, right._bits, false, Decimal64Rounding.HalfEven, ref status));
    }

    /// <summary>IEEE 754's remainder, which takes the quotient to the nearest integer.</summary>
    public static Decimal64 RemainderNear(Decimal64 left, Decimal64 right, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var result = Decimal64Arithmetic.Remainder(left._bits, right._bits, true, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal64(result);
    }

    public static Decimal64 RemainderNear(Decimal64 left, Decimal64 right)
    {
        var status = Decimal64Status.None;
        return new Decimal64(Decimal64Arithmetic.Remainder(left._bits, right._bits, true, Decimal64Rounding.HalfEven, ref status));
    }

    /// <summary>Multiply and add, rounded once rather than twice.</summary>
    public static Decimal64 FusedMultiplyAdd(Decimal64 left, Decimal64 right, Decimal64 addend, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var result = Decimal64Arithmetic.FusedMultiplyAdd(left._bits, right._bits, addend._bits, context.Rounding,
            ref status);

        context.Status |= status;
        return new Decimal64(result);
    }

    public static Decimal64 FusedMultiplyAdd(Decimal64 left, Decimal64 right, Decimal64 addend)
    {
        var status = Decimal64Status.None;
        return new Decimal64(Decimal64Arithmetic.FusedMultiplyAdd(left._bits, right._bits, addend._bits,
            Decimal64Rounding.HalfEven, ref status));
    }

    /// <summary>Zero plus the value, so the format's rounding is applied.</summary>
    public static Decimal64 Plus(Decimal64 value, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var result = Decimal64Arithmetic.AddToZero(value._bits, false, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal64(result);
    }

    public static Decimal64 Plus(Decimal64 value)
    {
        var status = Decimal64Status.None;
        return new Decimal64(Decimal64Arithmetic.AddToZero(value._bits, false, Decimal64Rounding.HalfEven, ref status));
    }

    /// <summary>Zero minus the value. Arithmetic, unlike <see cref="CopyNegate"/>.</summary>
    public static Decimal64 Minus(Decimal64 value, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var result = Decimal64Arithmetic.AddToZero(value._bits, true, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal64(result);
    }

    public static Decimal64 Minus(Decimal64 value)
    {
        var status = Decimal64Status.None;
        return new Decimal64(Decimal64Arithmetic.AddToZero(value._bits, true, Decimal64Rounding.HalfEven, ref status));
    }

    /// <summary>The magnitude, arithmetically. Unlike <see cref="CopyAbs"/> this can signal.</summary>
    public static Decimal64 Abs(Decimal64 value, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var negate = Decimal64Encoding.IsNegative(value._bits) && !Decimal64Encoding.IsNaN(value._bits);
        var result = Decimal64Arithmetic.AddToZero(value._bits, negate, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal64(result);
    }

    /// <summary>
    /// Numeric comparison giving -1, 0, 1, or NaN. Unlike <see cref="CompareTotal"/> the two
    /// zeros are equal and a NaN operand gives a NaN.
    /// </summary>
    public static Decimal64 Compare(Decimal64 left, Decimal64 right, ref Decimal64Context context) =>
        CompareToValue(left, right, false, ref context);

    /// <summary>Comparison that signals on any NaN, not only a signaling one.</summary>
    public static Decimal64 CompareSignal(Decimal64 left, Decimal64 right, ref Decimal64Context context) =>
        CompareToValue(left, right, true, ref context);

    private static Decimal64 CompareToValue(Decimal64 left, Decimal64 right, bool signaling, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var comparison = Decimal64Arithmetic.Compare(left._bits, right._bits, signaling, ref status, out var nan);
        context.Status |= status;

        if (comparison == int.MinValue)
        {
            return new Decimal64(nan);
        }

        return FromComparison(comparison);
    }

    private static Decimal64 FromComparison(int comparison)
    {
        if (comparison < 0)
        {
            return NegativeOne;
        }

        return comparison > 0 ? One : Zero;
    }

    // Digit-wise logical operations. Each operand must be a string of ones and zeros with
    // a zero exponent and no sign; anything else is an invalid operation.

    public static Decimal64 And(Decimal64 left, Decimal64 right, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var result = Decimal64Logical.And(left._bits, right._bits, ref status);
        context.Status |= status;
        return new Decimal64(result);
    }

    public static Decimal64 Or(Decimal64 left, Decimal64 right, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var result = Decimal64Logical.Or(left._bits, right._bits, ref status);
        context.Status |= status;
        return new Decimal64(result);
    }

    public static Decimal64 Xor(Decimal64 left, Decimal64 right, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var result = Decimal64Logical.Xor(left._bits, right._bits, ref status);
        context.Status |= status;
        return new Decimal64(result);
    }

    public static Decimal64 Invert(Decimal64 value, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var result = Decimal64Logical.Invert(value._bits, ref status);
        context.Status |= status;
        return new Decimal64(result);
    }

    /// <summary>The larger of the two. A quiet NaN beside a number loses to the number.</summary>
    public static Decimal64 Max(Decimal64 left, Decimal64 right, ref Decimal64Context context) =>
        Select(left, right, true, false, ref context);

    public static Decimal64 Min(Decimal64 left, Decimal64 right, ref Decimal64Context context) =>
        Select(left, right, false, false, ref context);

    public static Decimal64 MaxMagnitude(Decimal64 left, Decimal64 right, ref Decimal64Context context) =>
        Select(left, right, true, true, ref context);

    public static Decimal64 MinMagnitude(Decimal64 left, Decimal64 right, ref Decimal64Context context) =>
        Select(left, right, false, true, ref context);

    private static Decimal64 Select(Decimal64 left, Decimal64 right, bool wantLarger, bool byMagnitude, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var result = Decimal64Ordering.Select(left._bits, right._bits, wantLarger, byMagnitude, ref status);
        context.Status |= status;
        return new Decimal64(result);
    }

    /// <summary>The adjusted exponent, as an integer.</summary>
    public static Decimal64 LogB(Decimal64 value, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var result = Decimal64Shaping.LogB(value._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal64(result);
    }

    /// <summary>Multiplies by ten raised to the second operand.</summary>
    public static Decimal64 ScaleB(Decimal64 value, Decimal64 scale, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var result = Decimal64Shaping.ScaleB(value._bits, scale._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal64(result);
    }

    /// <summary>Removes trailing zeros, giving the shortest coefficient of the same value.</summary>
    public static Decimal64 Reduce(Decimal64 value, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var result = Decimal64Shaping.Reduce(value._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal64(result);
    }

    /// <summary>Like <see cref="Reduce"/>, but never past a zero exponent.</summary>
    public static Decimal64 Trim(Decimal64 value) => new(Decimal64Shaping.Trim(value._bits));

    /// <summary>Rounds to an integer without reporting that anything was rounded.</summary>
    public static Decimal64 RoundToIntegral(Decimal64 value, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var result = Decimal64Shaping.ToIntegral(value._bits, false, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal64(result);
    }

    /// <summary>Rounds to an integer, reporting inexactness.</summary>
    public static Decimal64 RoundToIntegralExact(Decimal64 value, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var result = Decimal64Shaping.ToIntegral(value._bits, true, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal64(result);
    }

    /// <summary>Rescales to the second operand's exponent.</summary>
    public static Decimal64 Quantize(Decimal64 value, Decimal64 pattern, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var result = Decimal64Shaping.Quantize(value._bits, pattern._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal64(result);
    }

    /// <summary>Rotates the coefficient's digits within the format's full width.</summary>
    public static Decimal64 Rotate(Decimal64 value, Decimal64 places, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var result = Decimal64Shaping.RotateOrShift(value._bits, places._bits, true, ref status);
        context.Status |= status;
        return new Decimal64(result);
    }

    /// <summary>Shifts the coefficient's digits, dropping what falls off the end.</summary>
    public static Decimal64 Shift(Decimal64 value, Decimal64 places, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var result = Decimal64Shaping.RotateOrShift(value._bits, places._bits, false, ref status);
        context.Status |= status;
        return new Decimal64(result);
    }

    /// <summary>The square root, correctly rounded.</summary>
    public static Decimal64 Sqrt(Decimal64 value, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var result = Decimal64SquareRoot.SquareRoot(value._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal64(result);
    }

    public static Decimal64 Sqrt(Decimal64 value)
    {
        var status = Decimal64Status.None;
        return new Decimal64(Decimal64SquareRoot.SquareRoot(value._bits, Decimal64Rounding.HalfEven, ref status));
    }

    // The elementary functions, each in the context form and the plain form.

    private delegate ulong Function(ulong value, Decimal64Rounding rounding, ref Decimal64Status status);

    private delegate ulong FunctionOfTwo(ulong left, ulong right, Decimal64Rounding rounding, ref Decimal64Status status);

    private static Decimal64 Apply(Function function, Decimal64 value, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var result = function(value._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal64(result);
    }

    private static Decimal64 Apply(Function function, Decimal64 value)
    {
        var status = Decimal64Status.None;
        return new Decimal64(function(value._bits, Decimal64Rounding.HalfEven, ref status));
    }

    private static Decimal64 Apply(FunctionOfTwo function, Decimal64 left, Decimal64 right, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var result = function(left._bits, right._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal64(result);
    }

    private static Decimal64 Apply(FunctionOfTwo function, Decimal64 left, Decimal64 right)
    {
        var status = Decimal64Status.None;
        return new Decimal64(function(left._bits, right._bits, Decimal64Rounding.HalfEven, ref status));
    }

    /// <summary>e raised to the value.</summary>
    public static Decimal64 Exp(Decimal64 value, ref Decimal64Context context) => Apply(Decimal64Math.Exp, value, ref context);

    public static Decimal64 Exp(Decimal64 value) => Apply(Decimal64Math.Exp, value);

    /// <summary>The natural logarithm.</summary>
    public static Decimal64 Log(Decimal64 value, ref Decimal64Context context) => Apply(Decimal64Math.Log, value, ref context);

    public static Decimal64 Log(Decimal64 value) => Apply(Decimal64Math.Log, value);

    /// <summary>The base-ten logarithm.</summary>
    public static Decimal64 Log10(Decimal64 value, ref Decimal64Context context) => Apply(Decimal64Math.Log10, value, ref context);

    public static Decimal64 Log10(Decimal64 value) => Apply(Decimal64Math.Log10, value);

    /// <summary>The first value raised to the second.</summary>
    public static Decimal64 Pow(Decimal64 value, Decimal64 power, ref Decimal64Context context) =>
        Apply(Decimal64Math.Power, value, power, ref context);

    public static Decimal64 Pow(Decimal64 value, Decimal64 power) => Apply(Decimal64Math.Power, value, power);

    /// <summary>Two raised to the value.</summary>
    public static Decimal64 Exp2(Decimal64 value, ref Decimal64Context context) => Apply(Decimal64Math.Exp2, value, ref context);

    public static Decimal64 Exp2(Decimal64 value) => Apply(Decimal64Math.Exp2, value);

    /// <summary>Ten raised to the value.</summary>
    public static Decimal64 Exp10(Decimal64 value, ref Decimal64Context context) => Apply(Decimal64Math.Exp10, value, ref context);

    public static Decimal64 Exp10(Decimal64 value) => Apply(Decimal64Math.Exp10, value);

    // The M1 and P1 names are the ones the generic math interfaces declare, so they stay as
    // spelled there rather than written out.

    /// <summary>e raised to the value, less one.</summary>
    public static Decimal64 ExpM1(Decimal64 value, ref Decimal64Context context) =>
        Apply(Decimal64Math.ExpMinusOne, value, ref context);

    public static Decimal64 ExpM1(Decimal64 value) => Apply(Decimal64Math.ExpMinusOne, value);

    /// <summary>Two raised to the value, less one.</summary>
    public static Decimal64 Exp2M1(Decimal64 value, ref Decimal64Context context) =>
        Apply(Decimal64Math.Exp2MinusOne, value, ref context);

    public static Decimal64 Exp2M1(Decimal64 value) => Apply(Decimal64Math.Exp2MinusOne, value);

    /// <summary>Ten raised to the value, less one.</summary>
    public static Decimal64 Exp10M1(Decimal64 value, ref Decimal64Context context) =>
        Apply(Decimal64Math.Exp10MinusOne, value, ref context);

    public static Decimal64 Exp10M1(Decimal64 value) => Apply(Decimal64Math.Exp10MinusOne, value);

    /// <summary>The logarithm in the given base.</summary>
    public static Decimal64 Log(Decimal64 value, Decimal64 newBase, ref Decimal64Context context) =>
        Apply(Decimal64Math.LogInBase, value, newBase, ref context);

    public static Decimal64 Log(Decimal64 value, Decimal64 newBase) => Apply(Decimal64Math.LogInBase, value, newBase);

    /// <summary>The base-two logarithm.</summary>
    public static Decimal64 Log2(Decimal64 value, ref Decimal64Context context) => Apply(Decimal64Math.Log2, value, ref context);

    public static Decimal64 Log2(Decimal64 value) => Apply(Decimal64Math.Log2, value);

    /// <summary>The natural logarithm of one plus the value.</summary>
    public static Decimal64 LogP1(Decimal64 value, ref Decimal64Context context) =>
        Apply(Decimal64Math.LogPlusOne, value, ref context);

    public static Decimal64 LogP1(Decimal64 value) => Apply(Decimal64Math.LogPlusOne, value);

    /// <summary>The base-two logarithm of one plus the value.</summary>
    public static Decimal64 Log2P1(Decimal64 value, ref Decimal64Context context) =>
        Apply(Decimal64Math.Log2PlusOne, value, ref context);

    public static Decimal64 Log2P1(Decimal64 value) => Apply(Decimal64Math.Log2PlusOne, value);

    /// <summary>The base-ten logarithm of one plus the value.</summary>
    public static Decimal64 Log10P1(Decimal64 value, ref Decimal64Context context) =>
        Apply(Decimal64Math.Log10PlusOne, value, ref context);

    public static Decimal64 Log10P1(Decimal64 value) => Apply(Decimal64Math.Log10PlusOne, value);

    /// <summary>The cube root.</summary>
    public static Decimal64 Cbrt(Decimal64 value, ref Decimal64Context context) => Apply(Decimal64Math.Cbrt, value, ref context);

    public static Decimal64 Cbrt(Decimal64 value) => Apply(Decimal64Math.Cbrt, value);

    /// <summary>The root of the given degree.</summary>
    public static Decimal64 RootN(Decimal64 value, int degree, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var result = Decimal64Math.RootN(value._bits, degree, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal64(result);
    }

    public static Decimal64 RootN(Decimal64 value, int degree)
    {
        var status = Decimal64Status.None;
        return new Decimal64(Decimal64Math.RootN(value._bits, degree, Decimal64Rounding.HalfEven, ref status));
    }

    /// <summary>The square root of the sum of two squares, without overflowing on the way.</summary>
    public static Decimal64 Hypot(Decimal64 left, Decimal64 right, ref Decimal64Context context) =>
        Apply(Decimal64Math.Hypot, left, right, ref context);

    public static Decimal64 Hypot(Decimal64 left, Decimal64 right) => Apply(Decimal64Math.Hypot, left, right);

    /// <summary>The next value toward positive infinity.</summary>
    public static Decimal64 NextPlus(Decimal64 value, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var result = Decimal64Shaping.Next(value._bits, true, true, ref status);
        context.Status |= status;
        return new Decimal64(result);
    }

    /// <summary>The next value toward negative infinity.</summary>
    public static Decimal64 NextMinus(Decimal64 value, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var result = Decimal64Shaping.Next(value._bits, false, true, ref status);
        context.Status |= status;
        return new Decimal64(result);
    }

    /// <summary>The next value from the first operand in the direction of the second.</summary>
    public static Decimal64 NextToward(Decimal64 value, Decimal64 target, ref Decimal64Context context)
    {
        var status = Decimal64Status.None;
        var result = Decimal64Shaping.NextToward(value._bits, target._bits, ref status);
        context.Status |= status;
        return new Decimal64(result);
    }

    // Four operations under the names .NET spells them with. Each is one of the operations
    // above, so code written against the built-in floating-point types finds them here.

    /// <summary>IEEE 754's remainder, which is <see cref="RemainderNear(Decimal64, Decimal64)"/>.</summary>
    public static Decimal64 Ieee754Remainder(Decimal64 left, Decimal64 right) => RemainderNear(left, right);

    /// <summary>
    /// The adjusted exponent as an integer, which is <see cref="LogB"/> narrowed to one.
    /// The results off the end of the range are .NET's rather than the specification's: a
    /// zero gives <see cref="int.MinValue"/> and a NaN or an infinity
    /// <see cref="int.MaxValue"/>, where logb gives -Infinity, a NaN, and +Infinity.
    /// </summary>
    public static int ILogB(Decimal64 value) => Decimal64Shaping.ILogB(value._bits);

    /// <summary>
    /// Multiplies by ten raised to <paramref name="scale"/>. The radix is this format's, so
    /// this is the specification's scaleb and not double's power of two.
    /// </summary>
    public static Decimal64 ScaleB(Decimal64 value, int scale)
    {
        var status = Decimal64Status.None;
        return new Decimal64(Decimal64Shaping.ScaleB(value._bits, scale, Decimal64Rounding.HalfEven, ref status));
    }

    /// <summary>The next value toward positive infinity, which is <see cref="NextPlus"/>.</summary>
    public static Decimal64 BitIncrement(Decimal64 value)
    {
        var status = Decimal64Status.None;
        return new Decimal64(Decimal64Shaping.Next(value._bits, true, true, ref status));
    }

    /// <summary>The next value toward negative infinity, which is <see cref="NextMinus"/>.</summary>
    public static Decimal64 BitDecrement(Decimal64 value)
    {
        var status = Decimal64Status.None;
        return new Decimal64(Decimal64Shaping.Next(value._bits, false, true, ref status));
    }

    public static Decimal64 operator +(Decimal64 left, Decimal64 right) => Add(left, right);

    public static Decimal64 operator -(Decimal64 left, Decimal64 right) => Subtract(left, right);

    public static Decimal64 operator *(Decimal64 left, Decimal64 right) => Multiply(left, right);

    public static Decimal64 operator /(Decimal64 left, Decimal64 right) => Divide(left, right);

    public static Decimal64 operator %(Decimal64 left, Decimal64 right) => Remainder(left, right);

    public static Decimal64 operator -(Decimal64 value) => Minus(value);

    public static Decimal64 operator +(Decimal64 value) => Plus(value);

    // Generic math. Numeric comparison here, not the total order: 1.0 equals 1.00, the two
    // zeros are equal, and a NaN is unordered against everything.

    static Decimal64 INumberBase<Decimal64>.Zero => Zero;

    static Decimal64 INumberBase<Decimal64>.One => One;

    static Decimal64 ISignedNumber<Decimal64>.NegativeOne => NegativeOne;

    /// <summary>Ten. This is a decimal format, not a binary one.</summary>
    public static int Radix => 10;

    /// <summary>Converts from another numeric type, refusing a value the format cannot hold.</summary>
    public static Decimal64 CreateChecked<TOther>(TOther value)
        where TOther : INumberBase<TOther>
    {
        if (TryConvertFrom(value, out var result))
        {
            return result;
        }

        if (TOther.TryConvertToChecked<Decimal64>(value, out var other))
        {
            return other;
        }

        throw new NotSupportedException($"Cannot convert {typeof(TOther)} to Decimal64.");
    }

    /// <summary>Converts from another numeric type, clamping to the format's range.</summary>
    public static Decimal64 CreateSaturating<TOther>(TOther value)
        where TOther : INumberBase<TOther>
    {
        if (TryConvertFrom(value, out var result))
        {
            return result;
        }

        if (TOther.TryConvertToSaturating<Decimal64>(value, out var other))
        {
            return other;
        }

        throw new NotSupportedException($"Cannot convert {typeof(TOther)} to Decimal64.");
    }

    /// <summary>Converts from another numeric type, keeping the low-order part.</summary>
    public static Decimal64 CreateTruncating<TOther>(TOther value)
        where TOther : INumberBase<TOther>
    {
        if (TryConvertFrom(value, out var result))
        {
            return result;
        }

        if (TOther.TryConvertToTruncating<Decimal64>(value, out var other))
        {
            return other;
        }

        throw new NotSupportedException($"Cannot convert {typeof(TOther)} to Decimal64.");
    }

    static Decimal64 IAdditiveIdentity<Decimal64, Decimal64>.AdditiveIdentity => Zero;

    static Decimal64 IMultiplicativeIdentity<Decimal64, Decimal64>.MultiplicativeIdentity => One;

    static Decimal64 IMinMaxValue<Decimal64>.MinValue => MinValue;

    static Decimal64 IMinMaxValue<Decimal64>.MaxValue => MaxValue;

    // The constants are their encodings, not text to be parsed at startup: a sixteen digit
    // coefficient at an exponent of -15.

    /// <summary>The base of natural logarithms: 2.718281828459045.</summary>
    public static Decimal64 E => new(Decimal64Encoding.Pack(false, -15, 2718281828459045));

    /// <summary>The ratio of a circle's circumference to its diameter: 3.141592653589793.</summary>
    public static Decimal64 Pi => new(Decimal64Encoding.Pack(false, -15, 3141592653589793));

    /// <summary>Two Pi: 6.283185307179586.</summary>
    public static Decimal64 Tau => new(Decimal64Encoding.Pack(false, -15, 6283185307179586));

    public static Decimal64 Abs(Decimal64 value) => CopyAbs(value);

    public static bool IsInteger(Decimal64 value)
    {
        if (Decimal64Encoding.IsSpecial(value._bits))
        {
            return false;
        }

        var coefficient = Decimal64Encoding.Unpack(value._bits, out var exponent);
        return exponent >= 0 || FractionIsZero(coefficient, exponent);
    }

    public static bool IsEvenInteger(Decimal64 value) => HasParity(value, 0);

    public static bool IsOddInteger(Decimal64 value) => HasParity(value, 1);

    private static bool HasParity(Decimal64 value, ulong wanted)
    {
        if (!IsInteger(value))
        {
            return false;
        }

        var coefficient = Decimal64Encoding.Unpack(value._bits, out var exponent);
        if (coefficient == 0 || exponent > 0)
        {
            // A positive exponent means trailing zeros, so the value is even whatever the
            // coefficient's last digit is.
            return wanted == 0;
        }

        var whole = exponent == 0
            ? coefficient
            : Decimal64Tables.DivRemPowerOfTen(coefficient, -exponent, out _);

        return (whole & 1) == wanted;
    }

    private static bool FractionIsZero(ulong coefficient, int exponent)
    {
        if (-exponent > Decimal64Tables.MaxPower)
        {
            return coefficient == 0;
        }

        Decimal64Tables.DivRemPowerOfTen(coefficient, -exponent, out var fraction);
        return fraction == 0;
    }

    public static bool IsPositive(Decimal64 value) => !IsNegative(value);

    public static bool IsRealNumber(Decimal64 value) => !IsNaN(value);

    public static bool IsComplexNumber(Decimal64 value) => false;

    public static bool IsImaginaryNumber(Decimal64 value) => false;

    // Selection, as .NET means it. Max and Min without a context are IEEE 754's maximum
    // and minimum, which give a NaN when either operand is one. The Number forms are the
    // specification's max and min, which hand back the number standing beside a quiet NaN
    // -- and so are the overloads taking a context, which the corpus is written against.

    public static Decimal64 Max(Decimal64 left, Decimal64 right) => SelectOrNaN(left, right, true, false);

    public static Decimal64 MaxNumber(Decimal64 left, Decimal64 right)
    {
        var context = new Decimal64Context();
        return Max(left, right, ref context);
    }

    public static Decimal64 Min(Decimal64 left, Decimal64 right) => SelectOrNaN(left, right, false, false);

    public static Decimal64 MinNumber(Decimal64 left, Decimal64 right)
    {
        var context = new Decimal64Context();
        return Min(left, right, ref context);
    }

    public static Decimal64 MaxMagnitude(Decimal64 left, Decimal64 right) => SelectOrNaN(left, right, true, true);

    public static Decimal64 MaxMagnitudeNumber(Decimal64 left, Decimal64 right)
    {
        var context = new Decimal64Context();
        return MaxMagnitude(left, right, ref context);
    }

    public static Decimal64 MinMagnitude(Decimal64 left, Decimal64 right) => SelectOrNaN(left, right, false, true);

    public static Decimal64 MinMagnitudeNumber(Decimal64 left, Decimal64 right)
    {
        var context = new Decimal64Context();
        return MinMagnitude(left, right, ref context);
    }

    private static Decimal64 SelectOrNaN(Decimal64 left, Decimal64 right, bool wantLarger, bool byMagnitude)
    {
        var status = Decimal64Status.None;
        if (Decimal64Encoding.IsNaN(left._bits) || Decimal64Encoding.IsNaN(right._bits))
        {
            return new Decimal64(Decimal64Arithmetic.PropagateNaN(left._bits, right._bits, ref status));
        }

        return new Decimal64(Decimal64Ordering.Select(left._bits, right._bits, wantLarger, byMagnitude, ref status));
    }

    /// <summary>
    /// Where the value sits against zero: -1, 0, or 1. A NaN has no place on that line and
    /// throws, as it does for the built-in floating-point types.
    /// </summary>
    public static int Sign(Decimal64 value)
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
    public static Decimal64 Clamp(Decimal64 value, Decimal64 min, Decimal64 max)
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

    public static Decimal64 Round(Decimal64 value) => Round(value, 0, MidpointRounding.ToEven);

    public static Decimal64 Round(Decimal64 value, int digits) => Round(value, digits, MidpointRounding.ToEven);

    public static Decimal64 Round(Decimal64 value, MidpointRounding mode) => Round(value, 0, mode);

    public static Decimal64 Round(Decimal64 value, int digits, MidpointRounding mode) =>
        new(Decimal64Shaping.Round(value._bits, digits, FromMidpointRounding(mode)));

    /// <summary>The value rounded toward positive infinity.</summary>
    public static Decimal64 Ceiling(Decimal64 value) => Round(value, 0, MidpointRounding.ToPositiveInfinity);

    /// <summary>The value rounded toward negative infinity.</summary>
    public static Decimal64 Floor(Decimal64 value) => Round(value, 0, MidpointRounding.ToNegativeInfinity);

    /// <summary>The value with its fractional part dropped, so rounded toward zero.</summary>
    public static Decimal64 Truncate(Decimal64 value) => Round(value, 0, MidpointRounding.ToZero);

    /// <summary>
    /// The value as an integer type, truncated toward zero and clamped to that type's
    /// range. A NaN converts to zero, which is what converting a double gives.
    /// </summary>
    public static TInteger ConvertToInteger<TInteger>(Decimal64 value)
        where TInteger : IBinaryInteger<TInteger> => TInteger.CreateSaturating(value);

    /// <summary>
    /// The same conversion. The two differ for the built-in types, where the native form
    /// leaves an out-of-range value to the hardware; here there is nothing to leave to it.
    /// </summary>
    public static TInteger ConvertToIntegerNative<TInteger>(Decimal64 value)
        where TInteger : IBinaryInteger<TInteger> => TInteger.CreateSaturating(value);

    // Text.

    public string ToString(string? format, IFormatProvider? provider) =>
        Decimal64Formatter.Format(_bits, format, provider);

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format,
        IFormatProvider? provider) =>
        Decimal64Formatter.TryFormat(_bits, destination, out charsWritten, format, provider);

    // The overloads that carry a culture read that culture's separators, signs, and
    // symbols; the ones above carry none and read the specification's grammar, which is
    // invariant by definition. The pair a caller picks has to match on both sides -- the
    // text of a value under a culture is only a number to that same culture.

    public static Decimal64 Parse(string s, IFormatProvider? provider) =>
        Parse(s, Decimal64CultureNormalizer.DefaultStyles, provider);

    public static Decimal64 Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        Parse(s, Decimal64CultureNormalizer.DefaultStyles, provider);

    public static Decimal64 Parse(string s, NumberStyles style, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return Parse(s.AsSpan(), style, provider);
    }

    public static Decimal64 Parse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider)
    {
        if (!TryParse(s, style, provider, out var value))
        {
            throw new FormatException($"'{s}' is not a Decimal64.");
        }

        return value;
    }

    public static bool TryParse(string? s, IFormatProvider? provider, out Decimal64 result) =>
        TryParse(s, Decimal64CultureNormalizer.DefaultStyles, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Decimal64 result) =>
        TryParse(s, Decimal64CultureNormalizer.DefaultStyles, provider, out result);

    public static bool TryParse(string? s, NumberStyles style, IFormatProvider? provider, out Decimal64 result)
    {
        Decimal64CultureNormalizer.ValidateStyles(style, nameof(style));

        if (s is null)
        {
            result = Zero;
            return false;
        }

        return TryParse(s.AsSpan(), style, provider, out result);
    }

    public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider,
        out Decimal64 result)
    {
        Decimal64CultureNormalizer.ValidateStyles(style, nameof(style));

        var status = Decimal64Status.None;
        var bits = Decimal64Parser.Parse(s, style, provider, Decimal64Rounding.HalfEven, ref status);
        return Reported(bits, status, out result);
    }

    private static bool Reported(ulong bits, Decimal64Status status, out Decimal64 result)
    {
        if ((status & Decimal64Status.ConversionSyntax) != 0)
        {
            result = Zero;
            return false;
        }

        result = new Decimal64(bits);
        return true;
    }

    // The same text as UTF-8, for callers working in bytes.

    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format,
        IFormatProvider? provider) =>
        Decimal64Formatter.TryFormat(_bits, utf8Destination, out bytesWritten, format, provider);

    public static Decimal64 Parse(ReadOnlySpan<byte> utf8Text)
    {
        if (!TryParse(utf8Text, out var value))
        {
            throw new FormatException($"'{Encoding.UTF8.GetString(utf8Text)}' is not a Decimal64.");
        }

        return value;
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, out Decimal64 value)
    {
        var status = Decimal64Status.None;
        var bits = Decimal64Parser.Parse(utf8Text, Decimal64Rounding.HalfEven, ref status);
        return Reported(bits, status, out value);
    }

    public static Decimal64 Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider)
    {
        if (!TryParse(utf8Text, provider, out var value))
        {
            throw new FormatException($"'{Encoding.UTF8.GetString(utf8Text)}' is not a Decimal64.");
        }

        return value;
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out Decimal64 result)
    {
        var status = Decimal64Status.None;
        var bits = Decimal64Parser.Parse(utf8Text, Decimal64CultureNormalizer.DefaultStyles, provider,
            Decimal64Rounding.HalfEven, ref status);

        return Reported(bits, status, out result);
    }

    // The exponent and the coefficient, for callers that want the parts as bytes.

    int IFloatingPoint<Decimal64>.GetExponentByteCount() => sizeof(int);

    int IFloatingPoint<Decimal64>.GetExponentShortestBitLength()
    {
        var exponent = Exponent(_bits);
        return exponent >= 0
            ? (sizeof(int) * 8) - int.LeadingZeroCount(exponent)
            : (sizeof(int) * 8) + 1 - int.LeadingZeroCount(~exponent);
    }

    int IFloatingPoint<Decimal64>.GetSignificandByteCount() => sizeof(ulong);

    int IFloatingPoint<Decimal64>.GetSignificandBitLength() => sizeof(ulong) * 8;

    bool IFloatingPoint<Decimal64>.TryWriteExponentBigEndian(Span<byte> destination, out int written) =>
        TryWrite(Exponent(_bits), destination, out written, true);

    bool IFloatingPoint<Decimal64>.TryWriteExponentLittleEndian(Span<byte> destination, out int written) =>
        TryWrite(Exponent(_bits), destination, out written, false);

    bool IFloatingPoint<Decimal64>.TryWriteSignificandBigEndian(Span<byte> destination, out int written) =>
        TryWrite(Coefficient(_bits), destination, out written, true);

    bool IFloatingPoint<Decimal64>.TryWriteSignificandLittleEndian(Span<byte> destination, out int written) =>
        TryWrite(Coefficient(_bits), destination, out written, false);

    private static int Exponent(ulong bits)
    {
        if (Decimal64Encoding.IsSpecial(bits))
        {
            return 0;
        }

        Decimal64Encoding.Unpack(bits, out var exponent);
        return exponent;
    }

    private static ulong Coefficient(ulong bits)
    {
        if (Decimal64Encoding.IsSpecial(bits))
        {
            return Decimal64Encoding.IsNaN(bits) ? Decimal64Encoding.Payload(bits) : 0;
        }

        return Decimal64Encoding.Unpack(bits, out _);
    }

    // Conversions to and from the other numeric types.

    static bool INumberBase<Decimal64>.TryConvertFromChecked<TOther>(TOther value, out Decimal64 result) =>
        TryConvertFrom(value, out result);

    static bool INumberBase<Decimal64>.TryConvertFromSaturating<TOther>(TOther value, out Decimal64 result) =>
        TryConvertFrom(value, out result);

    static bool INumberBase<Decimal64>.TryConvertFromTruncating<TOther>(TOther value, out Decimal64 result) =>
        TryConvertFrom(value, out result);

    static bool INumberBase<Decimal64>.TryConvertToChecked<TOther>(Decimal64 value, out TOther result)
        where TOther : default => TryConvertTo(value, out result);

    static bool INumberBase<Decimal64>.TryConvertToSaturating<TOther>(Decimal64 value, out TOther result)
        where TOther : default => TryConvertTo(value, out result);

    static bool INumberBase<Decimal64>.TryConvertToTruncating<TOther>(Decimal64 value, out TOther result)
        where TOther : default => TryConvertTo(value, out result);

    // Conversions. Widening is implicit: every value of an integer type up to ten digits is
    // held here exactly. Narrowing is explicit, since it can round, overflow to an infinity,
    // or underflow to a subnormal. Conversions to an integer truncate toward zero and throw
    // when the value will not fit, which is what System.Decimal does; the generic-math
    // CreateSaturating and CreateTruncating are there for the other two behaviors.

    public static implicit operator Decimal64(sbyte value) => new(Decimal64Conversions.FromInt64(value));

    public static implicit operator Decimal64(byte value) => new(Decimal64Conversions.FromUInt64(value, false));

    public static implicit operator Decimal64(short value) => new(Decimal64Conversions.FromInt64(value));

    public static implicit operator Decimal64(ushort value) => new(Decimal64Conversions.FromUInt64(value, false));

    public static implicit operator Decimal64(char value) => new(Decimal64Conversions.FromUInt64(value, false));

    public static implicit operator Decimal64(int value) => new(Decimal64Conversions.FromInt64(value));

    public static implicit operator Decimal64(uint value) => new(Decimal64Conversions.FromUInt64(value, false));

    // Nineteen and twenty digits, against the sixteen this format holds.
    public static explicit operator Decimal64(long value) => new(Decimal64Conversions.FromInt64(value));

    public static explicit operator Decimal64(ulong value) => new(Decimal64Conversions.FromUInt64(value, false));

    public static explicit operator Decimal64(Half value) => new(Decimal64Conversions.FromBinary(value));

    public static explicit operator Decimal64(float value) => new(Decimal64Conversions.FromBinary(value));

    public static explicit operator Decimal64(double value) => new(Decimal64Conversions.FromBinary(value));

    /// <summary>
    /// Reads a binary floating-point value the way <paramref name="conversion"/> asks for.
    /// The cast operators above take the shortest reading; this is how to ask for IEEE
    /// 754's convertFormat instead, which gives the binary value itself.
    /// </summary>
    public static Decimal64 FromBinary(double value, Decimal64BinaryConversion conversion) =>
        new(Decimal64Conversions.FromBinary(value, conversion));

    public static Decimal64 FromBinary(float value, Decimal64BinaryConversion conversion) =>
        new(Decimal64Conversions.FromBinary(value, conversion));

    public static Decimal64 FromBinary(Half value, Decimal64BinaryConversion conversion) =>
        new(Decimal64Conversions.FromBinary(value, conversion));

    public static explicit operator sbyte(Decimal64 value) => Decimal64Conversions.ToInteger<sbyte>(value._bits);

    public static explicit operator byte(Decimal64 value) => Decimal64Conversions.ToInteger<byte>(value._bits);

    public static explicit operator short(Decimal64 value) => Decimal64Conversions.ToInteger<short>(value._bits);

    public static explicit operator ushort(Decimal64 value) => Decimal64Conversions.ToInteger<ushort>(value._bits);

    public static explicit operator char(Decimal64 value) => (char)Decimal64Conversions.ToInteger<ushort>(value._bits);

    public static explicit operator int(Decimal64 value) => Decimal64Conversions.ToInteger<int>(value._bits);

    public static explicit operator uint(Decimal64 value) => Decimal64Conversions.ToInteger<uint>(value._bits);

    public static explicit operator long(Decimal64 value) => Decimal64Conversions.ToInteger<long>(value._bits);

    public static explicit operator ulong(Decimal64 value) => Decimal64Conversions.ToInteger<ulong>(value._bits);

    public static explicit operator Half(Decimal64 value) => (Half)Decimal64Formatter.ToDouble(value._bits);

    public static explicit operator float(Decimal64 value) => (float)Decimal64Formatter.ToDouble(value._bits);

    public static explicit operator double(Decimal64 value) => Decimal64Formatter.ToDouble(value._bits);

    private static bool TryConvertFrom<TOther>(TOther value, out Decimal64 result)
        where TOther : INumberBase<TOther>
    {
        var converted = Decimal64Conversions.TryFrom(value, out var bits);
        result = new Decimal64(bits);
        return converted;
    }

    private static bool TryConvertTo<TOther>(Decimal64 value, out TOther result)
        where TOther : INumberBase<TOther>?
    {
        var converted = Decimal64Conversions.TryTo<TOther>(value._bits, out var narrowed);
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

    private static bool TryWrite(ulong coefficient, Span<byte> destination, out int written, bool bigEndian)
    {
        written = sizeof(ulong);
        if (destination.Length < written)
        {
            written = 0;
            return false;
        }

        return bigEndian
            ? BinaryPrimitives.TryWriteUInt64BigEndian(destination, coefficient)
            : BinaryPrimitives.TryWriteUInt64LittleEndian(destination, coefficient);
    }

    private static Decimal64Rounding FromMidpointRounding(MidpointRounding mode) => mode switch
    {
        MidpointRounding.AwayFromZero => Decimal64Rounding.HalfUp,
        MidpointRounding.ToZero => Decimal64Rounding.Down,
        MidpointRounding.ToPositiveInfinity => Decimal64Rounding.Ceiling,
        MidpointRounding.ToNegativeInfinity => Decimal64Rounding.Floor,
        _ => Decimal64Rounding.HalfEven
    };

    /// <summary>
    /// Numeric equality, so 1.0 equals 1.00 and the two zeros are equal. NaN equals NaN
    /// here, unlike under <c>==</c>, so a value can be found in a collection.
    /// <see cref="CompareTotal"/> is the bit-level ordering; <see cref="ToBits"/> the
    /// bits themselves.
    /// </summary>
    public bool Equals(Decimal64 other)
    {
        var thisNaN = Decimal64Encoding.IsNaN(_bits);
        var otherNaN = Decimal64Encoding.IsNaN(other._bits);
        if (thisNaN || otherNaN)
        {
            return thisNaN && otherNaN;
        }

        return Decimal64Ordering.CompareValues(_bits, other._bits) == 0;
    }

    public override bool Equals(object? other) => other is Decimal64 value && Equals(value);

    /// <summary>Hashes the value, so members of one cohort agree.</summary>
    public override int GetHashCode() => Decimal64Ordering.ValueHashCode(_bits);

    /// <summary>Orders for sorting: NaNs sort below everything and equal each other.</summary>
    public int CompareTo(Decimal64 other)
    {
        var thisNaN = Decimal64Encoding.IsNaN(_bits);
        var otherNaN = Decimal64Encoding.IsNaN(other._bits);
        if (thisNaN || otherNaN)
        {
            if (thisNaN && otherNaN)
            {
                return 0;
            }

            return thisNaN ? -1 : 1;
        }

        return Decimal64Ordering.CompareValues(_bits, other._bits);
    }

    public int CompareTo(object? other)
    {
        if (other is null)
        {
            return 1;
        }

        if (other is not Decimal64 value)
        {
            throw new ArgumentException("Expected a Decimal64.", nameof(other));
        }

        return CompareTo(value);
    }

    // Numeric comparison, which leaves a NaN unordered: every one of these is false when
    // either side is a NaN, including a NaN against itself.

    private static bool Ordered(Decimal64 left, Decimal64 right, out int comparison)
    {
        if (Decimal64Encoding.IsNaN(left._bits) || Decimal64Encoding.IsNaN(right._bits))
        {
            comparison = 0;
            return false;
        }

        comparison = Decimal64Ordering.CompareValues(left._bits, right._bits);
        return true;
    }

    public static bool operator ==(Decimal64 left, Decimal64 right) =>
        Ordered(left, right, out var comparison) && comparison == 0;

    public static bool operator !=(Decimal64 left, Decimal64 right) => !(left == right);

    public static bool operator <(Decimal64 left, Decimal64 right) =>
        Ordered(left, right, out var comparison) && comparison < 0;

    public static bool operator >(Decimal64 left, Decimal64 right) =>
        Ordered(left, right, out var comparison) && comparison > 0;

    public static bool operator <=(Decimal64 left, Decimal64 right) =>
        Ordered(left, right, out var comparison) && comparison <= 0;

    public static bool operator >=(Decimal64 left, Decimal64 right) =>
        Ordered(left, right, out var comparison) && comparison >= 0;

    public static Decimal64 operator ++(Decimal64 value) => value + One;

    public static Decimal64 operator --(Decimal64 value) => value - One;
}
