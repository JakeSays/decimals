// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// What a decimal value is, once its encoding has been taken apart.
/// </summary>
internal enum DecimalKind
{
    Finite,
    Infinity,
    QuietNaN,
    SignalingNaN
}
