// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// What a value is once its encoding has been taken apart. The fixed-width arithmetic reads
/// this off the bits and never carries it; the arbitrary-precision engine behind the
/// elementary functions carries it beside each working value.
/// </summary>
internal enum Decimal32Kind
{
    Finite,
    Infinity,
    QuietNaN,
    SignalingNaN
}
