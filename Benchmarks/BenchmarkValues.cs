// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace Decimals.Benchmarks;

/// <summary>
/// The operands for all benchmarks. They are built the same way as the C++ <c>decbench</c>
/// operands, so the results can be compared.
/// </summary>
/// <remarks>
/// <para>
/// The wide set matches decbench: 1 to 16 digits, with exponents from -40 to +40. Its
/// results can be compared directly with the C++ results. The short set has the same shape
/// at decimal32's width. The full-width set fills a decimal128 coefficient.
/// </para>
/// <para>
/// The operands are text. Each benchmark parses them into its own type during setup. The
/// operand indexes also match decbench: an operation takes its operands from positions
/// <c>i</c>, <c>i*7+3</c>, and <c>i*13+5</c>. This way, consecutive operations do not use
/// consecutive array entries, and the operand pairs keep changing.
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
    /// The wide set's shape at decimal32's width: 1 to 7 digits, with exponents from -40 to
    /// +40. The wide set has up to 16 digits, and those operands would round when parsed
    /// into a 7-digit format, so the benchmarks would mostly measure that rounding.
    /// </summary>
    public static string[] Short32Text { get; }

    /// <summary>Where an operation's second operand comes from.</summary>
    public static int Second(int index) => ((index * 7) + 3) & (Count - 1);

    /// <summary>Where a three-operand operation's addend comes from.</summary>
    public static int Third(int index) => ((index * 13) + 5) & (Count - 1);

    /// <summary>
    /// Decimal128 operands that fill the 34-digit coefficient. Products of the wide set's
    /// 16-digit operands fit in 34 digits and never round. Products of these operands do
    /// round, so they measure the rounding paths.
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
            // The first digit is never zero, so the coefficient has the requested length.
            digits[position] = position == 0
                ? (char)('1' + random.Next(0, 9))
                : (char)('0' + random.Next(0, 10));
        }

        return new string(digits);
    }

    private static string[] BuildText(int exponentLimit)
    {
        // Fixed seed, so every run uses the same operands. Otherwise a change in the results
        // could come from the operands instead of the code.
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
