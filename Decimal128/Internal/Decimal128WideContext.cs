// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// The working context the elementary functions carry, which is decNumber's decContext with
/// the fields those functions vary.
/// </summary>
/// <remarks>
/// The algorithms in <see cref="Decimal128Math"/> lean on the context the way decNumber's do:
/// they widen the precision for one intermediate, lift the exponent bounds so another
/// cannot overflow, and turn clamping off where the format is not in play. Keeping the same
/// shape here is what lets the port be read against the original.
/// </remarks>
internal struct Decimal128WideContext
{
    /// <summary>
    /// decNumber's DEC_MAX_MATH: the widest exponent its mathematical functions accept.
    /// </summary>
    public const int MaxMathExponent = 999999;

    /// <summary>decNumber's DEC_MIN_EMIN, the smallest exponent any context may ask for.</summary>
    public const int SmallestExponent = -999999999;

    /// <summary>decNumber's DEC_MAX_EMAX.</summary>
    public const int LargestExponent = 999999999;

    /// <summary>Coefficient digits the result is rounded to.</summary>
    public int Digits { get; set; }

    /// <summary>Largest adjusted exponent before the result overflows.</summary>
    public int MaxExponent { get; set; }

    /// <summary>Smallest adjusted exponent of a normal result.</summary>
    public int MinExponent { get; set; }

    public Decimal128Rounding Rounding { get; set; }

    /// <summary>Whether an in-range exponent too large for the format is folded down.</summary>
    public bool Clamp { get; set; }

    /// <summary>The smallest exponent a result may carry, decNumber's Etiny.</summary>
    public readonly int TinyExponent => MinExponent - (Digits - 1);

    /// <summary>
    /// decNumber's DEC_INIT_DECIMAL128 defaults, which its functions start their working
    /// contexts from before overriding whichever fields the algorithm needs.
    /// </summary>
    public static Decimal128WideContext Default()
    {
        return new Decimal128WideContext
        {
            Digits = Decimal128Encoding.Precision,
            MaxExponent = Decimal128Encoding.MaxExponent,
            MinExponent = Decimal128Encoding.MinExponent,
            Rounding = Decimal128Rounding.HalfEven,
            Clamp = true
        };
    }

    /// <summary>The format itself, under a caller's rounding mode.</summary>
    public static Decimal128WideContext ForFormat(Decimal128Rounding rounding)
    {
        var context = Default();
        context.Rounding = rounding;
        return context;
    }
}
