// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// Everything discarded below the last digit a coefficient keeps, reduced to the one thing
/// rounding needs to know about it: which side of halfway it lies on.
/// </summary>
/// <remarks>
/// <para>
/// This is decNumber's residue, and it is what lets a thirty-four digit format be computed
/// in two machine words. An exact intermediate can run to seventy digits and more, but
/// once its top thirty-eight are in hand nothing below them can do more than tip a
/// rounding, and four states describe that completely. The numeric values are decNumber's
/// so that a comparison against <see cref="Half"/> reads the same way in both.
/// </para>
/// <para>
/// A residue is only safe to carry when every later discard happens above it. Folding two
/// inexact quantities into one residue and then adding them is wrong, because their sum
/// can carry into the digits above; every path here folds at most one operand.
/// </para>
/// </remarks>
internal enum Decimal128Residue : byte
{
    Exact = 0,

    /// <summary>Non-zero, and less than half a unit in the last place kept.</summary>
    BelowHalf = 1,

    /// <summary>Exactly half a unit in the last place kept.</summary>
    Half = 5,

    /// <summary>More than half a unit in the last place kept.</summary>
    AboveHalf = 7
}
