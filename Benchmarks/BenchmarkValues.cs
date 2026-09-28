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
/// The wide set is decbench's: one to sixteen digits at exponents from -40 to +40, and it
/// is the one whose absolute numbers can be read against the C++ figures. The short set is
/// the same shape at decimal32's width, and the full-width set fills a decimal128
/// coefficient.
/// </para>
/// <para>
/// The operands are text, and each benchmark parses them into its own type during setup.
/// The index pattern is decbench's too: an operation takes its operands from positions
/// <c>i</c>, <c>i*7+3</c>, and <c>i*13+5</c>, so consecutive operations do not run on
/// consecutive array entries and the operand pairing keeps changing.
/// </para>
/// </remarks>
public static class BenchmarkValues
{
    /// <summary>Operands per measured loop, a power of two so the wrap is a mask.</summary>
    public const int Count = 1024;

    /// <summary>The width of a decimal128 coefficient.</summary>
    private const int FullWidthDigits = 34;

    static BenchmarkValues()
    {
        WideText = BuildText(40);
        FullWidthText = BuildFullWidthText();
        Short32Text = BuildShortText();
    }

    public static string[] WideText { get; }

    public static string[] FullWidthText { get; }

    /// <summary>
    /// The wide set's shape at decimal32's width: one to seven digits at exponents from
    /// -40 to +40. The wide set itself is sixteen digits, and every one of those operands
    /// rounds on the way into a seven-digit format, which would make the parse the only
    /// thing being measured.
    /// </summary>
    public static string[] Short32Text { get; }

    /// <summary>Where an operation's second operand comes from.</summary>
    public static int Second(int index) => ((index * 7) + 3) & (Count - 1);

    /// <summary>Where a three-operand operation's addend comes from.</summary>
    public static int Third(int index) => ((index * 13) + 5) & (Count - 1);

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

    private static string[] BuildShortText()
    {
        var random = new Random(20260922);
        var values = new string[Count];

        for (var index = 0; index < Count; index++)
        {
            var digits = BuildDigits(random, random.Next(1, 8));
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
