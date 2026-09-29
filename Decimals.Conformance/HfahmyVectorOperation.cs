// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Conformance;

/// <summary>
/// The operations in the Sayed-Ahmed and Fahmy vectors. Each member's comment gives the
/// symbol used in the vector files.
/// </summary>
public enum HfahmyVectorOperation
{
    /// <summary><c>+</c></summary>
    Add,

    /// <summary><c>-</c></summary>
    Subtract,

    /// <summary><c>*</c></summary>
    Multiply,

    /// <summary><c>/</c></summary>
    Divide,

    /// <summary><c>*+</c>: a * b + c.</summary>
    FusedMultiplyAdd,

    /// <summary><c>*-</c>: a * b - c.</summary>
    FusedMultiplySubtract,

    /// <summary><c>V</c></summary>
    SquareRoot
}
