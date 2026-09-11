// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;

using Core = Decimals.DecimalCore<Decimals.Decimal128Format, System.UInt128>;

namespace Decimals;

/// <summary>
/// An IEEE 754 decimal128 value: 34 digits of coefficient, exponents from -6176 to +6111.
/// </summary>
/// <remarks>
/// The in-memory encoding is DPD, the interchange form, so the raw bits are the bits
/// decNumber writes and decimal hardware consumes. <see cref="ToBidBits"/> and
/// <see cref="FromBidBits"/> reach the binary-integer form. Everything here forwards to
/// <see cref="DecimalCore{TFormat, TBits}"/>, which holds the one copy of the logic.
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
    private readonly UInt128 _bits;

    private Decimal128(UInt128 bits) => _bits = bits;

    public static Decimal128 Zero => new(Core.Zero);

    public static Decimal128 NegativeZero => new(Core.NegativeZero);

    public static Decimal128 One => new(Core.One);

    public static Decimal128 NegativeOne => new(Core.NegativeOne);

    public static Decimal128 MaxValue => new(Core.MaxValue);

    public static Decimal128 MinValue => new(Core.MinValue);

    /// <summary>The smallest positive value, which is subnormal: 1E-6176.</summary>
    public static Decimal128 Epsilon => new(Core.Epsilon);

    public static Decimal128 PositiveInfinity => new(Core.Special(DecimalKind.Infinity, false));

    public static Decimal128 NegativeInfinity => new(Core.Special(DecimalKind.Infinity, true));

    public static Decimal128 NaN => new(Core.Special(DecimalKind.QuietNaN, false));

    /// <summary>The raw encoding, which is DPD.</summary>
    public UInt128 ToBits() => _bits;

    public static Decimal128 FromBits(UInt128 bits) => new(bits);

    /// <summary>
    /// The densely-packed-decimal interchange encoding, which is what decimal hardware and
    /// DPD-based libraries exchange. This is the in-memory encoding, so it costs nothing.
    /// </summary>
    public UInt128 ToDpdBits() => Decimal128Format.ToDpd(_bits);

    public static Decimal128 FromDpdBits(UInt128 bits) => new(Decimal128Format.FromDpd(bits));

    /// <summary>
    /// The binary-integer-decimal encoding, where the coefficient is a plain integer in the
    /// trailing field. IEEE 754 defines both encodings and either conforms; this one is
    /// what Intel's library and the hardware-free implementations built on it exchange.
    /// </summary>
    public UInt128 ToBidBits() => Decimal128Format.ToBid(_bits);

    public static Decimal128 FromBidBits(UInt128 bits) => new(Decimal128Format.FromBid(bits));

    public static bool IsNaN(Decimal128 value) => Core.IsNaN(value._bits);

    public static bool IsSignalingNaN(Decimal128 value) => Core.IsSignalingNaN(value._bits);

    public static bool IsInfinity(Decimal128 value) => Core.IsInfinity(value._bits);

    public static bool IsFinite(Decimal128 value) => Core.IsFinite(value._bits);

    public static bool IsNegative(Decimal128 value) => Core.IsNegative(value._bits);

    public static bool IsPositiveInfinity(Decimal128 value) => IsInfinity(value) && !IsNegative(value);

    public static bool IsNegativeInfinity(Decimal128 value) => IsInfinity(value) && IsNegative(value);

    public static bool IsZero(Decimal128 value) => Core.IsZero(value._bits);

    public static bool IsSubnormal(Decimal128 value) => Core.IsSubnormal(value._bits);

    public static bool IsNormal(Decimal128 value) => Core.IsNormal(value._bits);

    /// <summary>
    /// False when the encoding uses a coefficient or payload the format cannot hold. Those
    /// decode as zero, so re-encoding does not give the same bits back.
    /// </summary>
    public static bool IsCanonical(Decimal128 value) => Core.IsCanonical(value._bits);

    /// <summary>The same value in its canonical encoding.</summary>
    public static Decimal128 Canonical(Decimal128 value) => new(Core.Canonical(value._bits));

    public static DecimalClass Class(Decimal128 value) => Core.Classify(value._bits);

    /// <summary>Copies the value with a cleared sign. Quiet: a signaling NaN stays one.</summary>
    public static Decimal128 CopyAbs(Decimal128 value) => new(Core.CopyAbs(value._bits));

    /// <summary>Copies the value with its sign flipped. Quiet, unlike arithmetic negation.</summary>
    public static Decimal128 CopyNegate(Decimal128 value) => new(Core.CopyNegate(value._bits));

    public static Decimal128 CopySign(Decimal128 value, Decimal128 sign) =>
        new(Core.CopySign(value._bits, sign._bits));

    /// <summary>
    /// True when both are finite with the same exponent, or both are infinite, or both are
    /// NaN. Never raises a condition, even for a signaling NaN.
    /// </summary>
    public static bool SameQuantum(Decimal128 left, Decimal128 right) =>
        Core.SameQuantum(left._bits, right._bits);

    public static int CompareTotal(Decimal128 left, Decimal128 right) =>
        Core.CompareTotal(left._bits, right._bits);

    public static int CompareTotalMagnitude(Decimal128 left, Decimal128 right) =>
        Core.CompareTotalMagnitude(left._bits, right._bits);

    /// <summary>
    /// The specification's to-number conversion: a bad string gives a quiet NaN and raises
    /// <see cref="DecimalStatus.ConversionSyntax"/> rather than throwing.
    /// </summary>
    public static Decimal128 FromString(ReadOnlySpan<char> text, ref DecimalContext context) =>
        new(Core.FromString(text, ref context));

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
        var parsed = Core.TryParse(text, out var bits);
        value = new Decimal128(bits);
        return parsed;
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

    public override string ToString() => Core.ToScientificString(_bits);

    public string ToEngineeringString() => Core.ToEngineeringString(_bits);

    // Arithmetic. The plain form rounds half to even and drops the conditions, which is
    // IEEE 754 default exception handling; the overload taking a context does neither.

    public static Decimal128 Add(Decimal128 left, Decimal128 right, ref DecimalContext context) =>
        new(Core.Add(left._bits, right._bits, ref context));

    public static Decimal128 Add(Decimal128 left, Decimal128 right)
    {
        var context = new DecimalContext();
        return Add(left, right, ref context);
    }

    public static Decimal128 Subtract(Decimal128 left, Decimal128 right, ref DecimalContext context) =>
        new(Core.Subtract(left._bits, right._bits, ref context));

    public static Decimal128 Subtract(Decimal128 left, Decimal128 right)
    {
        var context = new DecimalContext();
        return Subtract(left, right, ref context);
    }

    public static Decimal128 Multiply(Decimal128 left, Decimal128 right, ref DecimalContext context) =>
        new(Core.Multiply(left._bits, right._bits, ref context));

    public static Decimal128 Multiply(Decimal128 left, Decimal128 right)
    {
        var context = new DecimalContext();
        return Multiply(left, right, ref context);
    }

    public static Decimal128 Divide(Decimal128 left, Decimal128 right, ref DecimalContext context) =>
        new(Core.Divide(left._bits, right._bits, ref context));

    public static Decimal128 Divide(Decimal128 left, Decimal128 right)
    {
        var context = new DecimalContext();
        return Divide(left, right, ref context);
    }

    /// <summary>The integer part of the quotient, with a zero exponent.</summary>
    public static Decimal128 DivideInteger(Decimal128 left, Decimal128 right, ref DecimalContext context) =>
        new(Core.DivideInteger(left._bits, right._bits, ref context));

    public static Decimal128 DivideInteger(Decimal128 left, Decimal128 right)
    {
        var context = new DecimalContext();
        return DivideInteger(left, right, ref context);
    }

    public static Decimal128 Remainder(Decimal128 left, Decimal128 right, ref DecimalContext context) =>
        new(Core.Remainder(left._bits, right._bits, ref context));

    public static Decimal128 Remainder(Decimal128 left, Decimal128 right)
    {
        var context = new DecimalContext();
        return Remainder(left, right, ref context);
    }

    /// <summary>IEEE 754's remainder, which takes the quotient to the nearest integer.</summary>
    public static Decimal128 RemainderNear(Decimal128 left, Decimal128 right, ref DecimalContext context) =>
        new(Core.RemainderNear(left._bits, right._bits, ref context));

    public static Decimal128 RemainderNear(Decimal128 left, Decimal128 right)
    {
        var context = new DecimalContext();
        return RemainderNear(left, right, ref context);
    }

    /// <summary>Multiply and add, rounded once rather than twice.</summary>
    public static Decimal128 FusedMultiplyAdd(Decimal128 left, Decimal128 right, Decimal128 addend,
        ref DecimalContext context) =>
        new(Core.FusedMultiplyAdd(left._bits, right._bits, addend._bits, ref context));

    public static Decimal128 FusedMultiplyAdd(Decimal128 left, Decimal128 right, Decimal128 addend)
    {
        var context = new DecimalContext();
        return FusedMultiplyAdd(left, right, addend, ref context);
    }

    /// <summary>Zero plus the value, so the format's rounding is applied.</summary>
    public static Decimal128 Plus(Decimal128 value, ref DecimalContext context) =>
        new(Core.Plus(value._bits, ref context));

    public static Decimal128 Plus(Decimal128 value)
    {
        var context = new DecimalContext();
        return Plus(value, ref context);
    }

    /// <summary>Zero minus the value. Arithmetic, unlike <see cref="CopyNegate"/>.</summary>
    public static Decimal128 Minus(Decimal128 value, ref DecimalContext context) =>
        new(Core.Minus(value._bits, ref context));

    public static Decimal128 Minus(Decimal128 value)
    {
        var context = new DecimalContext();
        return Minus(value, ref context);
    }

    /// <summary>The magnitude, arithmetically. Unlike <see cref="CopyAbs"/> this can signal.</summary>
    public static Decimal128 Abs(Decimal128 value, ref DecimalContext context) =>
        new(Core.Abs(value._bits, ref context));

    /// <summary>
    /// Numeric comparison giving -1, 0, 1, or NaN. Unlike <see cref="CompareTotal"/> the two
    /// zeros are equal and a NaN operand gives a NaN.
    /// </summary>
    public static Decimal128 Compare(Decimal128 left, Decimal128 right, ref DecimalContext context) =>
        new(Core.Compare(left._bits, right._bits, false, ref context));

    /// <summary>Comparison that signals on any NaN, not only a signaling one.</summary>
    public static Decimal128 CompareSignal(Decimal128 left, Decimal128 right, ref DecimalContext context) =>
        new(Core.Compare(left._bits, right._bits, true, ref context));

    // Digit-wise logical operations. Each operand must be a string of ones and zeros with
    // a zero exponent and no sign; anything else is an invalid operation.

    public static Decimal128 And(Decimal128 left, Decimal128 right, ref DecimalContext context) =>
        new(Core.And(left._bits, right._bits, ref context));

    public static Decimal128 Or(Decimal128 left, Decimal128 right, ref DecimalContext context) =>
        new(Core.Or(left._bits, right._bits, ref context));

    public static Decimal128 Xor(Decimal128 left, Decimal128 right, ref DecimalContext context) =>
        new(Core.Xor(left._bits, right._bits, ref context));

    public static Decimal128 Invert(Decimal128 value, ref DecimalContext context) =>
        new(Core.Invert(value._bits, ref context));

    /// <summary>The larger of the two. A quiet NaN beside a number loses to the number.</summary>
    public static Decimal128 Max(Decimal128 left, Decimal128 right, ref DecimalContext context) =>
        new(Core.Max(left._bits, right._bits, ref context));

    public static Decimal128 Min(Decimal128 left, Decimal128 right, ref DecimalContext context) =>
        new(Core.Min(left._bits, right._bits, ref context));

    public static Decimal128 MaxMagnitude(Decimal128 left, Decimal128 right, ref DecimalContext context) =>
        new(Core.MaxMagnitude(left._bits, right._bits, ref context));

    public static Decimal128 MinMagnitude(Decimal128 left, Decimal128 right, ref DecimalContext context) =>
        new(Core.MinMagnitude(left._bits, right._bits, ref context));

    /// <summary>The adjusted exponent, as an integer.</summary>
    public static Decimal128 LogB(Decimal128 value, ref DecimalContext context) =>
        new(Core.LogB(value._bits, ref context));

    /// <summary>Multiplies by ten raised to the second operand.</summary>
    public static Decimal128 ScaleB(Decimal128 value, Decimal128 scale, ref DecimalContext context) =>
        new(Core.ScaleB(value._bits, scale._bits, ref context));

    /// <summary>Removes trailing zeros, giving the shortest coefficient of the same value.</summary>
    public static Decimal128 Reduce(Decimal128 value, ref DecimalContext context) =>
        new(Core.Reduce(value._bits, ref context));

    /// <summary>Like <see cref="Reduce"/>, but never past a zero exponent.</summary>
    public static Decimal128 Trim(Decimal128 value) => new(Core.Trim(value._bits));

    /// <summary>Rounds to an integer without reporting that anything was rounded.</summary>
    public static Decimal128 RoundToIntegral(Decimal128 value, ref DecimalContext context) =>
        new(Core.ToIntegral(value._bits, false, ref context));

    /// <summary>Rounds to an integer, reporting inexactness.</summary>
    public static Decimal128 RoundToIntegralExact(Decimal128 value, ref DecimalContext context) =>
        new(Core.ToIntegral(value._bits, true, ref context));

    /// <summary>Rescales to the second operand's exponent.</summary>
    public static Decimal128 Quantize(Decimal128 value, Decimal128 pattern, ref DecimalContext context) =>
        new(Core.Quantize(value._bits, pattern._bits, ref context));

    /// <summary>Rotates the coefficient's digits within the format's full width.</summary>
    public static Decimal128 Rotate(Decimal128 value, Decimal128 places, ref DecimalContext context) =>
        new(Core.Rotate(value._bits, places._bits, ref context));

    /// <summary>Shifts the coefficient's digits, dropping what falls off the end.</summary>
    public static Decimal128 Shift(Decimal128 value, Decimal128 places, ref DecimalContext context) =>
        new(Core.Shift(value._bits, places._bits, ref context));

    /// <summary>The square root, correctly rounded.</summary>
    public static Decimal128 Sqrt(Decimal128 value, ref DecimalContext context) =>
        new(Core.SquareRoot(value._bits, ref context));

    public static Decimal128 Sqrt(Decimal128 value)
    {
        var context = new DecimalContext();
        return Sqrt(value, ref context);
    }

    /// <summary>e raised to the value.</summary>
    public static Decimal128 Exp(Decimal128 value, ref DecimalContext context) =>
        new(Core.Exp(value._bits, ref context));

    public static Decimal128 Exp(Decimal128 value)
    {
        var context = new DecimalContext();
        return Exp(value, ref context);
    }

    /// <summary>The natural logarithm.</summary>
    public static Decimal128 Log(Decimal128 value, ref DecimalContext context) =>
        new(Core.Log(value._bits, ref context));

    public static Decimal128 Log(Decimal128 value)
    {
        var context = new DecimalContext();
        return Log(value, ref context);
    }

    /// <summary>The base-ten logarithm.</summary>
    public static Decimal128 Log10(Decimal128 value, ref DecimalContext context) =>
        new(Core.Log10(value._bits, ref context));

    public static Decimal128 Log10(Decimal128 value)
    {
        var context = new DecimalContext();
        return Log10(value, ref context);
    }

    /// <summary>The first value raised to the second.</summary>
    public static Decimal128 Pow(Decimal128 value, Decimal128 power, ref DecimalContext context) =>
        new(Core.Power(value._bits, power._bits, ref context));

    public static Decimal128 Pow(Decimal128 value, Decimal128 power)
    {
        var context = new DecimalContext();
        return Pow(value, power, ref context);
    }

    /// <summary>Two raised to the value.</summary>
    public static Decimal128 Exp2(Decimal128 value, ref DecimalContext context) =>
        new(Core.Exp2(value._bits, ref context));

    public static Decimal128 Exp2(Decimal128 value)
    {
        var context = new DecimalContext();
        return Exp2(value, ref context);
    }

    /// <summary>Ten raised to the value.</summary>
    public static Decimal128 Exp10(Decimal128 value, ref DecimalContext context) =>
        new(Core.Exp10(value._bits, ref context));

    public static Decimal128 Exp10(Decimal128 value)
    {
        var context = new DecimalContext();
        return Exp10(value, ref context);
    }

    // The M1 and P1 names are the ones the generic math interfaces declare, so they stay as
    // spelled there rather than written out.

    /// <summary>e raised to the value, less one.</summary>
    public static Decimal128 ExpM1(Decimal128 value, ref DecimalContext context) =>
        new(Core.ExpMinusOne(value._bits, ref context));

    public static Decimal128 ExpM1(Decimal128 value)
    {
        var context = new DecimalContext();
        return ExpM1(value, ref context);
    }

    /// <summary>Two raised to the value, less one.</summary>
    public static Decimal128 Exp2M1(Decimal128 value, ref DecimalContext context) =>
        new(Core.Exp2MinusOne(value._bits, ref context));

    public static Decimal128 Exp2M1(Decimal128 value)
    {
        var context = new DecimalContext();
        return Exp2M1(value, ref context);
    }

    /// <summary>Ten raised to the value, less one.</summary>
    public static Decimal128 Exp10M1(Decimal128 value, ref DecimalContext context) =>
        new(Core.Exp10MinusOne(value._bits, ref context));

    public static Decimal128 Exp10M1(Decimal128 value)
    {
        var context = new DecimalContext();
        return Exp10M1(value, ref context);
    }

    /// <summary>The logarithm in the given base.</summary>
    public static Decimal128 Log(Decimal128 value, Decimal128 newBase, ref DecimalContext context) =>
        new(Core.LogInBase(value._bits, newBase._bits, ref context));

    public static Decimal128 Log(Decimal128 value, Decimal128 newBase)
    {
        var context = new DecimalContext();
        return Log(value, newBase, ref context);
    }

    /// <summary>The base-two logarithm.</summary>
    public static Decimal128 Log2(Decimal128 value, ref DecimalContext context) =>
        new(Core.Log2(value._bits, ref context));

    public static Decimal128 Log2(Decimal128 value)
    {
        var context = new DecimalContext();
        return Log2(value, ref context);
    }

    /// <summary>The natural logarithm of one plus the value.</summary>
    public static Decimal128 LogP1(Decimal128 value, ref DecimalContext context) =>
        new(Core.LogPlusOne(value._bits, ref context));

    public static Decimal128 LogP1(Decimal128 value)
    {
        var context = new DecimalContext();
        return LogP1(value, ref context);
    }

    /// <summary>The base-two logarithm of one plus the value.</summary>
    public static Decimal128 Log2P1(Decimal128 value, ref DecimalContext context) =>
        new(Core.Log2PlusOne(value._bits, ref context));

    public static Decimal128 Log2P1(Decimal128 value)
    {
        var context = new DecimalContext();
        return Log2P1(value, ref context);
    }

    /// <summary>The base-ten logarithm of one plus the value.</summary>
    public static Decimal128 Log10P1(Decimal128 value, ref DecimalContext context) =>
        new(Core.Log10PlusOne(value._bits, ref context));

    public static Decimal128 Log10P1(Decimal128 value)
    {
        var context = new DecimalContext();
        return Log10P1(value, ref context);
    }

    /// <summary>The cube root.</summary>
    public static Decimal128 Cbrt(Decimal128 value, ref DecimalContext context) =>
        new(Core.Cbrt(value._bits, ref context));

    public static Decimal128 Cbrt(Decimal128 value)
    {
        var context = new DecimalContext();
        return Cbrt(value, ref context);
    }

    /// <summary>The root of the given degree.</summary>
    public static Decimal128 RootN(Decimal128 value, int degree, ref DecimalContext context) =>
        new(Core.RootN(value._bits, degree, ref context));

    public static Decimal128 RootN(Decimal128 value, int degree)
    {
        var context = new DecimalContext();
        return RootN(value, degree, ref context);
    }

    /// <summary>The square root of the sum of two squares, without overflowing on the way.</summary>
    public static Decimal128 Hypot(Decimal128 left, Decimal128 right, ref DecimalContext context) =>
        new(Core.Hypot(left._bits, right._bits, ref context));

    public static Decimal128 Hypot(Decimal128 left, Decimal128 right)
    {
        var context = new DecimalContext();
        return Hypot(left, right, ref context);
    }

    /// <summary>The next value toward positive infinity.</summary>
    public static Decimal128 NextPlus(Decimal128 value, ref DecimalContext context) =>
        new(Core.NextPlus(value._bits, ref context));

    /// <summary>The next value toward negative infinity.</summary>
    public static Decimal128 NextMinus(Decimal128 value, ref DecimalContext context) =>
        new(Core.NextMinus(value._bits, ref context));

    /// <summary>The next value from the first operand in the direction of the second.</summary>
    public static Decimal128 NextToward(Decimal128 value, Decimal128 target, ref DecimalContext context) =>
        new(Core.NextToward(value._bits, target._bits, ref context));

    // Four operations under the names .NET spells them with. Each is one of the operations
    // above, so code written against the built-in floating-point types finds them here.

    /// <summary>
    /// IEEE 754's remainder, which is
    /// <see cref="RemainderNear(Decimal128, Decimal128)"/>.
    /// </summary>
    public static Decimal128 Ieee754Remainder(Decimal128 left, Decimal128 right) =>
        RemainderNear(left, right);

    /// <summary>
    /// The adjusted exponent as an integer, which is <see cref="LogB"/> narrowed to one.
    /// The results off the end of the range are .NET's rather than the specification's: a
    /// zero gives <see cref="int.MinValue"/> and a NaN or an infinity
    /// <see cref="int.MaxValue"/>, where logb gives -Infinity, a NaN, and +Infinity.
    /// </summary>
    public static int ILogB(Decimal128 value) => Core.ILogB(value._bits);

    /// <summary>
    /// Multiplies by ten raised to <paramref name="scale"/>. The radix is this format's, so
    /// this is the specification's scaleb and not double's power of two; a scale too large
    /// to be an operand of it is an invalid operation giving a NaN, where double would
    /// overflow to an infinity.
    /// </summary>
    public static Decimal128 ScaleB(Decimal128 value, int scale) =>
        new(Core.ScaleB(value._bits, scale));

    /// <summary>The next value toward positive infinity, which is <see cref="NextPlus"/>.</summary>
    public static Decimal128 BitIncrement(Decimal128 value)
    {
        var context = new DecimalContext();
        return NextPlus(value, ref context);
    }

    /// <summary>The next value toward negative infinity, which is <see cref="NextMinus"/>.</summary>
    public static Decimal128 BitDecrement(Decimal128 value)
    {
        var context = new DecimalContext();
        return NextMinus(value, ref context);
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


    /// <summary>Ten. These are decimal formats, not binary ones.</summary>
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

    // The constants are their encodings, not text to be parsed at startup. Each is the DPD
    // form of the value written beside it: sign 0, an exponent of -33, and the thirty-four
    // digit coefficient as a leading digit in the combination field and eleven declets.

    /// <summary>The base of natural logarithms: 2.718281828459045235360287471352662.</summary>
    public static Decimal128 E => new(new UInt128(0x29fff9842d2e9644, 0x54d5e05ae7174b62));

    /// <summary>
    /// The ratio of a circle's circumference to its diameter:
    /// 3.141592653589793238462643383279503.
    /// </summary>
    public static Decimal128 Pi => new(new UInt128(0x2dffcc1aeb53b3fb, 0xb4e262d0dab5e683));

    /// <summary>Two Pi: 6.283185307179586476925286766559006.</summary>
    public static Decimal128 Tau => new(new UInt128(0x39ffd2b32d873e6e, 0xa9daad5abe6b6406));

    public static Decimal128 Abs(Decimal128 value) => CopyAbs(value);

    public static bool IsInteger(Decimal128 value) => Core.IsInteger(value._bits);

    public static bool IsEvenInteger(Decimal128 value) => Core.IsEvenInteger(value._bits);

    public static bool IsOddInteger(Decimal128 value) => Core.IsOddInteger(value._bits);

    public static bool IsPositive(Decimal128 value) => !IsNegative(value);

    public static bool IsRealNumber(Decimal128 value) => !IsNaN(value);

    public static bool IsComplexNumber(Decimal128 value) => false;

    public static bool IsImaginaryNumber(Decimal128 value) => false;

    // Selection, as .NET means it. Max and Min without a context are IEEE 754's maximum
    // and minimum, which give a NaN when either operand is one. The Number forms are the
    // specification's max and min, which hand back the number standing beside a quiet NaN
    // -- and so are the overloads taking a context, which the corpus is written against.

    public static Decimal128 Max(Decimal128 left, Decimal128 right) =>
        new(Core.Maximum(left._bits, right._bits));

    public static Decimal128 MaxNumber(Decimal128 left, Decimal128 right)
    {
        var context = new DecimalContext();
        return Max(left, right, ref context);
    }

    public static Decimal128 Min(Decimal128 left, Decimal128 right) =>
        new(Core.Minimum(left._bits, right._bits));

    public static Decimal128 MinNumber(Decimal128 left, Decimal128 right)
    {
        var context = new DecimalContext();
        return Min(left, right, ref context);
    }

    public static Decimal128 MaxMagnitude(Decimal128 left, Decimal128 right) =>
        new(Core.MaximumMagnitude(left._bits, right._bits));

    public static Decimal128 MaxMagnitudeNumber(Decimal128 left, Decimal128 right)
    {
        var context = new DecimalContext();
        return MaxMagnitude(left, right, ref context);
    }

    public static Decimal128 MinMagnitude(Decimal128 left, Decimal128 right) =>
        new(Core.MinimumMagnitude(left._bits, right._bits));

    public static Decimal128 MinMagnitudeNumber(Decimal128 left, Decimal128 right)
    {
        var context = new DecimalContext();
        return MinMagnitude(left, right, ref context);
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

    public static Decimal128 Round(Decimal128 value, int digits) =>
        Round(value, digits, MidpointRounding.ToEven);

    public static Decimal128 Round(Decimal128 value, MidpointRounding mode) => Round(value, 0, mode);

    public static Decimal128 Round(Decimal128 value, int digits, MidpointRounding mode) =>
        new(Core.Round(value._bits, digits, FromMidpointRounding(mode)));

    /// <summary>The value rounded toward positive infinity.</summary>
    public static Decimal128 Ceiling(Decimal128 value) =>
        Round(value, 0, MidpointRounding.ToPositiveInfinity);

    /// <summary>The value rounded toward negative infinity.</summary>
    public static Decimal128 Floor(Decimal128 value) =>
        Round(value, 0, MidpointRounding.ToNegativeInfinity);

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
        DecimalFormatter.Format(Decimal128Format.Unpack(_bits), format, provider);

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format,
        IFormatProvider? provider) =>
        DecimalFormatter.TryFormat(Decimal128Format.Unpack(_bits), destination, out charsWritten, format, provider);

    // The overloads that carry a culture read that culture's separators, signs, and
    // symbols; the ones above carry none and read the specification's grammar, which is
    // invariant by definition. The pair a caller picks has to match on both sides -- the
    // text of a value under a culture is only a number to that same culture.

    public static Decimal128 Parse(string s, IFormatProvider? provider) =>
        Parse(s, CultureNormalizer.DefaultStyles, provider);

    public static Decimal128 Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        Parse(s, CultureNormalizer.DefaultStyles, provider);

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
        TryParse(s, CultureNormalizer.DefaultStyles, provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Decimal128 result) =>
        TryParse(s, CultureNormalizer.DefaultStyles, provider, out result);

    public static bool TryParse(string? s, NumberStyles style, IFormatProvider? provider,
        out Decimal128 result)
    {
        CultureNormalizer.ValidateStyles(style, nameof(style));

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
        CultureNormalizer.ValidateStyles(style, nameof(style));

        var parsed = Core.TryParse(s, style, provider, out var bits);
        result = new Decimal128(bits);
        return parsed;
    }

    // The same text as UTF-8, for callers working in bytes.

    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format,
        IFormatProvider? provider) =>
        DecimalFormatter.TryFormat(Decimal128Format.Unpack(_bits), utf8Destination, out bytesWritten,
            format, provider);

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
        var parsed = Core.TryParse(utf8Text, out var bits);
        value = new Decimal128(bits);
        return parsed;
    }

    public static Decimal128 Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider)
    {
        if (!TryParse(utf8Text, provider, out var value))
        {
            throw new FormatException($"'{Encoding.UTF8.GetString(utf8Text)}' is not a Decimal128.");
        }

        return value;
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider,
        out Decimal128 result)
    {
        var parsed = Core.TryParse(utf8Text, CultureNormalizer.DefaultStyles, provider, out var bits);
        result = new Decimal128(bits);
        return parsed;
    }

    // The exponent and the coefficient, for callers that want the parts as bytes.

    int IFloatingPoint<Decimal128>.GetExponentByteCount() => sizeof(int);

    int IFloatingPoint<Decimal128>.GetExponentShortestBitLength()
    {
        var exponent = Decimal128Format.Unpack(_bits).Exponent;
        return exponent >= 0
            ? (sizeof(int) * 8) - int.LeadingZeroCount(exponent)
            : (sizeof(int) * 8) + 1 - int.LeadingZeroCount(~exponent);
    }

    int IFloatingPoint<Decimal128>.GetSignificandByteCount() => 16;

    int IFloatingPoint<Decimal128>.GetSignificandBitLength() => 16 * 8;

    bool IFloatingPoint<Decimal128>.TryWriteExponentBigEndian(Span<byte> destination, out int written) =>
        TryWrite(Decimal128Format.Unpack(_bits).Exponent, destination, out written, bigEndian: true);

    bool IFloatingPoint<Decimal128>.TryWriteExponentLittleEndian(Span<byte> destination, out int written) =>
        TryWrite(Decimal128Format.Unpack(_bits).Exponent, destination, out written, bigEndian: false);

    bool IFloatingPoint<Decimal128>.TryWriteSignificandBigEndian(Span<byte> destination, out int written) =>
        TryWrite(Decimal128Format.Unpack(_bits).Coefficient, destination, out written, bigEndian: true);

    bool IFloatingPoint<Decimal128>.TryWriteSignificandLittleEndian(Span<byte> destination, out int written) =>
        TryWrite(Decimal128Format.Unpack(_bits).Coefficient, destination, out written, bigEndian: false);

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

    // Conversions. Thirty-four digits holds every value of the narrower decimal formats
    // and of every integer type up to twenty digits, so all of those widen implicitly.
    // Int128 and UInt128 carry thirty-nine, and the binary floats carry a range this
    // format cannot always meet, so those are explicit. Conversions to an integer truncate
    // toward zero and throw when the value will not fit, as System.Decimal's do.

    public static implicit operator Decimal128(Decimal32 value) =>
        FromOtherFormat(Decimal32.Unpacked(value));

    public static implicit operator Decimal128(Decimal64 value) =>
        FromOtherFormat(Decimal64.Unpacked(value));

    public static explicit operator Decimal32(Decimal128 value) =>
        Decimal32.FromOtherFormat(Unpacked(value));

    public static explicit operator Decimal64(Decimal128 value) =>
        Decimal64.FromOtherFormat(Unpacked(value));

    public static implicit operator Decimal128(sbyte value) => new(Core.FromInteger((Int128)value));

    public static implicit operator Decimal128(byte value) => new(Core.FromInteger((UInt128)value));

    public static implicit operator Decimal128(short value) => new(Core.FromInteger((Int128)value));

    public static implicit operator Decimal128(ushort value) => new(Core.FromInteger((UInt128)value));

    public static implicit operator Decimal128(char value) => new(Core.FromInteger((UInt128)value));

    public static implicit operator Decimal128(int value) => new(Core.FromInteger((Int128)value));

    public static implicit operator Decimal128(uint value) => new(Core.FromInteger((UInt128)value));

    public static implicit operator Decimal128(long value) => new(Core.FromInteger((Int128)value));

    public static implicit operator Decimal128(ulong value) => new(Core.FromInteger((UInt128)value));

    // Thirty-nine digits, against the thirty-four this format holds.
    public static explicit operator Decimal128(Int128 value) => new(Core.FromInteger(value));

    public static explicit operator Decimal128(UInt128 value) => new(Core.FromInteger(value));

    public static explicit operator Decimal128(Half value) => new(Core.FromBinary(value));

    public static explicit operator Decimal128(float value) => new(Core.FromBinary(value));

    public static explicit operator Decimal128(double value) => new(Core.FromBinary(value));

    /// <summary>
    /// Reads a binary floating-point value the way <paramref name="conversion"/> asks for.
    /// The cast operators above take the shortest reading; this is how to ask for IEEE
    /// 754's convertFormat instead, which gives the binary value itself. Thirty-four digits
    /// is wide enough to show the difference: 0.1 reads as 0.1 one way and as
    /// 0.1000000000000000055511151231257827 the other.
    /// </summary>
    public static Decimal128 FromBinary(double value, BinaryConversion conversion) =>
        new(Core.FromBinary(value, conversion));

    public static Decimal128 FromBinary(float value, BinaryConversion conversion) =>
        new(Core.FromBinary(value, conversion));

    public static Decimal128 FromBinary(Half value, BinaryConversion conversion) =>
        new(Core.FromBinary(value, conversion));

    public static explicit operator sbyte(Decimal128 value) => Core.ToInteger<sbyte>(value._bits);

    public static explicit operator byte(Decimal128 value) => Core.ToInteger<byte>(value._bits);

    public static explicit operator short(Decimal128 value) => Core.ToInteger<short>(value._bits);

    public static explicit operator ushort(Decimal128 value) => Core.ToInteger<ushort>(value._bits);

    public static explicit operator char(Decimal128 value) => (char)Core.ToInteger<ushort>(value._bits);

    public static explicit operator int(Decimal128 value) => Core.ToInteger<int>(value._bits);

    public static explicit operator uint(Decimal128 value) => Core.ToInteger<uint>(value._bits);

    public static explicit operator long(Decimal128 value) => Core.ToInteger<long>(value._bits);

    public static explicit operator ulong(Decimal128 value) => Core.ToInteger<ulong>(value._bits);

    public static explicit operator Int128(Decimal128 value) => Core.ToInteger<Int128>(value._bits);

    public static explicit operator UInt128(Decimal128 value) => Core.ToInteger<UInt128>(value._bits);

    public static explicit operator Half(Decimal128 value) => (Half)Core.ToBinary(value._bits);

    public static explicit operator float(Decimal128 value) => (float)Core.ToBinary(value._bits);

    public static explicit operator double(Decimal128 value) => Core.ToBinary(value._bits);

    /// <summary>The value taken apart, for another format to read it as a decimal.</summary>
    internal static UnpackedDecimal<UInt128> Unpacked(Decimal128 value) =>
        Decimal128Format.Unpack(value._bits);

    /// <summary>The reverse: a value from another format, rounded into this one.</summary>
    internal static Decimal128 FromOtherFormat(UnpackedDecimal<UInt128> value) =>
        new(DecimalConversions<Decimal128Format, UInt128>.FromOtherFormat(value));

    private static bool TryConvertFrom<TOther>(TOther value, out Decimal128 result)
        where TOther : INumberBase<TOther>
    {
        var converted = DecimalConversions<Decimal128Format, System.UInt128>.TryFrom(value, out var bits);
        result = new Decimal128(bits);
        return converted;
    }

    private static bool TryConvertTo<TOther>(Decimal128 value, out TOther result)
        where TOther : INumberBase<TOther>?
    {
        var converted = DecimalConversions<Decimal128Format, System.UInt128>.TryTo<TOther>(value._bits, out var narrowed);
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

    private static bool TryWrite(UInt128 coefficient, Span<byte> destination, out int written,
        bool bigEndian)
    {
        written = 16;
        if (destination.Length < written)
        {
            written = 0;
            return false;
        }

        Span<byte> full = stackalloc byte[16];
        if (bigEndian)
        {
            BinaryPrimitives.WriteUInt128BigEndian(full, coefficient);
            full[(16 - written)..].CopyTo(destination);
        }
        else
        {
            BinaryPrimitives.WriteUInt128LittleEndian(full, coefficient);
            full[..written].CopyTo(destination);
        }

        return true;
    }

    private static DecimalRounding FromMidpointRounding(MidpointRounding mode) => mode switch
    {
        MidpointRounding.AwayFromZero => DecimalRounding.HalfUp,
        MidpointRounding.ToZero => DecimalRounding.Down,
        MidpointRounding.ToPositiveInfinity => DecimalRounding.Ceiling,
        MidpointRounding.ToNegativeInfinity => DecimalRounding.Floor,
        _ => DecimalRounding.HalfEven
    };

    /// <summary>
    /// Numeric equality, so 1.0 equals 1.00 and the two zeros are equal. NaN equals NaN
    /// here, unlike under <c>==</c>, so a value can be found in a collection.
    /// <see cref="CompareTotal"/> is the bit-level ordering; <see cref="ToBits"/> the
    /// bits themselves.
    /// </summary>
    public bool Equals(Decimal128 other) => Core.EqualsNumeric(_bits, other._bits);

    public override bool Equals(object? other) => other is Decimal128 value && Equals(value);

    /// <summary>Hashes the value, so members of one cohort agree.</summary>
    public override int GetHashCode() => Core.GetValueHashCode(_bits);

    /// <summary>Orders for sorting: NaNs sort below everything and equal each other.</summary>
    public int CompareTo(Decimal128 other) => Core.CompareToNumeric(_bits, other._bits);

    public int CompareTo(object? other)
    {
        if (other is null)
        {
            return 1;
        }

        if (other is not Decimal128 value)
        {
            throw new ArgumentException($"Expected a Decimal128.", nameof(other));
        }

        return CompareTo(value);
    }

    // Numeric comparison, which leaves a NaN unordered: every one of these is false when
    // either side is a NaN, including a NaN against itself.

    public static bool operator ==(Decimal128 left, Decimal128 right) =>
        Core.CompareNumeric(left._bits, right._bits) == 0;

    public static bool operator !=(Decimal128 left, Decimal128 right) => !(left == right);

    public static bool operator <(Decimal128 left, Decimal128 right) =>
        Core.CompareNumeric(left._bits, right._bits) < 0;

    public static bool operator >(Decimal128 left, Decimal128 right) =>
        Core.CompareNumeric(left._bits, right._bits) > 0;

    public static bool operator <=(Decimal128 left, Decimal128 right) =>
        Core.CompareNumeric(left._bits, right._bits) <= 0;

    public static bool operator >=(Decimal128 left, Decimal128 right) =>
        Core.CompareNumeric(left._bits, right._bits) >= 0;

    public static Decimal128 operator ++(Decimal128 value) => value + One;

    public static Decimal128 operator --(Decimal128 value) => value - One;
}
