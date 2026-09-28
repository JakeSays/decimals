// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// How a binary floating-point value is read as a <see cref="Decimal32"/>. The two readings
/// differ because a <see cref="double"/> is a binary fraction: 0.1 is held as
/// 0.1000000000000000055511151231257827021181583404541015625, and both readings of that are
/// defensible.
/// </summary>
public enum Decimal32BinaryConversion
{
    /// <summary>
    /// The shortest decimal that reads back as the same binary value, which is what the
    /// writer of the literal meant: <c>0.1</c> becomes the decimal 0.1. This is what the
    /// cast operators do, and what <see cref="decimal"/> does.
    /// </summary>
    ShortestRoundTrip,

    /// <summary>
    /// The binary value itself, correctly rounded to seven digits. This is IEEE 754's
    /// convertFormat: at seven digits <c>0.1</c> still reads as 0.1000000, since the binary
    /// error does not reach that far, but 2.5 keeps its short quantum and a value like 0.3
    /// shows its binary neighbor once the digits run out.
    /// </summary>
    ExactValue
}
