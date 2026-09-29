// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// The working context of the elementary functions: decNumber's decContext, with the
/// fields those functions change.
/// </summary>
/// <remarks>
/// The algorithms in <see cref="Decimal32Math"/> use the context the same way decNumber's
/// do. They widen the precision for one intermediate value, raise the exponent limits so
/// another cannot overflow, and turn clamping off where the format does not apply. Keeping
/// the same structure lets the port be compared with the original.
/// </remarks>
internal struct Decimal32WideContext
{
    /// <summary>
    /// decNumber's DEC_MAX_MATH: the largest exponent its mathematical functions accept.
    /// </summary>
    public const int MaxMathExponent = 999999;

    /// <summary>decNumber's DEC_MIN_EMIN: the smallest exponent any context can use.</summary>
    public const int SmallestExponent = -999999999;

    /// <summary>decNumber's DEC_MAX_EMAX: the largest exponent any context can use.</summary>
    public const int LargestExponent = 999999999;

    /// <summary>The number of coefficient digits the result is rounded to.</summary>
    public int Digits { get; set; }

    /// <summary>The largest adjusted exponent before the result overflows.</summary>
    public int MaxExponent { get; set; }

    /// <summary>The smallest adjusted exponent of a normal result.</summary>
    public int MinExponent { get; set; }

    /// <summary>The rounding mode.</summary>
    public Decimal32Rounding Rounding { get; set; }

    /// <summary>Whether an exponent that is in range but too large for the format is reduced by padding the coefficient.</summary>
    public bool Clamp { get; set; }

    /// <summary>The smallest exponent a result can have: decNumber's Etiny.</summary>
    public readonly int TinyExponent => MinExponent - (Digits - 1);

    /// <summary>
    /// decNumber's DEC_INIT_DECIMAL32 defaults. Its functions start their working contexts
    /// from these and then change the fields each algorithm needs.
    /// </summary>
    /// <returns>A context with the format's precision and exponent limits, half-even rounding, and clamping on.</returns>
    public static Decimal32WideContext Default()
    {
        return new Decimal32WideContext
        {
            Digits = Decimal32Encoding.Precision,
            MaxExponent = Decimal32Encoding.MaxExponent,
            MinExponent = Decimal32Encoding.MinExponent,
            Rounding = Decimal32Rounding.HalfEven,
            Clamp = true
        };
    }

    /// <summary>The format's own context, with the caller's rounding mode.</summary>
    /// <param name="rounding">The rounding mode to use.</param>
    /// <returns>The <see cref="Default"/> context with <paramref name="rounding"/> in place of half-even.</returns>
    public static Decimal32WideContext ForFormat(Decimal32Rounding rounding)
    {
        var context = Default();
        context.Rounding = rounding;
        return context;
    }
}
