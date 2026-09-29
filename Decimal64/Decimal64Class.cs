// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// The ten classes returned by the specification's <c>class</c> operation. They are IEEE
/// 754's classes, with signaling and quiet NaN kept separate.
/// </summary>
public enum Decimal64Class
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
