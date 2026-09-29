// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// The kind of a decoded value. The fixed-width arithmetic reads the kind from the bits
/// when needed. The arbitrary-precision engine used by the elementary functions stores it
/// with each working value.
/// </summary>
internal enum Decimal64Kind
{
    Finite,
    Infinity,
    QuietNaN,
    SignalingNaN
}
