// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// The ten classes of the specification's <c>class</c> operation, which is IEEE 754's
/// <c>class</c> with signaling and quiet NaN kept apart.
/// </summary>
public enum DecimalClass
{
    SignalingNaN,
    QuietNaN,
    NegativeInfinity,
    NegativeNormal,
    NegativeSubnormal,
    NegativeZero,
    PositiveZero,
    PositiveSubnormal,
    PositiveNormal,
    PositiveInfinity
}
