// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Numerics;

namespace Decimals;

/// <summary>
/// Every operation the three public types offer, written once over the format and the
/// integer that holds it. <see cref="Decimal32"/>, <see cref="Decimal64"/>, and
/// <see cref="Decimal128"/> are thin forwarders onto this; nothing but width-specific
/// constants lives outside it.
/// </summary>
internal static class DecimalCore<TFormat, TBits>
    where TFormat : IDecimalFormat<TBits>
    where TBits : IBinaryInteger<TBits>, IUnsignedNumber<TBits>
{
    public static TBits Zero { get; } = FromParts(false, 0, UInt128.Zero);

    public static TBits NegativeZero { get; } = FromParts(true, 0, UInt128.Zero);

    public static TBits One { get; } = FromParts(false, 0, UInt128.One);

    public static TBits NegativeOne { get; } = FromParts(true, 0, UInt128.One);

    public static TBits MaxValue { get; } = FromParts(false, TFormat.MaxQuantumExponent, LargestCoefficient);

    public static TBits MinValue { get; } = FromParts(true, TFormat.MaxQuantumExponent, LargestCoefficient);

    /// <summary>The smallest positive value, which is subnormal.</summary>
    public static TBits Epsilon { get; } = FromParts(false, TFormat.MinQuantumExponent, UInt128.One);

    private static UInt128 LargestCoefficient => PowersOfTen.UInt128(TFormat.Precision) - UInt128.One;

    // Classification reads the packed bits: none of it needs the coefficient except the
    // tests that are about the coefficient.

    public static bool IsNaN(TBits bits) => (bits & TFormat.NaNMask) == TFormat.NaNBits;

    public static bool IsSignalingNaN(TBits bits) =>
        (bits & TFormat.SignalingNaNMask) == TFormat.SignalingNaNBits;

    public static bool IsInfinity(TBits bits) => (bits & TFormat.InfinityMask) == TFormat.InfinityBits;

    public static bool IsFinite(TBits bits) => (bits & TFormat.InfinityBits) != TFormat.InfinityBits;

    public static bool IsNegative(TBits bits) => (bits & TFormat.SignMask) != TBits.Zero;

    public static bool IsZero(TBits bits) =>
        IsFinite(bits) && TFormat.Unpack(bits).Coefficient == UInt128.Zero;

    public static bool IsSubnormal(TBits bits) =>
        DecimalOperations.IsSubnormal<TFormat>(TFormat.Unpack(bits));

    public static bool IsNormal(TBits bits) => IsFinite(bits) && !IsZero(bits) && !IsSubnormal(bits);

    /// <summary>
    /// False when the encoding uses a coefficient or payload the format cannot hold. Those
    /// decode as zero, so re-encoding does not give the same bits back.
    /// </summary>
    public static bool IsCanonical(TBits bits) => bits == TFormat.Pack(TFormat.Unpack(bits));

    public static DecimalClass Classify(TBits bits) =>
        DecimalOperations.Classify<TFormat>(TFormat.Unpack(bits));

    // The copy family, defined on the bits rather than on the value: quiet, and never
    // signals even for a signaling NaN.

    public static TBits CopyAbs(TBits bits) => bits & ~TFormat.SignMask;

    public static TBits CopyNegate(TBits bits) => bits ^ TFormat.SignMask;

    public static TBits CopySign(TBits value, TBits sign) =>
        (value & ~TFormat.SignMask) | (sign & TFormat.SignMask);

    public static bool SameQuantum(TBits left, TBits right)
    {
        var first = TFormat.Unpack(left);
        var second = TFormat.Unpack(right);

        if (first.Kind != DecimalKind.Finite || second.Kind != DecimalKind.Finite)
        {
            return (first.IsNaN && second.IsNaN)
                || (first.Kind == DecimalKind.Infinity && second.Kind == DecimalKind.Infinity);
        }

        return first.Exponent == second.Exponent;
    }

    public static int CompareTotal(TBits left, TBits right) =>
        DecimalOperations.CompareTotal(TFormat.Unpack(left), TFormat.Unpack(right));

    public static int CompareTotalMagnitude(TBits left, TBits right) =>
        DecimalOperations.CompareTotalMagnitude(TFormat.Unpack(left), TFormat.Unpack(right));

    // Text.

    public static TBits FromString(ReadOnlySpan<char> text, ref DecimalContext context)
    {
        var status = context.Status;
        var value = DecimalParser.Parse<TFormat>(text, context.Rounding, ref status);
        context.Status = status;
        return TFormat.Pack(value);
    }

    public static bool TryParse(ReadOnlySpan<char> text, out TBits bits)
    {
        var status = DecimalStatus.None;
        var parsed = DecimalParser.Parse<TFormat>(text, DecimalRounding.HalfEven, ref status);
        if ((status & DecimalStatus.ConversionSyntax) != 0)
        {
            bits = Zero;
            return false;
        }

        bits = TFormat.Pack(parsed);
        return true;
    }

    public static bool TryParse(ReadOnlySpan<char> text, NumberStyles styles,
        IFormatProvider? provider, out TBits bits)
    {
        var status = DecimalStatus.None;
        var parsed = DecimalParser.Parse<TFormat>(text, styles, provider, DecimalRounding.HalfEven,
            ref status);
        return Reported(parsed, status, out bits);
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, NumberStyles styles,
        IFormatProvider? provider, out TBits bits)
    {
        var status = DecimalStatus.None;
        var parsed = DecimalParser.Parse<TFormat>(utf8Text, styles, provider,
            DecimalRounding.HalfEven, ref status);
        return Reported(parsed, status, out bits);
    }

    private static bool Reported(UnpackedDecimal<UInt128> parsed, DecimalStatus status, out TBits bits)
    {
        if ((status & DecimalStatus.ConversionSyntax) != 0)
        {
            bits = Zero;
            return false;
        }

        bits = TFormat.Pack(parsed);
        return true;
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, out TBits bits)
    {
        var status = DecimalStatus.None;
        var parsed = DecimalParser.Parse<TFormat>(utf8Text, DecimalRounding.HalfEven, ref status);
        if ((status & DecimalStatus.ConversionSyntax) != 0)
        {
            bits = Zero;
            return false;
        }

        bits = TFormat.Pack(parsed);
        return true;
    }

    public static string ToScientificString(TBits bits) =>
        DecimalFormatter.ToScientificString(TFormat.Unpack(bits));

    public static string ToEngineeringString(TBits bits) =>
        DecimalFormatter.ToEngineeringString(TFormat.Unpack(bits));

    // Arithmetic.

    public static TBits Add(TBits left, TBits right, ref DecimalContext context)
    {
        var status = context.Status;
        var result = DecimalArithmetic.Add<TFormat>(
            TFormat.Unpack(left), TFormat.Unpack(right), context.Rounding, ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    public static TBits Subtract(TBits left, TBits right, ref DecimalContext context)
    {
        var status = context.Status;
        var result = DecimalArithmetic.Subtract<TFormat>(
            TFormat.Unpack(left), TFormat.Unpack(right), context.Rounding, ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    public static TBits Multiply(TBits left, TBits right, ref DecimalContext context)
    {
        var status = context.Status;
        var result = DecimalArithmetic.Multiply<TFormat>(
            TFormat.Unpack(left), TFormat.Unpack(right), context.Rounding, ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    public static TBits Divide(TBits left, TBits right, ref DecimalContext context)
    {
        var status = context.Status;
        var result = DecimalArithmetic.Divide<TFormat>(
            TFormat.Unpack(left), TFormat.Unpack(right), context.Rounding, ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    public static TBits DivideInteger(TBits left, TBits right, ref DecimalContext context)
    {
        var status = context.Status;
        var result = DecimalArithmetic.DivideInteger<TFormat>(
            TFormat.Unpack(left), TFormat.Unpack(right), context.Rounding, ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    public static TBits Remainder(TBits left, TBits right, ref DecimalContext context)
    {
        var status = context.Status;
        var result = DecimalArithmetic.Remainder<TFormat>(
            TFormat.Unpack(left), TFormat.Unpack(right), context.Rounding, ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    public static TBits RemainderNear(TBits left, TBits right, ref DecimalContext context)
    {
        var status = context.Status;
        var result = DecimalArithmetic.RemainderNear<TFormat>(
            TFormat.Unpack(left), TFormat.Unpack(right), context.Rounding, ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    public static TBits FusedMultiplyAdd(TBits left, TBits right, TBits addend, ref DecimalContext context)
    {
        var status = context.Status;
        var result = DecimalArithmetic.FusedMultiplyAdd<TFormat>(
            TFormat.Unpack(left), TFormat.Unpack(right), TFormat.Unpack(addend),
            context.Rounding, ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    public static TBits Plus(TBits value, ref DecimalContext context)
    {
        var status = context.Status;
        var result = DecimalArithmetic.Plus<TFormat>(TFormat.Unpack(value), context.Rounding, ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    public static TBits Minus(TBits value, ref DecimalContext context)
    {
        var status = context.Status;
        var result = DecimalArithmetic.Minus<TFormat>(TFormat.Unpack(value), context.Rounding, ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    public static TBits Abs(TBits value, ref DecimalContext context)
    {
        var status = context.Status;
        var result = DecimalArithmetic.Abs<TFormat>(TFormat.Unpack(value), context.Rounding, ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    /// <summary>
    /// Numeric comparison giving -1, 0, 1, or a NaN. Unlike <see cref="CompareTotal"/> the
    /// two zeros are equal and a NaN operand gives a NaN.
    /// </summary>
    public static TBits Compare(TBits left, TBits right, bool signaling, ref DecimalContext context)
    {
        var status = context.Status;
        var comparison = DecimalArithmetic.Compare(
            TFormat.Unpack(left), TFormat.Unpack(right), signaling, ref status, out var nan);
        context.Status = status;

        if (comparison is null)
        {
            return TFormat.Pack(nan);
        }

        return comparison.Value < 0 ? NegativeOne : (comparison.Value > 0 ? One : Zero);
    }

    // Digit-wise logical operations, which read the coefficient as ones and zeros.

    public static TBits And(TBits left, TBits right, ref DecimalContext context) =>
        Run(DecimalLogical.And<TFormat>, left, right, ref context);

    public static TBits Or(TBits left, TBits right, ref DecimalContext context) =>
        Run(DecimalLogical.Or<TFormat>, left, right, ref context);

    public static TBits Xor(TBits left, TBits right, ref DecimalContext context) =>
        Run(DecimalLogical.Xor<TFormat>, left, right, ref context);

    public static TBits Invert(TBits value, ref DecimalContext context)
    {
        var status = context.Status;
        var result = DecimalLogical.Invert<TFormat>(TFormat.Unpack(value), ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    // Selecting, reshaping, and stepping.

    public static TBits Max(TBits left, TBits right, ref DecimalContext context)
    {
        var status = context.Status;
        var result = DecimalShaping.Max<TFormat>(
            TFormat.Unpack(left), TFormat.Unpack(right), context.Rounding, ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    public static TBits Min(TBits left, TBits right, ref DecimalContext context)
    {
        var status = context.Status;
        var result = DecimalShaping.Min<TFormat>(
            TFormat.Unpack(left), TFormat.Unpack(right), context.Rounding, ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    public static TBits MaxMagnitude(TBits left, TBits right, ref DecimalContext context)
    {
        var status = context.Status;
        var result = DecimalShaping.MaxMagnitude<TFormat>(
            TFormat.Unpack(left), TFormat.Unpack(right), context.Rounding, ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    public static TBits MinMagnitude(TBits left, TBits right, ref DecimalContext context)
    {
        var status = context.Status;
        var result = DecimalShaping.MinMagnitude<TFormat>(
            TFormat.Unpack(left), TFormat.Unpack(right), context.Rounding, ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    // The IEEE 754 selection operations. They differ from the specification's max and min
    // on one point: a NaN operand gives a NaN here, where the specification hands back the
    // number standing beside it. .NET's Max, Min, MaxMagnitude, and MinMagnitude mean
    // these; its MaxNumber, MinNumber, MaxMagnitudeNumber, and MinMagnitudeNumber mean the
    // ones above.

    public static TBits Maximum(TBits left, TBits right)
    {
        if (TryPropagateNaN(left, right, out var nan))
        {
            return nan;
        }

        var context = new DecimalContext();
        return Max(left, right, ref context);
    }

    public static TBits Minimum(TBits left, TBits right)
    {
        if (TryPropagateNaN(left, right, out var nan))
        {
            return nan;
        }

        var context = new DecimalContext();
        return Min(left, right, ref context);
    }

    public static TBits MaximumMagnitude(TBits left, TBits right)
    {
        if (TryPropagateNaN(left, right, out var nan))
        {
            return nan;
        }

        var context = new DecimalContext();
        return MaxMagnitude(left, right, ref context);
    }

    public static TBits MinimumMagnitude(TBits left, TBits right)
    {
        if (TryPropagateNaN(left, right, out var nan))
        {
            return nan;
        }

        var context = new DecimalContext();
        return MinMagnitude(left, right, ref context);
    }

    private static bool TryPropagateNaN(TBits left, TBits right, out TBits result)
    {
        var status = DecimalStatus.None;
        if (DecimalArithmetic.TryHandleNaN(TFormat.Unpack(left), TFormat.Unpack(right), ref status,
            out var nan))
        {
            result = TFormat.Pack(nan);
            return true;
        }

        result = default!;
        return false;
    }

    public static TBits LogB(TBits value, ref DecimalContext context)
    {
        var status = context.Status;
        var result = DecimalShaping.LogB<TFormat>(TFormat.Unpack(value), context.Rounding, ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    public static TBits ScaleB(TBits value, TBits scale, ref DecimalContext context)
    {
        var status = context.Status;
        var result = DecimalShaping.ScaleB<TFormat>(
            TFormat.Unpack(value), TFormat.Unpack(scale), context.Rounding, ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    public static TBits ScaleB(TBits value, int scale)
    {
        var context = new DecimalContext();
        return ScaleB(value, FromInteger((Int128)scale), ref context);
    }

    /// <summary>
    /// The adjusted exponent as an integer, which is what .NET's ILogB gives. The results
    /// off the end of the range are .NET's too rather than the specification's: a zero
    /// gives <see cref="int.MinValue"/> and a NaN or an infinity gives
    /// <see cref="int.MaxValue"/>, where logb gives -Infinity, a NaN, and +Infinity.
    /// </summary>
    public static int ILogB(TBits bits)
    {
        var value = TFormat.Unpack(bits);
        if (value.Kind != DecimalKind.Finite)
        {
            return int.MaxValue;
        }

        if (value.Coefficient == UInt128.Zero)
        {
            return int.MinValue;
        }

        return value.Exponent + DecimalRounder.CountDigits(value.Coefficient) - 1;
    }

    public static TBits Reduce(TBits value, ref DecimalContext context)
    {
        var status = context.Status;
        var result = DecimalShaping.Reduce<TFormat>(TFormat.Unpack(value), context.Rounding, ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    public static TBits Trim(TBits value) => TFormat.Pack(DecimalShaping.Trim(TFormat.Unpack(value)));

    public static TBits ToIntegral(TBits value, bool exact, ref DecimalContext context)
    {
        var status = context.Status;
        var result = DecimalShaping.ToIntegral<TFormat>(
            TFormat.Unpack(value), exact, context.Rounding, ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    public static TBits Quantize(TBits value, TBits pattern, ref DecimalContext context)
    {
        var status = context.Status;
        var result = DecimalShaping.Quantize<TFormat>(
            TFormat.Unpack(value), TFormat.Unpack(pattern), context.Rounding, ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    public static TBits Rotate(TBits value, TBits places, ref DecimalContext context)
    {
        var status = context.Status;
        var result = DecimalShaping.RotateOrShift<TFormat>(
            TFormat.Unpack(value), TFormat.Unpack(places), true, ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    public static TBits Shift(TBits value, TBits places, ref DecimalContext context)
    {
        var status = context.Status;
        var result = DecimalShaping.RotateOrShift<TFormat>(
            TFormat.Unpack(value), TFormat.Unpack(places), false, ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    public static TBits NextPlus(TBits value, ref DecimalContext context)
    {
        var status = context.Status;
        var result = DecimalShaping.NextPlus<TFormat>(TFormat.Unpack(value), ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    public static TBits NextMinus(TBits value, ref DecimalContext context)
    {
        var status = context.Status;
        var result = DecimalShaping.NextMinus<TFormat>(TFormat.Unpack(value), ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    public static TBits NextToward(TBits value, TBits target, ref DecimalContext context)
    {
        var status = context.Status;
        var result = DecimalShaping.NextToward<TFormat>(
            TFormat.Unpack(value), TFormat.Unpack(target), ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    // Numeric equality and ordering, which is what the generic math contracts mean by
    // == and <. This is not the total order: here 1.0 equals 1.00, negative zero equals
    // positive zero, and a NaN is unordered against everything including itself.

    /// <summary>
    /// The numeric comparison, or null when either side is a NaN and the two are therefore
    /// unordered.
    /// </summary>
    public static int? CompareNumeric(TBits left, TBits right)
    {
        var status = DecimalStatus.None;
        return DecimalArithmetic.Compare(TFormat.Unpack(left), TFormat.Unpack(right), false,
            ref status, out _);
    }

    public static bool EqualsNumeric(TBits left, TBits right)
    {
        // Equals differs from == on one point, and follows the rest of .NET in doing so:
        // a NaN equals a NaN, so that a value can be found in a collection.
        var first = TFormat.Unpack(left);
        var second = TFormat.Unpack(right);
        if (first.IsNaN || second.IsNaN)
        {
            return first.IsNaN && second.IsNaN;
        }

        return CompareNumeric(left, right) == 0;
    }

    /// <summary>
    /// Ordering for sorting, which needs every value placed somewhere: NaNs sort below
    /// everything else and equal each other, as they do for the built-in types.
    /// </summary>
    public static int CompareToNumeric(TBits left, TBits right)
    {
        var first = TFormat.Unpack(left);
        var second = TFormat.Unpack(right);

        if (first.IsNaN || second.IsNaN)
        {
            if (first.IsNaN && second.IsNaN)
            {
                return 0;
            }

            return first.IsNaN ? -1 : 1;
        }

        return CompareNumeric(left, right) ?? 0;
    }

    /// <summary>
    /// A hash of the value rather than of the bits, so that members of one cohort agree and
    /// the two zeros agree, as <see cref="EqualsNumeric"/> requires.
    /// </summary>
    public static int GetValueHashCode(TBits bits)
    {
        var value = TFormat.Unpack(bits);

        if (value.IsNaN)
        {
            return 0x7FC00000;
        }

        if (value.Kind == DecimalKind.Infinity)
        {
            return value.IsNegative ? int.MinValue : int.MaxValue;
        }

        if (value.Coefficient == UInt128.Zero)
        {
            return 0;
        }

        var reduced = DecimalShaping.Trim(value);
        while (reduced.Coefficient % 10 == UInt128.Zero)
        {
            reduced = new UnpackedDecimal<UInt128>(reduced.Kind, reduced.IsNegative,
                reduced.Exponent + 1, reduced.Coefficient / 10);
        }

        return HashCode.Combine(reduced.IsNegative, reduced.Exponent, reduced.Coefficient);
    }

    public static bool IsInteger(TBits bits)
    {
        var value = TFormat.Unpack(bits);
        if (value.Kind != DecimalKind.Finite)
        {
            return false;
        }

        return value.Exponent >= 0 || FractionIsZero(value);
    }

    public static bool IsEvenInteger(TBits bits) => HasParity(bits, 0);

    public static bool IsOddInteger(TBits bits) => HasParity(bits, 1);

    private static bool HasParity(TBits bits, uint wanted)
    {
        if (!IsInteger(bits))
        {
            return false;
        }

        var value = TFormat.Unpack(bits);
        if (value.Coefficient == UInt128.Zero)
        {
            return wanted == 0;
        }

        // A positive exponent means trailing zeros, so the value is even whatever the
        // coefficient's last digit is.
        if (value.Exponent > 0)
        {
            return wanted == 0;
        }

        var whole = value.Exponent == 0
            ? value.Coefficient
            : value.Coefficient / PowersOfTen.UInt128(-value.Exponent);

        return (uint)(whole % 2) == wanted;
    }

    private static bool FractionIsZero(UnpackedDecimal<UInt128> value)
    {
        var drop = -value.Exponent;
        if (drop > PowersOfTen.MaxUInt128Power)
        {
            return value.Coefficient == UInt128.Zero;
        }

        var power = PowersOfTen.UInt128(drop);
        return value.Coefficient % power == UInt128.Zero;
    }

    /// <summary>Rounds to a given number of fractional digits, for the .NET Round overloads.</summary>
    public static TBits Round(TBits bits, int digits, DecimalRounding rounding)
    {
        // A NaN or an infinity has no fractional part to round away. Without this the
        // rescale below reads a NaN's payload as a coefficient and hands back a finite
        // number: rounding a NaN to two places gave 0.00.
        if (!IsFinite(bits))
        {
            return bits;
        }

        // Nothing to discard: the value already sits at or above the place asked for.
        // Rescaling it down to that place would pad the coefficient with zeros, which
        // changes the quantum and not the value -- that is what quantize is for.
        var value = TFormat.Unpack(bits);
        if (value.Exponent >= -digits)
        {
            return bits;
        }

        var status = DecimalStatus.None;
        var result = DecimalShaping.Rescale<TFormat>(value, -digits, rounding, ref status);

        // Asking for more digits than the value has is not an error, it just leaves it be.
        return (status & DecimalStatus.InvalidOperation) != 0 ? bits : TFormat.Pack(result);
    }

    public static TBits SquareRoot(TBits value, ref DecimalContext context)
    {
        var status = context.Status;
        var result = DecimalMath.SquareRoot<TFormat>(TFormat.Unpack(value), context.Rounding, ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    public static TBits Exp(TBits value, ref DecimalContext context)
    {
        var status = context.Status;
        var result = DecimalMath.Exp<TFormat>(TFormat.Unpack(value), context.Rounding, ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    public static TBits Log(TBits value, ref DecimalContext context)
    {
        var status = context.Status;
        var result = DecimalMath.Log<TFormat>(TFormat.Unpack(value), context.Rounding, ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    public static TBits Log10(TBits value, ref DecimalContext context)
    {
        var status = context.Status;
        var result = DecimalMath.Log10<TFormat>(TFormat.Unpack(value), context.Rounding, ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    public static TBits Power(TBits left, TBits right, ref DecimalContext context)
    {
        var status = context.Status;
        var result = DecimalMath.Power<TFormat>(TFormat.Unpack(left), TFormat.Unpack(right),
            context.Rounding, ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    public static TBits Exp2(TBits value, ref DecimalContext context) =>
        Apply(DecimalMath.Exp2<TFormat>, value, ref context);

    public static TBits Exp10(TBits value, ref DecimalContext context) =>
        Apply(DecimalMath.Exp10<TFormat>, value, ref context);

    public static TBits ExpMinusOne(TBits value, ref DecimalContext context) =>
        Apply(DecimalMath.ExpMinusOne<TFormat>, value, ref context);

    public static TBits Exp2MinusOne(TBits value, ref DecimalContext context) =>
        Apply(DecimalMath.Exp2MinusOne<TFormat>, value, ref context);

    public static TBits Exp10MinusOne(TBits value, ref DecimalContext context) =>
        Apply(DecimalMath.Exp10MinusOne<TFormat>, value, ref context);

    public static TBits Log2(TBits value, ref DecimalContext context) =>
        Apply(DecimalMath.Log2<TFormat>, value, ref context);

    public static TBits LogPlusOne(TBits value, ref DecimalContext context) =>
        Apply(DecimalMath.LogPlusOne<TFormat>, value, ref context);

    public static TBits Log2PlusOne(TBits value, ref DecimalContext context) =>
        Apply(DecimalMath.Log2PlusOne<TFormat>, value, ref context);

    public static TBits Log10PlusOne(TBits value, ref DecimalContext context) =>
        Apply(DecimalMath.Log10PlusOne<TFormat>, value, ref context);

    public static TBits Cbrt(TBits value, ref DecimalContext context) =>
        Apply(DecimalMath.Cbrt<TFormat>, value, ref context);

    public static TBits LogInBase(TBits value, TBits newBase, ref DecimalContext context) =>
        Apply(DecimalMath.LogInBase<TFormat>, value, newBase, ref context);

    public static TBits Hypot(TBits left, TBits right, ref DecimalContext context) =>
        Apply(DecimalMath.Hypot<TFormat>, left, right, ref context);

    public static TBits RootN(TBits value, int degree, ref DecimalContext context)
    {
        var status = context.Status;
        var result = DecimalMath.RootN<TFormat>(TFormat.Unpack(value), degree, context.Rounding,
            ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    /// <summary>The conversions the cast operators are written on.</summary>
    public static TBits FromInteger(Int128 value) =>
        DecimalConversions<TFormat, TBits>.FromInteger(value);

    public static TBits FromInteger(UInt128 value) =>
        DecimalConversions<TFormat, TBits>.FromInteger(value);

    public static TBits FromBinary(double value) =>
        DecimalConversions<TFormat, TBits>.FromBinary(value);

    public static TBits FromBinary(float value) =>
        DecimalConversions<TFormat, TBits>.FromBinary(value);

    public static TBits FromBinary(Half value) =>
        DecimalConversions<TFormat, TBits>.FromBinary(value);

    public static TBits FromBinary(double value, BinaryConversion conversion) =>
        DecimalConversions<TFormat, TBits>.FromBinary(value, conversion);

    public static TBits FromBinary(float value, BinaryConversion conversion) =>
        DecimalConversions<TFormat, TBits>.FromBinary(value, conversion);

    public static TBits FromBinary(Half value, BinaryConversion conversion) =>
        DecimalConversions<TFormat, TBits>.FromBinary(value, conversion);

    public static BigInteger ToInteger(TBits bits) =>
        DecimalConversions<TFormat, TBits>.ToInteger(bits);

    public static double ToBinary(TBits bits) =>
        DecimalConversions<TFormat, TBits>.ToBinary(bits);

    public static TBits Special(DecimalKind kind, bool isNegative) =>
        TFormat.Pack(new UnpackedDecimal<UInt128>(kind, isNegative, 0, UInt128.Zero));

    private delegate UnpackedDecimal<UInt128> ElementaryFunction(UnpackedDecimal<UInt128> value,
        DecimalRounding rounding, ref DecimalStatus status);

    private delegate UnpackedDecimal<UInt128> ElementaryFunctionOfTwo(
        UnpackedDecimal<UInt128> left, UnpackedDecimal<UInt128> right, DecimalRounding rounding,
        ref DecimalStatus status);

    private static TBits Apply(ElementaryFunction function, TBits value, ref DecimalContext context)
    {
        var status = context.Status;
        var result = function(TFormat.Unpack(value), context.Rounding, ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    private static TBits Apply(ElementaryFunctionOfTwo function, TBits left, TBits right,
        ref DecimalContext context)
    {
        var status = context.Status;
        var result = function(TFormat.Unpack(left), TFormat.Unpack(right), context.Rounding,
            ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    private delegate UnpackedDecimal<UInt128> LogicalOperation(UnpackedDecimal<UInt128> left,
        UnpackedDecimal<UInt128> right, ref DecimalStatus status);

    private static TBits Run(LogicalOperation operation, TBits left, TBits right,
        ref DecimalContext context)
    {
        var status = context.Status;
        var result = operation(TFormat.Unpack(left), TFormat.Unpack(right), ref status);
        context.Status = status;
        return TFormat.Pack(result);
    }

    private static TBits FromParts(bool isNegative, int exponent, UInt128 coefficient) =>
        TFormat.Pack(new UnpackedDecimal<UInt128>(DecimalKind.Finite, isNegative, exponent, coefficient));
}
