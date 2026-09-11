// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// Which digit-wise logical operation to apply. The three binary ones share a walk and
/// differ only in what they do with the pair of digits at each position.
/// </summary>
internal enum BcdLogicalOperation
{
    And,

    Or,

    Xor
}
