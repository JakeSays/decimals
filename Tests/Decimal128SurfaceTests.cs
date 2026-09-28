// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Numerics;
using Decimals.Internal;

namespace Decimals.Tests;

/// <summary>
/// The members of <see cref="Decimal128"/> a .NET caller reaches for by name: rounding,
/// selection, sign and clamp, the constants, the generic math hooks, and the conversions
/// that have to refuse a value.
/// </summary>
public class Decimal128SurfaceTests
{
    [Theory]
    [InlineData("2.7", "3", "2", "2", "3")]
    [InlineData("-2.7", "-2", "-3", "-2", "-3")]
    [InlineData("2.5", "3", "2", "2", "2")]
    [InlineData("-2.5", "-2", "-3", "-2", "-2")]
    [InlineData("3.5", "4", "3", "3", "4")]
    [InlineData("2", "2", "2", "2", "2")]
    [InlineData("-0.4", "-0", "-1", "-0", "-0")]
    public void RoundingToAnInteger(string text, string ceiling, string floor, string truncate, string round)
    {
        var value = Decimal128.Parse(text);

        Assert.Equal(ceiling, Decimal128.Ceiling(value).ToString());
        Assert.Equal(floor, Decimal128.Floor(value).ToString());
        Assert.Equal(truncate, Decimal128.Truncate(value).ToString());
        Assert.Equal(round, Decimal128.Round(value).ToString());
    }

    [Fact]
    public void RoundingToPlaces()
    {
        var value = Decimal128.Parse("2.345");

        Assert.Equal("2.34", Decimal128.Round(value, 2).ToString());
        Assert.Equal("2.35", Decimal128.Round(value, 2, MidpointRounding.AwayFromZero).ToString());
        Assert.Equal("2", Decimal128.Round(value, MidpointRounding.ToZero).ToString());

        // Asking for more places than the value carries leaves it alone, quantum and all.
        Assert.Equal("2.345", Decimal128.Round(value, 8).ToString());

        Assert.True(Decimal128.IsNaN(Decimal128.Round(Decimal128.NaN, 2)));
        Assert.Equal(Decimal128.PositiveInfinity, Decimal128.Round(Decimal128.PositiveInfinity, 2));
        Assert.Equal(Decimal128.Parse("1E+300"), Decimal128.Truncate(Decimal128.Parse("1E+300")));
    }

    [Theory]
    [InlineData("5", 1)]
    [InlineData("-5", -1)]
    [InlineData("0", 0)]
    [InlineData("-0", 0)]
    [InlineData("0.00", 0)]
    [InlineData("Infinity", 1)]
    [InlineData("-Infinity", -1)]
    public void Sign(string text, int expected)
    {
        Assert.Equal(expected, Decimal128.Sign(Decimal128.Parse(text)));
    }

    [Fact]
    public void SignOfANaNThrows()
    {
        Assert.Throws<ArithmeticException>(() => Decimal128.Sign(Decimal128.NaN));
    }

    [Fact]
    public void Clamp()
    {
        var low = Decimal128.Parse("0");
        var high = Decimal128.Parse("10");

        Assert.Equal(Decimal128.Parse("5"), Decimal128.Clamp(Decimal128.Parse("5"), low, high));
        Assert.Equal(high, Decimal128.Clamp(Decimal128.Parse("50"), low, high));
        Assert.Equal(low, Decimal128.Clamp(Decimal128.Parse("-50"), low, high));
        Assert.True(Decimal128.IsNaN(Decimal128.Clamp(Decimal128.NaN, low, high)));
        Assert.Throws<ArgumentException>(() => Decimal128.Clamp(low, high, low));
    }

    [Fact]
    public void SelectionAndNaN()
    {
        var one = Decimal128.One;

        Assert.True(Decimal128.IsNaN(Decimal128.Max(Decimal128.NaN, one)));
        Assert.True(Decimal128.IsNaN(Decimal128.Min(one, Decimal128.NaN)));
        Assert.Equal(one, Decimal128.MaxNumber(Decimal128.NaN, one));
        Assert.Equal(one, Decimal128.MinNumber(one, Decimal128.NaN));
        Assert.Equal(Decimal128.Parse("-3"), Decimal128.MaxMagnitudeNumber(Decimal128.Parse("-3"), Decimal128.Parse("2")));
        Assert.Equal(Decimal128.Parse("2"), Decimal128.MinMagnitudeNumber(Decimal128.Parse("-3"), Decimal128.Parse("2")));

        // A signaling NaN is invalid under every reading and comes back quiet.
        var signaling = Decimal128.Parse("sNaN5");
        Assert.Equal("NaN5", Decimal128.Max(signaling, one).ToString());
    }

    [Fact]
    public void ConstantsAreTheCorrectlyRoundedValues()
    {
        Assert.Equal("2.718281828459045235360287471352662", Decimal128.E.ToString());
        Assert.Equal("3.141592653589793238462643383279503", Decimal128.Pi.ToString());
        Assert.Equal("6.283185307179586476925286766559006", Decimal128.Tau.ToString());
        Assert.Equal(Decimal128.E, Decimal128.Exp(Decimal128.One));
        Assert.Equal("9.999999999999999999999999999999999E+6144", Decimal128.MaxValue.ToString());
        Assert.Equal("-9.999999999999999999999999999999999E+6144", Decimal128.MinValue.ToString());
        Assert.Equal("1E-6176", Decimal128.Epsilon.ToString());
        Assert.Equal(10, Decimal128.Radix);
    }

    [Fact]
    public void EqualityIsNumericAndHashingFollowsIt()
    {
        var one = Decimal128.Parse("1");
        var alsoOne = Decimal128.Parse("1.00");
        var scaled = Decimal128.Parse("100E-2");

        Assert.True(one == alsoOne);
        Assert.True(one.Equals(alsoOne));
        Assert.True(one.Equals(scaled));
        Assert.Equal(one.GetHashCode(), alsoOne.GetHashCode());
        Assert.Equal(one.GetHashCode(), scaled.GetHashCode());
        Assert.NotEqual(one.ToBits(), alsoOne.ToBits());
        Assert.True(Decimal128.CompareTotal(one, alsoOne) > 0);

        Assert.True(Decimal128.Zero == Decimal128.NegativeZero);
        Assert.Equal(Decimal128.Zero.GetHashCode(), Decimal128.NegativeZero.GetHashCode());

        Assert.False(Decimal128.NaN == Decimal128.NaN);
        Assert.True(Decimal128.NaN.Equals(Decimal128.NaN));
        Assert.False(Decimal128.NaN < one);
        Assert.False(Decimal128.NaN >= one);
        Assert.True(Decimal128.NaN.CompareTo(one) < 0);
        Assert.Equal(0, Decimal128.NaN.CompareTo(Decimal128.Parse("sNaN3")));

        var set = new HashSet<Decimal128> { one, alsoOne, scaled, Decimal128.Zero, Decimal128.NegativeZero };
        Assert.Equal(2, set.Count);
    }

    [Fact]
    public void GenericMathWorks()
    {
        Assert.Equal("2.5", Mean<Decimal128>([Decimal128.Parse("1"), Decimal128.Parse("2"), Decimal128.Parse("3"), Decimal128.Parse("4")]).ToString());
        Assert.Equal(Decimal128.Parse("42"), Decimal128.CreateChecked(42));
        Assert.Equal(Decimal128.Parse("42"), Decimal128.CreateChecked(42.0));
        Assert.Equal(Decimal128.Parse("0.1"), Decimal128.CreateChecked(0.1));
        Assert.Equal(Decimal128.Parse("1.5"), Decimal128.CreateChecked(1.5m));
        Assert.Equal(Decimal128.Parse("1234567890123456789012345678901"), Decimal128.CreateChecked(BigInteger.Parse("1234567890123456789012345678901")));
        Assert.Equal(Decimal128.Parse("123456789012345678"), Decimal128.CreateSaturating((Int128)123456789012345678));
        Assert.Equal(42, int.CreateChecked(Decimal128.Parse("42.9")));
        Assert.Equal(-42, int.CreateChecked(Decimal128.Parse("-42.9")));
        Assert.Equal(int.MaxValue, int.CreateSaturating(Decimal128.Parse("1E+30")));
        Assert.Equal(long.MinValue, long.CreateSaturating(Decimal128.NegativeInfinity));
        Assert.Equal(0, int.CreateSaturating(Decimal128.NaN));
        Assert.Equal(1.5, double.CreateChecked(Decimal128.Parse("1.5")));
        Assert.Equal(Int128.Parse("100000000000000000000"), Int128.CreateSaturating(Decimal128.Parse("1E+20")));
        Assert.Equal(Int128.Parse("-1234567890123456789012345678901234"),
            Int128.CreateSaturating(Decimal128.Parse("-1234567890123456789012345678901234.5")));

        Assert.Equal(UInt128.MaxValue, UInt128.CreateSaturating(Decimal128.Parse("1E+40")));
        Assert.Equal(BigInteger.Parse("1000000000000000000000000000000000000000"),
            BigInteger.CreateChecked(Decimal128.Parse("1E+39")));
    }

    [Fact]
    public void IntegerConversionsRefuseWhatDoesNotFit()
    {
        Assert.Equal(42, (int)Decimal128.Parse("42.9"));
        Assert.Equal(-42, (int)Decimal128.Parse("-42.9"));
        Assert.Equal(long.MinValue, (long)Decimal128.Parse("-9223372036854775808"));
        Assert.Equal(long.MaxValue, (long)Decimal128.Parse("9223372036854775807.999"));
        Assert.Equal(ulong.MaxValue, (ulong)Decimal128.Parse("18446744073709551615"));
        Assert.Equal(0, (int)Decimal128.Parse("0.999"));
        Assert.Equal(1000, (int)Decimal128.Parse("1E+3"));

        Assert.Throws<OverflowException>(() => (int)Decimal128.Parse("3000000000"));
        Assert.Throws<OverflowException>(() => (long)Decimal128.Parse("9223372036854775808"));
        Assert.Throws<OverflowException>(() => (ulong)Decimal128.Parse("18446744073709551616"));
        Assert.Throws<OverflowException>(() => (ulong)Decimal128.Parse("-1"));
        Assert.Throws<OverflowException>(() => (byte)Decimal128.Parse("256"));
        Assert.Throws<OverflowException>(() => (int)Decimal128.NaN);
        Assert.Throws<OverflowException>(() => (int)Decimal128.PositiveInfinity);
        Assert.Throws<OverflowException>(() => (ulong)Decimal128.Parse("1E+20"));
        Assert.Throws<OverflowException>(() => (long)Decimal128.Parse("1E+40"));
    }

    [Fact]
    public void BinaryConversionsHaveTwoReadings()
    {
        Assert.Equal("0.1", ((Decimal128)0.1).ToString());
        Assert.Equal("0.1", ((Decimal128)0.1f).ToString());
        Assert.Equal("0.1000000000000000055511151231257827",
            Decimal128.FromBinary(0.1, Decimal128BinaryConversion.ExactValue).ToString());

        Assert.Equal("2.5", Decimal128.FromBinary(2.5, Decimal128BinaryConversion.ExactValue).ToString());
        Assert.Equal("0.2999999999999999888977697537484346",
            Decimal128.FromBinary(0.3, Decimal128BinaryConversion.ExactValue).ToString());

        Assert.Equal("0.0009765625", Decimal128.FromBinary(0.0009765625, Decimal128BinaryConversion.ExactValue).ToString());
        Assert.Equal("1.100000000000000088817841970012523",
            Decimal128.FromBinary(1.1, Decimal128BinaryConversion.ExactValue).ToString());

        Assert.Equal(0.1, (double)Decimal128.Parse("0.1"));
        Assert.True(double.IsNaN((double)Decimal128.NaN));
        Assert.Equal(double.PositiveInfinity, (double)Decimal128.Parse("1E+6144"));
        Assert.Equal(0.0, (double)Decimal128.Parse("1E-6144"));
    }

    [Fact]
    public void SpecialsHaveCanonicalForms()
    {
        var garbage = Decimal128.FromBits(new UInt128(0x7800000000000000, 0x123));
        Assert.False(Decimal128.IsCanonical(garbage));
        Assert.True(Decimal128.IsInfinity(garbage));
        Assert.Equal(Decimal128.PositiveInfinity.ToBits(), Decimal128.Canonical(garbage).ToBits());

        // A coefficient of 10^34, one past the largest, in the plain form.
        var wideCoefficient = Decimal128.FromBits(new UInt128((6176UL << 49) | 0x0001ED09BEAD87C0, 0x378D8E6400000000));
        Assert.False(Decimal128.IsCanonical(wideCoefficient));
        Assert.True(Decimal128.IsZero(wideCoefficient));

        // The largest coefficient itself is canonical.
        var largest = Decimal128.FromBits(new UInt128((6176UL << 49) | 0x0001ED09BEAD87C0, 0x378D8E63FFFFFFFF));
        Assert.True(Decimal128.IsCanonical(largest));
        Assert.Equal("9999999999999999999999999999999999", largest.ToString());

        // The long form always carries a coefficient past the largest, so it is a zero.
        var longForm = Decimal128.FromBits(new UInt128(0x6000000000000000 | (6176UL << 47), 1));
        Assert.False(Decimal128.IsCanonical(longForm));
        Assert.True(Decimal128.IsZero(longForm));
        Assert.Equal("0", longForm.ToString());

        Assert.True(Decimal128.IsCanonical(Decimal128.Parse("1.5")));
        Assert.True(Decimal128.IsCanonical(Decimal128.Parse("sNaN12")));
        Assert.Equal(Decimal128.Parse("1.5").ToBits(), Decimal128.FromDpdBits(Decimal128.Parse("1.5").ToDpdBits()).ToBits());
    }

    [Fact]
    public void ConditionsDoNotLeakBetweenOperations()
    {
        // A subnormal result underflows only when it is itself inexact; an earlier
        // operation's inexactness in the same context must not make it so.
        var context = new Decimal128Context();
        Decimal128.Divide(Decimal128.One, Decimal128.Parse("3"), ref context);
        Assert.True(context.HasRaised(Decimal128Status.Inexact));

        var tiny = Decimal128.Add(Decimal128.Parse("1E-6176"), Decimal128.Parse("1E-6176"), ref context);
        Assert.Equal("2E-6176", tiny.ToString());
        Assert.True(context.HasRaised(Decimal128Status.Subnormal));
        Assert.False(context.HasRaised(Decimal128Status.Underflow));
    }

    /// <summary>
    /// A base of zero or infinity makes the divisor infinite, so the quotient is a zero at
    /// an exponent far below the format; it has to come back clamped rather than packed as
    /// whatever bits that exponent makes.
    /// </summary>
    [Fact]
    public void LogarithmInADegenerateBaseIsAClampedZero()
    {
        var context = new Decimal128Context();
        Assert.Equal("-0E-6176", Decimal128.Log(Decimal128.Parse("8"), Decimal128.Zero, ref context).ToString());
        Assert.True(context.HasRaised(Decimal128Status.Clamped));

        Assert.Equal("0E-6176", Decimal128.Log(Decimal128.Parse("8"), Decimal128.PositiveInfinity).ToString());

        // A base of one makes the divisor zero, which is a division by zero rather than an
        // invalid operation: the quotient is an infinity.
        var byOne = new Decimal128Context();
        Assert.True(Decimal128.IsPositiveInfinity(Decimal128.Log(Decimal128.Parse("8"), Decimal128.One, ref byOne)));
        Assert.True(byOne.HasRaised(Decimal128Status.DivisionByZero));

        Assert.Equal("3", Decimal128.Log(Decimal128.Parse("8"), Decimal128.Parse("2")).ToString());
    }

    /// <summary>
    /// The integer overload of ScaleB applies the same limit as the operand form: a shift
    /// no operand could express is invalid, not an overflow.
    /// </summary>
    [Fact]
    public void ScaleByAnIntegerHonorsTheOperandLimit()
    {
        Assert.Equal("1E+10", Decimal128.ScaleB(Decimal128.One, 10).ToString());
        Assert.Equal("Infinity", Decimal128.ScaleB(Decimal128.One, 12356).ToString());
        Assert.True(Decimal128.IsNaN(Decimal128.ScaleB(Decimal128.One, 12357)));
        Assert.True(Decimal128.IsNaN(Decimal128.ScaleB(Decimal128.One, -12357)));
        Assert.Equal("NaN7", Decimal128.ScaleB(Decimal128.Parse("NaN7"), 12357).ToString());
    }

    [Fact]
    public void ExponentAndSignificandBytes()
    {
        IFloatingPoint<Decimal128> value = Decimal128.Parse("1.5");
        Span<byte> exponent = stackalloc byte[4];
        Span<byte> significand = stackalloc byte[16];

        Assert.True(value.TryWriteExponentLittleEndian(exponent, out var exponentWritten));
        Assert.Equal(4, exponentWritten);
        Assert.Equal(-1, BitConverter.ToInt32(exponent));

        Assert.True(value.TryWriteSignificandLittleEndian(significand, out var significandWritten));
        Assert.Equal(16, significandWritten);
        Assert.Equal(15UL, BitConverter.ToUInt64(significand));
        Assert.Equal(0UL, BitConverter.ToUInt64(significand[8..]));

        IFloatingPoint<Decimal128> wide = Decimal128.Parse("1234567890123456789012345678901234");
        Assert.True(wide.TryWriteSignificandBigEndian(significand, out significandWritten));
        Assert.Equal(UInt128.Parse("1234567890123456789012345678901234"), BinaryPrimitivesUInt128(significand));
    }

    /// <summary>
    /// Every path of the wide arithmetic on a few hand-picked operands, each checked
    /// against a value worked out by hand. The square of thirty-four nines is
    /// 10^68 - 2*10^34 + 1, and two to the sixty-fourth squared is two to the hundred
    /// and twenty-eighth, whose thirty-nine digits round at the thirty-fifth.
    /// </summary>
    [Theory]
    [InlineData("9999999999999999999999999999999999", "9999999999999999999999999999999999", "9.999999999999999999999999999999998E+67")]
    [InlineData("18446744073709551616", "18446744073709551616", "3.402823669209384634633746074317682E+38")]
    [InlineData("18446744073709551615", "18446744073709551615", "3.402823669209384634264811192843491E+38")]
    [InlineData("1E-6176", "1E-6176", "0E-6176")]
    [InlineData("1234567890123456789012345678901234", "1E-6176", "1.234567890123456789012345678901234E-6143")]
    [InlineData("1000000000000000000000000000000000", "1000000000000000000000000000000000", "1.000000000000000000000000000000000E+66")]
    public void WideProductsRoundCorrectly(string left, string right, string expected)
    {
        Assert.Equal(expected, (Decimal128.Parse(left) * Decimal128.Parse(right)).ToString());
    }

    /// <summary>
    /// An inexact quotient carries thirty-four digits at whatever exponent that takes; an
    /// exact one gives back the trailing zeros toward the ideal exponent.
    /// </summary>
    [Theory]
    [InlineData("1", "3", "0.3333333333333333333333333333333333")]
    [InlineData("2", "3", "0.6666666666666666666666666666666667")]
    [InlineData("9999999999999999999999999999999999", "9999999999999999999999999999999998", "1.000000000000000000000000000000000")]
    [InlineData("1234567890123456789012345678901234", "1234567890123456789012345678901233", "1.000000000000000000000000000000001")]
    [InlineData("1E+6144", "1E-6176", "Infinity")]
    [InlineData("12345678901234567890", "12345678901234567890", "1")]
    [InlineData("1E+34", "7", "1428571428571428571428571428571429")]
    [InlineData("1E+34", "8", "1.25E+33")]
    public void WideQuotientsRoundCorrectly(string left, string right, string expected)
    {
        Assert.Equal(expected, (Decimal128.Parse(left) / Decimal128.Parse(right)).ToString());
    }

    /// <summary>
    /// An exact root is shortened toward half the operand's exponent; an inexact one keeps
    /// thirty-four digits at the exponent the scaling left.
    /// </summary>
    [Theory]
    [InlineData("2", "1.414213562373095048801688724209698")]
    [InlineData("9999999999999999999999999999999999", "99999999999999999.99999999999999999")]
    [InlineData("1E+33", "31622776601683793.31998893544432719")]
    [InlineData("1000000000000000000000000000000000", "31622776601683793.31998893544432719")]
    [InlineData("1.21", "1.1")]
    [InlineData("100", "10")]
    [InlineData("1E-6176", "1E-3088")]
    [InlineData("1E-6175", "3.162277660168379331998893544432719E-3088")]
    [InlineData("1E+32", "1E+16")]
    public void SquareRootsRoundCorrectly(string text, string expected)
    {
        Assert.Equal(expected, Decimal128.Sqrt(Decimal128.Parse(text)).ToString());
    }

    /// <summary>
    /// The product is kept exact until the addend has been taken in: the square of
    /// thirty-four nines less its own rounded value leaves the one that rounding took
    /// away, and two to the hundred and twenty-eighth less its rounded value leaves the
    /// low digits the rounding dropped.
    /// </summary>
    [Theory]
    [InlineData("9999999999999999999999999999999999", "9999999999999999999999999999999999", "-9.999999999999999999999999999999998E+67", "1")]
    [InlineData("18446744073709551616", "18446744073709551616", "-340282366920938463463374607431768211456", "11456")]
    [InlineData("18446744073709551616", "18446744073709551616", "1E-6176", "3.402823669209384634633746074317682E+38")]
    [InlineData("1E+20", "1E+20", "-1E+40", "0E+40")]
    [InlineData("1000000000000000000000000000000000", "1000000000000000000000000000000000", "1", "1.000000000000000000000000000000000E+66")]
    public void FusedMultiplyAddIsRoundedOnce(string left, string right, string addend, string expected)
    {
        var value = Decimal128.FusedMultiplyAdd(Decimal128.Parse(left), Decimal128.Parse(right), Decimal128.Parse(addend));
        Assert.Equal(expected, value.ToString());
    }

    private static UInt128 BinaryPrimitivesUInt128(ReadOnlySpan<byte> bigEndian)
    {
        var high = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(bigEndian);
        var low = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(bigEndian[8..]);
        return new UInt128(high, low);
    }

    private static T Mean<T>(ReadOnlySpan<T> values)
        where T : INumber<T>
    {
        var total = T.Zero;
        foreach (var value in values)
        {
            total += value;
        }

        return total / T.CreateChecked(values.Length);
    }
}
