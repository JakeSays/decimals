// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace Decimals.Tests;

/// <summary>
/// The two grammars and the line between them. Text with no culture attached is the
/// specification's and never moves; text with a culture attached reads and writes that
/// culture's separators, signs, and symbols.
/// </summary>
public class CultureTextTests
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>
    /// A culture built rather than looked up, so the awkward cases are pinned down whatever
    /// the platform's locale data says: a two-character decimal separator, a non-ASCII group
    /// separator and minus sign, and words of its own for the infinities and NaN.
    /// </summary>
    private static NumberFormatInfo Unusual()
    {
        var format = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        format.NumberDecimalSeparator = "::";
        format.NumberGroupSeparator = " ";
        format.NegativeSign = "−";
        format.NaNSymbol = "kein";
        format.PositiveInfinitySymbol = "unendlich";
        format.NegativeInfinitySymbol = "−unendlich";
        return format;
    }

    [Fact]
    public void ParsesACulturesSeparators()
    {
        Assert.Equal(Decimal64.Parse("1234.5"), Decimal64.Parse("1.234,5", German));
        Assert.Equal(Decimal64.Parse("1234.5"), Decimal64.Parse("1234,5", German));
        Assert.Equal(Decimal64.Parse("-0.25"), Decimal64.Parse("-0,25", German));

        // The French group separator is a narrow no-break space in current locale data,
        // which is exactly the kind of thing a byte-wise reading would miss.
        var grouped = 1234.5.ToString("N1", French);
        Assert.Equal(Decimal64.Parse("1234.5"), Decimal64.Parse(grouped, French));
    }

    [Fact]
    public void ParsesACulturesSigns()
    {
        var unusual = Unusual();

        Assert.Equal(Decimal64.Parse("-5"), Decimal64.Parse("−5", unusual));
        Assert.Equal(Decimal64.Parse("1234.5"), Decimal64.Parse("1 234::5", unusual));

        // The exponent carries the culture's sign too.
        Assert.Equal(Decimal64.Parse("1.5E-20"), Decimal64.Parse("1::5E−20", unusual));
        Assert.Equal(Decimal64.Parse("1.5E+20"), Decimal64.Parse("1::5E+20", unusual));
    }

    [Fact]
    public void ParsesACulturesSymbols()
    {
        var unusual = Unusual();

        Assert.Equal(Decimal64.PositiveInfinity, Decimal64.Parse("unendlich", unusual));
        Assert.Equal(Decimal64.NegativeInfinity, Decimal64.Parse("−unendlich", unusual));
        Assert.True(Decimal64.IsNaN(Decimal64.Parse("kein", unusual)));

        Assert.Equal(Decimal64.NegativeInfinity, Decimal64.Parse("-∞", German));
    }

    /// <summary>
    /// A signaling NaN and a NaN payload have no culture spelling, and neither has the
    /// specification's word for an infinity. All of them still read under a culture, since
    /// nothing else could be meant by them.
    /// </summary>
    [Fact]
    public void TheSpecificationsOwnSpellingsStillParseUnderACulture()
    {
        Assert.Equal(Decimal64.PositiveInfinity, Decimal64.Parse("Infinity", German));
        Assert.Equal(Decimal64.PositiveInfinity, Decimal64.Parse("inf", German));
        Assert.True(Decimal64.IsNaN(Decimal64.Parse("NaN255", German)));
        Assert.Equal(DecimalClass.SignalingNaN, Decimal64.Class(Decimal64.Parse("sNaN", German)));
        Assert.Equal("NaN255", Decimal64.Parse("NaN255", German).ToString());
    }

    [Fact]
    public void FormatsUnderACulture()
    {
        var value = Decimal64.Parse("1234.5");
        var unusual = Unusual();

        Assert.Equal("1234,5", value.ToString("G", German));
        Assert.Equal("1234::5", value.ToString("G", unusual));
        Assert.Equal("−1234::5", Decimal64.Parse("-1234.5").ToString("G", unusual));
        Assert.Equal("unendlich", Decimal64.PositiveInfinity.ToString("G", unusual));
        Assert.Equal("−unendlich", Decimal64.NegativeInfinity.ToString("G", unusual));
        Assert.Equal("kein", Decimal64.NaN.ToString("G", unusual));

        // The exponent's sign is the culture's, and the engineering form follows the same
        // rules as the scientific one.
        Assert.Equal("1::5E−20", Decimal64.Parse("1.5E-20").ToString("G", unusual));
        Assert.Equal("150E+18", Decimal64.Parse("1.5E+20").ToString("E", German));
        Assert.Equal("15E−21", Decimal64.Parse("1.5E-20").ToString("E", unusual));
    }

    /// <summary>
    /// The scientific form has no grouping, so a value written under a culture reads back
    /// under that same culture -- which is the whole reason the "G" output moved.
    /// </summary>
    [Theory]
    [InlineData("1234.5")]
    [InlineData("-1234.5")]
    [InlineData("0.000001")]
    [InlineData("1E-7")]
    [InlineData("-1.5E+20")]
    [InlineData("1.00")]
    [InlineData("0")]
    [InlineData("-0")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("NaN")]
    public void RoundTripsUnderEveryCulture(string text)
    {
        var value = Decimal64.Parse(text);

        foreach (var provider in Providers())
        {
            var written = value.ToString("G", provider);
            Assert.True(Decimal64.TryParse(written, provider, out var read),
                $"'{written}' did not read back");
            Assert.Equal(value.ToBits(), read.ToBits());
        }
    }

    [Fact]
    public void RoundTripsOnTheOtherTwoWidths()
    {
        foreach (var provider in Providers())
        {
            var narrow = Decimal32.Parse("-1.5E-20");
            Assert.Equal(narrow.ToBits(),
                Decimal32.Parse(narrow.ToString("G", provider), provider).ToBits());

            var wide = Decimal128.Parse("-1.234567890123456789012345678901234E+400");
            Assert.Equal(wide.ToBits(),
                Decimal128.Parse(wide.ToString("G", provider), provider).ToBits());
        }
    }

    [Theory]
    [InlineData("1,234.5", NumberStyles.Float, false)]
    [InlineData("1,234.5", NumberStyles.Float | NumberStyles.AllowThousands, true)]
    [InlineData("-5", NumberStyles.None | NumberStyles.AllowDecimalPoint, false)]
    [InlineData("-5", NumberStyles.AllowLeadingSign, true)]
    [InlineData("5-", NumberStyles.AllowLeadingSign, false)]
    [InlineData("5-", NumberStyles.AllowTrailingSign, true)]
    [InlineData("1.5", NumberStyles.AllowDecimalPoint, true)]
    [InlineData("1.5", NumberStyles.Integer, false)]
    [InlineData("1E5", NumberStyles.AllowExponent, true)]
    [InlineData("1E5", NumberStyles.Integer, false)]
    [InlineData(" 5 ", NumberStyles.Float, true)]
    [InlineData(" 5 ", NumberStyles.AllowLeadingSign, false)]
    [InlineData("(5)", NumberStyles.Float, false)]
    [InlineData("(5)", NumberStyles.Float | NumberStyles.AllowParentheses, true)]
    public void StylesDecideWhatIsAccepted(string text, NumberStyles styles, bool accepted)
    {
        Assert.Equal(accepted,
            Decimal64.TryParse(text, styles, CultureInfo.InvariantCulture, out _));
    }

    [Fact]
    public void StylesDecideWhatAValueMeans()
    {
        var invariant = CultureInfo.InvariantCulture;

        Assert.Equal(Decimal64.Parse("-5"),
            Decimal64.Parse("(5)", NumberStyles.Float | NumberStyles.AllowParentheses, invariant));
        Assert.Equal(Decimal64.Parse("-5"),
            Decimal64.Parse("5-", NumberStyles.Float | NumberStyles.AllowTrailingSign, invariant));
        Assert.Equal(Decimal64.Parse("12345"),
            Decimal64.Parse("12,345", NumberStyles.Float | NumberStyles.AllowThousands, invariant));
        var american = CultureInfo.GetCultureInfo("en-US");
        Assert.Equal(Decimal64.Parse("1234.50"),
            Decimal64.Parse("$1,234.50", NumberStyles.Currency, american));
        Assert.Equal(Decimal64.Parse("-1234.50"),
            Decimal64.Parse("-$1,234.50", NumberStyles.Currency, american));
        Assert.Equal(Decimal64.Parse("-1234.50"),
            Decimal64.Parse("($1,234.50)", NumberStyles.Currency, american));
    }

    [Fact]
    public void StylesADecimalFormatCannotHonorAreRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            Decimal64.Parse("1F", NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
        Assert.Throws<ArgumentException>(() =>
            Decimal64.TryParse("1F", NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _));
        Assert.Throws<ArgumentException>(() =>
            Decimal64.TryParse((string?)null, NumberStyles.AllowBinarySpecifier,
                CultureInfo.InvariantCulture, out _));
    }

    /// <summary>
    /// Nothing without a provider moves, whatever the thread is set to. This is the line the
    /// corpus stands on: it compares against the specification's text, and reaches it
    /// through the no-argument members.
    /// </summary>
    [Fact]
    public void TheSpecificationsTextIgnoresTheCurrentCulture()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = German;

            var value = Decimal64.Parse("-1234.5");
            Assert.Equal("-1234.5", value.ToString());
            Assert.Equal("-1234.5", Decimal64.Parse("-1234.5").ToString());
            Assert.Equal("-150E+18", Decimal64.Parse("-1.5E+20").ToEngineeringString());
            Assert.Equal("-Infinity", Decimal64.NegativeInfinity.ToString());
            Assert.Equal("NaN255", Decimal64.Parse("NaN255").ToString());
            Assert.Equal("-1234.5", Decimal64.Parse("-1234.5"u8).ToString());

            // And the invariant culture spells what the specification spells, so asking for
            // it by name gives the same text.
            Assert.Equal("-1234.5", value.ToString("G", CultureInfo.InvariantCulture));

            // The German reading of the same call is not the same text, which is the point.
            Assert.Equal("-1234,5", value.ToString("G", German));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    /// <summary>
    /// A culture cannot be inferred, and reading text under the wrong one is not always a
    /// failure -- a German 1234,5 read as invariant is a grouped 12345, silently. That is
    /// how the built-in types read it too, which is what this pins down: the two agree, so
    /// a caller who knows what double does knows what this does.
    /// </summary>
    [Fact]
    public void ACulturesTextReadUnderAnotherMeansSomethingElse()
    {
        var written = Decimal64.Parse("1234.5").ToString("G", German);
        Assert.Equal("1234,5", written);

        var invariant = CultureInfo.InvariantCulture;
        Assert.True(Decimal64.TryParse(written, invariant, out var misread));
        Assert.Equal(Decimal64.Parse("12345"), misread);
        Assert.Equal(double.Parse(written, invariant), (double)misread);

        // Without AllowThousands there is no reading of it at all, and double agrees.
        Assert.False(Decimal64.TryParse(written, NumberStyles.Float, invariant, out _));
        Assert.False(double.TryParse(written, NumberStyles.Float, invariant, out _));
    }

    /// <summary>
    /// Against double, on the same text under the same styles and the same culture. This is
    /// the part of parsing that is .NET's rather than the specification's, so the built-in
    /// type is the oracle for it: whatever it accepts this accepts, and to the same value.
    /// </summary>
    [Fact]
    public void AgreesWithDoubleOnWhatACultureMeans()
    {
        var cultures = new[]
        {
            CultureInfo.InvariantCulture,
            German,
            French,
            CultureInfo.GetCultureInfo("en-US")
        };

        var styles = new[]
        {
            NumberStyles.Float,
            NumberStyles.Float | NumberStyles.AllowThousands,
            NumberStyles.Currency,
            NumberStyles.Any,
            NumberStyles.None,
            NumberStyles.AllowDecimalPoint,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint
        };

        foreach (var culture in cultures)
        {
            // Grouped and currency text is asked of the culture rather than guessed at, so
            // the comparison holds whatever the platform's locale data says.
            var texts = new[]
            {
                "5",
                "-5",
                "+5",
                "5-",
                "(5)",
                " 5 ",
                "1E5",
                "1e-5",
                "1E+5",
                "",
                "-",
                "1..5",
                "1-5",
                "5.",
                ".5",
                ",5",
                "1,,5",
                "1,5",
                "1.5,2",
                "1E1,0",
                "5,",
                1234.5.ToString("F1", culture),
                1234.5.ToString("N1", culture),
                1234.5.ToString("C2", culture),
                (-1234.5).ToString("N1", culture),
                (-1234.5).ToString("C2", culture)
            };

            foreach (var style in styles)
            {
                foreach (var text in texts)
                {
                    var theirs = double.TryParse(text, style, culture, out var binary);
                    var ours = Decimal64.TryParse(text, style, culture, out var value);

                    Assert.True(theirs == ours,
                        $"'{text}' under {culture.Name} with {style}: double said {theirs}, "
                        + $"Decimal64 said {ours}");

                    if (ours)
                    {
                        Assert.True(binary == (double)value,
                            $"'{text}' under {culture.Name} with {style}: double read {binary}, "
                            + $"Decimal64 read {value}");
                    }
                }
            }
        }
    }

    private static IEnumerable<IFormatProvider> Providers()
    {
        yield return CultureInfo.InvariantCulture;
        yield return German;
        yield return French;
        yield return Unusual();
    }
}
