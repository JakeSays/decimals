// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// How a binary floating-point value converts to a <see cref="Decimal64"/>. There are two
/// choices because a <see cref="double"/> is a binary fraction. For example, the double 0.1
/// is exactly 0.1000000000000000055511151231257827021181583404541015625.
/// </summary>
public enum Decimal64BinaryConversion
{
    /// <summary>
    /// The shortest decimal that converts back to the same binary value. For <c>0.1</c>,
    /// this is the decimal 0.1, which is what the author of the literal meant. The cast
    /// operators and <see cref="decimal"/> use this conversion.
    /// </summary>
    ShortestRoundTrip,

    /// <summary>
    /// The exact binary value, correctly rounded to 16 digits. This is IEEE 754
    /// convertFormat. <c>0.1</c> becomes 0.1000000000000000, because the binary error is
    /// beyond the 16th digit. 2.5 is exact in binary, so it stays 2.5.
    /// </summary>
    ExactValue
}
