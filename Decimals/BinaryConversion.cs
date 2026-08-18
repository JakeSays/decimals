// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// How a binary floating-point value is read as a decimal. The two answers differ because a
/// <see cref="double"/> is a binary fraction: 0.1 is held as
/// 0.1000000000000000055511151231257827021181583404541015625, and both readings of that are
/// defensible.
/// </summary>
public enum BinaryConversion
{
    /// <summary>
    /// The shortest decimal that reads back as the same binary value, which is what the
    /// writer of the literal meant: <c>0.1</c> becomes the decimal 0.1. This is what the
    /// cast operators do, and what <see cref="decimal"/> does.
    /// </summary>
    ShortestRoundTrip,

    /// <summary>
    /// The binary value itself, correctly rounded to the format. This is IEEE 754's
    /// convertFormat: <c>0.1</c> becomes 0.1000000000000000055511151231257827 in a
    /// Decimal128, which is the exact value to thirty-four digits. At sixteen digits the
    /// two readings agree, since the error does not reach that far.
    /// </summary>
    ExactValue
}
