// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace Decimals.Benchmarks;

/// <summary>
/// The operands every benchmark runs on, built the way the C++ <c>decbench</c> builds its
/// own so the two sets of numbers can be read against each other.
/// </summary>
/// <remarks>
/// <para>
/// Two sets, because <see cref="decimal"/> and <see cref="double"/> cannot hold what a
/// decimal64 can. The wide set is decbench's: one to sixteen digits at exponents from -40
/// to +40, which only the decimal types carry, and it is the one whose absolute numbers can
/// be read against the C++ figures. The narrow set is shorter and shallower, so that all
/// three types hold both the operands and their products, and it is what the cross-type
/// comparisons run on -- a comparison on operands one side cannot represent measures
/// nothing at all.
/// </para>
/// <para>
/// The index pattern is decbench's too: an operation takes its operands from positions
/// <c>i</c>, <c>i*7+3</c>, and <c>i*13+5</c>, so consecutive operations do not run on
/// consecutive array entries and the operand pairing keeps changing.
/// </para>
/// </remarks>
public static class BenchmarkValues
{
    /// <summary>Operands per measured loop, a power of two so the wrap is a mask.</summary>
    public const int Count = 1024;

    /// <summary>The width of a Decimal128 coefficient.</summary>
    private const int FullWidthDigits = 34;

    static BenchmarkValues()
    {
        WideText = BuildText(40);
        NarrowText = BuildNarrowText();
        ModerateText = BuildModerateText();
        FullWidthText = BuildFullWidthText();

        FullWidth128 = new Decimal128[Count];

        Wide32 = new Decimal32[Count];
        Wide64 = new Decimal64[Count];
        Wide128 = new Decimal128[Count];
        Narrow64 = new Decimal64[Count];
        NarrowDecimal = new decimal[Count];
        NarrowDouble = new double[Count];
        Moderate64 = new Decimal64[Count];
        ModerateDouble = new double[Count];

        for (var index = 0; index < Count; index++)
        {
            Wide32[index] = Decimal32.Parse(WideText[index]);
            Wide64[index] = Decimal64.Parse(WideText[index]);
            Wide128[index] = Decimal128.Parse(WideText[index]);
            FullWidth128[index] = Decimal128.Parse(FullWidthText[index]);

            Narrow64[index] = Decimal64.Parse(NarrowText[index]);
            NarrowDecimal[index] = decimal.Parse(NarrowText[index],
                NumberStyles.Float, CultureInfo.InvariantCulture);
            NarrowDouble[index] = double.Parse(NarrowText[index],
                NumberStyles.Float, CultureInfo.InvariantCulture);

            Moderate64[index] = Decimal64.Parse(ModerateText[index]);
            ModerateDouble[index] = double.Parse(ModerateText[index],
                NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }

    public static string[] WideText { get; }

    public static string[] FullWidthText { get; }

    public static Decimal128[] FullWidth128 { get; }

    public static string[] NarrowText { get; }

    /// <summary>
    /// Operands the elementary functions can do real work on: full-length coefficients, but
    /// magnitudes between a thousandth and a thousand. Handed a decbench-shaped operand,
    /// exp overflows and log runs on an exponent rather than a coefficient, and neither
    /// would be measuring the series.
    /// </summary>
    public static string[] ModerateText { get; }

    public static Decimal32[] Wide32 { get; }

    public static Decimal64[] Wide64 { get; }

    public static Decimal128[] Wide128 { get; }

    public static Decimal64[] Narrow64 { get; }

    public static decimal[] NarrowDecimal { get; }

    public static double[] NarrowDouble { get; }

    public static Decimal64[] Moderate64 { get; }

    public static double[] ModerateDouble { get; }

    /// <summary>Where an operation's second operand comes from.</summary>
    public static int Second(int index) => ((index * 7) + 3) & (Count - 1);

    /// <summary>Where a three-operand operation's addend comes from.</summary>
    public static int Third(int index) => ((index * 13) + 5) & (Count - 1);

    /// <summary>
    /// The set the cross-type comparisons run on. Eight digits at exponents within five,
    /// because System.Decimal has to hold not only these but their products: two sixteen
    /// digit operands multiply to thirty-two, and its coefficient stops at twenty-nine.
    /// </summary>
    private static string[] BuildNarrowText()
    {
        var random = new Random(2718281);
        var values = new string[Count];

        for (var index = 0; index < Count; index++)
        {
            var digits = BuildDigits(random, random.Next(1, 9));
            var exponent = random.Next(-5, 6);
            values[index] = digits + "E" + exponent.ToString(CultureInfo.InvariantCulture);
        }

        return values;
    }

    private static string[] BuildModerateText()
    {
        var random = new Random(31415);
        var values = new string[Count];

        for (var index = 0; index < Count; index++)
        {
            var digits = BuildDigits(random, random.Next(1, 17));

            // The exponent is set from the digit count so that the value lands between a
            // thousandth and a thousand however long its coefficient is.
            var exponent = random.Next(-3, 4) - (digits.Length - 1);
            values[index] = digits + "E" + exponent.ToString(CultureInfo.InvariantCulture);
        }

        return values;
    }

    /// <summary>
    /// Decimal128 operands at the full width of its coefficient. The wide set tops out at
    /// sixteen digits, whose products still fit thirty-four and so never round; these do,
    /// which is the only way to see the format carry out the work it skips there.
    /// </summary>
    private static string[] BuildFullWidthText()
    {
        var random = new Random(20260818);
        var values = new string[Count];

        for (var index = 0; index < Count; index++)
        {
            var digits = BuildDigits(random, FullWidthDigits);
            var exponent = random.Next(-40, 41);
            values[index] = digits + "E" + exponent.ToString(CultureInfo.InvariantCulture);
        }

        return values;
    }

    private static string BuildDigits(Random random, int digitCount)
    {
        var digits = new char[digitCount];
        for (var position = 0; position < digitCount; position++)
        {
            // A leading zero would make the coefficient shorter than asked for.
            digits[position] = position == 0
                ? (char)('1' + random.Next(0, 9))
                : (char)('0' + random.Next(0, 10));
        }

        return new string(digits);
    }

    private static string[] BuildText(int exponentLimit)
    {
        // Fixed seed: the operands have to be the same set from run to run, or a change in
        // the numbers could be the operands rather than the code.
        var random = new Random(20260817);
        var values = new string[Count];

        for (var index = 0; index < Count; index++)
        {
            var digits = BuildDigits(random, random.Next(1, 17));
            var exponent = random.Next(-exponentLimit, exponentLimit + 1);
            values[index] = digits + "E" + exponent.ToString(CultureInfo.InvariantCulture);
        }

        return values;
    }
}
