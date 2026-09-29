// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// Summarizes the digits discarded below the last kept digit. Rounding only needs to know
/// whether the discarded part is zero, below half, exactly half, or above half.
/// </summary>
/// <remarks>
/// <para>
/// This is decNumber's residue. In this format, an exact product has up to 14 digits, and
/// an operand aligned against a much smaller one can have many more. Once the top 19
/// digits are known, the digits below them can only affect rounding, and these four states
/// describe that completely. The numeric values match decNumber's, so comparisons against
/// <see cref="Half"/> work the same way in both.
/// </para>
/// <para>
/// A residue is safe to carry only if every later discard happens above it. Folding two
/// inexact values into residues and then adding them is wrong, because their sum can carry
/// into the kept digits. Every code path here folds at most one operand.
/// </para>
/// </remarks>
internal enum Decimal32Residue : byte
{
    Exact = 0,

    /// <summary>Non-zero, and less than half a unit in the last kept place.</summary>
    BelowHalf = 1,

    /// <summary>Exactly half a unit in the last kept place.</summary>
    Half = 5,

    /// <summary>More than half a unit in the last kept place.</summary>
    AboveHalf = 7
}
