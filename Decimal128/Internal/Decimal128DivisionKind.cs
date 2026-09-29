// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// The four operations built on division. They share the handling of infinities and zero
/// divisors, but they give different results for them, so each call says which operation
/// it is.
/// </summary>
internal enum Decimal128DivisionKind
{
    Divide,
    DivideInteger,
    Remainder,
    RemainderNear
}
