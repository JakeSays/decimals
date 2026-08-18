// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// A decimal value with its encoding taken apart, as arithmetic works on it. Stays a
/// by-value struct the JIT can keep in registers: no arrays, no allocation, nothing that
/// would turn it into a general-purpose number.
/// </summary>
/// <typeparam name="TCoefficient">
/// The unsigned integer holding the coefficient: <see cref="uint"/> for Decimal32,
/// <see cref="ulong"/> for Decimal64, <see cref="UInt128"/> for Decimal128.
/// </typeparam>
internal readonly struct UnpackedDecimal<TCoefficient>
    where TCoefficient : unmanaged
{
    public UnpackedDecimal(DecimalKind kind, bool isNegative, int exponent, TCoefficient coefficient)
    {
        Kind = kind;
        IsNegative = isNegative;
        Exponent = exponent;
        Coefficient = coefficient;
    }

    public DecimalKind Kind { get; }

    public bool IsNegative { get; }

    /// <summary>
    /// The unbiased quantum exponent. Meaningless unless <see cref="Kind"/> is
    /// <see cref="DecimalKind.Finite"/>.
    /// </summary>
    public int Exponent { get; }

    /// <summary>
    /// The coefficient of a finite value, or the payload of a NaN.
    /// </summary>
    public TCoefficient Coefficient { get; }

    public bool IsFinite => Kind == DecimalKind.Finite;

    public bool IsNaN => Kind is DecimalKind.QuietNaN or DecimalKind.SignalingNaN;
}
