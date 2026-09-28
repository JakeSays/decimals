// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// The four operations built on division. They share the handling of infinities and zero
/// divisors but do not agree on what those mean, so the operation says which it is.
/// </summary>
internal enum Decimal32DivisionKind
{
    Divide,
    DivideInteger,
    Remainder,
    RemainderNear
}
