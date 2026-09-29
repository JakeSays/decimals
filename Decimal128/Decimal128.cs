// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using Decimals.Internal;

namespace Decimals;

/// <summary>
/// An IEEE 754 decimal128 value: a 34-digit coefficient and quantum exponents from -6176 to
/// +6111. It is stored in the BID encoding and computed with two 64-bit words.
/// </summary>
/// <remarks>
/// <para>
/// This type is self-contained. Its arithmetic, text, conversions, and elementary functions
/// use nothing outside this namespace. It does not compute with 128-bit or
/// arbitrary-precision integer types. The coefficient uses two words, and intermediate
/// results that need more use four. The elementary functions run on a digit-array engine
/// ported from decNumber. <see cref="UInt128"/> appears only in the public API, as the type
/// that holds the raw bits.
/// </para>
/// <para>
/// The in-memory encoding is BID, where the coefficient is a binary integer. This makes the
/// arithmetic fast, because a mask extracts the coefficient. <see cref="ToDpdBits"/> and
/// <see cref="FromDpdBits"/> convert to and from DPD, the encoding used by decimal hardware
/// and decNumber.
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

    /// <summary>The bits in their internal form, for code in this assembly.</summary>
    internal Decimal128Integer Bits => _bits;

    internal static Decimal128 FromInternal(Decimal128Integer bits)
    {
        return new Decimal128(bits);
    }

    /// <summary>Positive zero, with exponent zero.</summary>
    public static Decimal128 Zero => new(Decimal128Encoding.Zero(false, 0));

    /// <summary>Negative zero, with exponent zero.</summary>
    public static Decimal128 NegativeZero => new(Decimal128Encoding.Zero(true, 0));

    /// <summary>1, with exponent zero.</summary>
    public static Decimal128 One => new(Decimal128Encoding.Pack(false, 0, Decimal128Integer.One));

    /// <summary>-1, with exponent zero.</summary>
    public static Decimal128 NegativeOne => new(Decimal128Encoding.Pack(true, 0, Decimal128Integer.One));

    /// <summary>The largest finite value: 9.999999999999999999999999999999999E+6144.</summary>
    public static Decimal128 MaxValue =>
        new(Decimal128Encoding.Pack(false, Decimal128Encoding.MaxQuantumExponent, Decimal128Encoding.MaxCoefficient));

    /// <summary>The smallest finite value: -9.999999999999999999999999999999999E+6144.</summary>
    public static Decimal128 MinValue =>
        new(Decimal128Encoding.Pack(true, Decimal128Encoding.MaxQuantumExponent, Decimal128Encoding.MaxCoefficient));

    /// <summary>The smallest positive value, which is subnormal: 1E-6176.</summary>
    public static Decimal128 Epsilon =>
        new(Decimal128Encoding.Pack(false, Decimal128Encoding.MinQuantumExponent, Decimal128Integer.One));

    /// <summary>Positive infinity.</summary>
    public static Decimal128 PositiveInfinity => new(Decimal128Encoding.Infinity(false));

    /// <summary>Negative infinity.</summary>
    public static Decimal128 NegativeInfinity => new(Decimal128Encoding.Infinity(true));

    /// <summary>A positive quiet NaN with no payload.</summary>
    public static Decimal128 NaN => new(Decimal128Encoding.QuietNaN());

    /// <summary>The stored encoding, which is BID.</summary>
    /// <returns>The 128-bit BID encoding of this value.</returns>
    public UInt128 ToBits() => new(_bits.High, _bits.Low);

    /// <summary>Creates a value from its stored encoding, which is BID.</summary>
    /// <param name="bits">A 128-bit BID encoding. A non-canonical encoding is kept as is.</param>
    /// <returns>The value with that encoding.</returns>
    public static Decimal128 FromBits(UInt128 bits) => new(new Decimal128Integer((ulong)(bits >> 64), (ulong)bits));

    /// <summary>
    /// The BID encoding, where the coefficient is a binary integer. This is the stored
    /// encoding, so no conversion is needed.
    /// </summary>
    /// <returns>The 128-bit BID encoding of this value.</returns>
    public UInt128 ToBidBits() => ToBits();

    /// <summary>Creates a value from its BID encoding. Same as <see cref="FromBits"/>.</summary>
    /// <param name="bits">A 128-bit BID encoding. A non-canonical encoding is kept as is.</param>
    /// <returns>The value with that encoding.</returns>
    public static Decimal128 FromBidBits(UInt128 bits) => FromBits(bits);

    /// <summary>
    /// The DPD encoding, used by decimal hardware and DPD-based libraries.
    /// </summary>
    /// <returns>The 128-bit DPD encoding of this value.</returns>
    public UInt128 ToDpdBits()
    {
        var dpd = Decimal128Dpd.ToDpd(_bits);
        return new UInt128(dpd.High, dpd.Low);
    }

    /// <summary>Creates a value from its DPD encoding.</summary>
    /// <param name="bits">A 128-bit DPD encoding.</param>
    /// <returns>The value, in the canonical BID encoding.</returns>
    public static Decimal128 FromDpdBits(UInt128 bits) =>
        new(Decimal128Dpd.FromDpd(new Decimal128Integer((ulong)(bits >> 64), (ulong)bits)));

    /// <summary>Whether the value is a quiet or signaling NaN.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns>True if the value is a NaN.</returns>
    public static bool IsNaN(Decimal128 value) => Decimal128Encoding.IsNaN(value._bits);

    /// <summary>Whether the value is a signaling NaN.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns>True if the value is a signaling NaN.</returns>
    public static bool IsSignalingNaN(Decimal128 value) => Decimal128Encoding.IsSignalingNaN(value._bits);

    /// <summary>Whether the value is positive or negative infinity.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns>True if the value is an infinity.</returns>
    public static bool IsInfinity(Decimal128 value) => Decimal128Encoding.IsInfinity(value._bits);

    /// <summary>Whether the value is finite: zero, subnormal, or normal.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns>True if the value is neither an infinity nor a NaN.</returns>
    public static bool IsFinite(Decimal128 value) => !Decimal128Encoding.IsSpecial(value._bits);

    /// <summary>Whether the sign bit is set. This is true for negative zero and for a NaN with the sign set.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns>True if the sign bit is set.</returns>
    public static bool IsNegative(Decimal128 value) => Decimal128Encoding.IsNegative(value._bits);

    /// <summary>Whether the value is positive infinity.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns>True if the value is positive infinity.</returns>
    public static bool IsPositiveInfinity(Decimal128 value) => IsInfinity(value) && !IsNegative(value);

    /// <summary>Whether the value is negative infinity.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns>True if the value is negative infinity.</returns>
    public static bool IsNegativeInfinity(Decimal128 value) => IsInfinity(value) && IsNegative(value);

    /// <summary>Whether the value is a zero of either sign, with any exponent.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns>True if the value is zero.</returns>
    public static bool IsZero(Decimal128 value) => Decimal128Encoding.IsZero(value._bits);

    /// <summary>Whether the value is non-zero and smaller in magnitude than the smallest normal value, 1E-6143.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns>True if the value is subnormal.</returns>
    public static bool IsSubnormal(Decimal128 value) => Decimal128Ordering.IsSubnormal(value._bits);

    /// <summary>Whether the value is finite, non-zero, and not subnormal.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns>True if the value is normal.</returns>
    public static bool IsNormal(Decimal128 value) => IsFinite(value) && !IsZero(value) && !IsSubnormal(value);

    /// <summary>
    /// False if the encoding has a coefficient or payload larger than the format allows,
    /// or has bits set that a special value must leave clear. Re-encoding such a value
    /// gives different bits.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns>True if the value's encoding is canonical.</returns>
    public static bool IsCanonical(Decimal128 value) => value._bits == Decimal128Encoding.Canonical(value._bits);

    /// <summary>The same value in its canonical encoding.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The value with a canonical encoding.</returns>
    public static Decimal128 Canonical(Decimal128 value) => new(Decimal128Encoding.Canonical(value._bits));

    /// <summary>The specification's class of the value, such as positive normal or negative zero.</summary>
    /// <param name="value">The value to classify.</param>
    /// <returns>The value's class.</returns>
    public static Decimal128Class Class(Decimal128 value) => Decimal128Ordering.Classify(value._bits);

    /// <summary>Copies the value with the sign cleared. Raises no conditions, even for a signaling NaN.</summary>
    /// <param name="value">The value to copy.</param>
    /// <returns>The value with a positive sign.</returns>
    public static Decimal128 CopyAbs(Decimal128 value) => new(Decimal128Ordering.Magnitude(value._bits));

    /// <summary>Copies the value with the sign flipped. Unlike arithmetic negation, it raises no conditions.</summary>
    /// <param name="value">The value to copy.</param>
    /// <returns>The value with the opposite sign.</returns>
    public static Decimal128 CopyNegate(Decimal128 value) =>
        new(new Decimal128Integer(value._bits.High ^ Decimal128Encoding.SignMask, value._bits.Low));

    /// <summary>Copies the value with the sign of another value. Raises no conditions.</summary>
    /// <param name="value">The value to copy.</param>
    /// <param name="sign">The value whose sign is used.</param>
    /// <returns><paramref name="value"/> with the sign of <paramref name="sign"/>.</returns>
    public static Decimal128 CopySign(Decimal128 value, Decimal128 sign) =>
        new(new Decimal128Integer(
            (value._bits.High & ~Decimal128Encoding.SignMask) | (sign._bits.High & Decimal128Encoding.SignMask),
            value._bits.Low));

    /// <summary>
    /// True if both values are finite with the same exponent, both are infinite, or both
    /// are NaN. Raises no conditions, even for a signaling NaN.
    /// </summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>True if the values have the same quantum.</returns>
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

    /// <summary>
    /// Compares two values in the IEEE 754 total order. It orders every encoding, including
    /// NaNs, signs, and members of the same cohort.
    /// </summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>A negative number if <paramref name="left"/> comes first, zero if the encodings are equal in the order, or a positive number if it comes second.</returns>
    public static int CompareTotal(Decimal128 left, Decimal128 right) =>
        Decimal128Ordering.CompareTotal(left._bits, right._bits);

    /// <summary>Compares the absolute values of two values in the IEEE 754 total order.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>A negative number if <paramref name="left"/> comes first, zero if they are equal in the order, or a positive number if it comes second.</returns>
    public static int CompareTotalMagnitude(Decimal128 left, Decimal128 right) =>
        Decimal128Ordering.CompareTotalMagnitude(left._bits, right._bits);

    /// <summary>
    /// The specification's to-number conversion. An invalid string gives a quiet NaN and
    /// raises <see cref="Decimal128Status.ConversionSyntax"/> instead of throwing.
    /// </summary>
    /// <param name="text">The text, in the specification's syntax.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the conversion raises.</param>
    /// <returns>The value, rounded to the format, or a quiet NaN if the text is invalid.</returns>
    public static Decimal128 FromString(ReadOnlySpan<char> text, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var bits = Decimal128Parser.Parse(text, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(bits);
    }

    /// <summary>Parses text in the specification's syntax, rounding half to even.</summary>
    /// <param name="text">The text to parse.</param>
    /// <returns>The value, rounded to the format.</returns>
    /// <exception cref="FormatException">The text is not a valid number.</exception>
    public static Decimal128 Parse(ReadOnlySpan<char> text)
    {
        if (!TryParse(text, out var value))
        {
            throw new FormatException($"'{text}' is not a Decimal128.");
        }

        return value;
    }

    /// <summary>Parses text in the specification's syntax, rounding half to even.</summary>
    /// <param name="text">The text to parse.</param>
    /// <returns>The value, rounded to the format.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    /// <exception cref="FormatException">The text is not a valid number.</exception>
    public static Decimal128 Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Parse(text.AsSpan());
    }

    /// <summary>Parses text in the specification's syntax, rounding half to even.</summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="value">Receives the value when parsing succeeds, and zero otherwise.</param>
    /// <returns>True if the text is a valid number.</returns>
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

    /// <summary>Parses text in the specification's syntax, rounding half to even.</summary>
    /// <param name="text">The text to parse. Null fails.</param>
    /// <param name="value">Receives the value when parsing succeeds, and zero otherwise.</param>
    /// <returns>True if the text is a valid number.</returns>
    public static bool TryParse(string? text, out Decimal128 value)
    {
        if (text is null)
        {
            value = Zero;
            return false;
        }

        return TryParse(text.AsSpan(), out value);
    }

    /// <summary>The specification's to-scientific-string conversion. Does not depend on the current culture.</summary>
    /// <returns>The value in scientific notation.</returns>
    public override string ToString() => Decimal128Formatter.ToScientificString(_bits);

    /// <summary>The specification's to-scientific-string conversion. Same as <see cref="ToString()"/>.</summary>
    /// <returns>The value in scientific notation.</returns>
    public string ToScientificString() => Decimal128Formatter.ToScientificString(_bits);

    /// <summary>The specification's to-engineering-string conversion, where any exponent is a multiple of three.</summary>
    /// <returns>The value in engineering notation.</returns>
    public string ToEngineeringString() => Decimal128Formatter.ToEngineeringString(_bits);

    // Arithmetic. The overloads without a context round half to even and discard the
    // conditions, which is IEEE 754 default exception handling. The overloads with a context
    // use its rounding mode and add the conditions to it. Each operation works out its
    // conditions from scratch, so one operation's conditions never affect the next.

    /// <summary>Adds two values.</summary>
    /// <param name="left">The first operand.</param>
    /// <param name="right">The second operand.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>The rounded sum.</returns>
    public static Decimal128 Add(Decimal128 left, Decimal128 right, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Arithmetic.Add(left._bits, right._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>Adds two values, rounding half to even.</summary>
    /// <param name="left">The first operand.</param>
    /// <param name="right">The second operand.</param>
    /// <returns>The rounded sum.</returns>
    public static Decimal128 Add(Decimal128 left, Decimal128 right)
    {
        var status = Decimal128Status.None;
        return new Decimal128(Decimal128Arithmetic.Add(left._bits, right._bits, Decimal128Rounding.HalfEven, ref status));
    }

    /// <summary>Subtracts the second value from the first.</summary>
    /// <param name="left">The value to subtract from.</param>
    /// <param name="right">The value to subtract.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>The rounded difference.</returns>
    public static Decimal128 Subtract(Decimal128 left, Decimal128 right, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Arithmetic.Subtract(left._bits, right._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>Subtracts the second value from the first, rounding half to even.</summary>
    /// <param name="left">The value to subtract from.</param>
    /// <param name="right">The value to subtract.</param>
    /// <returns>The rounded difference.</returns>
    public static Decimal128 Subtract(Decimal128 left, Decimal128 right)
    {
        var status = Decimal128Status.None;
        return new Decimal128(Decimal128Arithmetic.Subtract(left._bits, right._bits, Decimal128Rounding.HalfEven,
            ref status));
    }

    /// <summary>Multiplies two values.</summary>
    /// <param name="left">The first factor.</param>
    /// <param name="right">The second factor.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>The rounded product.</returns>
    public static Decimal128 Multiply(Decimal128 left, Decimal128 right, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Arithmetic.Multiply(left._bits, right._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>Multiplies two values, rounding half to even.</summary>
    /// <param name="left">The first factor.</param>
    /// <param name="right">The second factor.</param>
    /// <returns>The rounded product.</returns>
    public static Decimal128 Multiply(Decimal128 left, Decimal128 right)
    {
        var status = Decimal128Status.None;
        return new Decimal128(Decimal128Arithmetic.Multiply(left._bits, right._bits, Decimal128Rounding.HalfEven,
            ref status));
    }

    /// <summary>Divides the first value by the second.</summary>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>The rounded quotient.</returns>
    public static Decimal128 Divide(Decimal128 left, Decimal128 right, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Arithmetic.Divide(left._bits, right._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>Divides the first value by the second, rounding half to even.</summary>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>The rounded quotient.</returns>
    public static Decimal128 Divide(Decimal128 left, Decimal128 right)
    {
        var status = Decimal128Status.None;
        return new Decimal128(Decimal128Arithmetic.Divide(left._bits, right._bits, Decimal128Rounding.HalfEven,
            ref status));
    }

    /// <summary>The integer part of the quotient, with exponent zero.</summary>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>The quotient truncated toward zero, or NaN if it does not fit in the precision.</returns>
    public static Decimal128 DivideInteger(Decimal128 left, Decimal128 right, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Arithmetic.DivideInteger(left._bits, right._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>The integer part of the quotient, with exponent zero.</summary>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>The quotient truncated toward zero, or NaN if it does not fit in the precision.</returns>
    public static Decimal128 DivideInteger(Decimal128 left, Decimal128 right)
    {
        var status = Decimal128Status.None;
        return new Decimal128(Decimal128Arithmetic.DivideInteger(left._bits, right._bits, Decimal128Rounding.HalfEven,
            ref status));
    }

    /// <summary>The remainder after <see cref="DivideInteger(Decimal128, Decimal128, ref Decimal128Context)"/>. It has the dividend's sign.</summary>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>The exact remainder, or NaN if the integer quotient does not fit in the precision.</returns>
    public static Decimal128 Remainder(Decimal128 left, Decimal128 right, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Arithmetic.Remainder(left._bits, right._bits, false, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>The remainder after <see cref="DivideInteger(Decimal128, Decimal128)"/>. It has the dividend's sign.</summary>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>The exact remainder, or NaN if the integer quotient does not fit in the precision.</returns>
    public static Decimal128 Remainder(Decimal128 left, Decimal128 right)
    {
        var status = Decimal128Status.None;
        return new Decimal128(Decimal128Arithmetic.Remainder(left._bits, right._bits, false,
            Decimal128Rounding.HalfEven, ref status));
    }

    /// <summary>The IEEE 754 remainder, which rounds the quotient to the nearest integer.</summary>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>The exact remainder, or NaN if the nearest integer quotient does not fit in the precision.</returns>
    public static Decimal128 RemainderNear(Decimal128 left, Decimal128 right, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Arithmetic.Remainder(left._bits, right._bits, true, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>The IEEE 754 remainder, which rounds the quotient to the nearest integer.</summary>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>The exact remainder, or NaN if the nearest integer quotient does not fit in the precision.</returns>
    public static Decimal128 RemainderNear(Decimal128 left, Decimal128 right)
    {
        var status = Decimal128Status.None;
        return new Decimal128(Decimal128Arithmetic.Remainder(left._bits, right._bits, true,
            Decimal128Rounding.HalfEven, ref status));
    }

    /// <summary>Multiplies and adds with a single rounding.</summary>
    /// <param name="left">The first factor.</param>
    /// <param name="right">The second factor.</param>
    /// <param name="addend">The value added to the exact product.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns><paramref name="left"/> * <paramref name="right"/> + <paramref name="addend"/>, rounded once.</returns>
    public static Decimal128 FusedMultiplyAdd(Decimal128 left, Decimal128 right, Decimal128 addend,
        ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Arithmetic.FusedMultiplyAdd(left._bits, right._bits, addend._bits, context.Rounding,
            ref status);

        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>Multiplies and adds with a single rounding, half to even.</summary>
    /// <param name="left">The first factor.</param>
    /// <param name="right">The second factor.</param>
    /// <param name="addend">The value added to the exact product.</param>
    /// <returns><paramref name="left"/> * <paramref name="right"/> + <paramref name="addend"/>, rounded once.</returns>
    public static Decimal128 FusedMultiplyAdd(Decimal128 left, Decimal128 right, Decimal128 addend)
    {
        var status = Decimal128Status.None;
        return new Decimal128(Decimal128Arithmetic.FusedMultiplyAdd(left._bits, right._bits, addend._bits,
            Decimal128Rounding.HalfEven, ref status));
    }

    /// <summary>Adds the value to zero, which applies the format's rounding.</summary>
    /// <param name="value">The operand.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>The value, rounded to the format. Negative zero becomes positive zero except when rounding toward negative infinity.</returns>
    public static Decimal128 Plus(Decimal128 value, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Arithmetic.AddToZero(value._bits, false, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>Adds the value to zero, rounding half to even.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The value, rounded to the format. Negative zero becomes positive zero.</returns>
    public static Decimal128 Plus(Decimal128 value)
    {
        var status = Decimal128Status.None;
        return new Decimal128(Decimal128Arithmetic.AddToZero(value._bits, false, Decimal128Rounding.HalfEven, ref status));
    }

    /// <summary>Subtracts the value from zero. Unlike <see cref="CopyNegate"/>, this is arithmetic and can raise conditions.</summary>
    /// <param name="value">The operand.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>The negated value, rounded to the format.</returns>
    public static Decimal128 Minus(Decimal128 value, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Arithmetic.AddToZero(value._bits, true, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>Subtracts the value from zero, rounding half to even.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The negated value, rounded to the format.</returns>
    public static Decimal128 Minus(Decimal128 value)
    {
        var status = Decimal128Status.None;
        return new Decimal128(Decimal128Arithmetic.AddToZero(value._bits, true, Decimal128Rounding.HalfEven, ref status));
    }

    /// <summary>The absolute value. Unlike <see cref="CopyAbs"/>, this is arithmetic and can raise conditions.</summary>
    /// <param name="value">The operand.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>The absolute value, rounded to the format.</returns>
    public static Decimal128 Abs(Decimal128 value, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var negate = Decimal128Encoding.IsNegative(value._bits) && !Decimal128Encoding.IsNaN(value._bits);
        var result = Decimal128Arithmetic.AddToZero(value._bits, negate, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>
    /// Compares numerically and returns -1, 0, 1, or NaN. Unlike <see cref="CompareTotal"/>,
    /// the two zeros are equal and a NaN operand gives NaN.
    /// </summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <param name="context">Receives InvalidOperation if an operand is a signaling NaN.</param>
    /// <returns>-1 if <paramref name="left"/> is smaller, 0 if they are equal, 1 if it is larger, or NaN if either is a NaN.</returns>
    public static Decimal128 Compare(Decimal128 left, Decimal128 right, ref Decimal128Context context) =>
        CompareToValue(left, right, false, ref context);

    /// <summary>Like <see cref="Compare"/>, but any NaN operand raises InvalidOperation, not only a signaling NaN.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <param name="context">Receives InvalidOperation if either operand is a NaN.</param>
    /// <returns>-1 if <paramref name="left"/> is smaller, 0 if they are equal, 1 if it is larger, or NaN if either is a NaN.</returns>
    public static Decimal128 CompareSignal(Decimal128 left, Decimal128 right, ref Decimal128Context context) =>
        CompareToValue(left, right, true, ref context);

    // Digit-wise logical operations. Each operand must be positive, have exponent zero, and
    // have only the digits 0 and 1. Any other operand is an invalid operation.

    /// <summary>The digit-wise AND of two logical operands.</summary>
    /// <param name="left">The first logical operand.</param>
    /// <param name="right">The second logical operand.</param>
    /// <param name="context">Receives InvalidOperation if an operand is not a logical operand.</param>
    /// <returns>The digit-wise AND, or NaN if an operand is invalid.</returns>
    public static Decimal128 And(Decimal128 left, Decimal128 right, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Logical.And(left._bits, right._bits, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>The digit-wise OR of two logical operands.</summary>
    /// <param name="left">The first logical operand.</param>
    /// <param name="right">The second logical operand.</param>
    /// <param name="context">Receives InvalidOperation if an operand is not a logical operand.</param>
    /// <returns>The digit-wise OR, or NaN if an operand is invalid.</returns>
    public static Decimal128 Or(Decimal128 left, Decimal128 right, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Logical.Or(left._bits, right._bits, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>The digit-wise exclusive OR of two logical operands.</summary>
    /// <param name="left">The first logical operand.</param>
    /// <param name="right">The second logical operand.</param>
    /// <param name="context">Receives InvalidOperation if an operand is not a logical operand.</param>
    /// <returns>The digit-wise exclusive OR, or NaN if an operand is invalid.</returns>
    public static Decimal128 Xor(Decimal128 left, Decimal128 right, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Logical.Xor(left._bits, right._bits, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>Inverts every digit of a logical operand, across the format's full precision.</summary>
    /// <param name="value">The logical operand.</param>
    /// <param name="context">Receives InvalidOperation if the operand is not a logical operand.</param>
    /// <returns>The digit-wise inversion, or NaN if the operand is invalid.</returns>
    public static Decimal128 Invert(Decimal128 value, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Logical.Invert(value._bits, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>The larger of the two. If one operand is a quiet NaN and the other is a number, returns the number.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>The larger value, rounded to the format.</returns>
    public static Decimal128 Max(Decimal128 left, Decimal128 right, ref Decimal128Context context) =>
        Select(left, right, true, false, ref context);

    /// <summary>The smaller of the two. If one operand is a quiet NaN and the other is a number, returns the number.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>The smaller value, rounded to the format.</returns>
    public static Decimal128 Min(Decimal128 left, Decimal128 right, ref Decimal128Context context) =>
        Select(left, right, false, false, ref context);

    /// <summary>The value with the larger magnitude. If one operand is a quiet NaN and the other is a number, returns the number.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>The value with the larger magnitude, rounded to the format.</returns>
    public static Decimal128 MaxMagnitude(Decimal128 left, Decimal128 right, ref Decimal128Context context) =>
        Select(left, right, true, true, ref context);

    /// <summary>The value with the smaller magnitude. If one operand is a quiet NaN and the other is a number, returns the number.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>The value with the smaller magnitude, rounded to the format.</returns>
    public static Decimal128 MinMagnitude(Decimal128 left, Decimal128 right, ref Decimal128Context context) =>
        Select(left, right, false, true, ref context);

    /// <summary>The adjusted exponent, as a decimal integer.</summary>
    /// <param name="value">The operand.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>The exponent of the leading digit. Zero gives -Infinity and raises DivisionByZero, and an infinity gives +Infinity.</returns>
    public static Decimal128 LogB(Decimal128 value, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Shaping.LogB(value._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>Multiplies the value by 10 to the power of the second operand.</summary>
    /// <param name="value">The value to scale.</param>
    /// <param name="scale">The power of ten. It must be an integer with exponent zero.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>The scaled value, or NaN if <paramref name="scale"/> is not a valid integer.</returns>
    public static Decimal128 ScaleB(Decimal128 value, Decimal128 scale, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Shaping.ScaleB(value._bits, scale._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>Removes trailing zeros, which gives the shortest coefficient for the same value.</summary>
    /// <param name="value">The operand.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>The value with no trailing zeros in its coefficient. Every zero becomes a zero with exponent zero.</returns>
    public static Decimal128 Reduce(Decimal128 value, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Shaping.Reduce(value._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>Like <see cref="Reduce"/>, but stops at exponent zero.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The value with trailing zeros removed from its fractional part.</returns>
    public static Decimal128 Trim(Decimal128 value) => new(Decimal128Shaping.Trim(value._bits));

    /// <summary>Rounds to an integer without raising Inexact or Rounded.</summary>
    /// <param name="value">The operand.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>The value rounded to an integer.</returns>
    public static Decimal128 RoundToIntegral(Decimal128 value, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Shaping.ToIntegral(value._bits, false, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>Rounds to an integer and raises Inexact if the value changed.</summary>
    /// <param name="value">The operand.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>The value rounded to an integer.</returns>
    public static Decimal128 RoundToIntegralExact(Decimal128 value, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Shaping.ToIntegral(value._bits, true, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>Rounds or pads the value to the second operand's exponent.</summary>
    /// <param name="value">The value to rescale.</param>
    /// <param name="pattern">The value whose exponent is used. Only its exponent matters.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>The value with the exponent of <paramref name="pattern"/>, or NaN if the result needs more digits than the format holds.</returns>
    public static Decimal128 Quantize(Decimal128 value, Decimal128 pattern, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Shaping.Quantize(value._bits, pattern._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>Rotates the coefficient's digits within the format's full precision.</summary>
    /// <param name="value">The value whose coefficient is rotated.</param>
    /// <param name="places">The number of places. Positive rotates toward the most significant digit. It must be an integer with exponent zero.</param>
    /// <param name="context">Receives InvalidOperation if <paramref name="places"/> is not valid.</param>
    /// <returns>The value with its coefficient rotated, or NaN if <paramref name="places"/> is not valid.</returns>
    public static Decimal128 Rotate(Decimal128 value, Decimal128 places, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Shaping.RotateOrShift(value._bits, places._bits, true, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>Shifts the coefficient's digits. Digits shifted past either end are lost.</summary>
    /// <param name="value">The value whose coefficient is shifted.</param>
    /// <param name="places">The number of places. Positive shifts toward the most significant digit. It must be an integer with exponent zero.</param>
    /// <param name="context">Receives InvalidOperation if <paramref name="places"/> is not valid.</param>
    /// <returns>The value with its coefficient shifted, or NaN if <paramref name="places"/> is not valid.</returns>
    public static Decimal128 Shift(Decimal128 value, Decimal128 places, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Shaping.RotateOrShift(value._bits, places._bits, false, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>The square root, correctly rounded.</summary>
    /// <param name="value">The operand.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>The rounded square root, or NaN if the operand is negative and not zero.</returns>
    public static Decimal128 Sqrt(Decimal128 value, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128SquareRoot.SquareRoot(value._bits, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>The square root, correctly rounded half to even.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The rounded square root, or NaN if the operand is negative and not zero.</returns>
    public static Decimal128 Sqrt(Decimal128 value)
    {
        var status = Decimal128Status.None;
        return new Decimal128(Decimal128SquareRoot.SquareRoot(value._bits, Decimal128Rounding.HalfEven, ref status));
    }

    // The elementary functions, each with and without a context.

    /// <summary>e to the power of the value.</summary>
    /// <param name="value">The exponent.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>e^<paramref name="value"/>, rounded to the format.</returns>
    public static Decimal128 Exp(Decimal128 value, ref Decimal128Context context) =>
        Apply(Decimal128Math.Exp, value, ref context);

    /// <summary>e to the power of the value, rounding half to even.</summary>
    /// <param name="value">The exponent.</param>
    /// <returns>e^<paramref name="value"/>, rounded to the format.</returns>
    public static Decimal128 Exp(Decimal128 value) => Apply(Decimal128Math.Exp, value);

    /// <summary>The natural logarithm.</summary>
    /// <param name="value">The operand.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>ln(<paramref name="value"/>), rounded to the format. Zero gives -Infinity, and a negative value gives NaN.</returns>
    public static Decimal128 Log(Decimal128 value, ref Decimal128Context context) =>
        Apply(Decimal128Math.Log, value, ref context);

    /// <summary>The natural logarithm, rounding half to even.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>ln(<paramref name="value"/>), rounded to the format. Zero gives -Infinity, and a negative value gives NaN.</returns>
    public static Decimal128 Log(Decimal128 value) => Apply(Decimal128Math.Log, value);

    /// <summary>The base-10 logarithm.</summary>
    /// <param name="value">The operand.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>log10(<paramref name="value"/>), rounded to the format. A power of ten gives its exponent exactly.</returns>
    public static Decimal128 Log10(Decimal128 value, ref Decimal128Context context) =>
        Apply(Decimal128Math.Log10, value, ref context);

    /// <summary>The base-10 logarithm, rounding half to even.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>log10(<paramref name="value"/>), rounded to the format. A power of ten gives its exponent exactly.</returns>
    public static Decimal128 Log10(Decimal128 value) => Apply(Decimal128Math.Log10, value);

    /// <summary>The first value to the power of the second.</summary>
    /// <param name="value">The base.</param>
    /// <param name="power">The exponent.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns><paramref name="value"/>^<paramref name="power"/>, rounded to the format.</returns>
    public static Decimal128 Pow(Decimal128 value, Decimal128 power, ref Decimal128Context context) =>
        Apply(Decimal128Math.Power, value, power, ref context);

    /// <summary>The first value to the power of the second, rounding half to even.</summary>
    /// <param name="value">The base.</param>
    /// <param name="power">The exponent.</param>
    /// <returns><paramref name="value"/>^<paramref name="power"/>, rounded to the format.</returns>
    public static Decimal128 Pow(Decimal128 value, Decimal128 power) => Apply(Decimal128Math.Power, value, power);

    /// <summary>2 to the power of the value.</summary>
    /// <param name="value">The exponent.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>2^<paramref name="value"/>, rounded to the format.</returns>
    public static Decimal128 Exp2(Decimal128 value, ref Decimal128Context context) =>
        Apply(Decimal128Math.Exp2, value, ref context);

    /// <summary>2 to the power of the value, rounding half to even.</summary>
    /// <param name="value">The exponent.</param>
    /// <returns>2^<paramref name="value"/>, rounded to the format.</returns>
    public static Decimal128 Exp2(Decimal128 value) => Apply(Decimal128Math.Exp2, value);

    /// <summary>10 to the power of the value.</summary>
    /// <param name="value">The exponent.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>10^<paramref name="value"/>, rounded to the format.</returns>
    public static Decimal128 Exp10(Decimal128 value, ref Decimal128Context context) =>
        Apply(Decimal128Math.Exp10, value, ref context);

    /// <summary>10 to the power of the value, rounding half to even.</summary>
    /// <param name="value">The exponent.</param>
    /// <returns>10^<paramref name="value"/>, rounded to the format.</returns>
    public static Decimal128 Exp10(Decimal128 value) => Apply(Decimal128Math.Exp10, value);

    // The M1 and P1 names come from the generic math interfaces, so they are kept as is.

    /// <summary>e to the power of the value, minus 1.</summary>
    /// <param name="value">The exponent.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>e^<paramref name="value"/> - 1, rounded to the format.</returns>
    public static Decimal128 ExpM1(Decimal128 value, ref Decimal128Context context) =>
        Apply(Decimal128Math.ExpMinusOne, value, ref context);

    /// <summary>e to the power of the value, minus 1, rounding half to even.</summary>
    /// <param name="value">The exponent.</param>
    /// <returns>e^<paramref name="value"/> - 1, rounded to the format.</returns>
    public static Decimal128 ExpM1(Decimal128 value) => Apply(Decimal128Math.ExpMinusOne, value);

    /// <summary>2 to the power of the value, minus 1.</summary>
    /// <param name="value">The exponent.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>2^<paramref name="value"/> - 1, rounded to the format.</returns>
    public static Decimal128 Exp2M1(Decimal128 value, ref Decimal128Context context) =>
        Apply(Decimal128Math.Exp2MinusOne, value, ref context);

    /// <summary>2 to the power of the value, minus 1, rounding half to even.</summary>
    /// <param name="value">The exponent.</param>
    /// <returns>2^<paramref name="value"/> - 1, rounded to the format.</returns>
    public static Decimal128 Exp2M1(Decimal128 value) => Apply(Decimal128Math.Exp2MinusOne, value);

    /// <summary>10 to the power of the value, minus 1.</summary>
    /// <param name="value">The exponent.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>10^<paramref name="value"/> - 1, rounded to the format.</returns>
    public static Decimal128 Exp10M1(Decimal128 value, ref Decimal128Context context) =>
        Apply(Decimal128Math.Exp10MinusOne, value, ref context);

    /// <summary>10 to the power of the value, minus 1, rounding half to even.</summary>
    /// <param name="value">The exponent.</param>
    /// <returns>10^<paramref name="value"/> - 1, rounded to the format.</returns>
    public static Decimal128 Exp10M1(Decimal128 value) => Apply(Decimal128Math.Exp10MinusOne, value);

    /// <summary>The logarithm in the given base.</summary>
    /// <param name="value">The operand.</param>
    /// <param name="newBase">The base of the logarithm.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>The logarithm of <paramref name="value"/> in base <paramref name="newBase"/>, rounded to the format.</returns>
    public static Decimal128 Log(Decimal128 value, Decimal128 newBase, ref Decimal128Context context) =>
        Apply(Decimal128Math.LogInBase, value, newBase, ref context);

    /// <summary>The logarithm in the given base, rounding half to even.</summary>
    /// <param name="value">The operand.</param>
    /// <param name="newBase">The base of the logarithm.</param>
    /// <returns>The logarithm of <paramref name="value"/> in base <paramref name="newBase"/>, rounded to the format.</returns>
    public static Decimal128 Log(Decimal128 value, Decimal128 newBase) => Apply(Decimal128Math.LogInBase, value, newBase);

    /// <summary>The base-2 logarithm.</summary>
    /// <param name="value">The operand.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>log2(<paramref name="value"/>), rounded to the format. A power of two gives its exponent exactly.</returns>
    public static Decimal128 Log2(Decimal128 value, ref Decimal128Context context) =>
        Apply(Decimal128Math.Log2, value, ref context);

    /// <summary>The base-2 logarithm, rounding half to even.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>log2(<paramref name="value"/>), rounded to the format. A power of two gives its exponent exactly.</returns>
    public static Decimal128 Log2(Decimal128 value) => Apply(Decimal128Math.Log2, value);

    /// <summary>The natural logarithm of 1 plus the value.</summary>
    /// <param name="value">The operand.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>ln(1 + <paramref name="value"/>), rounded to the format.</returns>
    public static Decimal128 LogP1(Decimal128 value, ref Decimal128Context context) =>
        Apply(Decimal128Math.LogPlusOne, value, ref context);

    /// <summary>The natural logarithm of 1 plus the value, rounding half to even.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>ln(1 + <paramref name="value"/>), rounded to the format.</returns>
    public static Decimal128 LogP1(Decimal128 value) => Apply(Decimal128Math.LogPlusOne, value);

    /// <summary>The base-2 logarithm of 1 plus the value.</summary>
    /// <param name="value">The operand.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>log2(1 + <paramref name="value"/>), rounded to the format.</returns>
    public static Decimal128 Log2P1(Decimal128 value, ref Decimal128Context context) =>
        Apply(Decimal128Math.Log2PlusOne, value, ref context);

    /// <summary>The base-2 logarithm of 1 plus the value, rounding half to even.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>log2(1 + <paramref name="value"/>), rounded to the format.</returns>
    public static Decimal128 Log2P1(Decimal128 value) => Apply(Decimal128Math.Log2PlusOne, value);

    /// <summary>The base-10 logarithm of 1 plus the value.</summary>
    /// <param name="value">The operand.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>log10(1 + <paramref name="value"/>), rounded to the format.</returns>
    public static Decimal128 Log10P1(Decimal128 value, ref Decimal128Context context) =>
        Apply(Decimal128Math.Log10PlusOne, value, ref context);

    /// <summary>The base-10 logarithm of 1 plus the value, rounding half to even.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>log10(1 + <paramref name="value"/>), rounded to the format.</returns>
    public static Decimal128 Log10P1(Decimal128 value) => Apply(Decimal128Math.Log10PlusOne, value);

    /// <summary>The cube root.</summary>
    /// <param name="value">The operand. It can be negative.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>The cube root, rounded to the format.</returns>
    public static Decimal128 Cbrt(Decimal128 value, ref Decimal128Context context) =>
        Apply(Decimal128Math.Cbrt, value, ref context);

    /// <summary>The cube root, rounding half to even.</summary>
    /// <param name="value">The operand. It can be negative.</param>
    /// <returns>The cube root, rounded to the format.</returns>
    public static Decimal128 Cbrt(Decimal128 value) => Apply(Decimal128Math.Cbrt, value);

    /// <summary>The root of the given degree.</summary>
    /// <param name="value">The operand.</param>
    /// <param name="degree">The degree of the root. A negative degree gives the reciprocal of the root.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>The root, rounded to the format, or NaN for an even root of a negative value.</returns>
    public static Decimal128 RootN(Decimal128 value, int degree, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Math.RootN(value._bits, degree, context.Rounding, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>The root of the given degree, rounding half to even.</summary>
    /// <param name="value">The operand.</param>
    /// <param name="degree">The degree of the root. A negative degree gives the reciprocal of the root.</param>
    /// <returns>The root, rounded to the format, or NaN for an even root of a negative value.</returns>
    public static Decimal128 RootN(Decimal128 value, int degree)
    {
        var status = Decimal128Status.None;
        return new Decimal128(Decimal128Math.RootN(value._bits, degree, Decimal128Rounding.HalfEven, ref status));
    }

    /// <summary>The square root of the sum of the two squares, without intermediate overflow.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <param name="context">Supplies the rounding mode and receives the conditions the operation raises.</param>
    /// <returns>sqrt(<paramref name="left"/>^2 + <paramref name="right"/>^2), rounded to the format.</returns>
    public static Decimal128 Hypot(Decimal128 left, Decimal128 right, ref Decimal128Context context) =>
        Apply(Decimal128Math.Hypot, left, right, ref context);

    /// <summary>The square root of the sum of the two squares, rounding half to even.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>sqrt(<paramref name="left"/>^2 + <paramref name="right"/>^2), rounded to the format.</returns>
    public static Decimal128 Hypot(Decimal128 left, Decimal128 right) => Apply(Decimal128Math.Hypot, left, right);

    /// <summary>The next value toward positive infinity.</summary>
    /// <param name="value">The starting value.</param>
    /// <param name="context">Receives InvalidOperation if the value is a signaling NaN.</param>
    /// <returns>The smallest value greater than <paramref name="value"/>.</returns>
    public static Decimal128 NextPlus(Decimal128 value, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Shaping.Next(value._bits, true, true, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>The next value toward negative infinity.</summary>
    /// <param name="value">The starting value.</param>
    /// <param name="context">Receives InvalidOperation if the value is a signaling NaN.</param>
    /// <returns>The largest value less than <paramref name="value"/>.</returns>
    public static Decimal128 NextMinus(Decimal128 value, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Shaping.Next(value._bits, false, true, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    /// <summary>The next value after the first operand, in the direction of the second.</summary>
    /// <param name="value">The starting value.</param>
    /// <param name="target">The value that sets the direction.</param>
    /// <param name="context">Receives the conditions the operation raises, including Underflow for a subnormal result.</param>
    /// <returns>The next value toward <paramref name="target"/>, or <paramref name="value"/> with the sign of <paramref name="target"/> if they are equal.</returns>
    public static Decimal128 NextToward(Decimal128 value, Decimal128 target, ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Shaping.NextToward(value._bits, target._bits, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

    // Four operations under their .NET names. Each is the same as an operation above. They
    // let code written for the built-in floating-point types work with this type.

    /// <summary>The IEEE 754 remainder. Same as <see cref="RemainderNear(Decimal128, Decimal128)"/>.</summary>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>The exact remainder, or NaN if the nearest integer quotient does not fit in the precision.</returns>
    public static Decimal128 Ieee754Remainder(Decimal128 left, Decimal128 right) => RemainderNear(left, right);

    /// <summary>
    /// The adjusted exponent as an <see cref="int"/>. This is <see cref="LogB"/> as an
    /// integer, except for special values. Zero gives <see cref="int.MinValue"/>, and NaN
    /// or infinity gives <see cref="int.MaxValue"/>, as in .NET. The specification's logb
    /// gives -Infinity, NaN, and +Infinity instead.
    /// </summary>
    /// <param name="value">The operand.</param>
    /// <returns>The exponent of the leading digit.</returns>
    public static int ILogB(Decimal128 value) => Decimal128Shaping.ILogB(value._bits);

    /// <summary>
    /// Multiplies the value by 10 to the power of <paramref name="scale"/>. This is the
    /// specification's scaleb, which uses the format's radix of 10. Unlike
    /// <see cref="double.ScaleB"/>, it does not use powers of 2.
    /// </summary>
    /// <param name="value">The value to scale.</param>
    /// <param name="scale">The power of ten.</param>
    /// <returns>The scaled value, rounded half to even, or NaN if <paramref name="scale"/> is out of range.</returns>
    public static Decimal128 ScaleB(Decimal128 value, int scale)
    {
        var status = Decimal128Status.None;
        return new Decimal128(Decimal128Shaping.ScaleB(value._bits, scale, Decimal128Rounding.HalfEven, ref status));
    }

    /// <summary>The next value toward positive infinity. Same as <see cref="NextPlus"/>.</summary>
    /// <param name="value">The starting value.</param>
    /// <returns>The smallest value greater than <paramref name="value"/>.</returns>
    public static Decimal128 BitIncrement(Decimal128 value)
    {
        var status = Decimal128Status.None;
        return new Decimal128(Decimal128Shaping.Next(value._bits, true, true, ref status));
    }

    /// <summary>The next value toward negative infinity. Same as <see cref="NextMinus"/>.</summary>
    /// <param name="value">The starting value.</param>
    /// <returns>The largest value less than <paramref name="value"/>.</returns>
    public static Decimal128 BitDecrement(Decimal128 value)
    {
        var status = Decimal128Status.None;
        return new Decimal128(Decimal128Shaping.Next(value._bits, false, true, ref status));
    }

    /// <summary>Adds two values, rounding half to even.</summary>
    /// <param name="left">The first operand.</param>
    /// <param name="right">The second operand.</param>
    /// <returns>The rounded sum.</returns>
    public static Decimal128 operator +(Decimal128 left, Decimal128 right) => Add(left, right);

    /// <summary>Subtracts the second value from the first, rounding half to even.</summary>
    /// <param name="left">The value to subtract from.</param>
    /// <param name="right">The value to subtract.</param>
    /// <returns>The rounded difference.</returns>
    public static Decimal128 operator -(Decimal128 left, Decimal128 right) => Subtract(left, right);

    /// <summary>Multiplies two values, rounding half to even.</summary>
    /// <param name="left">The first factor.</param>
    /// <param name="right">The second factor.</param>
    /// <returns>The rounded product.</returns>
    public static Decimal128 operator *(Decimal128 left, Decimal128 right) => Multiply(left, right);

    /// <summary>Divides the first value by the second, rounding half to even.</summary>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>The rounded quotient.</returns>
    public static Decimal128 operator /(Decimal128 left, Decimal128 right) => Divide(left, right);

    /// <summary>The remainder after integer division. Same as <see cref="Remainder(Decimal128, Decimal128)"/>.</summary>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>The exact remainder, with the dividend's sign.</returns>
    public static Decimal128 operator %(Decimal128 left, Decimal128 right) => Remainder(left, right);

    /// <summary>Negates the value arithmetically. Same as <see cref="Minus(Decimal128)"/>.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The negated value, rounded half to even.</returns>
    public static Decimal128 operator -(Decimal128 value) => Minus(value);

    /// <summary>Applies the format's rounding. Same as <see cref="Plus(Decimal128)"/>.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The value, rounded half to even.</returns>
    public static Decimal128 operator +(Decimal128 value) => Plus(value);

    // Generic math. These members compare numerically, not by the total order: 1.0 equals
    // 1.00, the two zeros are equal, and NaN is unordered.

    /// <summary>Positive zero, with exponent zero.</summary>
    static Decimal128 INumberBase<Decimal128>.Zero => Zero;

    /// <summary>1, with exponent zero.</summary>
    static Decimal128 INumberBase<Decimal128>.One => One;

    /// <summary>-1, with exponent zero.</summary>
    static Decimal128 ISignedNumber<Decimal128>.NegativeOne => NegativeOne;

    /// <summary>10, because this is a decimal format.</summary>
    public static int Radix => 10;

    /// <summary>Converts from another numeric type. Throws if the value does not fit.</summary>
    /// <typeparam name="TOther">The type to convert from.</typeparam>
    /// <param name="value">The value to convert.</param>
    /// <returns>The value, rounded to the format.</returns>
    /// <exception cref="NotSupportedException"><typeparamref name="TOther"/> cannot be converted to this type.</exception>
    public static Decimal128 CreateChecked<TOther>(TOther value)
        where TOther : INumberBase<TOther>
    {
        if (ConvertFrom(value) is { } result)
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
    /// <typeparam name="TOther">The type to convert from.</typeparam>
    /// <param name="value">The value to convert.</param>
    /// <returns>The value, rounded to the format.</returns>
    /// <exception cref="NotSupportedException"><typeparamref name="TOther"/> cannot be converted to this type.</exception>
    public static Decimal128 CreateSaturating<TOther>(TOther value)
        where TOther : INumberBase<TOther>
    {
        if (ConvertFrom(value) is { } result)
        {
            return result;
        }

        if (TOther.TryConvertToSaturating<Decimal128>(value, out var other))
        {
            return other;
        }

        throw new NotSupportedException($"Cannot convert {typeof(TOther)} to Decimal128.");
    }

    /// <summary>Converts from another numeric type, keeping the low-order part if it does not fit.</summary>
    /// <typeparam name="TOther">The type to convert from.</typeparam>
    /// <param name="value">The value to convert.</param>
    /// <returns>The value, rounded to the format.</returns>
    /// <exception cref="NotSupportedException"><typeparamref name="TOther"/> cannot be converted to this type.</exception>
    public static Decimal128 CreateTruncating<TOther>(TOther value)
        where TOther : INumberBase<TOther>
    {
        if (ConvertFrom(value) is { } result)
        {
            return result;
        }

        if (TOther.TryConvertToTruncating<Decimal128>(value, out var other))
        {
            return other;
        }

        throw new NotSupportedException($"Cannot convert {typeof(TOther)} to Decimal128.");
    }

    /// <summary>Positive zero, the additive identity.</summary>
    static Decimal128 IAdditiveIdentity<Decimal128, Decimal128>.AdditiveIdentity => Zero;

    /// <summary>1, the multiplicative identity.</summary>
    static Decimal128 IMultiplicativeIdentity<Decimal128, Decimal128>.MultiplicativeIdentity => One;

    /// <summary>The smallest finite value.</summary>
    static Decimal128 IMinMaxValue<Decimal128>.MinValue => MinValue;

    /// <summary>The largest finite value.</summary>
    static Decimal128 IMinMaxValue<Decimal128>.MaxValue => MaxValue;

    // The constants are built from their encodings instead of parsed from text: a 34-digit
    // coefficient with exponent -33.

    /// <summary>The base of natural logarithms: 2.718281828459045235360287471352662.</summary>
    public static Decimal128 E =>
        new(Decimal128Encoding.Pack(false, -33, new Decimal128Integer(0x000086058A4BF4DE, 0x4E906ACCB26ABB56)));

    /// <summary>The ratio of a circle's circumference to its diameter: 3.141592653589793238462643383279503.</summary>
    public static Decimal128 Pi =>
        new(Decimal128Encoding.Pack(false, -33, new Decimal128Integer(0x00009AE4795796A7, 0xBABE5564E6F39F8F)));

    /// <summary>2 * Pi: 6.283185307179586476925286766559006.</summary>
    public static Decimal128 Tau =>
        new(Decimal128Encoding.Pack(false, -33, new Decimal128Integer(0x000135C8F2AF2D4F, 0x757CAAC9CDE73F1E)));

    /// <summary>The absolute value. Same as <see cref="CopyAbs"/>: it raises no conditions and does not round.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The value with a positive sign.</returns>
    public static Decimal128 Abs(Decimal128 value) => CopyAbs(value);

    /// <summary>Whether the value is a finite integer.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns>True if the value is finite and has no non-zero fractional digits.</returns>
    public static bool IsInteger(Decimal128 value)
    {
        if (Decimal128Encoding.IsSpecial(value._bits))
        {
            return false;
        }

        var coefficient = Decimal128Encoding.Unpack(value._bits, out var exponent);
        return exponent >= 0 || FractionIsZero(coefficient, exponent);
    }

    /// <summary>Whether the value is an even integer.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns>True if the value is a finite integer divisible by 2.</returns>
    public static bool IsEvenInteger(Decimal128 value) => HasParity(value, 0);

    /// <summary>Whether the value is an odd integer.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns>True if the value is a finite integer not divisible by 2.</returns>
    public static bool IsOddInteger(Decimal128 value) => HasParity(value, 1);

    /// <summary>Whether the sign bit is clear. This is true for positive zero and for a NaN without the sign set.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns>True if the sign bit is clear.</returns>
    public static bool IsPositive(Decimal128 value) => !IsNegative(value);

    /// <summary>Whether the value is a real number, which is any value except a NaN.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns>True if the value is not a NaN.</returns>
    public static bool IsRealNumber(Decimal128 value) => !IsNaN(value);

    /// <summary>Whether the value is a complex number. Always false for this type.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns>False.</returns>
    public static bool IsComplexNumber(Decimal128 value) => false;

    /// <summary>Whether the value is an imaginary number. Always false for this type.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns>False.</returns>
    public static bool IsImaginaryNumber(Decimal128 value) => false;

    // Max and min under their .NET names. Max and Min without a context are IEEE 754-2019
    // maximum and minimum, which return NaN if either operand is NaN. The Number versions,
    // and the overloads with a context, are the specification's max and min. They return
    // the number when the other operand is a quiet NaN. The corpus tests the context
    // overloads.

    /// <summary>The larger of the two, as IEEE 754-2019 maximum. A NaN operand gives NaN.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>The larger value, or NaN if either operand is a NaN.</returns>
    public static Decimal128 Max(Decimal128 left, Decimal128 right) => SelectOrNaN(left, right, true, false);

    /// <summary>The larger of the two, as the specification's max. A quiet NaN operand is ignored.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>The larger value, or the number if the other operand is a quiet NaN.</returns>
    public static Decimal128 MaxNumber(Decimal128 left, Decimal128 right)
    {
        var context = new Decimal128Context();
        return Max(left, right, ref context);
    }

    /// <summary>The smaller of the two, as IEEE 754-2019 minimum. A NaN operand gives NaN.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>The smaller value, or NaN if either operand is a NaN.</returns>
    public static Decimal128 Min(Decimal128 left, Decimal128 right) => SelectOrNaN(left, right, false, false);

    /// <summary>The smaller of the two, as the specification's min. A quiet NaN operand is ignored.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>The smaller value, or the number if the other operand is a quiet NaN.</returns>
    public static Decimal128 MinNumber(Decimal128 left, Decimal128 right)
    {
        var context = new Decimal128Context();
        return Min(left, right, ref context);
    }

    /// <summary>The value with the larger magnitude. A NaN operand gives NaN.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>The value with the larger magnitude, or NaN if either operand is a NaN.</returns>
    public static Decimal128 MaxMagnitude(Decimal128 left, Decimal128 right) => SelectOrNaN(left, right, true, true);

    /// <summary>The value with the larger magnitude. A quiet NaN operand is ignored.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>The value with the larger magnitude, or the number if the other operand is a quiet NaN.</returns>
    public static Decimal128 MaxMagnitudeNumber(Decimal128 left, Decimal128 right)
    {
        var context = new Decimal128Context();
        return MaxMagnitude(left, right, ref context);
    }

    /// <summary>The value with the smaller magnitude. A NaN operand gives NaN.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>The value with the smaller magnitude, or NaN if either operand is a NaN.</returns>
    public static Decimal128 MinMagnitude(Decimal128 left, Decimal128 right) => SelectOrNaN(left, right, false, true);

    /// <summary>The value with the smaller magnitude. A quiet NaN operand is ignored.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>The value with the smaller magnitude, or the number if the other operand is a quiet NaN.</returns>
    public static Decimal128 MinMagnitudeNumber(Decimal128 left, Decimal128 right)
    {
        var context = new Decimal128Context();
        return MinMagnitude(left, right, ref context);
    }

    /// <summary>
    /// The sign of the value: -1, 0, or 1. Throws for NaN, like the built-in floating-point
    /// types.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns>-1 for a negative value, 0 for zero, and 1 for a positive value.</returns>
    /// <exception cref="ArithmeticException">The value is a NaN.</exception>
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
    /// Limits the value to the range from <paramref name="min"/> to <paramref name="max"/>.
    /// NaN is returned unchanged. Throws if <paramref name="min"/> is greater than
    /// <paramref name="max"/>.
    /// </summary>
    /// <param name="value">The value to limit.</param>
    /// <param name="min">The lower bound.</param>
    /// <param name="max">The upper bound.</param>
    /// <returns><paramref name="min"/> if the value is below it, <paramref name="max"/> if the value is above it, and the value otherwise.</returns>
    /// <exception cref="ArgumentException"><paramref name="min"/> is greater than <paramref name="max"/>.</exception>
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

    // Rounding to an integer or to a number of decimal places. The default mode is half to
    // even, as in the rest of .NET.

    /// <summary>Rounds to an integer, half to even.</summary>
    /// <param name="value">The value to round.</param>
    /// <returns>The nearest integer. A value that is already an integer is returned unchanged.</returns>
    public static Decimal128 Round(Decimal128 value) => Round(value, 0, MidpointRounding.ToEven);

    /// <summary>Rounds to a number of decimal places, half to even. Does not add trailing zeros.</summary>
    /// <param name="value">The value to round.</param>
    /// <param name="digits">The number of decimal places to keep.</param>
    /// <returns>The rounded value. A value with <paramref name="digits"/> or fewer decimal places is returned unchanged.</returns>
    public static Decimal128 Round(Decimal128 value, int digits) => Round(value, digits, MidpointRounding.ToEven);

    /// <summary>Rounds to an integer with the given rounding mode.</summary>
    /// <param name="value">The value to round.</param>
    /// <param name="mode">The rounding mode.</param>
    /// <returns>The rounded integer. A value that is already an integer is returned unchanged.</returns>
    public static Decimal128 Round(Decimal128 value, MidpointRounding mode) => Round(value, 0, mode);

    /// <summary>Rounds to a number of decimal places with the given rounding mode. Does not add trailing zeros.</summary>
    /// <param name="value">The value to round.</param>
    /// <param name="digits">The number of decimal places to keep.</param>
    /// <param name="mode">The rounding mode.</param>
    /// <returns>The rounded value. A value with <paramref name="digits"/> or fewer decimal places is returned unchanged.</returns>
    public static Decimal128 Round(Decimal128 value, int digits, MidpointRounding mode) =>
        new(Decimal128Shaping.Round(value._bits, digits, FromMidpointRounding(mode)));

    /// <summary>The value rounded toward positive infinity.</summary>
    /// <param name="value">The value to round.</param>
    /// <returns>The smallest integer not less than the value.</returns>
    public static Decimal128 Ceiling(Decimal128 value) => Round(value, 0, MidpointRounding.ToPositiveInfinity);

    /// <summary>The value rounded toward negative infinity.</summary>
    /// <param name="value">The value to round.</param>
    /// <returns>The largest integer not greater than the value.</returns>
    public static Decimal128 Floor(Decimal128 value) => Round(value, 0, MidpointRounding.ToNegativeInfinity);

    /// <summary>The value with its fractional part removed, which rounds toward zero.</summary>
    /// <param name="value">The value to round.</param>
    /// <returns>The integer part of the value.</returns>
    public static Decimal128 Truncate(Decimal128 value) => Round(value, 0, MidpointRounding.ToZero);

    /// <summary>
    /// Converts to an integer type, truncating toward zero and clamping to the type's
    /// range. NaN converts to zero, as it does for double.
    /// </summary>
    /// <typeparam name="TInteger">The integer type to convert to.</typeparam>
    /// <param name="value">The value to convert.</param>
    /// <returns>The value truncated toward zero and clamped to the range of <typeparamref name="TInteger"/>.</returns>
    public static TInteger ConvertToInteger<TInteger>(Decimal128 value)
        where TInteger : IBinaryInteger<TInteger> => TInteger.CreateSaturating(value);

    /// <summary>
    /// Same as <see cref="ConvertToInteger{TInteger}"/>. For the built-in types, the native
    /// version lets the hardware decide out-of-range results. This type has no hardware
    /// conversion, so the two are the same.
    /// </summary>
    /// <typeparam name="TInteger">The integer type to convert to.</typeparam>
    /// <param name="value">The value to convert.</param>
    /// <returns>The value truncated toward zero and clamped to the range of <typeparamref name="TInteger"/>.</returns>
    public static TInteger ConvertToIntegerNative<TInteger>(Decimal128 value)
        where TInteger : IBinaryInteger<TInteger> => TInteger.CreateSaturating(value);

    // Text.

    /// <summary>
    /// Formats the value. An empty format or "G" gives scientific notation, "E" gives
    /// engineering notation, "F" and "N" give fixed-point notation, and other formats use the
    /// formatting of the nearest <see cref="double"/>.
    /// </summary>
    /// <param name="format">The format string, or null for scientific notation.</param>
    /// <param name="provider">The culture's symbols, or null for the current culture.</param>
    /// <returns>The formatted value.</returns>
    public string ToString(string? format, IFormatProvider? provider) =>
        Decimal128Formatter.Format(_bits, format, provider);

    /// <summary>Formats the value into a span of characters. The formats are the same as for <see cref="ToString(string?, IFormatProvider?)"/>.</summary>
    /// <param name="destination">Receives the text.</param>
    /// <param name="charsWritten">Receives the number of characters written, or zero if the destination is too short.</param>
    /// <param name="format">The format string. Empty gives scientific notation.</param>
    /// <param name="provider">The culture's symbols, or null for the current culture.</param>
    /// <returns>True if the text fit in <paramref name="destination"/>.</returns>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format,
        IFormatProvider? provider) =>
        Decimal128Formatter.TryFormat(_bits, destination, out charsWritten, format, provider);

    // The overloads with a provider use that culture's separators, signs, and symbols. The
    // overloads above take no provider and use the specification's syntax, which does not
    // depend on culture. Format and parse with the same choice: text formatted for one
    // culture may not parse under another.

    /// <summary>Parses text in a culture's format, with <see cref="NumberStyles.Float"/> and <see cref="NumberStyles.AllowThousands"/>.</summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="provider">The culture's symbols, or null for the current culture.</param>
    /// <returns>The value, rounded half to even.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="s"/> is null.</exception>
    /// <exception cref="FormatException">The text is not a valid number.</exception>
    public static Decimal128 Parse(string s, IFormatProvider? provider) =>
        Parse(s, Decimal128CultureNormalizer.DefaultStyles, provider);

    /// <summary>Parses text in a culture's format, with <see cref="NumberStyles.Float"/> and <see cref="NumberStyles.AllowThousands"/>.</summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="provider">The culture's symbols, or null for the current culture.</param>
    /// <returns>The value, rounded half to even.</returns>
    /// <exception cref="FormatException">The text is not a valid number.</exception>
    public static Decimal128 Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        Parse(s, Decimal128CultureNormalizer.DefaultStyles, provider);

    /// <summary>Parses text in a culture's format with the given styles.</summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="style">The styles allowed in the text. The hex and binary specifiers are not supported.</param>
    /// <param name="provider">The culture's symbols, or null for the current culture.</param>
    /// <returns>The value, rounded half to even.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="s"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="style"/> includes a style this type does not support.</exception>
    /// <exception cref="FormatException">The text is not a valid number.</exception>
    public static Decimal128 Parse(string s, NumberStyles style, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return Parse(s.AsSpan(), style, provider);
    }

    /// <summary>Parses text in a culture's format with the given styles.</summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="style">The styles allowed in the text. The hex and binary specifiers are not supported.</param>
    /// <param name="provider">The culture's symbols, or null for the current culture.</param>
    /// <returns>The value, rounded half to even.</returns>
    /// <exception cref="ArgumentException"><paramref name="style"/> includes a style this type does not support.</exception>
    /// <exception cref="FormatException">The text is not a valid number.</exception>
    public static Decimal128 Parse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider)
    {
        if (!TryParse(s, style, provider, out var value))
        {
            throw new FormatException($"'{s}' is not a Decimal128.");
        }

        return value;
    }

    /// <summary>Parses text in a culture's format, with <see cref="NumberStyles.Float"/> and <see cref="NumberStyles.AllowThousands"/>.</summary>
    /// <param name="s">The text to parse. Null fails.</param>
    /// <param name="provider">The culture's symbols, or null for the current culture.</param>
    /// <param name="result">Receives the value when parsing succeeds, and zero otherwise.</param>
    /// <returns>True if the text is a valid number.</returns>
    public static bool TryParse(string? s, IFormatProvider? provider, out Decimal128 result) =>
        TryParse(s, Decimal128CultureNormalizer.DefaultStyles, provider, out result);

    /// <summary>Parses text in a culture's format, with <see cref="NumberStyles.Float"/> and <see cref="NumberStyles.AllowThousands"/>.</summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="provider">The culture's symbols, or null for the current culture.</param>
    /// <param name="result">Receives the value when parsing succeeds, and zero otherwise.</param>
    /// <returns>True if the text is a valid number.</returns>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Decimal128 result) =>
        TryParse(s, Decimal128CultureNormalizer.DefaultStyles, provider, out result);

    /// <summary>Parses text in a culture's format with the given styles.</summary>
    /// <param name="s">The text to parse. Null fails.</param>
    /// <param name="style">The styles allowed in the text. The hex and binary specifiers are not supported.</param>
    /// <param name="provider">The culture's symbols, or null for the current culture.</param>
    /// <param name="result">Receives the value when parsing succeeds, and zero otherwise.</param>
    /// <returns>True if the text is a valid number.</returns>
    /// <exception cref="ArgumentException"><paramref name="style"/> includes a style this type does not support.</exception>
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

    /// <summary>Parses text in a culture's format with the given styles.</summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="style">The styles allowed in the text. The hex and binary specifiers are not supported.</param>
    /// <param name="provider">The culture's symbols, or null for the current culture.</param>
    /// <param name="result">Receives the value when parsing succeeds, and zero otherwise.</param>
    /// <returns>True if the text is a valid number.</returns>
    /// <exception cref="ArgumentException"><paramref name="style"/> includes a style this type does not support.</exception>
    public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider,
        out Decimal128 result)
    {
        Decimal128CultureNormalizer.ValidateStyles(style, nameof(style));

        var status = Decimal128Status.None;
        var bits = Decimal128Parser.Parse(s, style, provider, Decimal128Rounding.HalfEven, ref status);
        return Reported(bits, status, out result);
    }

    // The same text as UTF-8 bytes.

    /// <summary>Formats the value as UTF-8. The formats are the same as for <see cref="ToString(string?, IFormatProvider?)"/>.</summary>
    /// <param name="utf8Destination">Receives the UTF-8 text.</param>
    /// <param name="bytesWritten">Receives the number of bytes written, or zero if the destination is too short.</param>
    /// <param name="format">The format string. Empty gives scientific notation.</param>
    /// <param name="provider">The culture's symbols, or null for the current culture.</param>
    /// <returns>True if the text fit in <paramref name="utf8Destination"/>.</returns>
    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format,
        IFormatProvider? provider) =>
        Decimal128Formatter.TryFormat(_bits, utf8Destination, out bytesWritten, format, provider);

    /// <summary>Parses UTF-8 text in the specification's syntax, rounding half to even.</summary>
    /// <param name="utf8Text">The UTF-8 text to parse.</param>
    /// <returns>The value, rounded to the format.</returns>
    /// <exception cref="FormatException">The text is not a valid number.</exception>
    public static Decimal128 Parse(ReadOnlySpan<byte> utf8Text)
    {
        if (!TryParse(utf8Text, out var value))
        {
            throw new FormatException($"'{Encoding.UTF8.GetString(utf8Text)}' is not a Decimal128.");
        }

        return value;
    }

    /// <summary>Parses UTF-8 text in the specification's syntax, rounding half to even.</summary>
    /// <param name="utf8Text">The UTF-8 text to parse.</param>
    /// <param name="value">Receives the value when parsing succeeds, and zero otherwise.</param>
    /// <returns>True if the text is a valid number.</returns>
    public static bool TryParse(ReadOnlySpan<byte> utf8Text, out Decimal128 value)
    {
        var status = Decimal128Status.None;
        var bits = Decimal128Parser.Parse(utf8Text, Decimal128Rounding.HalfEven, ref status);
        return Reported(bits, status, out value);
    }

    /// <summary>Parses UTF-8 text in a culture's format, with <see cref="NumberStyles.Float"/> and <see cref="NumberStyles.AllowThousands"/>.</summary>
    /// <param name="utf8Text">The UTF-8 text to parse.</param>
    /// <param name="provider">The culture's symbols, or null for the current culture.</param>
    /// <returns>The value, rounded half to even.</returns>
    /// <exception cref="FormatException">The text is not a valid number.</exception>
    public static Decimal128 Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider)
    {
        if (!TryParse(utf8Text, provider, out var value))
        {
            throw new FormatException($"'{Encoding.UTF8.GetString(utf8Text)}' is not a Decimal128.");
        }

        return value;
    }

    /// <summary>Parses UTF-8 text in a culture's format, with <see cref="NumberStyles.Float"/> and <see cref="NumberStyles.AllowThousands"/>.</summary>
    /// <param name="utf8Text">The UTF-8 text to parse.</param>
    /// <param name="provider">The culture's symbols, or null for the current culture.</param>
    /// <param name="result">Receives the value when parsing succeeds, and zero otherwise.</param>
    /// <returns>True if the text is a valid number.</returns>
    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out Decimal128 result)
    {
        var status = Decimal128Status.None;
        var bits = Decimal128Parser.Parse(utf8Text, Decimal128CultureNormalizer.DefaultStyles, provider,
            Decimal128Rounding.HalfEven, ref status);

        return Reported(bits, status, out result);
    }

    // The exponent and coefficient as bytes, for IFloatingPoint.

    /// <summary>The number of bytes <see cref="IFloatingPoint{TSelf}.TryWriteExponentBigEndian"/> writes.</summary>
    /// <returns>4, the size of an <see cref="int"/>.</returns>
    int IFloatingPoint<Decimal128>.GetExponentByteCount() => sizeof(int);

    /// <summary>The number of bits needed to hold the exponent as a two's complement integer.</summary>
    /// <returns>The shortest bit length of the exponent, including the sign bit. Special values have exponent zero.</returns>
    int IFloatingPoint<Decimal128>.GetExponentShortestBitLength()
    {
        var exponent = Exponent(_bits);
        return exponent >= 0
            ? (sizeof(int) * 8) - int.LeadingZeroCount(exponent)
            : (sizeof(int) * 8) + 1 - int.LeadingZeroCount(~exponent);
    }

    /// <summary>The number of bytes <see cref="IFloatingPoint{TSelf}.TryWriteSignificandBigEndian"/> writes.</summary>
    /// <returns>16, the size of the coefficient's two 64-bit words.</returns>
    int IFloatingPoint<Decimal128>.GetSignificandByteCount() => 2 * sizeof(ulong);

    /// <summary>The number of bits in the coefficient's two 64-bit words.</summary>
    /// <returns>128.</returns>
    int IFloatingPoint<Decimal128>.GetSignificandBitLength() => 2 * sizeof(ulong) * 8;

    /// <summary>Writes the exponent as a big-endian <see cref="int"/>.</summary>
    /// <param name="destination">Receives the bytes.</param>
    /// <param name="written">Receives the number of bytes written, or zero if the destination is too short.</param>
    /// <returns>True if the exponent fit in <paramref name="destination"/>.</returns>
    bool IFloatingPoint<Decimal128>.TryWriteExponentBigEndian(Span<byte> destination, out int written) =>
        WriteBytes(Exponent(_bits), destination, out written, true);

    /// <summary>Writes the exponent as a little-endian <see cref="int"/>.</summary>
    /// <param name="destination">Receives the bytes.</param>
    /// <param name="written">Receives the number of bytes written, or zero if the destination is too short.</param>
    /// <returns>True if the exponent fit in <paramref name="destination"/>.</returns>
    bool IFloatingPoint<Decimal128>.TryWriteExponentLittleEndian(Span<byte> destination, out int written) =>
        WriteBytes(Exponent(_bits), destination, out written, false);

    /// <summary>Writes the coefficient, or a NaN's payload, as a big-endian integer.</summary>
    /// <param name="destination">Receives the bytes.</param>
    /// <param name="written">Receives the number of bytes written, or zero if the destination is too short.</param>
    /// <returns>True if the coefficient fit in <paramref name="destination"/>.</returns>
    bool IFloatingPoint<Decimal128>.TryWriteSignificandBigEndian(Span<byte> destination, out int written) =>
        WriteBytes(Coefficient(_bits), destination, out written, true);

    /// <summary>Writes the coefficient, or a NaN's payload, as a little-endian integer.</summary>
    /// <param name="destination">Receives the bytes.</param>
    /// <param name="written">Receives the number of bytes written, or zero if the destination is too short.</param>
    /// <returns>True if the coefficient fit in <paramref name="destination"/>.</returns>
    bool IFloatingPoint<Decimal128>.TryWriteSignificandLittleEndian(Span<byte> destination, out int written) =>
        WriteBytes(Coefficient(_bits), destination, out written, false);

    // Conversions to and from the other numeric types.

    /// <summary>Converts from another numeric type, if this type supports it directly.</summary>
    /// <typeparam name="TOther">The type to convert from.</typeparam>
    /// <param name="value">The value to convert.</param>
    /// <param name="result">Receives the converted value, rounded to the format.</param>
    /// <returns>True if this type can convert from <typeparamref name="TOther"/>.</returns>
    static bool INumberBase<Decimal128>.TryConvertFromChecked<TOther>(TOther value, out Decimal128 result)
    {
        var converted = ConvertFrom(value);
        result = converted.GetValueOrDefault();
        return converted.HasValue;
    }

    /// <summary>Converts from another numeric type, if this type supports it directly.</summary>
    /// <typeparam name="TOther">The type to convert from.</typeparam>
    /// <param name="value">The value to convert.</param>
    /// <param name="result">Receives the converted value, rounded to the format.</param>
    /// <returns>True if this type can convert from <typeparamref name="TOther"/>.</returns>
    static bool INumberBase<Decimal128>.TryConvertFromSaturating<TOther>(TOther value, out Decimal128 result)
    {
        var converted = ConvertFrom(value);
        result = converted.GetValueOrDefault();
        return converted.HasValue;
    }

    /// <summary>Converts from another numeric type, if this type supports it directly.</summary>
    /// <typeparam name="TOther">The type to convert from.</typeparam>
    /// <param name="value">The value to convert.</param>
    /// <param name="result">Receives the converted value, rounded to the format.</param>
    /// <returns>True if this type can convert from <typeparamref name="TOther"/>.</returns>
    static bool INumberBase<Decimal128>.TryConvertFromTruncating<TOther>(TOther value, out Decimal128 result)
    {
        var converted = ConvertFrom(value);
        result = converted.GetValueOrDefault();
        return converted.HasValue;
    }

    /// <summary>Converts to another numeric type. An integer target gets the value truncated toward zero and clamped to its range.</summary>
    /// <typeparam name="TOther">The type to convert to.</typeparam>
    /// <param name="value">The value to convert.</param>
    /// <param name="result">Receives the converted value.</param>
    /// <returns>True. Every <see cref="INumberBase{TSelf}"/> type is supported.</returns>
    static bool INumberBase<Decimal128>.TryConvertToChecked<TOther>(Decimal128 value, out TOther result)
        where TOther : default
    {
        result = Decimal128Conversions.ConvertSaturating<TOther>(value._bits)!;
        return true;
    }

    /// <summary>Converts to another numeric type. An integer target gets the value truncated toward zero and clamped to its range.</summary>
    /// <typeparam name="TOther">The type to convert to.</typeparam>
    /// <param name="value">The value to convert.</param>
    /// <param name="result">Receives the converted value.</param>
    /// <returns>True. Every <see cref="INumberBase{TSelf}"/> type is supported.</returns>
    static bool INumberBase<Decimal128>.TryConvertToSaturating<TOther>(Decimal128 value, out TOther result)
        where TOther : default
    {
        result = Decimal128Conversions.ConvertSaturating<TOther>(value._bits)!;
        return true;
    }

    /// <summary>Converts to another numeric type. An integer target gets the value truncated toward zero and clamped to its range.</summary>
    /// <typeparam name="TOther">The type to convert to.</typeparam>
    /// <param name="value">The value to convert.</param>
    /// <param name="result">Receives the converted value.</param>
    /// <returns>True. Every <see cref="INumberBase{TSelf}"/> type is supported.</returns>
    static bool INumberBase<Decimal128>.TryConvertToTruncating<TOther>(Decimal128 value, out TOther result)
        where TOther : default
    {
        result = Decimal128Conversions.ConvertSaturating<TOther>(value._bits)!;
        return true;
    }

    // Conversions. Conversion from every built-in integer type is implicit, because 34 digits
    // hold the 20 digits of the widest one exactly. Other conversions are explicit, because
    // they can round, overflow to infinity, or underflow to a subnormal. Conversions to an
    // integer truncate toward zero and throw if the value does not fit, as System.Decimal
    // does. Use CreateSaturating or CreateTruncating for the other behaviors.

    /// <summary>Converts an <see cref="sbyte"/> exactly.</summary>
    /// <param name="value">The integer to convert.</param>
    /// <returns>The integer, with exponent zero.</returns>
    public static implicit operator Decimal128(sbyte value) => new(Decimal128Conversions.FromInt64(value));

    /// <summary>Converts a <see cref="byte"/> exactly.</summary>
    /// <param name="value">The integer to convert.</param>
    /// <returns>The integer, with exponent zero.</returns>
    public static implicit operator Decimal128(byte value) => new(Decimal128Conversions.FromUInt64(value, false));

    /// <summary>Converts a <see cref="short"/> exactly.</summary>
    /// <param name="value">The integer to convert.</param>
    /// <returns>The integer, with exponent zero.</returns>
    public static implicit operator Decimal128(short value) => new(Decimal128Conversions.FromInt64(value));

    /// <summary>Converts a <see cref="ushort"/> exactly.</summary>
    /// <param name="value">The integer to convert.</param>
    /// <returns>The integer, with exponent zero.</returns>
    public static implicit operator Decimal128(ushort value) => new(Decimal128Conversions.FromUInt64(value, false));

    /// <summary>Converts a <see cref="char"/>'s code unit exactly.</summary>
    /// <param name="value">The character to convert.</param>
    /// <returns>The code unit, with exponent zero.</returns>
    public static implicit operator Decimal128(char value) => new(Decimal128Conversions.FromUInt64(value, false));

    /// <summary>Converts an <see cref="int"/> exactly.</summary>
    /// <param name="value">The integer to convert.</param>
    /// <returns>The integer, with exponent zero.</returns>
    public static implicit operator Decimal128(int value) => new(Decimal128Conversions.FromInt64(value));

    /// <summary>Converts a <see cref="uint"/> exactly.</summary>
    /// <param name="value">The integer to convert.</param>
    /// <returns>The integer, with exponent zero.</returns>
    public static implicit operator Decimal128(uint value) => new(Decimal128Conversions.FromUInt64(value, false));

    /// <summary>Converts a <see cref="long"/> exactly.</summary>
    /// <param name="value">The integer to convert.</param>
    /// <returns>The integer, with exponent zero.</returns>
    public static implicit operator Decimal128(long value) => new(Decimal128Conversions.FromInt64(value));

    /// <summary>Converts a <see cref="ulong"/> exactly.</summary>
    /// <param name="value">The integer to convert.</param>
    /// <returns>The integer, with exponent zero.</returns>
    public static implicit operator Decimal128(ulong value) => new(Decimal128Conversions.FromUInt64(value, false));

    /// <summary>Converts a <see cref="Half"/> through its shortest round-trip text.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The value, rounded half to even.</returns>
    public static explicit operator Decimal128(Half value) => new(Decimal128Conversions.FromBinary(value));

    /// <summary>Converts a <see cref="float"/> through its shortest round-trip text, so 0.1f becomes 0.1.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The value, rounded half to even.</returns>
    public static explicit operator Decimal128(float value) => new(Decimal128Conversions.FromBinary(value));

    /// <summary>Converts a <see cref="double"/> through its shortest round-trip text, so 0.1 becomes 0.1.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The value, rounded half to even.</returns>
    public static explicit operator Decimal128(double value) => new(Decimal128Conversions.FromBinary(value));

    /// <summary>
    /// Converts a binary floating-point value using the given conversion. The cast
    /// operators use the shortest round-trip conversion. Use this method to get IEEE 754
    /// convertFormat instead, which converts the exact binary value.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="conversion">Whether to convert the shortest round-trip text or the exact binary value.</param>
    /// <returns>The value, rounded half to even.</returns>
    public static Decimal128 FromBinary(double value, Decimal128BinaryConversion conversion) =>
        new(Decimal128Conversions.FromBinary(value, conversion));

    /// <summary>Converts a <see cref="float"/> using the given conversion.</summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="conversion">Whether to convert the shortest round-trip text or the exact binary value.</param>
    /// <returns>The value, rounded half to even.</returns>
    public static Decimal128 FromBinary(float value, Decimal128BinaryConversion conversion) =>
        new(Decimal128Conversions.FromBinary(value, conversion));

    /// <summary>Converts a <see cref="Half"/> using the given conversion.</summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="conversion">Whether to convert the shortest round-trip text or the exact binary value.</param>
    /// <returns>The value, rounded half to even.</returns>
    public static Decimal128 FromBinary(Half value, Decimal128BinaryConversion conversion) =>
        new(Decimal128Conversions.FromBinary(value, conversion));

    /// <summary>Converts to an <see cref="sbyte"/>, truncating toward zero.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The integer part of the value.</returns>
    /// <exception cref="OverflowException">The value is a NaN, an infinity, or out of range.</exception>
    public static explicit operator sbyte(Decimal128 value) => Decimal128Conversions.ToInteger<sbyte>(value._bits);

    /// <summary>Converts to a <see cref="byte"/>, truncating toward zero.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The integer part of the value.</returns>
    /// <exception cref="OverflowException">The value is a NaN, an infinity, or out of range.</exception>
    public static explicit operator byte(Decimal128 value) => Decimal128Conversions.ToInteger<byte>(value._bits);

    /// <summary>Converts to a <see cref="short"/>, truncating toward zero.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The integer part of the value.</returns>
    /// <exception cref="OverflowException">The value is a NaN, an infinity, or out of range.</exception>
    public static explicit operator short(Decimal128 value) => Decimal128Conversions.ToInteger<short>(value._bits);

    /// <summary>Converts to a <see cref="ushort"/>, truncating toward zero.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The integer part of the value.</returns>
    /// <exception cref="OverflowException">The value is a NaN, an infinity, or out of range.</exception>
    public static explicit operator ushort(Decimal128 value) => Decimal128Conversions.ToInteger<ushort>(value._bits);

    /// <summary>Converts to a <see cref="char"/> code unit, truncating toward zero.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The character whose code unit is the integer part of the value.</returns>
    /// <exception cref="OverflowException">The value is a NaN, an infinity, or out of range.</exception>
    public static explicit operator char(Decimal128 value) => (char)Decimal128Conversions.ToInteger<ushort>(value._bits);

    /// <summary>Converts to an <see cref="int"/>, truncating toward zero.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The integer part of the value.</returns>
    /// <exception cref="OverflowException">The value is a NaN, an infinity, or out of range.</exception>
    public static explicit operator int(Decimal128 value) => Decimal128Conversions.ToInteger<int>(value._bits);

    /// <summary>Converts to a <see cref="uint"/>, truncating toward zero.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The integer part of the value.</returns>
    /// <exception cref="OverflowException">The value is a NaN, an infinity, or out of range.</exception>
    public static explicit operator uint(Decimal128 value) => Decimal128Conversions.ToInteger<uint>(value._bits);

    /// <summary>Converts to a <see cref="long"/>, truncating toward zero.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The integer part of the value.</returns>
    /// <exception cref="OverflowException">The value is a NaN, an infinity, or out of range.</exception>
    public static explicit operator long(Decimal128 value) => Decimal128Conversions.ToInteger<long>(value._bits);

    /// <summary>Converts to a <see cref="ulong"/>, truncating toward zero.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The integer part of the value.</returns>
    /// <exception cref="OverflowException">The value is a NaN, an infinity, or out of range.</exception>
    public static explicit operator ulong(Decimal128 value) => Decimal128Conversions.ToInteger<ulong>(value._bits);

    /// <summary>Converts to the nearest <see cref="Half"/>, through the nearest <see cref="double"/>.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The converted value. Out-of-range values give an infinity.</returns>
    public static explicit operator Half(Decimal128 value) => (Half)Decimal128Formatter.ToDouble(value._bits);

    /// <summary>Converts to the nearest <see cref="float"/>, through the nearest <see cref="double"/>.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The converted value. Out-of-range values give an infinity.</returns>
    public static explicit operator float(Decimal128 value) => (float)Decimal128Formatter.ToDouble(value._bits);

    /// <summary>Converts to the nearest <see cref="double"/>.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The nearest double. Out-of-range values give an infinity.</returns>
    public static explicit operator double(Decimal128 value) => Decimal128Formatter.ToDouble(value._bits);

    /// <summary>
    /// Numeric equality: 1.0 equals 1.00, and the two zeros are equal. Unlike <c>==</c>,
    /// NaN equals NaN, so NaN can be found in a collection. Use <see cref="CompareTotal"/>
    /// or <see cref="ToBits"/> to tell encodings apart.
    /// </summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns>True if both values are NaN, or both are numbers with the same value.</returns>
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

    /// <summary>Numeric equality with a boxed value. Same as <see cref="Equals(Decimal128)"/> for a <see cref="Decimal128"/>.</summary>
    /// <param name="other">The object to compare with.</param>
    /// <returns>True if <paramref name="other"/> is a <see cref="Decimal128"/> equal to this value.</returns>
    public override bool Equals(object? other) => other is Decimal128 value && Equals(value);

    /// <summary>Hashes the numeric value, so equal values with different exponents hash the same.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => Decimal128Ordering.ValueHashCode(_bits);

    /// <summary>Order for sorting. NaN sorts before all other values and equals other NaNs.</summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns>A negative number if this value sorts first, zero if the values are equal, or a positive number if it sorts second.</returns>
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

    /// <summary>Order for sorting, with a boxed value. Null sorts before every value.</summary>
    /// <param name="other">The object to compare with. It must be null or a <see cref="Decimal128"/>.</param>
    /// <returns>A negative number if this value sorts first, zero if the values are equal, or a positive number if it sorts second.</returns>
    /// <exception cref="ArgumentException"><paramref name="other"/> is not a <see cref="Decimal128"/>.</exception>
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

    // Numeric comparison, with NaN unordered. If either side is NaN, every operator returns
    // false except !=, which returns true. This holds even when comparing a NaN to itself.

    /// <summary>Numeric equality. NaN is not equal to anything, including itself.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>True if neither is a NaN and the values are equal.</returns>
    public static bool operator ==(Decimal128 left, Decimal128 right) =>
        Ordered(left, right, out var comparison) && comparison == 0;

    /// <summary>Numeric inequality. True if either value is a NaN.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>True if either is a NaN or the values differ.</returns>
    public static bool operator !=(Decimal128 left, Decimal128 right) => !(left == right);

    /// <summary>Whether the first value is less than the second. False if either is a NaN.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>True if neither is a NaN and <paramref name="left"/> is less.</returns>
    public static bool operator <(Decimal128 left, Decimal128 right) =>
        Ordered(left, right, out var comparison) && comparison < 0;

    /// <summary>Whether the first value is greater than the second. False if either is a NaN.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>True if neither is a NaN and <paramref name="left"/> is greater.</returns>
    public static bool operator >(Decimal128 left, Decimal128 right) =>
        Ordered(left, right, out var comparison) && comparison > 0;

    /// <summary>Whether the first value is less than or equal to the second. False if either is a NaN.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>True if neither is a NaN and <paramref name="left"/> is less or equal.</returns>
    public static bool operator <=(Decimal128 left, Decimal128 right) =>
        Ordered(left, right, out var comparison) && comparison <= 0;

    /// <summary>Whether the first value is greater than or equal to the second. False if either is a NaN.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>True if neither is a NaN and <paramref name="left"/> is greater or equal.</returns>
    public static bool operator >=(Decimal128 left, Decimal128 right) =>
        Ordered(left, right, out var comparison) && comparison >= 0;

    /// <summary>Adds 1, rounding half to even.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The value plus 1.</returns>
    public static Decimal128 operator ++(Decimal128 value) => value + One;

    /// <summary>Subtracts 1, rounding half to even.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The value minus 1.</returns>
    public static Decimal128 operator --(Decimal128 value) => value - One;

    // The private helpers the members above call.

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

    private static Decimal128 Select(Decimal128 left, Decimal128 right, bool wantLarger, bool byMagnitude,
        ref Decimal128Context context)
    {
        var status = Decimal128Status.None;
        var result = Decimal128Ordering.Select(left._bits, right._bits, wantLarger, byMagnitude, ref status);
        context.Status |= status;
        return new Decimal128(result);
    }

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

    private static bool HasParity(Decimal128 value, ulong wanted)
    {
        if (!IsInteger(value))
        {
            return false;
        }

        var coefficient = Decimal128Encoding.Unpack(value._bits, out var exponent);
        if (coefficient.IsZero || exponent > 0)
        {
            // A positive exponent means the value ends in zeros, so it is even regardless of
            // the coefficient's last digit.
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

    private static Decimal128 SelectOrNaN(Decimal128 left, Decimal128 right, bool wantLarger, bool byMagnitude)
    {
        var status = Decimal128Status.None;
        if (Decimal128Encoding.IsNaN(left._bits) || Decimal128Encoding.IsNaN(right._bits))
        {
            return new Decimal128(Decimal128Arithmetic.PropagateNaN(left._bits, right._bits, ref status));
        }

        return new Decimal128(Decimal128Ordering.Select(left._bits, right._bits, wantLarger, byMagnitude, ref status));
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

    /// <summary>The value converted from another numeric type, or null if this type has no direct conversion from it.</summary>
    private static Decimal128? ConvertFrom<TOther>(TOther value)
        where TOther : INumberBase<TOther>
    {
        if (Decimal128Conversions.ConvertFrom(value) is not { } bits)
        {
            return null;
        }

        return new Decimal128(bits);
    }

    private static bool WriteBytes(int exponent, Span<byte> destination, out int written, bool bigEndian)
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

    private static bool WriteBytes(Decimal128Integer coefficient, Span<byte> destination, out int written, bool bigEndian)
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
}
